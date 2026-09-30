using System.Numerics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.MapTools.Eraser;
using BaanishUiImprovements.MapTools.Waypoint;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// The waypoint route: when a waypoint counts as reached, how progress follows undo and the eraser, the numbered
/// drawing, and the 3D label text. Positions are meters, X east and Y north; the aircraft flies north unless a test
/// says otherwise, with NOAutopilot's default distances, 2.5 km to reach and 10 km once passed.
/// </summary>
internal static class WaypointTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a waypoint within reach is reached", WaypointWithinReachIsReached),
        ("a waypoint behind within the passed distance is reached", WaypointBehindIsPassed),
        ("a waypoint ahead or far behind stays next", WaypointAheadOrFarBehindStaysNext),
        ("the route advances one waypoint at a time", RouteAdvancesOneAtATime),
        ("a restart doesn't pass waypoints already behind", RestartKeepsWaypointsBehindUntilSeenAhead),
        ("undo takes back waypoints but not progress", UndoTakesBackWaypointsNotProgress),
        ("undoing an erase keeps the route's progress", UndoingEraseKeepsProgress),
        ("a new route starts at its first waypoint", NewRouteStartsAtFirstWaypoint),
        ("undoing back to an older route keeps its progress", UndoBackToOlderRouteKeepsProgress),
        ("the route draws numbered markers on one line", RouteDrawsNumberedMarkers),
        ("the eraser takes the whole route", EraserTakesWholeRoute),
        ("the 3D label re-formats only when its text changes, and says lost", CalloutReformatsOnlyOnChange),
    };

    private const float Reach = 2500f;
    private const float Passed = 10000f;
    private static readonly Vector2 North = new(0, 1);
    private static readonly ShapeColor White = new(255, 255, 255);

    private static void WaypointWithinReachIsReached()
    {
        Expect(RouteProgress.IsReached(Vector2.Zero, North, new Vector2(2000, 1000), Reach, Passed), "expected 2.2 km off to the side to count");
        Expect(!RouteProgress.IsReached(Vector2.Zero, North, new Vector2(0, 2600), Reach, Passed), "expected 2.6 km ahead not to count");
    }

    private static void WaypointBehindIsPassed() =>
        Expect(RouteProgress.IsReached(Vector2.Zero, North, new Vector2(3000, -8000), Reach, Passed), "expected a waypoint 8.5 km behind to count as passed");

    private static void WaypointAheadOrFarBehindStaysNext()
    {
        Expect(!RouteProgress.IsReached(Vector2.Zero, North, new Vector2(3000, 5000), Reach, Passed), "expected a waypoint ahead to stay next");
        Expect(!RouteProgress.IsReached(Vector2.Zero, North, new Vector2(0, -11000), Reach, Passed), "expected a waypoint 11 km behind to stay next");
        Expect(!RouteProgress.IsReached(Vector2.Zero, North, new Vector2(0, -5000), Reach, 0f), "expected a passed distance of 0 to turn passing off");
    }

    private static void RouteAdvancesOneAtATime()
    {
        var context = new FakeContext();
        var progress = new RouteProgress();
        AddRoute(context.Shapes, new Vector2(0, 1000), new Vector2(0, 2000), new Vector2(0, 30000));
        progress.Refresh(context.Shapes);
        Expect(progress.Advance(context, Vector2.Zero, North, Reach, Passed), "expected the first waypoint to be reached");
        Expect(progress.Next == 1, $"expected waypoint 2 next, got {progress.Next + 1}");
        Expect(progress.Advance(context, Vector2.Zero, North, Reach, Passed), "expected the second waypoint on the next call");
        Expect(!progress.Advance(context, Vector2.Zero, North, Reach, Passed) && progress.Next == 2, "expected the far waypoint to stay next");
        progress.Skip();
        Expect(!progress.HasNext && progress.Next == 3, "expected skipping the last waypoint to finish the route");
        Expect(!progress.Advance(context, Vector2.Zero, North, Reach, Passed), "expected nothing to advance on a flown route");
        progress.Restart();
        Expect(progress.Next == 0, "expected restart to go back to the first waypoint");
    }

    /// <summary>Restarting past the route's start: its first waypoints sit behind, inside the passed distance.</summary>
    private static void RestartKeepsWaypointsBehindUntilSeenAhead()
    {
        var context = new FakeContext();
        var progress = new RouteProgress();
        AddRoute(context.Shapes, new Vector2(0, -6000), new Vector2(0, -3000), new Vector2(0, 30000));
        progress.Refresh(context.Shapes);
        progress.Restart();
        Expect(!progress.Advance(context, Vector2.Zero, North, Reach, Passed) && progress.Next == 0, "expected waypoint 1 behind to stay next after a restart");
        var south = new Vector2(0, -1);
        Expect(!progress.Advance(context, Vector2.Zero, south, Reach, Passed), "expected waypoint 1 ahead after turning round to stay next");
        Expect(progress.Advance(context, Vector2.Zero, North, Reach, Passed), "expected waypoint 1 seen ahead, then behind, to count as passed");
        Expect(!progress.Advance(context, Vector2.Zero, North, Reach, Passed) && progress.Next == 1, "expected waypoint 2, never seen ahead, to stay next");
    }

    private static void UndoTakesBackWaypointsNotProgress()
    {
        var context = new FakeContext();
        var progress = new RouteProgress();
        AddRoute(context.Shapes, new Vector2(0, 1000), new Vector2(0, 2000), new Vector2(0, 3000));
        progress.Refresh(context.Shapes);
        progress.Skip();
        progress.Skip();
        context.Shapes.Undo();
        progress.Refresh(context.Shapes);
        Expect(progress.Route!.Count == 2 && progress.Next == 2, $"expected 2 waypoints, both flown, got {progress.Route.Count} with {progress.Next} flown");
        context.Shapes.Undo();
        progress.Refresh(context.Shapes);
        Expect(progress.Route!.Count == 1 && progress.Next == 1, "expected progress to shrink with the route");
        context.Shapes.Redo();
        progress.Refresh(context.Shapes);
        Expect(progress.Route!.Count == 2 && progress.Next == 1, "expected a redone waypoint to be unflown");
    }

    private static void UndoingEraseKeepsProgress()
    {
        var context = new FakeContext();
        var progress = new RouteProgress();
        var route = AddRoute(context.Shapes, new Vector2(0, 1000), new Vector2(0, 2000));
        progress.Refresh(context.Shapes);
        progress.Skip();
        context.Shapes.Remove(route);
        progress.Refresh(context.Shapes);
        Expect(progress.Route is null && !progress.HasNext, "expected no route once erased");
        context.Shapes.Undo();
        progress.Refresh(context.Shapes);
        Expect(ReferenceEquals(progress.Route, route) && progress.Next == 1, "expected the route back with waypoint 2 next");
    }

    private static void NewRouteStartsAtFirstWaypoint()
    {
        var context = new FakeContext();
        var progress = new RouteProgress();
        var old = AddRoute(context.Shapes, new Vector2(0, 1000), new Vector2(0, 2000));
        progress.Refresh(context.Shapes);
        progress.Skip();
        context.Shapes.Remove(old);
        AddRoute(context.Shapes, new Vector2(5000, 0), new Vector2(6000, 0));
        progress.Refresh(context.Shapes);
        Expect(progress.Route is { Count: 2 } && progress.Next == 0, $"expected the new route from waypoint 1, got {progress.Next + 1}");
    }

    private static void UndoBackToOlderRouteKeepsProgress()
    {
        var context = new FakeContext();
        var progress = new RouteProgress();
        var old = AddRoute(context.Shapes, new Vector2(0, 1000), new Vector2(0, 2000), new Vector2(0, 3000));
        progress.Refresh(context.Shapes);
        progress.Skip();
        progress.Skip();
        context.Shapes.Remove(old);
        AddRoute(context.Shapes, new Vector2(5000, 0), new Vector2(6000, 0));
        progress.Refresh(context.Shapes);
        progress.Skip();
        for (var i = 0; i < 3; i++)
        {
            context.Shapes.Undo();
            progress.Refresh(context.Shapes);
        }

        Expect(ReferenceEquals(progress.Route, old) && progress.Next == 2, $"expected the old route back with waypoint 3 next, got {progress.Next + 1}");
        for (var i = 0; i < 3; i++)
        {
            context.Shapes.Redo();
            progress.Refresh(context.Shapes);
        }

        Expect(progress.Route is { Count: 2 } && progress.Next == 1, $"expected the redone route back with waypoint 2 next, got {progress.Next + 1}");
    }

    private static void RouteDrawsNumberedMarkers()
    {
        var route = new WaypointRoute(White, Fixed(0, 0)).With(Fixed(0, 1000)).With(Fixed(1000, 1000));
        var canvas = new RecordingCanvas();
        route.Draw(canvas);
        Expect(canvas.Markers == 3 && canvas.Polylines == 1 && canvas.PolylinePoints == 3, "expected three markers on one three-point line");
        Expect(string.Join(",", canvas.Labels) == "1,2,3", $"expected labels 1,2,3, got {string.Join(",", canvas.Labels)}");
    }

    private static void EraserTakesWholeRoute()
    {
        var context = new FakeContext();
        var route = AddRoute(context.Shapes, new Vector2(0, 0), new Vector2(0, 5000), new Vector2(5000, 5000));
        new EraserTool(context).OnClick(new MapPointer(new Vector2(2500, 5030), null));
        Expect(context.Shapes.Shapes.Count == 0, "expected a click on the last leg to erase the route");
        context.Shapes.Undo();
        Expect(context.Shapes.Shapes.Count == 1 && ReferenceEquals(context.Shapes.Shapes[0], route), "expected undo to bring the whole route back");
    }

    private static void CalloutReformatsOnlyOnChange()
    {
        var callout = new WaypointCallout();
        var first = callout.Text(2, Vector2.Zero, new Vector2(0, NavFormat.MetersPerNauticalMile * 4.21f), DistanceUnit.NauticalMiles, lost: false);
        ExpectText(first, "WP2 4.2nm 000°");
        var same = callout.Text(2, new Vector2(0, 20), new Vector2(0, NavFormat.MetersPerNauticalMile * 4.21f), DistanceUnit.NauticalMiles, lost: false);
        Expect(ReferenceEquals(first, same), "expected the same text object while the text reads the same");
        ExpectText(callout.Text(3, Vector2.Zero, new Vector2(12000, 0), DistanceUnit.Kilometres, lost: false), "WP3 12km 090°");
        ExpectText(callout.Text(3, Vector2.Zero, new Vector2(12000, 0), DistanceUnit.Kilometres, lost: true), "WP3 12km 090° lost");
    }

    /// <summary>Adds a route one waypoint at a time, as the tool does: one Add, then a Replace per waypoint.</summary>
    private static WaypointRoute AddRoute(ShapeStore store, params Vector2[] points)
    {
        var route = new WaypointRoute(White, Fixed(points[0].X, points[0].Y));
        store.Add(route);
        for (var i = 1; i < points.Length; i++)
        {
            var longer = route.With(Fixed(points[i].X, points[i].Y));
            store.Replace(route, longer);
            route = longer;
        }

        return route;
    }

    private static RouteWaypoint Fixed(float x, float y) => new(new MapPoint(new Vector2(x, y)), 0f);

    private sealed class RecordingCanvas : IMapCanvas
    {
        public List<string> Labels { get; } = new();
        public int Markers { get; private set; }
        public int Polylines { get; private set; }
        public int PolylinePoints { get; private set; }

        public DistanceUnit Units => DistanceUnit.NauticalMiles;
        public float MetersPerIconUnit => 10f;
        public float TextSize => 10f;
        public MapPoint? OwnAircraft => null;

        public bool TryResolve(MapPoint point, out Vector2 position)
        {
            position = point.Position;
            return true;
        }

        public bool TryResolveWorld(MapPoint point, out Vector3 position)
        {
            position = new Vector3(point.Position.X, 0f, point.Position.Y);
            return true;
        }

        public void Line(Vector2 from, Vector2 to, ShapeColor color)
        {
        }

        public void Arrow(Vector2 from, Vector2 to, ShapeColor color)
        {
        }

        public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color)
        {
            Polylines++;
            PolylinePoints += points.Count;
        }

        public void Circle(Vector2 center, float radius, ShapeColor color)
        {
        }

        public void Marker(Vector2 position, ShapeColor color) => Markers++;

        public void Label(LabelAnchor anchor, string text, ShapeColor color) => Labels.Add(text);
    }

    private sealed class FakeContext : IMapToolContext
    {
        public ShapeStore Shapes { get; } = new();
        public ShapeColor Color => White;
        public DistanceUnit Units => DistanceUnit.NauticalMiles;
        public float MetersPerIconUnit => 10f;
        public float TextSize => 10f;
        public MapPoint? OwnAircraft => null;

        public bool TryResolve(MapPoint point, out Vector2 position)
        {
            position = point.Position;
            return true;
        }

        public bool TryResolveWorld(MapPoint point, out Vector3 position)
        {
            position = new Vector3(point.Position.X, 0f, point.Position.Y);
            return true;
        }

        public float GroundElevation(Vector2 position) => 0f;
    }
}
