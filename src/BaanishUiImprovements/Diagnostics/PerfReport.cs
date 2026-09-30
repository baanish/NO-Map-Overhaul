using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BaanishUiImprovements.Diagnostics;

/// <summary>One measured phase of the perf test. <see cref="Baseline"/> is the index of the mod-off phase it's compared with, or -1 for a baseline.</summary>
public readonly struct PerfPhase
{
    public PerfPhase(string name, FrameSummary summary, int baseline, RenderSummary render = default)
    {
        Name = name;
        Summary = summary;
        Baseline = baseline;
        Render = render;
    }

    public string Name { get; }

    public FrameSummary Summary { get; }

    public int Baseline { get; }

    public RenderSummary Render { get; }
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

    /// <param name="drawings">What the heavy phases drew, for the header.</param>
    public static string Table(IReadOnlyList<PerfPhase> phases, float settleSeconds, float measureSeconds, string drawings)
    {
        var text = new StringBuilder();
        text.AppendFormat(CultureInfo.InvariantCulture,
            "Perf test: {0:0} s per phase after {1:0} s to settle. Heavy drawings: {2}. Changes are against the same view with the mod off.",
            measureSeconds, settleSeconds, drawings).AppendLine();
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

        return text.ToString().TrimEnd();
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
