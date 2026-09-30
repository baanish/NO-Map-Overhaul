using System;
using System.Collections.Generic;

namespace BaanishUiImprovements.MapTools;

/// <summary>An RGBA colour in bytes, so shapes stay free of Unity types.</summary>
public readonly struct ShapeColor : IEquatable<ShapeColor>
{
    public ShapeColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    /// <summary>
    /// The menu's swatches: green (the default), white, orange, red, magenta, cyan. All bright, to read over the dark map.
    /// The green is the HUD callout's #33FF33 rather than the game's pure #00FF00 HUD default: the map is a grey texture
    /// tinted pure green, so the touch of red and blue sets a line apart from the map's own roads and coastlines.
    /// </summary>
    public static IReadOnlyList<ShapeColor> Palette { get; } = new[]
    {
        new ShapeColor(51, 255, 51),
        new ShapeColor(255, 255, 255),
        new ShapeColor(255, 140, 26),
        new ShapeColor(255, 64, 64),
        new ShapeColor(255, 102, 255),
        new ShapeColor(64, 217, 255),
    };

    public bool Equals(ShapeColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) => obj is ShapeColor other && Equals(other);

    public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

    public static bool operator ==(ShapeColor left, ShapeColor right) => left.Equals(right);

    public static bool operator !=(ShapeColor left, ShapeColor right) => !left.Equals(right);
}
