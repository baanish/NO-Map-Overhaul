using System.Numerics;

namespace NoMapOverhaul.MapTools.BearingRange;

/// <summary>
/// A measurement's text, kept until what it shows changes: a live shape draws ten times a second and its 3D label every
/// frame, so a string is built only when the rounded bearing, the shown distance or altitude, the unit, or the lost
/// state changes. Used by the bearing/range arrows and the circles.
/// </summary>
public sealed class MeasureLabel
{
    /// <summary>Added when an anchored unit is destroyed or no longer tracked, and the drawing stays where it was last seen.</summary>
    public const string LostLine = "\nlost";

    /// <summary>Set in radius keys only. A rounded bearing is at most 359, so shifted left by one it stays below this bit.</summary>
    private const long RadiusFlag = 1L << 10;

    /// <summary>The altitude key of text with no altitude. <see cref="NavFormat.AltitudeKey"/> is never negative.</summary>
    private const long NoAltitude = -1;

    private long _key = -1;
    private long _altitudeKey = NoAltitude;
    private string _text = string.Empty;

    /// <summary>
    /// "045° 4.2nm 9.8k ft" from one point to the other, in global meters (X east, Y up, Z north): degrees true across
    /// the map, the straight-line range through the air, and the far point's altitude when <paramref name="toOnUnit"/>.
    /// </summary>
    public string BearingRange(Vector3 from, Vector3 to, bool toOnUnit, DistanceUnit unit, bool lost)
    {
        var meters = Vector3.Distance(from, to);
        var bearing = NavFormat.RoundBearing(NavFormat.BearingDegrees(new Vector2(from.X, from.Z), new Vector2(to.X, to.Z)));
        var key = (NavFormat.DistanceKey(meters, unit) << 11) | ((long)bearing << 1) | (lost ? 1L : 0L);
        var altitudeKey = toOnUnit ? NavFormat.AltitudeKey(to.Y, unit) : NoAltitude;
        if (key != _key || altitudeKey != _altitudeKey)
        {
            _key = key;
            _altitudeKey = altitudeKey;
            _text = NavFormat.Bearing(bearing) + " " + NavFormat.Distance(meters, unit) +
                    (toOnUnit ? " " + NavFormat.Altitude(to.Y, unit) : string.Empty) + (lost ? LostLine : string.Empty);
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
