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
/// The perf test's drawing loads, in a circle the test fits to the minimap so all of it is on screen. The heavy load is
/// every kind of drawing the tools make, near the store's caps: pen strokes take 90% of the point cap, the route is
/// full, and the arrows, a route waypoint in ten, and every other circle ride on live units, so they redraw on each
/// 10 Hz map refresh like real drawings on targets. The typical load is what a player might keep up in a mission: a
/// short route, a few arrows and circles on targets, a few notes, and two short pen lines. Unity-free, so the counts
/// are unit-tested.
/// </summary>
public static class StressDrawings
{
    public const int Strokes = 10;
    public const int Bearings = 20;
    public const int Circles = 20;
    public const int Notes = 20;

    public const int TypicalWaypoints = 7;
    public const int TypicalStrokes = 2;
    public const int TypicalStrokePoints = 30;
    public const int TypicalBearings = 3;
    public const int TypicalCircles = 2;
    public const int TypicalNotes = 3;
    public const int TypicalShapes = 1 + TypicalStrokes + TypicalBearings + TypicalCircles + TypicalNotes;

    /// <summary>The most units the typical drawings ride on: each arrow ends on its own, and the first circle shares the first.</summary>
    public const int TypicalAnchors = TypicalBearings;

    /// <param name="units">Units the map shows, nearest first; an empty list leaves every point fixed.</param>
    /// <param name="elevation">Ground height under a fixed point, for the 3D labels.</param>
    public static List<MapShape> BuildHeavy(Vector2 center, float radius, IReadOnlyList<MapPoint> units, int maxPoints, Func<Vector2, float> elevation)
    {
        var shapes = new List<MapShape> { Route(center, radius, units, elevation) };
        var perStroke = Math.Max(2, maxPoints * 9 / 10 / Strokes);
        for (var i = 0; i < Strokes; i++)
        {
            var start = center + new Vector2(radius * -0.9f, radius * (-0.9f + 1.8f * i / (Strokes - 1)));
            shapes.Add(new PenStroke(Stroke(start, radius * 1.8f, radius * 0.05f, perStroke), Color(i)));
        }

        for (var i = 0; i < Bearings; i++)
        {
            var from = units.Count > 1 ? units[i % units.Count] : new MapPoint(OnRing(center, radius * 0.3f, i, Bearings));
            var to = units.Count > 0 ? units[(i + 1) % units.Count] : new MapPoint(OnRing(center, radius * 0.9f, i, Bearings));
            shapes.Add(new BearingRangeShape(from, to, from.IsAnchored ? 0f : elevation(from.Position), to.IsAnchored ? 0f : elevation(to.Position), Color(i)));
        }

        for (var i = 0; i < Circles; i++)
        {
            var at = i % 2 == 0 && units.Count > 0 ? units[i / 2 % units.Count] : new MapPoint(OnRing(center, radius * 0.5f, i, Circles));
            shapes.Add(new CircleShape(at, radius * (0.05f + 0.02f * i), at.IsAnchored ? 0f : elevation(at.Position), Color(i)));
        }

        for (var i = 0; i < Notes; i++)
        {
            var at = OnRing(center, radius * 0.7f, i, Notes);
            var text = "Perf test note " + (i + 1).ToString(CultureInfo.InvariantCulture);
            shapes.Add(new TextNote(at, elevation(at), text, Color(i)));
        }

        return shapes;
    }

    /// <summary>
    /// A 7-waypoint route, 2 short pen lines, 3 arrows, 2 circles, and 3 notes. Each arrow ends on one of the nearest
    /// units, the first also starts on one, and the first circle sits on the nearest, so its 3D ring is live.
    /// </summary>
    /// <param name="units">Units the map shows, nearest first; an empty list leaves every point fixed.</param>
    /// <param name="elevation">Ground height under a fixed point, for the 3D labels.</param>
    public static List<MapShape> BuildTypical(Vector2 center, float radius, IReadOnlyList<MapPoint> units, Func<Vector2, float> elevation)
    {
        WaypointRoute? route = null;
        for (var i = 0; i < TypicalWaypoints; i++)
        {
            var t = i / (float)(TypicalWaypoints - 1);
            var position = center + radius * new Vector2(-0.8f + 1.6f * t, 0.3f * MathF.Sin(t * 2f * MathF.PI));
            var waypoint = new RouteWaypoint(new MapPoint(position), elevation(position));
            route = route == null ? new WaypointRoute(Color(0), waypoint) : route.With(waypoint);
        }

        var shapes = new List<MapShape> { route! };
        for (var i = 0; i < TypicalStrokes; i++)
        {
            var start = center + radius * new Vector2(-0.6f + 0.8f * i, -0.6f);
            shapes.Add(new PenStroke(Stroke(start, radius * 0.4f, radius * 0.03f, TypicalStrokePoints), Color(i + 1)));
        }

        for (var i = 0; i < TypicalBearings; i++)
        {
            var from = i == 0 && units.Count > 1 ? units[1] : new MapPoint(OnRing(center, radius * 0.2f, i, TypicalBearings));
            var to = units.Count > 0 ? units[i % units.Count] : new MapPoint(OnRing(center, radius * 0.8f, i, TypicalBearings));
            shapes.Add(new BearingRangeShape(from, to, from.IsAnchored ? 0f : elevation(from.Position), to.IsAnchored ? 0f : elevation(to.Position), Color(i + 2)));
        }

        for (var i = 0; i < TypicalCircles; i++)
        {
            var at = i == 0 && units.Count > 0 ? units[0] : new MapPoint(OnRing(center, radius * 0.5f, 2 * i + 1, 2 * TypicalCircles));
            shapes.Add(new CircleShape(at, radius * (0.1f + 0.05f * i), at.IsAnchored ? 0f : elevation(at.Position), Color(i + 3)));
        }

        for (var i = 0; i < TypicalNotes; i++)
        {
            var at = OnRing(center, radius * 0.7f, 2 * i + 1, 2 * TypicalNotes);
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

    /// <summary>A wavy line running east from <paramref name="start"/>, four waves along its length.</summary>
    private static Vector2[] Stroke(Vector2 start, float length, float wave, int count)
    {
        var points = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)(count - 1);
            points[i] = start + new Vector2(length * t, wave * MathF.Sin(t * 8f * MathF.PI));
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
