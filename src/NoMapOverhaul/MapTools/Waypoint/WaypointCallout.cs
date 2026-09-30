using System.Globalization;
using System.Numerics;

namespace NoMapOverhaul.MapTools.Waypoint;

/// <summary>
/// The text of one waypoint's label in the 3D view, "WP2 4.2nm 045°": its number, then the horizontal distance and
/// the bearing from the aircraft. Built again only when the shown text would change, since it's asked for every frame.
/// </summary>
public sealed class WaypointCallout
{
    private string _text = string.Empty;
    private int _number = -1;
    private long _distanceKey = -1;
    private int _bearing = -1;
    private bool _lost;

    /// <param name="lost">The waypoint is on a unit that's destroyed or no longer tracked, and stays where it was last known.</param>
    public string Text(int number, Vector2 aircraft, Vector2 waypoint, DistanceUnit unit, bool lost)
    {
        var meters = Vector2.Distance(aircraft, waypoint);
        var degrees = NavFormat.BearingDegrees(aircraft, waypoint);
        var distanceKey = NavFormat.DistanceKey(meters, unit);
        var bearing = NavFormat.RoundBearing(degrees);
        if (number != _number || distanceKey != _distanceKey || bearing != _bearing || lost != _lost)
        {
            _number = number;
            _distanceKey = distanceKey;
            _bearing = bearing;
            _lost = lost;
            _text = "WP" + number.ToString(CultureInfo.InvariantCulture) + " " + NavFormat.Distance(meters, unit) + " " + NavFormat.Bearing(degrees) +
                    (lost ? " lost" : string.Empty);
        }

        return _text;
    }
}
