using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// The player's own swatches, up to <see cref="Max"/>, kept in the hidden CustomColors setting as a comma-separated list
/// of <c>#RRGGBB</c>. They're opaque, since a drawing's colour is. Free of Unity so the parsing is unit-tested.
/// </summary>
internal static class CustomColors
{
    /// <summary>The second row of swatches holds this many.</summary>
    public const int Max = 6;

    public static readonly IReadOnlyList<ShapeColor> None = Array.Empty<ShapeColor>();

    /// <summary>The colours in a saved list. An entry that isn't <c>#RRGGBB</c> is skipped, and any past the sixth valid one dropped.</summary>
    public static IReadOnlyList<ShapeColor> Parse(string text)
    {
        var colors = new List<ShapeColor>(Max);
        foreach (var entry in text.Split(','))
        {
            if (colors.Count < Max && TryParseHex(entry.Trim(), out var color))
            {
                colors.Add(color);
            }
        }

        return colors.Count == 0 ? None : colors.ToArray();
    }

    public static string Format(IReadOnlyList<ShapeColor> colors)
    {
        var entries = new string[colors.Count];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = ToHex(colors[i]);
        }

        return string.Join(",", entries);
    }

    /// <summary>
    /// The list with <paramref name="color"/> in the next free slot, or unchanged when every slot is used or a built-in or
    /// custom swatch already has its RGB, so Add on such a colour picks that swatch rather than saving a copy.
    /// </summary>
    public static IReadOnlyList<ShapeColor> With(IReadOnlyList<ShapeColor> colors, ShapeColor color)
    {
        if (colors.Count >= Max || HasRgb(ShapeColor.Palette, color) || HasRgb(colors, color))
        {
            return colors;
        }

        var added = new ShapeColor[colors.Count + 1];
        for (var i = 0; i < colors.Count; i++)
        {
            added[i] = colors[i];
        }

        added[colors.Count] = new ShapeColor(color.R, color.G, color.B);
        return added;
    }

    /// <summary>The list without slot <paramref name="index"/>, the later ones moving up; unchanged for a slot that's empty.</summary>
    public static IReadOnlyList<ShapeColor> Without(IReadOnlyList<ShapeColor> colors, int index)
    {
        if (index < 0 || index >= colors.Count)
        {
            return colors;
        }

        var kept = new List<ShapeColor>(colors);
        kept.RemoveAt(index);
        return kept.Count == 0 ? None : kept.ToArray();
    }

    private static bool HasRgb(IReadOnlyList<ShapeColor> colors, ShapeColor color)
    {
        foreach (var other in colors)
        {
            if (other.R == color.R && other.G == color.G && other.B == color.B)
            {
                return true;
            }
        }

        return false;
    }

    public static string ToHex(ShapeColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary><c>#RRGGBB</c> in either case, and nothing else: no alpha, no shorthand, no spaces.</summary>
    public static bool TryParseHex(string text, out ShapeColor color)
    {
        color = default;
        if (text.Length != 7 || text[0] != '#' ||
            !byte.TryParse(text.Substring(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(text.Substring(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(text.Substring(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new ShapeColor(r, g, b);
        return true;
    }
}
