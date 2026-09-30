using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;
using WorldVector = System.Numerics.Vector3;

namespace BaanishUiImprovements.MapTools.Waypoint;

/// <summary>
/// Plans a route: each click on the map adds a numbered waypoint to the end, on the unit under the cursor if there is
/// one. While flying, the map joins the aircraft to the next waypoint, and the 3D view labels the next two with
/// distance and bearing. A waypoint counts as reached by <see cref="RouteProgress.IsReached"/>, and the route moves on.
/// With NOAutopilot loaded, its right-click route is the only route: clicks here do nothing and the labels follow its
/// route, so the two mods never point different ways.
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
    private const string NoAutopilotStatus = "NOAutopilot owns the route: right-click the map to plan it.";

    /// <summary>NOAutopilot's default route colour, for the labels on its route.</summary>
    private static readonly ShapeColor NoAutopilotColor = new(0, 255, 255);

    private static readonly IReadOnlyList<string> RouteOptions = new[] { "Skip", "Restart" };

    private readonly ModSettings _settings;
    private readonly RouteProgress _progress = new();
    private readonly NoAutopilotRoute _noAutopilot = new();
    private readonly WaypointCallout[] _callouts = { new(), new() };
    private readonly FlatVector[] _groundedAt = { new(float.NaN), new(float.NaN) };
    private readonly float[] _groundElevation = new float[LabelledAhead];
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
            if (_noAutopilot.Queue != null)
            {
                return NoAutopilotStatus;
            }

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
            return _noAutopilot.Queue == null && _progress.Route is { IsFull: true };
        }
    }

    public override IReadOnlyList<string> Options => _noAutopilot.Queue != null ? Array.Empty<string>() : RouteOptions;

    public override void OnMissionStart()
    {
        _progress.Forget();
        _noAutopilot.Reload();
        Array.Fill(_groundedAt, new FlatVector(float.NaN)); // a new map has new ground under the same coordinates
    }

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
        if (_noAutopilot.Queue != null)
        {
            return;
        }

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

    /// <summary>The leg being flown, from the aircraft to the next waypoint. NOAutopilot draws its own.</summary>
    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_noAutopilot.Queue != null)
        {
            return;
        }

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

        if (_noAutopilot.Queue is { } queue)
        {
            LabelNoAutopilotRoute(labels, queue, aircraft);
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
            var position = WorldPosition(waypoint);
            var text = _callouts[i].Text(_progress.Next + i + 1, aircraft, new FlatVector(position.X, position.Z), Context.Units);
            labels.Add(position, text, route.Color);
        }
    }

    /// <summary>NOAutopilot's queue holds only the waypoints still to fly, so its next is always "WP1".</summary>
    private void LabelNoAutopilotRoute(IWorldLabels labels, IReadOnlyList<Vector3> queue, FlatVector aircraft)
    {
        for (var i = 0; i < LabelledAhead && i < queue.Count; i++)
        {
            var point = queue[i];
            var flat = new FlatVector(point.x, point.z);
            var position = new WorldVector(point.x, Mathf.Max(point.y, GroundElevation(i, flat)), point.z);
            labels.Add(position, _callouts[i].Text(i + 1, aircraft, flat, Context.Units), NoAutopilotColor);
        }
    }

    /// <summary>A fixed waypoint's ground height was found when it was placed; a unit's altitude comes with its position.</summary>
    private WorldVector WorldPosition(RouteWaypoint waypoint)
    {
        var point = waypoint.Point;
        if (point.IsAnchored)
        {
            Context.TryResolveWorld(point, out var unit);
            return unit;
        }

        return new WorldVector(point.Position.X, waypoint.Elevation, point.Position.Y);
    }

    /// <summary>NOAutopilot's waypoints come at sea level, so each label slot casts a ray only when its waypoint moves.</summary>
    private float GroundElevation(int slot, FlatVector position)
    {
        if (_groundedAt[slot] != position)
        {
            _groundedAt[slot] = position;
            _groundElevation[slot] = Context.GroundElevation(position);
        }

        return _groundElevation[slot];
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
