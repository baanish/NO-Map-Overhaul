using System.Collections.Generic;
using System.Globalization;
using FlatVector = System.Numerics.Vector2;
using WorldVector = System.Numerics.Vector3;

namespace NoMapOverhaul.MapTools.Waypoint;

/// <summary>
/// Plans a route: each click on the map adds a numbered waypoint to the end, on the unit under the cursor if there is
/// one. While flying, the map joins the aircraft to the next waypoint, and the 3D view labels the next two with
/// distance and bearing. A waypoint counts as reached by <see cref="RouteProgress.IsReached"/>, and the route moves on.
/// The route never reads NOAutopilot's: that mod plans with right clicks and this tool with left clicks, so each
/// keeps its own route and labels.
/// </summary>
public sealed class WaypointTool : MapTool
{
    /// <summary>Below this radar altitude the aircraft is on the ground, where taxiing past a waypoint shouldn't fly it.</summary>
    private const float AirborneRadarAlt = 1f;

    /// <summary>Waypoints ahead that get a label in the 3D view.</summary>
    private const int LabelledAhead = 2;

    private const string StartStatus = "Click the map to start a route.";
    private const string FlownStatus = "Route flown. Click to add a waypoint, or Restart.";
    private const string FullFlownStatus = "Route flown, and full. Restart, or erase it to plan another.";

    private static readonly IReadOnlyList<string> RouteOptions = new[] { "Skip", "Restart" };

    private readonly ModSettings _settings;
    private readonly RouteProgress _progress = new();
    private readonly WaypointCallout[] _callouts = { new(), new() };
    private (int Count, int Next) _statusKey = (-1, -1);
    private string _status = string.Empty;

    internal WaypointTool(IMapToolContext context, ModSettings settings)
        : base(context) =>
        _settings = settings;

    public override string Name => "Waypoint";

    public override string Status
    {
        get
        {
            _progress.Refresh(Context.Shapes);
            if (_progress.Route is not { } route)
            {
                return Context.Shapes.IsFull ? string.Empty : StartStatus; // the menu explains a full store
            }

            var key = (route.Count, _progress.Next);
            if (key != _statusKey)
            {
                _statusKey = key;
                _status = _progress.HasNext
                    ? "Next: WP" + (_progress.Next + 1).ToString(CultureInfo.InvariantCulture) + " of " +
                      route.Count.ToString(CultureInfo.InvariantCulture) + (route.IsFull ? ". The route is full." : ". Click to add a waypoint.")
                    : route.IsFull ? FullFlownStatus : FlownStatus;
            }

            return _status;
        }
    }

    /// <summary>A full route takes no more clicks.</summary>
    public override bool Warning
    {
        get
        {
            _progress.Refresh(Context.Shapes);
            return _progress.Route is { IsFull: true };
        }
    }

    public override IReadOnlyList<string> Options => RouteOptions;

    public override void OnMissionStart() => _progress.Forget();

    public override void OnOption(int index)
    {
        if (index == 0)
        {
            _progress.Skip();
        }
        else
        {
            _progress.Restart();
        }
    }

    public override void OnClick(MapPointer pointer)
    {
        _progress.Refresh(Context.Shapes);
        if (_progress.Route is { IsFull: true })
        {
            return;
        }

        var point = pointer.Point;
        if (point.IsAnchored && point.UnitId == Context.OwnAircraft?.UnitId)
        {
            point = new MapPoint(pointer.Position); // a waypoint riding on the aircraft would be reached at once
        }

        var waypoint = new RouteWaypoint(point, point.IsAnchored ? 0f : Context.GroundElevation(point.Position));
        if (_progress.Route is { } route)
        {
            Context.Shapes.Replace(route, route.With(waypoint));
        }
        else
        {
            Context.Shapes.Add(new WaypointRoute(Context.Color, waypoint));
        }
    }

    /// <summary>The leg being flown, from the aircraft to the next waypoint.</summary>
    public override void DrawOverlay(IMapCanvas canvas)
    {
        _progress.Refresh(Context.Shapes);
        if (_progress.Route is not { } route || !_progress.HasNext || canvas.OwnAircraft is not { } aircraft)
        {
            return;
        }

        canvas.TryResolve(route[_progress.Next].Point, out var next);
        canvas.Line(aircraft.Position, next, route.Color);
    }

    public override void OnFrame(IWorldLabels labels)
    {
        if (!TryGetFlight(out var aircraft, out var forward, out var airborne))
        {
            return;
        }

        _progress.Refresh(Context.Shapes);
        if (airborne)
        {
            _progress.Advance(Context, aircraft, forward, _settings.MapToolWaypointReachKm.Value * 1000f,
                _settings.MapToolWaypointPassedKm.Value * 1000f);
        }

        if (_progress.Route is not { } route)
        {
            return;
        }

        for (var i = 0; i < LabelledAhead && _progress.Next + i < route.Count; i++)
        {
            var waypoint = route[_progress.Next + i];
            var found = WorldPosition(waypoint, out var position);
            var text = _callouts[i].Text(_progress.Next + i + 1, aircraft, new FlatVector(position.X, position.Z), Context.Units, !found);
            labels.Add(position, text, route.Color);
        }
    }

    /// <summary>
    /// A fixed waypoint's ground height was found when it was placed; a unit's known altitude comes with its position.
    /// False for a waypoint on a lost unit.
    /// </summary>
    private bool WorldPosition(RouteWaypoint waypoint, out WorldVector position)
    {
        var point = waypoint.Point;
        if (point.IsAnchored)
        {
            return Context.TryResolveWorld(point, out position);
        }

        position = new WorldVector(point.Position.X, waypoint.Elevation, point.Position.Y);
        return true;
    }

    private static bool TryGetFlight(out FlatVector position, out FlatVector forward, out bool airborne)
    {
        var hud = SceneSingleton<CombatHUD>.i;
        var aircraft = hud != null ? hud.aircraft : null;
        if (aircraft == null || aircraft.disabled)
        {
            position = forward = default;
            airborne = false;
            return false;
        }

        var global = aircraft.GlobalPosition();
        var nose = aircraft.transform.forward;
        position = new FlatVector(global.x, global.z);
        forward = new FlatVector(nose.x, nose.z);
        airborne = aircraft.radarAlt >= AirborneRadarAlt;
        return true;
    }
}
