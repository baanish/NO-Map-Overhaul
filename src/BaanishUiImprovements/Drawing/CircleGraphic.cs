using UnityEngine;
using UnityEngine.UI;

namespace BaanishUiImprovements.Drawing;

/// <summary>
/// A filled disk with an edge ring and anti-aliased edges, built as UI mesh so the ring keeps a set width at
/// any zoom (a scaled sprite would thicken its edge as the map zooms in). The ring sits inside the radius.
/// </summary>
internal sealed class CircleGraphic : ModGraphic
{
    private const int Segments = 96;

    private float _radius;
    private float _ringWidth;
    private float _feather;
    private Color32 _fill;
    private Color32 _ring;

    public void Set(float radius, float ringWidth, Color32 fill, Color32 ring, float feather)
    {
        if (_radius == radius && _ringWidth == ringWidth && _feather == feather && _fill.Equals(fill) && _ring.Equals(ring))
        {
            return;
        }

        _radius = radius;
        _ringWidth = ringWidth;
        _feather = feather;
        _fill = fill;
        _ring = ring;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var profile = EdgeProfile.Build(_fill, _ringWidth, _ring, _feather);
        var edge = _radius - _ringWidth; // the profile's nominal edge is where the fill meets the ring

        vh.AddVert(Vector3.zero, _fill, Vector4.zero);
        for (var i = 0; i < Segments; i++)
        {
            var angle = i * Mathf.PI * 2f / Segments;
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle));
            foreach (var (offset, color) in profile)
            {
                vh.AddVert(direction * Mathf.Max(0f, edge + offset), color, Vector4.zero);
            }
        }

        var rings = profile.Length;
        for (var i = 0; i < Segments; i++)
        {
            var current = 1 + rings * i;
            var next = 1 + rings * ((i + 1) % Segments);
            vh.AddTriangle(0, current, next);
            for (var ring = 1; ring < rings; ring++)
            {
                vh.AddTriangle(current + ring - 1, current + ring, next + ring);
                vh.AddTriangle(next + ring, next + ring - 1, current + ring - 1);
            }
        }
    }
}
