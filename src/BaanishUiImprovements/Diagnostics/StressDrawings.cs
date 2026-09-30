using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.MapTools.BearingRange;
using BaanishUiImprovements.MapTools.Circle;
using BaanishUiImprovements.MapTools.Pen;
using BaanishUiImprovements.MapTools.Text;
using BaanishUiImprovements.MapTools.Waypoint;

namespace BaanishUiImprovements.Diagnostics;

/// <summary>
/// The perf test's heavy drawing load: every kind of drawing the tools make, near the store's caps, in a circle the
/// test fits to the minimap so all of it is on screen. Pen strokes take 90% of the point cap, the route is full, and
/// the arrows, a route waypoint in ten, and every other circle ride on live units, so they redraw on each 10 Hz map
/// refresh like real drawings on targets. Unity-free, so the counts are unit-tested.
/// </summary>
public static class StressDrawings
{
    public const int Strokes = 10;
    public const int Bearings = 20;
    public const int Circles = 20;
    public const int Notes = 20;

    /// <param name="units">Units the map shows, nearest first; an empty list leaves every point fixed.</param>
    /// <param name="elevation">Ground height under a fixed point, for the 3D labels.</param>
    public static List<MapShape> Build(Vector2 center, float radius, IReadOnlyList<MapPoint> units, int maxPoints, Func<Vector2, float> elevation)
    {
        var shapes = new List<MapShape> { Route(center, radius, units, elevation) };
        var perStroke = Math.Max(2, maxPoints * 9 / 10 / Strokes);
        for (var i = 0; i < Strokes; i++)
        {
            shapes.Add(new PenStroke(Stroke(center, radius, i, perStroke), Color(i)));
        }

        for (var i = 0; i < Bearings; i++)
        {
            var from = units.Count > 1 ? units[i % units.Count] : new MapPoint(OnRing(center, radius * 0.3f, i, Bearings));
            var to = units.Count > 0 ? units[(i + 1) % units.Count] : new MapPoint(OnRing(center, radius * 0.9f, i, Bearings));
            shapes.Add(new BearingRangeShape(from, to, to.IsAnchored ? 0f : elevation(to.Position), Color(i)));
        }

        for (var i = 0; i < Circles; i++)
        {
            var at = i % 2 == 0 && units.Count > 0 ? units[i / 2 % units.Count] : new MapPoint(OnRing(center, radius * 0.5f, i, Circles));
            shapes.Add(new CircleShape(at, radius * (0.05f + 0.02f * i), Color(i)));
        }

        for (var i = 0; i < Notes; i++)
        {
            var at = OnRing(center, radius * 0.7f, i, Notes);
            var text = "Perf test note " + (i + 1).ToString(CultureInfo.InvariantCulture);
            shapes.Add(new TextNote(at, elevation(at), text, Color(i)));
        }

        return shapes;
    }

    /// <summary>A full route spiralling out three turns from the centre.</summary>
    private static WaypointRoute Route(Vector2 center, float radius, IReadOnlyList<MapPoint> units, Func<Vector2, float> elevation)
    {
        WaypointRoute? route = null;
        for (var i = 0; i < WaypointRoute.MaxWaypoints; i++)
        {
            var t = i / (float)(WaypointRoute.MaxWaypoints - 1);
            var angle = t * 6f * MathF.PI;
            var position = center + radius * (0.1f + 0.8f * t) * new Vector2(MathF.Sin(angle), MathF.Cos(angle));
            var waypoint = i % 10 == 9 && units.Count > 0
                ? new RouteWaypoint(units[i / 10 % units.Count], 0f)
                : new RouteWaypoint(new MapPoint(position), elevation(position));
            route = route == null ? new WaypointRoute(Color(0), waypoint) : route.With(waypoint);
        }

        return route!;
    }

    /// <summary>A wavy line across the circle, one per row.</summary>
    private static Vector2[] Stroke(Vector2 center, float radius, int row, int count)
    {
        var points = new Vector2[count];
        var y = radius * (-0.9f + 1.8f * row / (Strokes - 1));
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)(count - 1);
            var x = radius * (-0.9f + 1.8f * t);
            points[i] = center + new Vector2(x, y + radius * 0.05f * MathF.Sin(t * 8f * MathF.PI));
        }

        return points;
    }

    private static Vector2 OnRing(Vector2 center, float radius, int index, int count)
    {
        var angle = index * 2f * MathF.PI / count;
        return center + radius * new Vector2(MathF.Sin(angle), MathF.Cos(angle));
    }

    private static ShapeColor Color(int index) => ShapeColor.Palette[index % ShapeColor.Palette.Count];
}
