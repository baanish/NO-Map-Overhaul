using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// One measured condition of the perf test, over all its slices. <see cref="Baseline"/> is the index of the mod-off
/// condition it's compared with, or -1 for a baseline.
/// </summary>
public readonly struct PerfPhase
{
    public PerfPhase(string name, FrameSummary summary, int baseline, RenderSummary render = default, float[]? sectionMs = null,
        PerfSharingSummary? sharing = null, float[]? sectionWorstMs = null)
    {
        SectionWorstMs = sectionWorstMs;
        Name = name;
        Summary = summary;
        Baseline = baseline;
        Render = render;
        SectionMs = sectionMs;
        Sharing = sharing;
    }

    /// <summary>Sharing's traffic and the garbage collections over the phase, or null if they weren't counted.</summary>
    public PerfSharingSummary? Sharing { get; }

    public string Name { get; }

    public FrameSummary Summary { get; }

    public int Baseline { get; }

    public RenderSummary Render { get; }

    /// <summary>Milliseconds per frame in each <see cref="ModSection"/>, in its order, or null if they weren't timed.</summary>
    public float[]? SectionMs { get; }

    /// <summary>Each <see cref="ModSection"/>'s costliest single frame in the phase, or null if they weren't timed.</summary>
    public float[]? SectionWorstMs { get; }
}

/// <summary>
/// What Unity spent on UI over one phase, averaged per frame. NaN marks a stat this build of the game doesn't record.
/// <see cref="CanvasMs"/> covers every canvas, the game's too. The mod counts are its enabled graphics at the phase's end.
/// </summary>
public readonly struct RenderSummary
{
    public RenderSummary(float canvasMs, float buildBatchMs, float willRenderMs, float batches, float setPassCalls, float drawCalls,
        float vertices, int modGraphics, int modTexts)
    {
        CanvasMs = canvasMs;
        BuildBatchMs = buildBatchMs;
        WillRenderMs = willRenderMs;
        Batches = batches;
        SetPassCalls = setPassCalls;
        DrawCalls = drawCalls;
        Vertices = vertices;
        ModGraphics = modGraphics;
        ModTexts = modTexts;
    }

    public float CanvasMs { get; }
    public float BuildBatchMs { get; }
    public float WillRenderMs { get; }
    public float Batches { get; }
    public float SetPassCalls { get; }
    public float DrawCalls { get; }
    public float Vertices { get; }
    public int ModGraphics { get; }

    /// <summary>TextMeshPro objects among <see cref="ModGraphics"/>.</summary>
    public int ModTexts { get; }
}

/// <summary>The perf test's results: a table for the log and a few lines for the screen. Unity-free, so the deltas are unit-tested.</summary>
public static class PerfReport
{
    /// <summary>The change from a baseline, as "-1.3 (-0.9%)". A zero baseline gives no percentage.</summary>
    public static string Change(float value, float baseline)
    {
        var change = value - baseline;
        return baseline > 0f
            ? string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0} ({1:+0.0;-0.0;0.0}%)", change, change / baseline * 100f)
            : change.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>Column heads of the third table, one per <see cref="ModSection"/>, then the rest of the mod's frame.</summary>
    internal static readonly string[] SectionHeads = { "Draw", "Labels", "Upright", "3D text", "3D rings", "Input", "Runways", "Sharing", "Meshes", "Other" };

