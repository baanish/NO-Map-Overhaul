using NoMapOverhaul.MapTools;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>The custom colour picker's hue, saturation, and value maths, and where a press lands on its wheel.</summary>
internal static class ColorWheelTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("the six primary and secondary hues sit a sixth of the wheel apart", PrimaryHues),
        ("saturation fades to white and value to black", SaturationAndValue),
        ("every palette colour and a byte sweep survive the trip through HSV", RgbRoundTrips),
        ("a press lands on the wheel inside its rim and nowhere past it", WheelContainsItsDisc),
        ("a point picks the hue of its direction and the saturation of its distance", PickReadsDirectionAndDistance),
        ("a drag past the rim keeps the rim's colour in its direction", DragPastRimClamps),
        ("the marker's position picks back its own hue and saturation", PositionRoundTrips),
        ("the wheel texture is opaque inside, clear outside, and white at the centre", TextureTexels),
    };

    private static void PrimaryHues()
    {
        ExpectColor(ColorWheel.ToRgb(0f, 1f, 1f), 255, 0, 0, "hue 0");
        ExpectColor(ColorWheel.ToRgb(1f / 6f, 1f, 1f), 255, 255, 0, "hue 1/6");
        ExpectColor(ColorWheel.ToRgb(2f / 6f, 1f, 1f), 0, 255, 0, "hue 2/6");
        ExpectColor(ColorWheel.ToRgb(3f / 6f, 1f, 1f), 0, 255, 255, "hue 3/6");
        ExpectColor(ColorWheel.ToRgb(4f / 6f, 1f, 1f), 0, 0, 255, "hue 4/6");
        ExpectColor(ColorWheel.ToRgb(5f / 6f, 1f, 1f), 255, 0, 255, "hue 5/6");
        ExpectColor(ColorWheel.ToRgb(1f, 1f, 1f), 255, 0, 0, "hue 1, round to red again");
        ExpectColor(ColorWheel.ToRgb(0.9999999f, 1f, 1f), 255, 0, 0, "just under hue 1");
    }

    private static void SaturationAndValue()
    {
        ExpectColor(ColorWheel.ToRgb(0.3f, 0f, 1f), 255, 255, 255, "no saturation");
        ExpectColor(ColorWheel.ToRgb(0.3f, 1f, 0f), 0, 0, 0, "no value");
        ExpectColor(ColorWheel.ToRgb(0f, 0.5f, 1f), 255, 128, 128, "half-saturated red");
        ExpectColor(ColorWheel.ToRgb(0f, 1f, 0.5f), 128, 0, 0, "half-bright red");
        ExpectColor(ColorWheel.ToRgb(0f, 2f, -1f), 0, 0, 0, "out of range clamps");
        var (hue, saturation, value) = ColorWheel.FromRgb(new ShapeColor(128, 128, 128));
        Expect(hue == 0f && saturation == 0f && MathF.Abs(value - 128f / 255f) < 1e-6f, $"expected a grey with no hue or saturation, got {hue} {saturation} {value}");
    }

    private static void RgbRoundTrips()
    {
        foreach (var color in ShapeColor.Palette)
        {
            ExpectRoundTrip(color);
        }

        for (var r = 0; r < 256; r += 15)
        {
            for (var g = 0; g < 256; g += 15)
            {
                for (var b = 0; b < 256; b += 15)
                {
                    ExpectRoundTrip(new ShapeColor((byte)r, (byte)g, (byte)b));
                }
            }
        }
    }

    private static void WheelContainsItsDisc()
    {
        Expect(ColorWheel.Contains(0f, 0f), "expected the centre on the wheel");
        Expect(ColorWheel.Contains(1f, 0f), "expected the rim on the wheel");
        Expect(ColorWheel.Contains(0.7f, -0.7f), "expected a point just inside the rim on the wheel");
        Expect(!ColorWheel.Contains(0.72f, 0.72f), "expected the square's corner off the wheel");
        Expect(!ColorWheel.Contains(-1.01f, 0f), "expected a point past the rim off the wheel");
    }

    private static void PickReadsDirectionAndDistance()
    {
        ExpectPick(ColorWheel.Pick(1f, 0f), 0f, 1f, "3 o'clock");
        ExpectPick(ColorWheel.Pick(0f, 0.5f), 0.25f, 0.5f, "12 o'clock, halfway out");
        ExpectPick(ColorWheel.Pick(-0.25f, 0f), 0.5f, 0.25f, "9 o'clock");
        ExpectPick(ColorWheel.Pick(0f, -1f), 0.75f, 1f, "6 o'clock");
        ExpectPick(ColorWheel.Pick(0.5f, -0.0001f), 1f - 0.0001f / 0.5f / (2f * MathF.PI), 0.5f, "just below 3 o'clock, hue just under 1");
        ExpectPick(ColorWheel.Pick(0f, 0f), 0f, 0f, "centre");
        var (hue, saturation) = ColorWheel.Pick(MathF.Cos(MathF.PI / 3f), MathF.Sin(MathF.PI / 3f));
        ExpectColor(ColorWheel.ToRgb(hue, saturation, 1f), 255, 255, 0, "a sixth of the way round");
    }

    private static void DragPastRimClamps()
    {
        ExpectPick(ColorWheel.Pick(3f, 0f), 0f, 1f, "far right");
        ExpectPick(ColorWheel.Pick(-2f, -2f), 0.625f, 1f, "far lower left");
    }

    private static void PositionRoundTrips()
    {
        foreach (var (hue, saturation) in new[] { (0f, 0f), (0.1f, 0.3f), (0.5f, 1f), (0.9f, 0.75f), (0.999f, 0.5f) })
        {
            var (x, y) = ColorWheel.Position(hue, saturation);
            Expect(ColorWheel.Contains(x, y), $"expected hue {hue} saturation {saturation} on the wheel");
            var picked = ColorWheel.Pick(x, y);
            ExpectNear(picked.Saturation, saturation, $"saturation of hue {hue}");
            if (saturation > 0f)
            {
                ExpectNear(picked.Hue, hue, $"hue {hue}");
            }
        }
    }

    private static void TextureTexels()
    {
        const int size = 64;
        var (center, centerAlpha) = ColorWheel.Texel(size / 2, size / 2, size);
        Expect(centerAlpha == 255, $"expected the centre opaque, got {centerAlpha}");
        Expect(center.R > 245 && center.G > 245 && center.B > 245,$"expected the centre near white, got {center.R} {center.G} {center.B}");
        Expect(ColorWheel.Texel(0, 0, size).Alpha == 0, "expected the corner clear");
        var (rim, rimAlpha) = ColorWheel.Texel(size - 1, size / 2, size);
        Expect(rimAlpha is > 0 and < 255, $"expected the rim's last texel part clear, got {rimAlpha}");
        Expect(rim.R == 255 && rim.B < 10, $"expected the right rim red, got {rim.R} {rim.G} {rim.B}");
        var (top, _) = ColorWheel.Texel(size / 2, size - 2, size);
        Expect(top.G == 255 && top.R < 160 && top.B < 20,$"expected the top rim yellow-green, row 0 being the bottom, got {top.R} {top.G} {top.B}");
    }

    private static void ExpectRoundTrip(ShapeColor color)
    {
        var (hue, saturation, value) = ColorWheel.FromRgb(color);
        Expect(hue is >= 0f and < 1f && saturation is >= 0f and <= 1f && value is >= 0f and <= 1f, $"expected HSV in range for {Hex(color)}");
        var back = ColorWheel.ToRgb(hue, saturation, value);
        Expect(back == color, $"expected {Hex(color)} back, got {Hex(back)}");
    }

    private static void ExpectColor(ShapeColor color, int r, int g, int b, string what) =>
        Expect(color.R == r && color.G == g && color.B == b && color.A == 255, $"expected {what} {r} {g} {b}, got {color.R} {color.G} {color.B} {color.A}");

    private static void ExpectPick((float Hue, float Saturation) picked, float hue, float saturation, string what)
    {
        ExpectNear(picked.Hue, hue, what + " hue");
        ExpectNear(picked.Saturation, saturation, what + " saturation");
    }

    private static void ExpectNear(float actual, float expected, string what) =>
        Expect(MathF.Abs(actual - expected) < 1e-4f, $"expected {what} {expected}, got {actual}");

    private static string Hex(ShapeColor color) => CustomColors.ToHex(color);
}
