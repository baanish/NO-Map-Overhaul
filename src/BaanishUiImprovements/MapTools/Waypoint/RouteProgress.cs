using System;
using System.Numerics;

namespace BaanishUiImprovements.MapTools.Waypoint;

/// <summary>
/// Which waypoint of the store's route is next. Flying the route isn't a drawing change, so it stays out of the
/// store's history: undo takes back waypoints, never progress. Progress belongs to one route by id, which a
/// <see cref="ShapeStore.Replace"/> keeps, so it survives adding waypoints and undoing an erase, and a new route
/// starts from its first waypoint.
/// </summary>
public sealed class RouteProgress
{
    private int _storeVersion = -1;
    private int _routeId;

    /// <summary>The route in the store, or null when there is none (never drawn, erased, undone, or cleared).</summary>
    public WaypointRoute? Route { get; private set; }

    /// <summary>Index of the waypoint to fly to next; the route's count once every waypoint is flown.</summary>
    public int Next { get; private set; }

    public bool HasNext => Route != null && Next < Route.Count;

    /// <summary>
    /// A waypoint counts as reached within <paramref name="reach"/> meters, or once it's behind the aircraft and within
    /// <paramref name="passed"/> meters, so a wide miss still moves the route on. Horizontal distances only.
    /// Adapted from NOAutopilot's waypoint sequencing (MIT, (c) 2026 qwerty1423 and NOAutopilot contributors; see
    /// THIRD_PARTY_NOTICES.md), which tests "behind" against the aircraft's nose the same way.
    /// </summary>
    public static bool IsReached(Vector2 aircraft, Vector2 forward, Vector2 waypoint, float reach, float passed)
    {
        var offset = waypoint - aircraft;
        var distanceSquared = offset.LengthSquared();
        var behind = Vector2.Dot(forward, offset) < 0f;
        return distanceSquared < reach * reach || (behind && distanceSquared < passed * passed);
    }

    /// <summary>Finds the route again after any store change. Cheap when nothing changed, so call it before each use.</summary>
    public void Refresh(ShapeStore store)
    {
        if (store.Version == _storeVersion)
        {
            return;
        }

        _storeVersion = store.Version;
        Route = Find(store);
        if (Route == null)
        {
            return; // keep the progress for an undo that brings the route back
        }

        if (Route.Id != _routeId)
        {
            _routeId = Route.Id;
            Next = 0;
        }
        else
        {
            Next = Math.Min(Next, Route.Count);
        }
    }

    /// <summary>Moves on past the next waypoint if the aircraft has reached it. At most one waypoint per call.</summary>
    public bool Advance(IMapView view, Vector2 aircraft, Vector2 forward, float reach, float passed)
    {
        if (!HasNext)
        {
            return false;
        }

        view.TryResolve(Route![Next].Point, out var waypoint);
        if (!IsReached(aircraft, forward, waypoint, reach, passed))
        {
            return false;
        }

        Next++;
        return true;
    }

    public void Skip()
    {
        if (HasNext)
        {
            Next++;
        }
    }

    public void Restart() => Next = 0;

    /// <summary>A new mission: the old route's id means nothing now.</summary>
    public void Forget()
    {
        _storeVersion = -1;
        _routeId = 0;
        Route = null;
        Next = 0;
    }

    /// <summary>The topmost route. The tool only starts a route when there is none, so there is at most one.</summary>
    private static WaypointRoute? Find(ShapeStore store)
    {
        var shapes = store.Shapes;
        for (var i = shapes.Count - 1; i >= 0; i--)
        {
            if (shapes[i] is WaypointRoute route)
            {
                return route;
            }
        }

        return null;
    }
}
