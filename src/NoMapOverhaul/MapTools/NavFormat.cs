using System;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;

namespace NoMapOverhaul.MapTools;

public enum DistanceUnit
{
    NauticalMiles,
    Kilometres,
    StatuteMiles,
}

/// <summary>The DistanceUnits setting: follow the game's unit system, or always use one unit.</summary>
public enum UnitsSetting
{
    /// <summary>F1's dropdown shows the description in place of the bare name.</summary>
    [Description("Game setting")]
    Game,
    NauticalMiles,
    Kilometres,
    StatuteMiles,
}

/// <summary>
/// Bearings, distances, and altitudes as the map tools show them, in the style of the game's own readouts ("045°",
/// "12.3nm"). Bearings are degrees true, clockwise from map north, three digits from 000 to 359. Distances get one
/// decimal under 10 and whole numbers from 10 up; kilometres show meters under 1 km, as the game does. Text is
/// culture-invariant.
/// </summary>
public static class NavFormat
{
    public const float MetersPerNauticalMile = 1852f;
    public const float MetersPerStatuteMile = 1609.344f;
    public const float MetersPerFoot = 0.3048f;

    private static readonly string[] BearingTexts = BuildBearingTexts();

    private enum Precision
    {
        Meters,
        Tenths,
        Whole,
    }

    /// <summary>The game's imperial readouts give distance in nautical miles, so imperial means nautical miles here too.</summary>
    public static DistanceUnit Resolve(UnitsSetting setting, bool gameIsMetric) => setting switch
    {
        UnitsSetting.NauticalMiles => DistanceUnit.NauticalMiles,
        UnitsSetting.Kilometres => DistanceUnit.Kilometres,
        UnitsSetting.StatuteMiles => DistanceUnit.StatuteMiles,
        _ => gameIsMetric ? DistanceUnit.Kilometres : DistanceUnit.NauticalMiles,
    };

    /// <summary>Degrees true from one map point to another, in [0, 360).</summary>
    public static float BearingDegrees(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var degrees = MathF.Atan2(delta.X, delta.Y) * (180f / MathF.PI);
        return degrees < 0f ? degrees + 360f : degrees;
    }

    /// <summary>The whole degree a bearing shows as, 0 to 359: 359.6 shows as 000.</summary>
    public static int RoundBearing(float degrees)
    {
        if (float.IsNaN(degrees) || float.IsInfinity(degrees))
        {
            return 0;
        }

        var whole = (int)MathF.Round(degrees % 360f, MidpointRounding.AwayFromZero) % 360;
        return whole < 0 ? whole + 360 : whole;
    }

    /// <summary>"045°". Allocation-free: each bearing's text is built once.</summary>
    public static string Bearing(float degrees) => BearingTexts[RoundBearing(degrees)];

    /// <summary>"4.2nm", "12km", "850m". Allocates, so text shown every frame should re-format only when <see cref="DistanceKey"/> changes.</summary>
    public static string Distance(float meters, DistanceUnit unit)
    {
        var (value, precision) = Display(meters, unit);
        var invariant = CultureInfo.InvariantCulture;
        return precision switch
        {
            Precision.Meters => value.ToString(invariant) + "m",
            Precision.Tenths => (value / 10).ToString(invariant) + "." + (value % 10).ToString(invariant) + Suffix(unit),
            _ => value.ToString(invariant) + Suffix(unit),
        };
    }

    /// <summary>Equal for two distances that show the same text, so a label refreshed every frame can skip re-formatting.</summary>
    public static long DistanceKey(float meters, DistanceUnit unit)
    {
        var (value, precision) = Display(meters, unit);
        return (value << 4) | ((long)precision << 2) | (long)unit;
    }

    /// <summary>
    /// "4.5k ft", "18k ft", "5500 m": an altitude above sea level, in feet beside miles and meters beside kilometres.
    /// Feet show in thousands, to the nearest 100 ft under 10,000 ft and the nearest 1,000 ft from there up; meters show
    /// to the nearest 100 m. Below sea level shows as 0. Allocates, so re-format only when <see cref="AltitudeKey"/> changes.
    /// </summary>
    public static string Altitude(float meters, DistanceUnit unit)
    {
        var (value, precision) = AltitudeDisplay(meters, unit);
        var invariant = CultureInfo.InvariantCulture;
        return precision switch
        {
            Precision.Meters => value.ToString(invariant) + " m",
            Precision.Tenths => (value / 10).ToString(invariant) + "." + (value % 10).ToString(invariant) + "k ft",
            _ => value.ToString(invariant) + "k ft",
        };
    }

    /// <summary>Equal for two altitudes that show the same text, and never negative.</summary>
    public static long AltitudeKey(float meters, DistanceUnit unit)
    {
        var (value, precision) = AltitudeDisplay(meters, unit);
        return (value << 2) | (long)precision;
    }

    private static (long Value, Precision Precision) AltitudeDisplay(float meters, DistanceUnit unit)
    {
        if (float.IsNaN(meters) || meters < 0f)
        {
            meters = 0f;
        }

        if (unit == DistanceUnit.Kilometres)
        {
            return ((long)Math.Round(meters / 100.0, MidpointRounding.AwayFromZero) * 100, Precision.Meters);
        }

        var feet = (double)meters / MetersPerFoot;
        var hundreds = (long)Math.Round(feet / 100, MidpointRounding.AwayFromZero);
        return hundreds < 100 ? (hundreds, Precision.Tenths) : ((long)Math.Round(feet / 1000, MidpointRounding.AwayFromZero), Precision.Whole);
    }

    private static (long Value, Precision Precision) Display(float meters, DistanceUnit unit)
    {
        if (float.IsNaN(meters) || meters < 0f)
        {
            meters = 0f;
        }

        if (unit == DistanceUnit.Kilometres && Math.Round(meters, MidpointRounding.AwayFromZero) < 1000)
        {
            return ((long)Math.Round(meters, MidpointRounding.AwayFromZero), Precision.Meters);
        }

        var inUnit = (double)meters / MetersPer(unit);
        var tenths = (long)Math.Round(inUnit * 10, MidpointRounding.AwayFromZero);
        return tenths < 100 ? (tenths, Precision.Tenths) : ((long)Math.Round(inUnit, MidpointRounding.AwayFromZero), Precision.Whole);
    }

    private static float MetersPer(DistanceUnit unit) => unit switch
    {
        DistanceUnit.Kilometres => 1000f,
        DistanceUnit.StatuteMiles => MetersPerStatuteMile,
        _ => MetersPerNauticalMile,
    };

    private static string Suffix(DistanceUnit unit) => unit switch
    {
        DistanceUnit.Kilometres => "km",
        DistanceUnit.StatuteMiles => "mi",
        _ => "nm",
    };

    private static string[] BuildBearingTexts()
    {
        var texts = new string[360];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = i.ToString("000", CultureInfo.InvariantCulture) + "°";
        }

        return texts;
    }
}
