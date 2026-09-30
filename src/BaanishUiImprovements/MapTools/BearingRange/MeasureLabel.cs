using System.Numerics;

namespace BaanishUiImprovements.MapTools.BearingRange;

/// <summary>
/// A measurement's text, kept until what it shows changes: a live shape draws ten times a second and its 3D label every
/// frame, so a string is built only when the rounded bearing, the shown distance, the unit, or the lost state changes.
/// Used by the bearing/range arrows and the circles.
/// </summary>
public sealed class MeasureLabel
{
    /// <summary>Added when an anchored unit is destroyed or no longer tracked, and the drawing stays where it was last seen.</summary>
    public const string LostLine = "\nlost";

    /// <summary>Set in radius keys only. A rounded bearing is at most 359, so shifted left by one it stays below this bit.</summary>
    private const long RadiusFlag = 1L << 10;

    private long _key = -1;
    private string _text = string.Empty;

    /// <summary>"045° 4.2nm": degrees true and distance from one point to the other.</summary>
    public string BearingRange(Vector2 from, Vector2 to, DistanceUnit unit, bool lost)
    {
        var meters = Vector2.Distance(from, to);
        var bearing = NavFormat.RoundBearing(NavFormat.BearingDegrees(from, to));
        var key = (NavFormat.DistanceKey(meters, unit) << 11) | ((long)bearing << 1) | (lost ? 1L : 0L);
        if (key != _key)
        {
            _key = key;
            _text = NavFormat.Bearing(bearing) + " " + NavFormat.Distance(meters, unit) + (lost ? LostLine : string.Empty);
        }

        return _text;
    }

    /// <summary>"10nm": a circle's radius.</summary>
    public string Radius(float meters, DistanceUnit unit, bool lost)
    {
        var key = (NavFormat.DistanceKey(meters, unit) << 11) | RadiusFlag | (lost ? 1L : 0L);
        if (key != _key)
        {
            _key = key;
            _text = NavFormat.Distance(meters, unit) + (lost ? LostLine : string.Empty);
        }

        return _text;
    }
}
