using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// The perf test's order of slices. Each view (the minimap, then the full map) cycles its four conditions (mod off,
/// mod on, mod on with typical drawings, mod on with heavy drawings) a set number of times, <see cref="Cycles"/> by
/// default, in short slices, so a frame rate that drifts while flying spreads evenly over the conditions instead of
/// landing on whichever was measured last. The order reverses every other cycle (off, on, typical, heavy, then heavy,
/// typical, on, off), and the cycle count is even, so each condition's slices sit at the same average time within the
/// view and a steady drift cancels out. Unity-free, so the order is unit-tested.
/// </summary>
public static class PerfSchedule
{
    public const int Views = 2;
    public const int ConditionsPerView = 4;
    public const int Cycles = 4;
    public const int SlicesPerView = ConditionsPerView * Cycles;
    public const int SliceCount = Views * SlicesPerView;

    /// <summary>Sharing modes start on a whole multiple of this many seconds of UTC, so two PCs line up.</summary>
    public const double AlignSeconds = 30;

    /// <summary>
    /// The command line flag that starts one test by itself, for runs across two PCs: <c>-nomo-perftest receive</c>, or
    /// <c>receive@&lt;unix seconds&gt;</c> to name when its first slice measures.
    /// </summary>
    public const string AutoFlag = "-nomo-perftest";

    public static int SliceCountFor(int cycles) => Views * ConditionsPerView * cycles;

    /// <summary>The mode and start a <see cref="AutoFlag"/> asks for, or nulls without one or with a mode it doesn't name.</summary>
    public static (PerfTestSharing? Mode, double? StartAt) ReadAutoFlag(IReadOnlyList<string> args)
    {
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (!string.Equals(args[i], AutoFlag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = args[i + 1];
            var at = value.IndexOf('@');
            double? start = null;
            if (at >= 0 && double.TryParse(value.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var unix))
            {
                start = unix;
            }

            var name = (at >= 0 ? value.Substring(0, at) : value).Replace("-", string.Empty);
            return Enum.TryParse<PerfTestSharing>(name, true, out var mode) && Enum.IsDefined(typeof(PerfTestSharing), mode)
                ? (mode, start) : (null, null);
        }

        return (null, null);
    }

    /// <summary>
    /// When the first slice of a test asked for at <paramref name="now"/> measures, in seconds of UTC: in a sharing mode,
    /// <paramref name="requested"/> if it's at least <paramref name="settle"/> away, else the next whole
    /// <see cref="AlignSeconds"/> that is; locally, <paramref name="settle"/> away.
    /// </summary>
    public static double FirstSliceAt(double now, double settle, bool aligned, double? requested)
    {
        var earliest = now + settle;
        if (!aligned)
        {
            return earliest;
        }

        if (requested is { } at && at >= earliest)
        {
            return at;
        }

        return Math.Ceiling(earliest / AlignSeconds) * AlignSeconds;
    }

    /// <summary>The view a slice measures: 0 the minimap, 1 the full map.</summary>
    public static int View(int slice, int cycles = Cycles) => slice / (ConditionsPerView * cycles);

    /// <summary>Which of its view's cycles a slice is in, from 0.</summary>
    public static int Cycle(int slice, int cycles = Cycles) => slice % (ConditionsPerView * cycles) / ConditionsPerView;

    /// <summary>The condition a slice measures, as an index into all eight: the view's four, minimap first.</summary>
    public static int Condition(int slice, int cycles = Cycles)
    {
        var inView = slice % (ConditionsPerView * cycles);
        var cycle = inView / ConditionsPerView;
        var step = inView % ConditionsPerView;
        return View(slice, cycles) * ConditionsPerView + (cycle % 2 == 0 ? step : ConditionsPerView - 1 - step);
    }

    /// <summary>The mod-off condition of the same view, which every condition is compared with; -1 for the mod-off one itself.</summary>
    public static int Baseline(int condition) => condition % ConditionsPerView == 0 ? -1 : condition - condition % ConditionsPerView;

    /// <summary>
    /// How long before a slice measures, after the slice before it ends: <paramref name="first"/> for the first,
    /// <paramref name="view"/> when the view changes, none when the condition is the one just measured (a cycle's last
    /// and the next's first), and <paramref name="condition"/> otherwise.
    /// </summary>
    public static double SettleSeconds(int slice, int cycles, double first, double view, double condition)
    {
        if (slice == 0)
        {
            return first;
        }

        if (View(slice, cycles) != View(slice - 1, cycles))
        {
            return view;
        }

        return Condition(slice, cycles) == Condition(slice - 1, cycles) ? 0 : condition;
    }
}
