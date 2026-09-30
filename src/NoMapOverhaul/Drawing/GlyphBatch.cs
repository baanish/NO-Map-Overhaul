using System.Collections.Generic;
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
    /// <summary>VertexHelper throws at 65000 vertices, inside the canvas update where nothing catches it.</summary>
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
            rectTransform.localPosition = (_min + _max) * 0.5f;
            rectTransform.sizeDelta = _max - _min;
        }

        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var origin = rectTransform.localPosition;
        for (var first = 0; first + 3 < _vertices.Count; first += 4)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = _vertices[first + corner];
                vertex.position -= origin;
                vh.AddVert(vertex);
            }

            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
