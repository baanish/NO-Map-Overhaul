using System.Numerics;
using BaanishUiImprovements.Diagnostics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.MapTools.BearingRange;
using BaanishUiImprovements.MapTools.Circle;
using BaanishUiImprovements.MapTools.Pen;
using BaanishUiImprovements.MapTools.Waypoint;
using static BaanishUiImprovements.Tests.Program;
using static BaanishUiImprovements.Tests.FrameStatsTests;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// The perf test's Unity-free parts: the order of its slices, the changes it reports against the mod-off conditions,
/// the mod's time by part, and its typical and heavy drawings.
/// </summary>
internal static class PerfTestTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("perf changes read in fps and percent", PerfChangesReadInFpsAndPercent),
        ("the perf summary compares each phase with its baseline", SummaryComparesWithBaseline),
        ("render stats a build doesn't record read n/a", RenderStatsReadNaNAsUnavailable),
        ("the heavy drawings fill the caps and ride on units", HeavyDrawingsFillCapsAndRideOnUnits),
        ("the typical drawings are few and ride on the nearest units", TypicalDrawingsAreFewAndRideOnNearestUnits),
        ("perf slices take turns within each view so drift cancels", SlicesTakeTurnsSoDriftCancels),
        ("a condition's slices add up to one window", SlicesAddUpToOneWindow),
        ("a timed section leaves out the sections inside it", TimedSectionLeavesOutNestedSections),
        ("the per-part table puts the rest of the mod's frame in Other", PerPartTableShowsOther),
    };

    private static void SlicesTakeTurnsSoDriftCancels()
    {
        Expect(PerfSchedule.SliceCount == PerfSchedule.Views * PerfSchedule.ConditionsPerView * PerfSchedule.Cycles, "expected every condition once per cycle");
        var order = string.Join(",", Enumerable.Range(0, PerfSchedule.SlicesPerView).Select(PerfSchedule.Condition));
        ExpectText(order, "0,1,2,3,3,2,1,0,0,1,2,3,3,2,1,0");
        for (var condition = 0; condition < PerfSchedule.Views * PerfSchedule.ConditionsPerView; condition++)
        {
            var slices = Enumerable.Range(0, PerfSchedule.SliceCount).Where(slice => PerfSchedule.Condition(slice) == condition).ToList();
            Expect(slices.Count == PerfSchedule.Cycles, $"expected condition {condition} in {PerfSchedule.Cycles} slices, got {slices.Count}");
            var view = condition / PerfSchedule.ConditionsPerView;
            Expect(slices.All(slice => PerfSchedule.View(slice) == view), $"expected condition {condition} only in view {view}");
            // A steady drift adds the same to each condition when their slices sit at the same average time in the view.
            var middle = slices.Average(slice => slice % PerfSchedule.SlicesPerView);
            ExpectNear((float)middle, (PerfSchedule.SlicesPerView - 1) / 2f, $"condition {condition}'s average slice");
        }

        Expect(PerfSchedule.Baseline(0) == -1 && PerfSchedule.Baseline(3) == 0 && PerfSchedule.Baseline(4) == -1 && PerfSchedule.Baseline(7) == 4,
            "expected each condition compared with the mod off in its own view");
    }

    /// <summary>One condition's stats gather every slice it had, so its averages and 1% low cover them all.</summary>
    private static void SlicesAddUpToOneWindow()
    {
        var stats = new FrameStats();
        for (var i = 0; i < 300; i++)
        {
            stats.AddFrame(10f, 0.1f, modOn: true); // an early slice at 100 fps
        }

        for (var i = 0; i < 100; i++)
        {
            stats.AddFrame(20f, 0.3f, modOn: true); // a later slice at 50 fps, after the frame rate drifted
        }

        var summary = stats.Summarize();
        Expect(summary.Frames == 400, $"expected both slices' 400 frames, got {summary.Frames}");
        ExpectNear(summary.FrameAvgMs, 12.5f, "frame average over both slices");
        ExpectNear(summary.ModAvgMs, 0.15f, "mod average over both slices");
        ExpectNear(summary.LowFps, 50f, "1% low over both slices");
    }

    private static void TimedSectionLeavesOutNestedSections()
    {
        var ms = new double[ModTimings.Count];
        ModTimings.TakeMs(ms);
        Array.Clear(ms, 0, ms.Length);
        var outer = ModTimings.Start(1000L);
        var inner = ModTimings.Start(1100L);
        ModTimings.Stop(ModSection.WorldRings, inner, 1400L);
        ModTimings.Stop(ModSection.WorldLabels, outer, 2000L);
        var after = ModTimings.Start(3000L);
        ModTimings.Stop(ModSection.ShapeDrawing, after, 3500L);
        ModTimings.TakeMs(ms);
        var perTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        ExpectNear((float)(ms[(int)ModSection.WorldRings] / perTick), 300f, "inner ticks");
        ExpectNear((float)(ms[(int)ModSection.WorldLabels] / perTick), 700f, "outer ticks without the inner section");
        ExpectNear((float)(ms[(int)ModSection.ShapeDrawing] / perTick), 500f, "a later section's ticks");

        var again = new double[ModTimings.Count];
        ModTimings.TakeMs(again);
        Expect(again.All(value => value == 0), "expected TakeMs to start every section from zero");
    }

    private static void PerPartTableShowsOther()
    {
        var sections = new float[ModTimings.Count];
        sections[(int)ModSection.ShapeDrawing] = 0.1f;
        sections[(int)ModSection.LabelPlacement] = 0.2f;
        sections[(int)ModSection.MeshRebuilds] = 0.5f; // in the canvas update, so not part of the mod's frame
        var stats = new FrameStats();
        stats.AddFrame(10f, 0.4f, modOn: true);
        var table = PerfReport.Table(new[] { new PerfPhase("Minimap, mod on", stats.Summarize(), -1, default, sections) }, "slices", "71 shapes");
        var row = System.Text.RegularExpressions.Regex.Replace(table.Split('\n').Last().TrimEnd(), " +", " ");
        ExpectText(row, "Minimap, mod on 0.100 0.200 0.000 0.000 0.000 0.000 0.000 0.000 0.500 0.100");
        Expect(!PerfReport.Table(new[] { new PerfPhase("Minimap, mod on", Fps(100f), -1) }, "slices", "71 shapes").Contains("by part"),
            "expected no per-part table without timings");
    }

    private static void PerfChangesReadInFpsAndPercent()
    {
        ExpectText(PerfReport.Change(99f, 100f), "-1.0 (-1.0%)");
        ExpectText(PerfReport.Change(110f, 100f), "+10.0 (+10.0%)");
        ExpectText(PerfReport.Change(5f, 0f), "+5.0");
    }

    private static void SummaryComparesWithBaseline()
    {
        var sections = new float[ModTimings.Count];
        var phases = new[]
        {
            new PerfPhase("Minimap, mod off", Fps(100f), -1, default, sections),
            new PerfPhase("Minimap, mod on", Fps(80f), 0, default, sections),
            new PerfPhase("Minimap, mod on, typical drawings", Fps(75f), 0, default, sections),
            new PerfPhase("Minimap, mod on, heavy drawings", Fps(50f), 0, default, sections),
        };
        var lines = PerfReport.Summary(phases).Split('\n');
        Expect(lines.Length == 4, $"expected a header and three lines, got {lines.Length}");
        ExpectText(lines[1].TrimEnd(), "Minimap, mod on: avg -20.0 (-20.0%), 1% low -20.0 (-20.0%)");
        ExpectText(lines[2].TrimEnd(), "Minimap, mod on, typical drawings: avg -25.0 (-25.0%), 1% low -25.0 (-25.0%)");
        ExpectText(lines[3].TrimEnd(), "Minimap, mod on, heavy drawings: avg -50.0 (-50.0%), 1% low -50.0 (-50.0%)");

        var table = PerfReport.Table(phases, "slices", "typical 11 shapes");
        Expect(table.Contains("baseline"), "expected the mod-off phase marked as the baseline");
        var typicalRows = table.Split('\n').Count(line => line.StartsWith("Minimap, mod on, typical drawings ", StringComparison.Ordinal));
        Expect(typicalRows == 3, $"expected the typical phase in all three tables, got {typicalRows} rows");
    }

    private static void TypicalDrawingsAreFewAndRideOnNearestUnits()
    {
        var units = Enumerable.Range(1, 5).Select(i => new MapPoint(new Vector2(100 * i, 100), (uint)i)).ToList();
        var shapes = StressDrawings.BuildTypical(Vector2.Zero, 10000f, units, _ => 0f);
        Expect(shapes.Count == StressDrawings.TypicalShapes && shapes.Count is >= 10 and <= 20, $"expected {StressDrawings.TypicalShapes} shapes, got {shapes.Count}");
        Expect(shapes.OfType<WaypointRoute>().Single().Count == StressDrawings.TypicalWaypoints, "expected one short route");
        Expect(shapes.OfType<PenStroke>().Count() == StressDrawings.TypicalStrokes, "expected two pen lines");
        Expect(shapes.Sum(shape => shape.PointCount) == StressDrawings.TypicalStrokes * StressDrawings.TypicalStrokePoints, "expected short pen lines");

        var arrows = shapes.OfType<BearingRangeShape>().ToList();
        Expect(arrows.Count == StressDrawings.TypicalBearings && arrows.All(arrow => arrow.To.IsAnchored), "expected every arrow to end on a unit");
        Expect(arrows.Count(arrow => arrow.From.IsAnchored) == 1, "expected one arrow between two units");
        var circles = shapes.OfType<CircleShape>().ToList();
        Expect(circles.Count == StressDrawings.TypicalCircles && circles.Count(circle => circle.Center.IsAnchored) == 1, "expected one circle on a unit");

        foreach (var found in new[] { 0, 1, 2, 5 })
        {
            var anchors = AnchoredUnits(StressDrawings.BuildTypical(Vector2.Zero, 10000f, units.Take(found).ToList(), _ => 0f));
            var expected = Math.Min(found, StressDrawings.TypicalAnchors);
            Expect(anchors == expected, $"expected {expected} of {found} units anchored, got {anchors}");
        }
    }

    private static int AnchoredUnits(IEnumerable<MapShape> shapes) => shapes
        .SelectMany(shape => shape switch
        {
            BearingRangeShape arrow => new[] { arrow.From, arrow.To },
            CircleShape circle => new[] { circle.Center },
            _ => Array.Empty<MapPoint>(),
        })
        .Where(point => point.IsAnchored).Select(point => point.UnitId).Distinct().Count();

    private static void RenderStatsReadNaNAsUnavailable()
    {
        var render = new RenderSummary(0.5f, float.NaN, float.NaN, 42f, 9f, 40f, 1234f, 30, 12);
        var table = PerfReport.Table(new[] { new PerfPhase("Minimap, mod on", Fps(100f), -1, render) }, "slices", "typical 11 shapes, heavy 71 shapes");
        Expect(table.Contains("Drawings: typical 11 shapes, heavy 71 shapes."), "expected the drawings in the header");
        var row = table.Split('\n').Last().TrimEnd();
        ExpectText(System.Text.RegularExpressions.Regex.Replace(row, " +", " "), "Minimap, mod on 0.500 n/a n/a 42 9 40 1234 30/12");
    }

    private static void HeavyDrawingsFillCapsAndRideOnUnits()
    {
        var units = new[] { new MapPoint(new Vector2(100, 100), 7), new MapPoint(new Vector2(-100, 100), 8) };
        var shapes = StressDrawings.BuildHeavy(Vector2.Zero, 10000f, units, 5000, _ => 0f);
        var expected = 1 + StressDrawings.Strokes + StressDrawings.Bearings + StressDrawings.Circles + StressDrawings.Notes;
        Expect(shapes.Count == expected, $"expected {expected} shapes, got {shapes.Count}");

        var points = shapes.Sum(shape => shape.PointCount);
        Expect(points is > 4000 and <= 4500, $"expected 90% of the 5000-point cap, got {points}");

        var route = shapes.OfType<WaypointRoute>().Single();
        Expect(route.Count == WaypointRoute.MaxWaypoints, $"expected a full route, got {route.Count} waypoints");
        Expect(shapes.OfType<BearingRangeShape>().All(arrow => arrow.From.IsAnchored && arrow.To.IsAnchored), "expected every arrow on live units");

        var fixedOnly = StressDrawings.BuildHeavy(Vector2.Zero, 10000f, Array.Empty<MapPoint>(), 5000, _ => 0f);
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
