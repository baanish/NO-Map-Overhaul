using NoMapOverhaul.Diagnostics;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>The LogPerformance math: averages and worsts per window, and the 1% low over the ring of frame times.</summary>
internal static class FrameStatsTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("frame stats average and keep the worst", FrameStatsAverageAndKeepWorst),
        ("the 1% low is the frame 99% of frames beat", LowIsTheFrameNinetyNinePercentBeat),
        ("the 1% low reads only the ring's latest frames", LowReadsLatestFrames),
    };

    private static void FrameStatsAverageAndKeepWorst()
    {
        var stats = new FrameStats();
        stats.AddFrame(10f, 0.1f, modOn: true);
        stats.AddFrame(20f, 0.3f, modOn: true);
        stats.AddFrame(30f, 0.2f, modOn: false);
        stats.AddRefresh(1f);
        stats.AddRefresh(3f);
        var summary = stats.Summarize();
        ExpectNear(summary.FrameAvgMs, 20f, "frame average");
        ExpectNear(summary.AvgFps, 50f, "average fps");
        ExpectNear(summary.FrameWorstMs, 30f, "worst frame");
        ExpectNear(summary.ModAvgMs, 0.2f, "mod average");
        ExpectNear(summary.ModWorstMs, 0.3f, "mod worst");
        ExpectNear(summary.RefreshAvgMs, 2f, "refresh average");
        ExpectNear(summary.RefreshWorstMs, 3f, "refresh worst");
        Expect(summary.Refreshes == 2, $"expected 2 refreshes, got {summary.Refreshes}");
        ExpectText(summary.ModState, "on and off");

        stats.Clear();
        stats.AddFrame(5f, 0f, modOn: false);
        var cleared = stats.Summarize();
        Expect(cleared.Frames == 1 && cleared.Refreshes == 0, "expected Clear to start a fresh window");
        ExpectNear(cleared.FrameWorstMs, 5f, "worst frame after Clear");
        ExpectText(cleared.ModState, "off");
    }

    /// <summary>In 200 frames, the two slowest are the 1% and don't count; a third slow one does.</summary>
    private static void LowIsTheFrameNinetyNinePercentBeat()
    {
        var stats = new FrameStats();
        AddFrames(stats, 198, 10f);
        AddFrames(stats, 2, 50f);
        ExpectNear(stats.Summarize().LowFps, 100f, "1% low with 1% slow frames");

        stats.Clear();
        AddFrames(stats, 197, 10f);
        AddFrames(stats, 3, 50f);
        ExpectNear(stats.Summarize().LowFps, 20f, "1% low with 1.5% slow frames");
    }

    private static void LowReadsLatestFrames()
    {
        var stats = new FrameStats();
        AddFrames(stats, FrameStats.Capacity, 50f);
        AddFrames(stats, FrameStats.Capacity, 10f);
        var summary = stats.Summarize();
        ExpectNear(summary.LowFps, 100f, "1% low once slow frames leave the ring");
        ExpectNear(summary.FrameWorstMs, 50f, "worst frame over the whole window");
    }

    private static void AddFrames(FrameStats stats, int count, float frameMs)
    {
        for (var i = 0; i < count; i++)
        {
            stats.AddFrame(frameMs, 0f, modOn: true);
        }
    }

    internal static void ExpectNear(float actual, float expected, string what) =>
        Expect(MathF.Abs(actual - expected) < 0.001f, $"expected {what} {expected}, got {actual}");
}
