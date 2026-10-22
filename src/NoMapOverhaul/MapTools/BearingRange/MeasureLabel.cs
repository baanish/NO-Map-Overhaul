using System;
using System.Numerics;

namespace NoMapOverhaul.MapTools.BearingRange;

/// <summary>
/// A measurement's text, kept until what it shows changes: a live shape draws ten times a second and its 3D label every
/// frame, so a string is built only when the rounded bearing, the shown distance or altitude, the unit, or the lost
/// state changes. The distance and altitude parts are kept too, so a turn alone formats neither again. Used by the
/// bearing/range arrows and the circles.
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

    /// <summary>The last distance part and its <see cref="NavFormat.DistanceKey"/>, which is never negative and carries the unit.</summary>
    private long _distanceKey = -1;
    private string _distanceText = string.Empty;

    /// <summary>The last altitude part, kept while the text drops it, and its <see cref="NavFormat.AltitudeKey"/>.</summary>
    private long _altitudePartKey = NoAltitude;
    private string _altitudeText = string.Empty;

    /// <summary>
    /// "045° 4.2nm 9.8k ft" from one point to the other, in global meters (X east, Y up, Z north): degrees true across
    /// the map, the straight-line range through the air, and the far point's altitude when <paramref name="toOnUnit"/>.
    /// </summary>
    public string BearingRange(Vector3 from, Vector3 to, bool toOnUnit, DistanceUnit unit, bool lost)
    {
        var meters = Vector3.Distance(from, to);
        var bearing = NavFormat.RoundBearing(NavFormat.BearingDegrees(new Vector2(from.X, from.Z), new Vector2(to.X, to.Z)));
        var distanceKey = NavFormat.DistanceKey(meters, unit);
        var key = (distanceKey << 11) | ((long)bearing << 1) | (lost ? 1L : 0L);
        var altitudeKey = toOnUnit ? NavFormat.AltitudeKey(to.Y, unit) : NoAltitude;
        if (key != _key || altitudeKey != _altitudeKey)
        {
            _key = key;
            _altitudeKey = altitudeKey;
            _text = Join(NavFormat.Bearing(bearing), DistanceText(meters, unit, distanceKey),
                         toOnUnit ? AltitudeText(to.Y, unit, altitudeKey) : null, lost);
        }

        return _text;
    }

    /// <summary>"10nm": a circle's radius.</summary>
    public string Radius(float meters, DistanceUnit unit, bool lost)
    {
        var distanceKey = NavFormat.DistanceKey(meters, unit);
        var key = (distanceKey << 11) | RadiusFlag | (lost ? 1L : 0L);
        if (key != _key)
        {
            _key = key;
            var distance = DistanceText(meters, unit, distanceKey);
            _text = lost ? distance + LostLine : distance;
        }

        return _text;
    }

    private string DistanceText(float meters, DistanceUnit unit, long distanceKey)
    {
        if (distanceKey != _distanceKey)
        {
            _distanceKey = distanceKey;
            _distanceText = NavFormat.Distance(meters, unit);
        }

        return _distanceText;
    }

    private string AltitudeText(float meters, DistanceUnit unit, long altitudeKey)
    {
        if (altitudeKey != _altitudePartKey)
        {
            _altitudePartKey = altitudeKey;
            _altitudeText = NavFormat.Altitude(meters, unit);
        }

        return _altitudeText;
    }

    /// <summary>"bearing distance[ altitude][\nlost]" as one string, with no array or partial strings on the way.</summary>
    private static string Join(string bearing, string distance, string? altitude, bool lost)
    {
        var length = bearing.Length + 1 + distance.Length + (altitude == null ? 0 : 1 + altitude.Length) + (lost ? LostLine.Length : 0);
        Span<char> chars = stackalloc char[length];
        var at = Put(chars, 0, bearing);
        chars[at++] = ' ';
        at = Put(chars, at, distance);
        if (altitude != null)
        {
            chars[at++] = ' ';
            at = Put(chars, at, altitude);
        }

        if (lost)
        {
            Put(chars, at, LostLine);
        }

        return new string(chars);
    }

    private static int Put(Span<char> chars, int at, string part)
    {
        part.AsSpan().CopyTo(chars.Slice(at));
        return at + part.Length;
    }
}
