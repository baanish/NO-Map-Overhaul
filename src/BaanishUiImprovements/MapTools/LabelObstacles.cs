using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Boxes around what map labels keep clear of on the full map, in the drawings layer's icon units: the game's unit and
/// airbase icons, the grid's row letters and column numbers, the mod's own runway numbers and airbase names, and the
/// map tools menu while it sits inside the map. Read only when labels are placed, which is when a drawing changes or the map comes to rest, never
/// per frame. Its lists are kept, so collecting allocates nothing.
/// </summary>
internal sealed class LabelObstacles
{
    /// <summary>How the mod's other map layers are named. Their text is runway numbers and airbase names.</summary>
    private const string ModLayerPrefix = "Baanish";

    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<TextMeshProUGUI> _modTexts = new();
    private readonly List<UnityEngine.UI.Text> _gridTexts = new();

    /// <param name="layer">The drawings layer; its parent holds the other map layers and the icons.</param>
    /// <param name="screenAreas">Rects fixed on screen, such as the rail. Inactive ones are skipped.</param>
    public void Collect(DynamicMap map, RectTransform layer, float inverseScale, IReadOnlyList<RectTransform> screenAreas, List<LabelBox> into)
    {
        into.Clear();
        var icons = map.mapIcons;
        for (var i = 0; i < icons.Count; i++)
        {
            var icon = icons[i];
            if (icon != null && icon.isActiveAndEnabled && icon.iconImage != null && icon.iconImage.enabled)
            {
                into.Add(Corners(icon.iconImage.rectTransform, layer, inverseScale).Grown(LabelLayout.IconMargin));
            }
        }

        var parent = layer.parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == layer || !child.gameObject.activeInHierarchy || !child.name.StartsWith(ModLayerPrefix, System.StringComparison.Ordinal))
            {
                continue;
            }

            child.GetComponentsInChildren(false, _modTexts);
            foreach (var text in _modTexts)
            {
                if (text.enabled)
                {
                    var bounds = text.textBounds;
                    if (bounds.size.x > 0f)
                    {
                        into.Add(Box(text.transform, bounds.min, bounds.max, layer, inverseScale));
                    }
                }
            }
        }

        if (map.gridLabels != null)
        {
            map.gridLabels.GetComponentsInChildren(false, _gridTexts);
            foreach (var text in _gridTexts)
            {
                // The game sizes each grid label's rect at the default 100 units, far wider than its one or two letters.
                var center = text.rectTransform.rect.center;
                var half = new Vector2((text.text.Length * 0.6f + 0.4f) * text.fontSize, 1.2f * text.fontSize) * 0.5f;
                into.Add(Box(text.transform, center - half, center + half, layer, inverseScale));
            }
        }

        for (var i = 0; i < screenAreas.Count; i++)
        {
            var area = screenAreas[i];
            if (area != null && area.gameObject.activeInHierarchy)
            {
                into.Add(Corners(area, layer, inverseScale));
            }
        }
    }

    private LabelBox Corners(RectTransform rect, RectTransform layer, float inverseScale)
    {
        rect.GetWorldCorners(_corners);
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var corner in _corners)
        {
            Vector2 local = layer.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        return ToIconUnits(min, max, inverseScale);
    }

    private static LabelBox Box(Transform space, Vector2 min, Vector2 max, RectTransform layer, float inverseScale)
    {
        Vector2 a = layer.InverseTransformPoint(space.TransformPoint(min));
        Vector2 b = layer.InverseTransformPoint(space.TransformPoint(max));
        return ToIconUnits(Vector2.Min(a, b), Vector2.Max(a, b), inverseScale);
    }

    private static LabelBox ToIconUnits(Vector2 min, Vector2 max, float inverseScale) =>
        new(new FlatVector(min.x, min.y) / inverseScale, new FlatVector(max.x, max.y) / inverseScale);
}
