using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace BaanishUiImprovements.MapTools.Waypoint;

/// <summary>One stop on the route. <see cref="Elevation"/> is the ground height under a fixed point, for its 3D label; an anchored point uses its unit's altitude instead.</summary>
public readonly struct RouteWaypoint
{
    public RouteWaypoint(MapPoint point, float elevation)
    {
        Point = point;
        Elevation = elevation;
    }

    public MapPoint Point { get; }

    public float Elevation { get; }
}

/// <summary>
/// The planned route: numbered waypoints in flying order, joined by one line, each on the game's own waypoint marker.
/// The whole route is one shape, so adding a waypoint is a <see cref="ShapeStore.Replace"/> that undo takes back, and
/// the eraser takes the whole route. Which waypoints are already flown isn't stored here: <see cref="RouteProgress"/>
/// keeps that, since undo must not un-fly a waypoint.
/// </summary>
public sealed class WaypointRoute : MapShape
{
    private static readonly List<string> NumberTexts = new();
    private static readonly List<string> LostTexts = new();

    private readonly RouteWaypoint[] _waypoints;
    private readonly List<Vector2> _line;
    private readonly bool[] _lost;

    public WaypointRoute(ShapeColor color, RouteWaypoint first)
        : this(color, new[] { first })
    {
    }

    private WaypointRoute(ShapeColor color, RouteWaypoint[] waypoints)
        : base(color)
    {
        _waypoints = waypoints;
        _line = new List<Vector2>(waypoints.Length);
        _lost = new bool[waypoints.Length];
    }

    public int Count => _waypoints.Length;

    public RouteWaypoint this[int index] => _waypoints[index];

    /// <summary>A copy with one more waypoint at the end, in the same colour.</summary>
    public WaypointRoute With(RouteWaypoint waypoint)
    {
        var waypoints = new RouteWaypoint[_waypoints.Length + 1];
        _waypoints.CopyTo(waypoints, 0);
        waypoints[_waypoints.Length] = waypoint;
        return new WaypointRoute(Color, waypoints);
    }

    /// <summary>
    /// The number sits on a point beside the marker far enough out that a two-digit number clears it in any direction,
    /// since the heading-up minimap turns that point around the marker while the text stays upright.
    /// </summary>
    public override void Draw(IMapCanvas canvas)
    {
        _line.Clear();
        for (var i = 0; i < _waypoints.Length; i++)
        {
            _lost[i] = !canvas.TryResolve(_waypoints[i].Point, out var position);
            _line.Add(position);
        }

        if (_line.Count > 1)
        {
            canvas.Polyline(_line, Color);
        }

        var offset = new Vector2(0f, (MapCanvasMetrics.MarkerRadius + canvas.TextSize * MapCanvasMetrics.CharWidth) * canvas.MetersPerIconUnit);
        for (var i = 0; i < _line.Count; i++)
        {
            canvas.Marker(_line[i], Color);
            canvas.Label(_line[i] + offset, Number(i, _lost[i]), Color);
        }
    }

    /// <summary>"3", or "3 lost" for a waypoint on a unit that's destroyed or no longer tracked. Built the first time a number is drawn, then shared by every route.</summary>
    private static string Number(int index, bool lost)
    {
        var texts = lost ? LostTexts : NumberTexts;
        while (texts.Count <= index)
        {
            var number = (texts.Count + 1).ToString(CultureInfo.InvariantCulture);
            texts.Add(lost ? number + " lost" : number);
        }

        return texts[index];
    }
}
