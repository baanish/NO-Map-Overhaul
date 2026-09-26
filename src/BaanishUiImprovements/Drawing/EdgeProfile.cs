using UnityEngine;

namespace BaanishUiImprovements.Drawing;

/// <summary>
/// The cross-section of a shape's edge, as offsets from its nominal edge (negative is inside) with a colour at each.
/// Each colour change is spread across <c>feather</c> units, which is the whole anti-aliasing: the map canvas has
/// no MSAA, and a hard vertex edge on a rotated shape stair-steps.
/// </summary>
internal static class EdgeProfile
{
    /// <summary>Edges fade over one map icon unit, about one screen pixel.</summary>
    public const float AntiAliasWidth = 1f;

    /// <summary>Fill, then an optional rim outside the nominal edge, fading to transparent.</summary>
    public static (float Offset, Color32 Color)[] Build(Color32 fill, float rimWidth, Color32 rim, float feather)
    {
        var half = feather * 0.5f;
        if (rimWidth <= 0f || rim.a == 0)
        {
            return new[] { (-half, fill), (half, Transparent(fill)) };
        }

        return new[]
        {
            (-half, fill),
            (half, rim),
            (Mathf.Max(half, rimWidth - half), rim),
            (rimWidth + half, Transparent(rim)),
        };
    }

    private static Color32 Transparent(Color32 color) => new(color.r, color.g, color.b, 0);
}
