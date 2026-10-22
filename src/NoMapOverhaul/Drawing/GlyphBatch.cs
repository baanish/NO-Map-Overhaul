using System.Collections.Generic;
using NoMapOverhaul.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// Glyph quads copied out of TextMeshPro, from any number of texts, as one mesh in one font material: one graphic for
/// Unity to cull and batch instead of one per text. Quads come four vertices at a time in TextMeshPro's order, in the
/// parent's space. <see cref="Apply"/> fits the rect around them, since a RectMask2D culls a graphic by its rect.
/// </summary>
internal sealed class GlyphBatch : ModGraphic
{
    /// <summary>The shared mesh's 16-bit indices reach 65535 vertices; past that the canvas update would fail, where nothing catches it.</summary>
    private const int MaxVertices = 64000;

    private readonly List<UIVertex> _vertices = new();
    private Vector2 _min;
    private Vector2 _max;
    private bool _wasEmpty = true;

    public override Texture mainTexture =>
        m_Material != null && m_Material.mainTexture != null ? m_Material.mainTexture : s_WhiteTexture;

    /// <summary>Room for another quad; past it, quads are dropped.</summary>
    public bool HasRoom => _vertices.Count + 4 <= MaxVertices;

    public void Clear() => _vertices.Clear();

    /// <summary>One corner of a quad; add all four, in TextMeshPro's order: bottom left, top left, top right, bottom right.</summary>
    public void Add(UIVertex vertex)
    {
        Vector2 position = vertex.position;
        if (_vertices.Count == 0)
        {
            _min = _max = position;
        }
        else
        {
            _min = Vector2.Min(_min, position);
            _max = Vector2.Max(_max, position);
        }

        _vertices.Add(vertex);
    }

    /// <summary>Whole quads already made, <paramref name="count"/> vertices from <paramref name="first"/>, lying within <paramref name="min"/> and <paramref name="max"/>.</summary>
    public void AddRange(List<UIVertex> source, int first, int count, Vector2 min, Vector2 max)
    {
        if (_vertices.Count == 0)
        {
            _min = min;
            _max = max;
        }
        else
        {
            _min = Vector2.Min(_min, min);
            _max = Vector2.Max(_max, max);
        }

        for (var i = first; i < first + count; i++)
        {
            _vertices.Add(source[i]);
        }
    }

    /// <summary>Fits the rect and queues the mesh rebuild. Skips the rebuild when the batch was empty and still is.</summary>
    public void Apply()
    {
        var empty = _vertices.Count == 0;
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
    /// The mesh handed to the canvas, filled straight from these lists with the channels TextMeshPro fills for its own
    /// text: <see cref="VertexHelper"/> would also write and upload two texture coordinates the SDF shader never reads.
    /// The canvas copies the mesh, so one serves every batch.
    /// </summary>
    private static Mesh? _sharedMesh;
    private static readonly List<Vector3> Positions = new();
    private static readonly List<Color32> Colors = new();
    private static readonly List<Vector4> Uv0 = new();
    private static readonly List<Vector4> Uv1 = new();
    private static readonly List<Vector3> Normals = new();
    private static readonly List<Vector4> Tangents = new();
    private static readonly List<int> Indices = new();

    protected override void UpdateGeometry()
    {
        var start = ModTimings.Start();
        Positions.Clear();
        Colors.Clear();
        Uv0.Clear();
        Uv1.Clear();
        Normals.Clear();
        Tangents.Clear();
        Indices.Clear();
        var origin = rectTransform.localPosition;
        for (var first = 0; first + 3 < _vertices.Count; first += 4)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = _vertices[first + corner];
                Positions.Add(vertex.position - origin);
                Colors.Add(vertex.color);
                Uv0.Add(vertex.uv0);
                Uv1.Add(vertex.uv1);
                Normals.Add(vertex.normal);
                Tangents.Add(vertex.tangent);
            }

            Indices.Add(first); Indices.Add(first + 1); Indices.Add(first + 2);
            Indices.Add(first + 2); Indices.Add(first + 3); Indices.Add(first);
        }

        if (_sharedMesh == null)
        {
            _sharedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _sharedMesh.MarkDynamic();
        }

        _sharedMesh.Clear();
        _sharedMesh.SetVertices(Positions);
        _sharedMesh.SetColors(Colors);
        _sharedMesh.SetUVs(0, Uv0);
        _sharedMesh.SetUVs(1, Uv1);
        _sharedMesh.SetNormals(Normals);
        _sharedMesh.SetTangents(Tangents);
        _sharedMesh.SetTriangles(Indices, 0, false);
        canvasRenderer.SetMesh(_sharedMesh);
        ModTimings.Stop(ModSection.MeshRebuilds, start);
    }

    /// <summary>Unused: <see cref="UpdateGeometry"/> builds the mesh without a <see cref="VertexHelper"/>.</summary>
    protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
}
