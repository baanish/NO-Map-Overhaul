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
    public static void AddTo(VertexHelper vh, Vector2 center, Vector2 across, Vector2 half, (float Offset, Color32 Color)[] profile, int rings)
    {
        var up = new Vector2(-across.y, across.x);
        var maxInset = Mathf.Min(half.x, half.y);
        var first = vh.currentVertCount;
        for (var ring = 0; ring < rings; ring++)
        {
            var offset = Mathf.Max(profile[ring].Offset, -maxInset);
            var x = across * (half.x + offset);
            var y = up * (half.y + offset);
            var c = profile[ring].Color;
            vh.AddVert(center - x - y, c, Vector4.zero);
            vh.AddVert(center - x + y, c, Vector4.zero);
            vh.AddVert(center + x + y, c, Vector4.zero);
            vh.AddVert(center + x - y, c, Vector4.zero);
        }

        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
        for (var ring = 1; ring < rings; ring++)
        {
            var inner = first + (ring - 1) * 4;
            var outer = first + ring * 4;
            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                vh.AddTriangle(inner + side, outer + side, outer + next);
                vh.AddTriangle(outer + next, inner + next, inner + side);
            }
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = rectTransform.rect;
        var rings = EdgeProfile.Write(_profile, color, _rimWidth, _rim, _feather);
        AddTo(vh, rect.center, Vector2.right, rect.size * 0.5f, _profile, rings);
    }
}