    /// <param name="method">How the phases were measured, for the header.</param>
    /// <param name="drawings">What the typical and heavy phases drew, for the header.</param>
    /// <param name="sharing">Which way the test's drawings went and how sharing stood at the end, for the header.</param>
    public static string Table(IReadOnlyList<PerfPhase> phases, string method, string drawings, string? sharing = null)
    {
        var text = new StringBuilder();
        text.AppendFormat(CultureInfo.InvariantCulture,
            "Perf test: {0}. Drawings: {1}. Changes are against the same view with the mod off.", method, drawings).AppendLine();
        if (sharing != null)
        {
            text.Append("Mode: ").Append(sharing).AppendLine();
        }

        text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36} {1,8} {2,8} {3,14} {4,20} {5,16} {6,16}",
            "Phase", "Avg fps", "1% low", "Mod ms avg/max", "Refresh ms avg/max", "Avg fps change", "1% low change").AppendLine();
        foreach (var phase in phases)
        {
            var s = phase.Summary;
            var baseline = phase.Baseline >= 0 ? phases[phase.Baseline].Summary : (FrameSummary?)null;
            text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36} {1,8:0.0} {2,8:0.0} {3,14} {4,20} {5,16} {6,16}",
                phase.Name,
                s.AvgFps,
                s.LowFps,
                string.Format(CultureInfo.InvariantCulture, "{0:0.000}/{1:0.000}", s.ModAvgMs, s.ModWorstMs),
                string.Format(CultureInfo.InvariantCulture, "{0:0.000}/{1:0.000} {2}x", s.RefreshAvgMs, s.RefreshWorstMs, s.Refreshes),
                baseline is { } b ? Change(s.AvgFps, b.AvgFps) : "baseline",
                baseline is { } l ? Change(s.LowFps, l.LowFps) : "baseline").AppendLine();
        }

        text.AppendLine("Rendering per frame. Canvas ms is every canvas's rebuild and batching, the game's too. n/a is a stat this build doesn't record.");
        text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36} {1,10} {2,14} {3,14} {4,8} {5,8} {6,10} {7,10} {8,17}",
            "Phase", "Canvas ms", "BuildBatch ms", "WillRender ms", "Batches", "SetPass", "Draw calls", "Vertices", "Mod graphics/TMP").AppendLine();
        foreach (var phase in phases)
        {
            var r = phase.Render;
            text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36} {1,10} {2,14} {3,14} {4,8} {5,8} {6,10} {7,10} {8,17}",
                phase.Name, Stat(r.CanvasMs, "0.000"), Stat(r.BuildBatchMs, "0.000"), Stat(r.WillRenderMs, "0.000"), Stat(r.Batches, "0"),
                Stat(r.SetPassCalls, "0"), Stat(r.DrawCalls, "0"), Stat(r.Vertices, "0"),
                string.Format(CultureInfo.InvariantCulture, "{0}/{1}", r.ModGraphics, r.ModTexts)).AppendLine();
        }

        AppendSections(text, phases);
        AppendSharing(text, phases);
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Sharing's traffic per second, the main thread's time in Steam's calls, what the mod allocated and the game's
    /// garbage collections, per phase, when the test counted them.
    /// </summary>
    private static void AppendSharing(StringBuilder text, IReadOnlyList<PerfPhase> phases)
    {
        var any = false;
        foreach (var phase in phases)
        {
            any |= phase.Sharing != null;
        }

        if (!any)
        {
            return;
        }

        text.AppendLine("Sharing and memory. Per second unless marked; Steam ms is the game's main thread inside Steam's calls; Shown is teammates' drawings on the map.");
        text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36} {1,9} {2,8} {3,9} {4,8} {5,8} {6,12} {7,11} {8,13} {9,6} {10,6}",
            "Phase", "Sent KB", "Sent", "Recv KB", "Recv", "Applied", "Steam ms/fr", "Steam worst", "Alloc KB/fr", "GC", "Shown").AppendLine();
        foreach (var phase in phases)
        {
            if (phase.Sharing is not { } s)
            {
                continue;
            }

            text.AppendFormat(CultureInfo.InvariantCulture,
                "{0,-36} {1,9:0.00} {2,8:0.0} {3,9:0.00} {4,8:0.0} {5,8:0.00} {6,12:0.000} {7,11:0.0} {8,13:0.00} {9,6:0.00} {10,6}",
                phase.Name, s.SentKb, s.SentMessages, s.ReceivedKb, s.ReceivedMessages, s.SnapshotsApplied, s.SteamMsPerFrame,
                s.SteamWorstMs, phase.Summary.AllocKbPerFrame, s.Collections, s.ReceivedShown).AppendLine();
        }
    }

    /// <summary>
    /// Each cycle's average frame rate per phase, for a test of more cycles than the default to show whether the frame
    /// rate drifts as it runs. Empty for a short test. Rows are cycles, columns phases.
    /// </summary>
    public static string Cycles(IReadOnlyList<string> names, IReadOnlyList<(int Frames, double Ms)[]> cycles)
    {
        if (cycles.Count <= PerfSchedule.Cycles)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        text.AppendLine().Append("Avg fps per cycle, in phase order:");
        for (var i = 0; i < names.Count; i++)
        {
            text.Append(i == 0 ? " " : " | ").Append(names[i]);
        }

        for (var cycle = 0; cycle < cycles.Count; cycle++)
        {
            text.AppendLine().AppendFormat(CultureInfo.InvariantCulture, "Cycle {0,3}:", cycle + 1);
            for (var i = 0; i < names.Count && i < cycles[cycle].Length; i++)
            {
                var (frames, ms) = cycles[cycle][i];
                text.AppendFormat(CultureInfo.InvariantCulture, " {0,7}", ms > 0 ? (frames * 1000.0 / ms).ToString("0.0", CultureInfo.InvariantCulture) : "-");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The mod's own time per frame by part. Meshes is inside Canvas ms rather than Mod ms, since Unity rebuilds meshes in
    /// its canvas update; Other is the rest of Mod ms.
    /// </summary>
    private static void AppendSections(StringBuilder text, IReadOnlyList<PerfPhase> phases)
    {
        var any = false;
        foreach (var phase in phases)
        {
            any |= phase.SectionMs != null;
        }

        if (!any)
        {
            return;
        }

        text.AppendLine("Mod ms per frame by part. Meshes is the mod's graphics rebuilding their meshes, which counts in Canvas ms, not Mod ms. Other is the rest of Mod ms.");
        text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36}", "Phase");
        foreach (var head in SectionHeads)
        {
            text.AppendFormat(CultureInfo.InvariantCulture, " {0,9}", head);
        }

        text.AppendLine();
        foreach (var phase in phases)
        {
            if (phase.SectionMs is not { } sections)
            {
                continue;
            }

            text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36}", phase.Name);
            var inFrame = 0f;
            for (var i = 0; i < sections.Length; i++)
            {
                text.AppendFormat(CultureInfo.InvariantCulture, " {0,9:0.000}", sections[i]);
                inFrame += i == (int)ModSection.MeshRebuilds ? 0f : sections[i];
            }

            text.AppendFormat(CultureInfo.InvariantCulture, " {0,9:0.000}", System.Math.Max(0f, phase.Summary.ModAvgMs - inFrame)).AppendLine();
        }

        var anyWorst = false;
        foreach (var phase in phases)
        {
            anyWorst |= phase.SectionWorstMs != null;
        }

        if (!anyWorst)
        {
            return;
        }

        text.AppendLine("Worst frame ms by part: each part's costliest single frame without a garbage collection, the spikes behind a 1% low.");
        text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36}", "Phase");
        for (var i = 0; i < ModTimings.Count; i++)
        {
            text.AppendFormat(CultureInfo.InvariantCulture, " {0,9}", SectionHeads[i]);
        }

        text.AppendLine();
        foreach (var phase in phases)
        {
            if (phase.SectionWorstMs is not { } worst)
            {
                continue;
            }

            text.AppendFormat(CultureInfo.InvariantCulture, "{0,-36}", phase.Name);
            foreach (var ms in worst)
            {
                text.AppendFormat(CultureInfo.InvariantCulture, " {0,9:0.000}", ms);
            }

            text.AppendLine();
        }
    }

    /// <summary>The value, or "n/a" for NaN.</summary>
    public static string Stat(float value, string format) =>
        float.IsNaN(value) ? "n/a" : value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>One line per compared phase, short enough for the game's message feed.</summary>
    public static string Summary(IReadOnlyList<PerfPhase> phases)
    {
        var text = new StringBuilder("Perf test done, full table in LogOutput.log. Fps against the mod off:");
        foreach (var phase in phases)
        {
            if (phase.Baseline >= 0)
            {
                var baseline = phases[phase.Baseline].Summary;
                text.Append('\n').Append(phase.Name).Append(": avg ").Append(Change(phase.Summary.AvgFps, baseline.AvgFps))
                    .Append(", 1% low ").Append(Change(phase.Summary.LowFps, baseline.LowFps));
            }
        }

        return text.ToString();
    }
}
