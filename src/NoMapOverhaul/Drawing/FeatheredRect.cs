using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// A filled rectangle with an optional dark rim and anti-aliased edges, drawn as nested rectangles whose
/// vertex colours step through an <see cref="EdgeProfile"/>. The fill colour is <see cref="Graphic.color"/>.
/// </summary>
internal sealed class FeatheredRect : ModGraphic
{
    private readonly (float Offset, Color32 Color)[] _profile = new (float, Color32)[EdgeProfile.MaxRings];
    private float _rimWidth;
    private float _feather;
    private Color32 _rim;

    public void SetEdges(float rimWidth, Color32 rim, float feather)
    {
        if (_rimWidth == rimWidth && _feather == feather && _rim.Equals(rim))
        {
            return;
        }

        _rimWidth = rimWidth;
        _rim = rim;
        _feather = feather;
        SetVerticesDirty();
    }

    /// <summary>
    /// Adds a rectangle as nested rings, one per <paramref name="profile"/> entry, to a mesh: centred on
    /// <paramref name="center"/>, <paramref name="half"/> its half width along <paramref name="across"/> (a unit vector)
    /// and half height along the perpendicular. An inset past the middle stops there.
    /// </summary>
    public static void AddTo(List<Vector3> vertices, List<Color32> colors, List<int> indices, Vector2 center, Vector2 across,
        Vector2 half, (float Offset, Color32 Color)[] profile, int rings)
    {
        var up = new Vector2(-across.y, across.x);
        var maxInset = Mathf.Min(half.x, half.y);
        var first = vertices.Count;
        for (var ring = 0; ring < rings; ring++)
        {
            var offset = Mathf.Max(profile[ring].Offset, -maxInset);
            var x = across * (half.x + offset);
            var y = up * (half.y + offset);
            var c = profile[ring].Color;
            vertices.Add(center - x - y);
            vertices.Add(center - x + y);
            vertices.Add(center + x + y);
            vertices.Add(center + x - y);
            colors.Add(c);
            colors.Add(c);
            colors.Add(c);
            colors.Add(c);
        }

        AddTriangle(indices, first, first + 1, first + 2);
        AddTriangle(indices, first + 2, first + 3, first);
        for (var ring = 1; ring < rings; ring++)
        {
            var inner = first + (ring - 1) * 4;
            var outer = first + ring * 4;
            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                AddTriangle(indices, inner + side, outer + side, outer + next);
                AddTriangle(indices, outer + next, inner + next, inner + side);
            }
        }
    }

    private static readonly List<Vector3> Vertices = new();
    private static readonly List<Color32> VertexColors = new();
    private static readonly List<int> Indices = new();

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Vertices.Clear();
        VertexColors.Clear();
        Indices.Clear();
        var rect = rectTransform.rect;
        var rings = EdgeProfile.Write(_profile, color, _rimWidth, _rim, _feather);
        AddTo(Vertices, VertexColors, Indices, rect.center, Vector2.right, rect.size * 0.5f, _profile, rings);
        for (var i = 0; i < Vertices.Count; i++)
        {
            vh.AddVert(Vertices[i], VertexColors[i], Vector4.zero);
        }

        for (var i = 0; i < Indices.Count; i += 3)
        {
            vh.AddTriangle(Indices[i], Indices[i + 1], Indices[i + 2]);
        }
    }

    private static void AddTriangle(List<int> indices, int a, int b, int c)
    {
        indices.Add(a);
        indices.Add(b);
        indices.Add(c);
    }
}
