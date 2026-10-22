using System.Collections.Generic;
using NoMapOverhaul.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// Any number of thick lines, open or closed, as one UI mesh. Each line's cross-section is an <see cref="EdgeProfile"/>
/// on both sides of its centre: fill, dark rim, anti-aliased fade. Joins are mitred, with the mitre clamped so a
/// hairpin can't spike, and open ends fade out over the rim, so a freehand stroke of many short segments reads as one line.
/// Points are in the parent's space. <see cref="Apply"/> fits this graphic's rect around them: a RectMask2D culls a
/// graphic by its rect, not its mesh, so a rect left at the default size would vanish once scrolled off.
/// </summary>
internal sealed class StrokeGraphic : ModGraphic
{
    /// <summary>
    /// VertexHelper throws at 65000 vertices, inside the canvas update where nothing catches it; strokes past this are
    /// dropped instead. With the rim on, a vertex budget of 64000 is about 7900 points, which the MaxPenPoints setting's
    /// ceiling keeps one freehand stroke under.
    /// </summary>
    private const int MaxVertices = 64000;

    /// <summary>How far a join's corner may reach, as a multiple of the half width, before it is clipped.</summary>
    private const float MiterLimit = 3f;

    private readonly List<Vector2> _points = new();
    private readonly List<Stroke> _strokes = new();
    private readonly (float Offset, Color32 Color)[] _rings = new (float, Color32)[EdgeProfile.MaxRings];
    private readonly float[] _lateral = new float[EdgeProfile.MaxRings * 2];
    private readonly Color32[] _colors = new Color32[EdgeProfile.MaxRings * 2];
    private int _strokeStart;
    private Vector2 _min;
    private Vector2 _max;
    private float _reach;
    private bool _wasEmpty = true;

    /// <summary>No line since the last <see cref="Clear"/>.</summary>
    public bool IsEmpty => _strokes.Count == 0;

    public void Clear()
    {
        _points.Clear();
        _strokes.Clear();
        _strokeStart = 0;
        _reach = 0f;
    }

    /// <summary>Adds a point to the line being built. A repeat of the previous point is skipped.</summary>
    public void AddPoint(Vector2 point)
    {
        if (_points.Count > _strokeStart && (_points[_points.Count - 1] - point).sqrMagnitude < 1e-10f)
        {
            return;
        }

        _points.Add(point);
    }

    /// <summary>Finishes the line from the points added since the last one. A single point draws as a dot.</summary>
    public void EndStroke(bool closed, float halfWidth, Color32 fill, float rimWidth, Color32 rim, float feather)
    {
        var count = _points.Count - _strokeStart;
        if (closed && count > 2 && (_points[_points.Count - 1] - _points[_strokeStart]).sqrMagnitude < 1e-10f)
        {
            _points.RemoveAt(_points.Count - 1);
            count--;
        }

        if (count == 0)
        {
            return;
        }

        if (count == 1)
        {
            _points.Add(_points[_points.Count - 1] + new Vector2(1e-3f, 0f));
            count = 2;
            closed = false;
        }

        if (_strokes.Count == 0)
        {
            _min = _max = _points[_strokeStart];
        }

        for (var i = _strokeStart; i < _points.Count; i++)
        {
            _min = Vector2.Min(_min, _points[i]);
            _max = Vector2.Max(_max, _points[i]);
        }

        _reach = Mathf.Max(_reach, (halfWidth + rimWidth + feather) * MiterLimit);
        _strokes.Add(new Stroke(_strokeStart, count, closed && count > 2, halfWidth, fill, rimWidth, rim, feather));
        _strokeStart = _points.Count;
    }

    /// <summary>Fits the rect and queues the mesh rebuild. Skips the rebuild when the graphic was empty and still is.</summary>
    public void Apply()
    {
        var empty = _strokes.Count == 0;
        if (empty && _wasEmpty)
        {
            return;
        }

        _wasEmpty = empty;
        if (!empty)
        {
            FitRect((_min + _max) * 0.5f, _max - _min + Vector2.one * (2f * _reach));
        }

        SetVerticesDirty();
    }

    /// <summary>
    /// The mesh handed to the canvas, filled straight from these lists: <see cref="VertexHelper"/> would write and upload
    /// eight channels per vertex where a line needs two, positions and colours. The canvas copies the mesh, so one serves
    /// every stroke graphic.
    /// </summary>
    private static Mesh? _sharedMesh;
    private static readonly List<Vector3> Vertices = new();
    private static readonly List<Color32> VertexColors = new();
    private static readonly List<int> Indices = new();

    protected override void UpdateGeometry()
    {
        var start = ModTimings.Start();
        Vertices.Clear();
        VertexColors.Clear();
        Indices.Clear();
        Fill();
        if (_sharedMesh == null)
        {
            _sharedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _sharedMesh.MarkDynamic();
        }

        _sharedMesh.Clear();
        _sharedMesh.SetVertices(Vertices);
        _sharedMesh.SetColors(VertexColors);
        _sharedMesh.SetTriangles(Indices, 0, false);
        canvasRenderer.SetMesh(_sharedMesh);
        ModTimings.Stop(ModSection.MeshRebuilds, start);
    }

