using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// A filled convex polygon in <see cref="Graphic.color"/>, points in this graphic's own space. No anti-aliasing, so it
/// suits straight edges, with a <see cref="StrokeGraphic"/> rim over any slanted one. With no points it draws nothing
/// but still takes clicks over its whole rect, which makes it a hit area.
/// </summary>
internal sealed class PolygonGraphic : ModGraphic
{
    private readonly List<Vector2> _points = new();

    /// <summary>Replaces the outline, in order around it. Pass nothing for an invisible hit area.</summary>
    public void SetPoints(params Vector2[] points)
    {
        _points.Clear();
        _points.AddRange(points);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_points.Count < 3)
        {
            return;
        }

        Color32 fill = color;
        foreach (var point in _points)
        {
            vh.AddVert(point, fill, Vector4.zero);
        }

        for (var i = 1; i < _points.Count - 1; i++)
        {
            vh.AddTriangle(0, i, i + 1);
        }
    }
}
