using System.Numerics;
using BaanishUiImprovements.Diagnostics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.MapTools.BearingRange;
using BaanishUiImprovements.MapTools.Pen;
using BaanishUiImprovements.MapTools.Waypoint;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>The perf test's Unity-free parts: the changes it reports against the mod-off phases, and its heavy drawings.</summary>
internal static class PerfTestTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("perf changes read in fps and percent", PerfChangesReadInFpsAndPercent),
        ("the perf summary compares each phase with its baseline", SummaryComparesWithBaseline),
        ("the heavy drawings fill the caps and ride on units", HeavyDrawingsFillCapsAndRideOnUnits),
    };

    private static void PerfChangesReadInFpsAndPercent()
    {
        ExpectText(PerfReport.Change(99f, 100f), "-1.0 (-1.0%)");
        ExpectText(PerfReport.Change(110f, 100f), "+10.0 (+10.0%)");
        ExpectText(PerfReport.Change(5f, 0f), "+5.0");
    }

    private static void SummaryComparesWithBaseline()
    {
        var phases = new[]
        {
            new PerfPhase("Minimap, mod off", Fps(100f), -1),
            new PerfPhase("Minimap, mod on", Fps(80f), 0),
        };
        var lines = PerfReport.Summary(phases).Split('\n');
        Expect(lines.Length == 2, $"expected a header and one line, got {lines.Length}");
        ExpectText(lines[1].TrimEnd(), "Minimap, mod on: avg -20.0 (-20.0%), 1% low -20.0 (-20.0%)");
        Expect(PerfReport.Table(phases, 2f, 10f).Contains("baseline"), "expected the mod-off phase marked as the baseline");
    }

    private static void HeavyDrawingsFillCapsAndRideOnUnits()
    {
        var units = new[] { new MapPoint(new Vector2(100, 100), 7), new MapPoint(new Vector2(-100, 100), 8) };
        var shapes = StressDrawings.Build(Vector2.Zero, 10000f, units, 5000, _ => 0f);
        var expected = 1 + StressDrawings.Strokes + StressDrawings.Bearings + StressDrawings.Circles + StressDrawings.Notes;
        Expect(shapes.Count == expected, $"expected {expected} shapes, got {shapes.Count}");

        var points = shapes.Sum(shape => shape.PointCount);
        Expect(points is > 4000 and <= 4500, $"expected 90% of the 5000-point cap, got {points}");

        var route = shapes.OfType<WaypointRoute>().Single();
        Expect(route.Count == WaypointRoute.MaxWaypoints, $"expected a full route, got {route.Count} waypoints");
        Expect(shapes.OfType<BearingRangeShape>().All(arrow => arrow.From.IsAnchored && arrow.To.IsAnchored), "expected every arrow on live units");

        var fixedOnly = StressDrawings.Build(Vector2.Zero, 10000f, Array.Empty<MapPoint>(), 5000, _ => 0f);
        Expect(fixedOnly.OfType<BearingRangeShape>().All(arrow => !arrow.From.IsAnchored && !arrow.To.IsAnchored), "expected fixed arrows with no units");
        Expect(fixedOnly.OfType<PenStroke>().Count() == StressDrawings.Strokes, "expected every stroke without units too");
    }

    /// <summary>A window of 1000 frames at one steady frame rate.</summary>
    private static FrameSummary Fps(float fps)
    {
        var stats = new FrameStats();
        for (var i = 0; i < 1000; i++)
        {
            stats.AddFrame(1000f / fps, 0f, modOn: true);
        }

        return stats.Summarize();
    }
}
