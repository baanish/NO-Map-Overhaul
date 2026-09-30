using UnityEngine;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// The cross-section of a shape's edge, as offsets from its nominal edge (negative is inside) with a colour at each.
/// Each colour change is spread across <c>feather</c> units, which is the whole anti-aliasing: the map canvas has
/// no MSAA, and a hard vertex edge on a rotated shape stair-steps.
/// </summary>
internal static class EdgeProfile
{
    /// <summary>Edges fade over one map icon unit, about one screen pixel.</summary>
    public const float AntiAliasWidth = 1f;

    /// <summary>The most rings a profile has.</summary>
    public const int MaxRings = 4;

    /// <summary>Fill, then an optional rim outside the nominal edge, fading to transparent.</summary>
    public static (float Offset, Color32 Color)[] Build(Color32 fill, float rimWidth, Color32 rim, float feather)
    {
        var rings = new (float Offset, Color32 Color)[MaxRings];
        System.Array.Resize(ref rings, Write(rings, fill, rimWidth, rim, feather));
        return rings;
    }

    /// <summary><see cref="Build"/> into a buffer of <see cref="MaxRings"/>, for meshes rebuilt often. Returns the ring count.</summary>
    public static int Write((float Offset, Color32 Color)[] rings, Color32 fill, float rimWidth, Color32 rim, float feather)
    {
        var half = feather * 0.5f;
        rings[0] = (-half, fill);
        if (rimWidth <= 0f || rim.a == 0)
        {
            rings[1] = (half, Transparent(fill));
            return 2;
        }

        rings[1] = (half, rim);
        rings[2] = (Mathf.Max(half, rimWidth - half), rim);
        rings[3] = (rimWidth + half, Transparent(rim));
        return 4;
    }

    private static Color32 Transparent(Color32 color) => new(color.r, color.g, color.b, 0);
}
