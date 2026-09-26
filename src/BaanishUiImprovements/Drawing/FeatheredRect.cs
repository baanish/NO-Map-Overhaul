using UnityEngine;
using UnityEngine.UI;

namespace BaanishUiImprovements.Drawing;

/// <summary>
/// A filled rectangle with an optional dark rim and anti-aliased edges, drawn as nested rectangles whose
/// vertex colours step through an <see cref="EdgeProfile"/>. The fill colour is <see cref="Graphic.color"/>.
/// </summary>
internal sealed class FeatheredRect : MaskableGraphic
{
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

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = rectTransform.rect;
        var profile = EdgeProfile.Build(color, _rimWidth, _rim, _feather);
        var maxInset = Mathf.Min(rect.width, rect.height) * 0.5f;

        for (var ring = 0; ring < profile.Length; ring++)
        {
            var offset = Mathf.Max(profile[ring].Offset, -maxInset);
            var r = new Rect(rect.xMin - offset, rect.yMin - offset, rect.width + 2f * offset, rect.height + 2f * offset);
            var c = profile[ring].Color;
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector4.zero);
        }

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
        for (var ring = 1; ring < profile.Length; ring++)
        {
            var inner = (ring - 1) * 4;
            var outer = ring * 4;
            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                vh.AddTriangle(inner + side, outer + side, outer + next);
                vh.AddTriangle(outer + next, inner + next, inner + side);
            }
        }
    }
}
