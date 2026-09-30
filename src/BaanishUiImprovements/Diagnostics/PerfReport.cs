using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BaanishUiImprovements.Diagnostics;

/// <summary>One measured phase of the perf test. <see cref="Baseline"/> is the index of the mod-off phase it's compared with, or -1 for a baseline.</summary>
public readonly struct PerfPhase
{
    public PerfPhase(string name, FrameSummary summary, int baseline)
    {
        Name = name;
        Summary = summary;
        Baseline = baseline;
    }

    public string Name { get; }

    public FrameSummary Summary { get; }

    public int Baseline { get; }
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

    public static string Table(IReadOnlyList<PerfPhase> phases, float settleSeconds, float measureSeconds)
    {
        var text = new StringBuilder();
        text.AppendFormat(CultureInfo.InvariantCulture, "Perf test: {0:0} s per phase after {1:0} s to settle. Changes are against the same view with the mod off.", measureSeconds, settleSeconds).AppendLine();
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

        return text.ToString().TrimEnd();
    }

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
