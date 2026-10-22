using System;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// The custom colour picker's maths, free of Unity so it's unit-tested: hue, saturation, and value (brightness), each 0
/// to 1, and the wheel that shows hue around it and saturation from its centre out. Wheel points are in radii from its
/// centre, X right and Y up, with hue 0 (red) at 3 o'clock and rising anticlockwise through yellow and green.
/// </summary>
internal static class ColorWheel
{
    public static ShapeColor ToRgb(float hue, float saturation, float value)
    {
        var h = (hue - MathF.Floor(hue)) * 6f;
        var sector = Math.Min((int)h, 5);
        var f = h - sector;
        var s = Math.Clamp(saturation, 0f, 1f);
        var v = Math.Clamp(value, 0f, 1f);
        var p = v * (1f - s);
        var q = v * (1f - s * f);
        var t = v * (1f - s * (1f - f));
        var (r, g, b) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };

        return new ShapeColor(Byte(r), Byte(g), Byte(b));
    }

    /// <summary>A grey has no hue, so it comes back as hue 0.</summary>
    public static (float Hue, float Saturation, float Value) FromRgb(ShapeColor color)
    {
        float r = color.R / 255f, g = color.G / 255f, b = color.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var range = max - MathF.Min(r, MathF.Min(g, b));
        if (range <= 0f)
        {
            return (0f, 0f, max);
        }

        var hue = max == r ? (g - b) / range : max == g ? 2f + (b - r) / range : 4f + (r - g) / range;
        hue /= 6f;
        return (hue < 0f ? hue + 1f : hue, range / max, max);
    }

    /// <summary>Whether a press here lands on the wheel. Its rim counts.</summary>
    public static bool Contains(float x, float y) => x * x + y * y <= 1f;

    /// <summary>The hue and saturation under a point. One past the rim, as a drag goes, takes the rim's colour in its direction.</summary>
    public static (float Hue, float Saturation) Pick(float x, float y)
    {
        var hue = MathF.Atan2(y, x) / (2f * MathF.PI);
        return (hue < 0f ? hue + 1f : hue, MathF.Min(MathF.Sqrt(x * x + y * y), 1f));
    }

    /// <summary>Where <see cref="Pick"/> finds this hue and saturation.</summary>
    public static (float X, float Y) Position(float hue, float saturation)
    {
        var angle = hue * 2f * MathF.PI;
        return (MathF.Cos(angle) * saturation, MathF.Sin(angle) * saturation);
    }

    /// <summary>
    /// The texel at column <paramref name="x"/> and row <paramref name="y"/>, from the bottom left, of a
    /// <paramref name="size"/>-square wheel texture at full value: its colour, and an opacity that fades out over one
    /// texel across the rim so the circle's edge is smooth.
    /// </summary>
    public static (ShapeColor Color, byte Alpha) Texel(int x, int y, int size)
    {
        var half = size * 0.5f;
        var px = (x + 0.5f - half) / half;
        var py = (y + 0.5f - half) / half;
        var radius = MathF.Sqrt(px * px + py * py);
        var (hue, saturation) = Pick(px, py);
        return (ToRgb(hue, saturation, 1f), Byte(Math.Clamp((1f - radius) * half + 0.5f, 0f, 1f)));
    }

    private static byte Byte(float unit) => (byte)MathF.Round(unit * 255f);
}
