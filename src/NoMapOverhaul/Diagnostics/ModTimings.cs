using System.Diagnostics;

namespace NoMapOverhaul.Diagnostics;

/// <summary>The parts of the mod's work the perf test times separately, in the order its third table prints them.</summary>
public enum ModSection
{
    /// <summary>The map tools layer drawing shapes and overlays into their meshes.</summary>
    ShapeDrawing,

    /// <summary>Placing the map labels, their plates, and their text.</summary>
    LabelPlacement,

    /// <summary>Turning markers and notes upright on the heading-up minimap.</summary>
    UprightTurning,

    /// <summary>The tools' per-frame work and their 3D labels, rings excluded.</summary>
    WorldLabels,

    /// <summary>The Circle tool's 3D rings.</summary>
    WorldRings,

    /// <summary>The tools menu, mouse and keyboard routing, and the undo keys.</summary>
    InputAndMenu,

    /// <summary>The HUD runway callout and the runway numbers kept upright.</summary>
    RunwayOverlays,

    /// <summary>Drawing sharing: Steam's calls, decoding what teammates sent, and building what goes to them.</summary>
    Sharing,

    /// <summary>The mod's own graphics rebuilding their meshes, inside Unity's canvas update rather than the mod's frame.</summary>
    MeshRebuilds,
}

/// <summary>
/// Timestamps around each <see cref="ModSection"/> while the perf test runs or LogPerformance is on. Sections may nest, and each counts only its
/// own time: a section's total leaves out the sections timed inside it. Off, it reads no clock. Preallocated, so timing
/// a frame never allocates.
/// </summary>
public static class ModTimings
{
    public const int Count = (int)ModSection.MeshRebuilds + 1;

    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;
    private static readonly long[] Ticks = new long[Count];

    /// <summary>This frame's ticks, until <see cref="EndFrame"/> adds them to <see cref="Ticks"/> and the worst frame.</summary>
    private static readonly long[] FrameTicks = new long[Count];

    /// <summary>Each section's costliest frame since the last <see cref="TakeWorstMs"/>: one slow frame drags the 1% low.</summary>
    private static readonly long[] WorstTicks = new long[Count];

    /// <summary>Every tick recorded so far, nested sections included once, for the section enclosing them to leave out.</summary>
    private static long _recorded;

    /// <summary>While the perf test runs, which takes the times slice by slice.</summary>
    public static bool Testing { get; set; }

    /// <summary>While LogPerformance is on, whose log takes the times when no test is running.</summary>
    public static bool Logging { get; set; }

    public static bool On => Testing || Logging;

    public static SectionStart Start() => On ? Start(Stopwatch.GetTimestamp()) : default;

    public static void Stop(ModSection section, SectionStart start)
    {
        if (start.At != 0L)
        {
            Stop(section, start, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>Adds each section's milliseconds since the last call to <paramref name="ms"/> and starts every section from zero.</summary>
    public static void TakeMs(double[] ms)
    {
        for (var i = 0; i < Count; i++)
        {
            ms[i] += Ticks[i] * MsPerTick;
            Ticks[i] = 0L;
        }
    }

    /// <summary>Raises each of <paramref name="worst"/> to the section's costliest frame since the last call, and starts over.</summary>
    public static void TakeWorstMs(double[] worst)
    {
        for (var i = 0; i < Count; i++)
        {
            worst[i] = System.Math.Max(worst[i], WorstTicks[i] * MsPerTick);
            WorstTicks[i] = 0L;
        }
    }

    /// <summary>
    /// Once a frame, after the mod's frame work: closes the frame's times into the totals and worst frames. A frame with
    /// a garbage collection counts in the totals but not the worst frames, since the collection pauses whichever part
    /// happens to allocate and would read as that part's spike.
    /// </summary>
    public static void EndFrame(bool collected = false)
    {
        for (var i = 0; i < Count; i++)
        {
            Ticks[i] += FrameTicks[i];
            if (!collected && FrameTicks[i] > WorstTicks[i]) WorstTicks[i] = FrameTicks[i];
            FrameTicks[i] = 0L;
        }
    }

    internal static SectionStart Start(long now) => new(now, _recorded);

    internal static void Stop(ModSection section, SectionStart start, long now)
    {
        var elapsed = now - start.At;
        FrameTicks[(int)section] += elapsed - (_recorded - start.Recorded);
        _recorded = start.Recorded + elapsed;
    }

    /// <summary>When a section started, and how much was recorded by then.</summary>
    public readonly struct SectionStart
    {
        internal SectionStart(long at, long recorded)
        {
            At = at;
            Recorded = recorded;
        }

        internal long At { get; }

        internal long Recorded { get; }
    }
}
