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

    /// <summary>Incoming missile arrows.</summary>
    MissileArrows,

    /// <summary>The HUD runway callout and the runway numbers kept upright.</summary>
    RunwayOverlays,

    /// <summary>The mod's own graphics rebuilding their meshes, inside Unity's canvas update rather than the mod's frame.</summary>
    MeshRebuilds,
}

/// <summary>
/// Timestamps around each <see cref="ModSection"/> while the perf test runs. Sections may nest, and each counts only its
/// own time: a section's total leaves out the sections timed inside it. Off, it reads no clock. Preallocated, so timing
/// a frame never allocates.
/// </summary>
public static class ModTimings
{
    public const int Count = (int)ModSection.MeshRebuilds + 1;

    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;
    private static readonly long[] Ticks = new long[Count];

    /// <summary>Every tick recorded so far, nested sections included once, for the section enclosing them to leave out.</summary>
    private static long _recorded;

    public static bool On { get; set; }

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

    internal static SectionStart Start(long now) => new(now, _recorded);

    internal static void Stop(ModSection section, SectionStart start, long now)
    {
        var elapsed = now - start.At;
        Ticks[(int)section] += elapsed - (_recorded - start.Recorded);
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
