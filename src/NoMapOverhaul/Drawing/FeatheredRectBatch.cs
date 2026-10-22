using System.Collections.Generic;
using NoMapOverhaul.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// Any number of <see cref="FeatheredRect"/> rectangles, each at its own place and angle, as one mesh: one graphic for
/// Unity to cull and batch instead of one per rectangle. Positions are in the parent's space. <see cref="Apply"/> fits
/// this graphic's rect around them, since a RectMask2D culls a graphic by its rect.
/// </summary>
internal sealed class FeatheredRectBatch : ModGraphic
{
    /// <summary>
    /// The shared mesh's 16-bit indices reach 65535 vertices; past that the canvas update would fail, where nothing
    /// catches it. A rimmed label plate
    /// is 16 vertices, so rectangles past about 4,000 plates are dropped instead.
    /// </summary>
    private const int MaxVertices = 64000;

    private readonly List<Item> _items = new();
    private readonly (float Offset, Color32 Color)[] _profile = new (float, Color32)[EdgeProfile.MaxRings];
    private Vector2 _min;
    private Vector2 _max;
    private bool _wasEmpty = true;

    public void Clear() => _items.Clear();

    /// <param name="across">A unit vector along the rectangle's width.</param>
    public void Add(Vector2 center, Vector2 across, Vector2 half, Color32 fill, float rimWidth, Color32 rim, float feather)
    {
        var reach = new Vector2(Mathf.Abs(across.x) * half.x + Mathf.Abs(across.y) * half.y,
            Mathf.Abs(across.y) * half.x + Mathf.Abs(across.x) * half.y) + Vector2.one * (rimWidth + feather);
        if (_items.Count == 0)
        {
            _min = center - reach;
            _max = center + reach;
        }
        else
        {
            _min = Vector2.Min(_min, center - reach);
            _max = Vector2.Max(_max, center + reach);
        }

        _items.Add(new Item(center, across, half, fill, rimWidth, rim, feather));
    }

    /// <summary>A line as a thin rectangle, faded at its ends as at its sides.</summary>
    public void AddLine(Vector2 from, Vector2 to, float halfWidth, Color32 color, float feather)
    {
        var along = to - from;
        var length = along.magnitude;
        if (length > 1e-5f)
        {
            Add((from + to) * 0.5f, along / length, new Vector2(length * 0.5f, halfWidth), color, 0f, color, feather);
        }
    }

    /// <summary>Fits the rect and queues the mesh rebuild. Skips the rebuild when the batch was empty and still is.</summary>
    public void Apply()
    {
        var empty = _items.Count == 0;
        if (empty && _wasEmpty)
        {
            return;
        }

        _wasEmpty = empty;
        if (!empty)
        {
            FitRect((_min + _max) * 0.5f, _max - _min);
        }

        SetVerticesDirty();
    }

    /// <summary>
    /// The mesh handed to the canvas, filled straight from these lists: <see cref="VertexHelper"/> would write and upload
    /// eight channels per vertex where a rectangle needs two, positions and colours. The canvas copies the mesh, so one
    /// serves every batch.
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
        var origin = (Vector2)rectTransform.localPosition;
        foreach (var item in _items)
        {
            var rings = EdgeProfile.Write(_profile, item.Fill, item.RimWidth, item.Rim, item.Feather);
            if (Vertices.Count + rings * 4 > MaxVertices)
            {
                break;
            }

            FeatheredRect.AddTo(Vertices, VertexColors, Indices, item.Center - origin, item.Across, item.Half, _profile, rings);
        }

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

    private readonly struct Item
    {
        public Item(Vector2 center, Vector2 across, Vector2 half, Color32 fill, float rimWidth, Color32 rim, float feather)
        {
            Center = center;
            Across = across;
            Half = half;
            Fill = fill;
            RimWidth = rimWidth;
            Rim = rim;
            Feather = feather;
        }

        public Vector2 Center { get; }
        public Vector2 Across { get; }
        public Vector2 Half { get; }
        public Color32 Fill { get; }
        public float RimWidth { get; }
        public Color32 Rim { get; }
        public float Feather { get; }
    }
}