    /// <summary>Unused: <see cref="UpdateGeometry"/> builds the mesh without a <see cref="VertexHelper"/>.</summary>
    protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

    private void Fill()
    {
        var origin = (Vector2)rectTransform.localPosition;
        foreach (var stroke in _strokes)
        {
            var rings = EdgeProfile.Write(_rings, stroke.Fill, stroke.RimWidth, stroke.Rim, stroke.Feather);
            var lanes = rings * 2;
            for (var ring = 0; ring < rings; ring++)
            {
                var reach = Mathf.Max(0f, stroke.HalfWidth + _rings[ring].Offset);
                _lateral[rings - 1 - ring] = -reach;
                _lateral[rings + ring] = reach;
                _colors[rings - 1 - ring] = _rings[ring].Color;
                _colors[rings + ring] = _rings[ring].Color;
            }

            var sections = stroke.Count + (stroke.Closed ? 0 : 2);
            if (Vertices.Count + sections * lanes > MaxVertices)
            {
                break;
            }

            var first = Vertices.Count;
            var cap = stroke.RimWidth + stroke.Feather;
            if (!stroke.Closed)
            {
                var start = _points[stroke.Start];
                var along = (_points[stroke.Start + 1] - start).normalized;
                AddSection(start - along * cap - origin, Normal(along), 1f, lanes, transparent: true);
            }

            for (var i = 0; i < stroke.Count; i++)
            {
                var (normal, scale) = Join(stroke, i);
                AddSection(_points[stroke.Start + i] - origin, normal, scale, lanes, transparent: false);
            }

            if (!stroke.Closed)
            {
                var end = _points[stroke.Start + stroke.Count - 1];
                var along = (end - _points[stroke.Start + stroke.Count - 2]).normalized;
                AddSection(end + along * cap - origin, Normal(along), 1f, lanes, transparent: true);
            }

            for (var section = 1; section < sections; section++)
            {
                Connect(first + (section - 1) * lanes, first + section * lanes, lanes);
            }

            if (stroke.Closed)
            {
                Connect(first + (sections - 1) * lanes, first, lanes);
            }
        }
    }

    /// <summary>The mitre direction at a point and how far to stretch along it so each side keeps its width.</summary>
    private (Vector2 Normal, float Scale) Join(Stroke stroke, int i)
    {
        var last = stroke.Count - 1;
        var point = _points[stroke.Start + i];
        if (!stroke.Closed && (i == 0 || i == last))
        {
            var other = _points[stroke.Start + (i == 0 ? 1 : last - 1)];
            return (Normal(i == 0 ? (other - point).normalized : (point - other).normalized), 1f);
        }

        var previous = _points[stroke.Start + (i == 0 ? last : i - 1)];
        var next = _points[stroke.Start + (i == last ? 0 : i + 1)];
        var inbound = Normal((point - previous).normalized);
        var miter = inbound + Normal((next - point).normalized);
        if (miter.sqrMagnitude < 1e-6f)
        {
            return (inbound, 1f); // a full reversal: no corner to fill
        }

        miter.Normalize();
        return (miter, Mathf.Min(1f / Mathf.Max(Vector2.Dot(miter, inbound), 1e-3f), MiterLimit));
    }

    private void AddSection(Vector2 center, Vector2 normal, float scale, int lanes, bool transparent)
    {
        for (var lane = 0; lane < lanes; lane++)
        {
            var color = _colors[lane];
            if (transparent)
            {
                color.a = 0;
            }

            Vertices.Add(center + normal * (_lateral[lane] * scale));
            VertexColors.Add(color);
        }
    }

    private static void Connect(int from, int to, int lanes)
    {
        for (var lane = 0; lane < lanes - 1; lane++)
        {
            Indices.Add(from + lane); Indices.Add(to + lane); Indices.Add(to + lane + 1);
            Indices.Add(to + lane + 1); Indices.Add(from + lane + 1); Indices.Add(from + lane);
        }
    }

    private static Vector2 Normal(Vector2 direction) => new(-direction.y, direction.x);

    private readonly struct Stroke
    {
        public Stroke(int start, int count, bool closed, float halfWidth, Color32 fill, float rimWidth, Color32 rim, float feather)
        {
            Start = start;
            Count = count;
            Closed = closed;
            HalfWidth = halfWidth;
            Fill = fill;
            RimWidth = rimWidth;
            Rim = rim;
            Feather = feather;
        }

        public int Start { get; }
        public int Count { get; }
        public bool Closed { get; }
        public float HalfWidth { get; }
        public Color32 Fill { get; }
        public float RimWidth { get; }
        public Color32 Rim { get; }
        public float Feather { get; }
    }
}
