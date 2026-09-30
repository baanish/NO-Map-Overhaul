namespace BaanishUiImprovements.Diagnostics;

/// <summary>
/// The perf test's order of slices. Each view (the minimap, then the full map) cycles its three conditions (mod off,
/// mod on, mod on with heavy drawings) <see cref="Cycles"/> times, in short slices, so a frame rate that drifts while
/// flying spreads evenly over the conditions instead of landing on whichever was measured last. The order reverses
/// every other cycle (off, on, heavy, then heavy, on, off), and the cycle count is even, so each condition's slices sit
/// at the same average time within the view and a steady drift cancels out. Unity-free, so the order is unit-tested.
/// </summary>
public static class PerfSchedule
{
    public const int Views = 2;
    public const int ConditionsPerView = 3;
    public const int Cycles = 4;
    public const int SlicesPerView = ConditionsPerView * Cycles;
    public const int SliceCount = Views * SlicesPerView;

    /// <summary>The view a slice measures: 0 the minimap, 1 the full map.</summary>
    public static int View(int slice) => slice / SlicesPerView;

    /// <summary>The condition a slice measures, as an index into all six: the view's three, minimap first.</summary>
    public static int Condition(int slice)
    {
        var inView = slice % SlicesPerView;
        var cycle = inView / ConditionsPerView;
        var step = inView % ConditionsPerView;
        return View(slice) * ConditionsPerView + (cycle % 2 == 0 ? step : ConditionsPerView - 1 - step);
    }

    /// <summary>The mod-off condition of the same view, which every condition is compared with; -1 for the mod-off one itself.</summary>
    public static int Baseline(int condition) => condition % ConditionsPerView == 0 ? -1 : condition - condition % ConditionsPerView;
}
