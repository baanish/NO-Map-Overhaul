namespace NoMapOverhaul.Diagnostics;

/// <summary>Which way the 3D rings were drawn in a logged window, if any frame drew them.</summary>
public enum RingPath
{
    /// <summary>No frame in the window got as far as drawing rings: no map tools, no HUD, or 3D circles off.</summary>
    None,
    Gpu,
    Cpu,
}

/// <summary>
/// How often the mod's costlier work ran since the performance log last took the counts. Each count is a plain
/// increment where the work happens, kept whether or not the log is on, since that costs less than checking; the log
/// drops what built up before its first window. Unity-free.
/// </summary>
public static class ModActivity
{
    /// <summary><c>MapShapeLayer.PlaceLabels</c> runs that laid the labels out, rather than finding nothing to place.</summary>
    public static int LabelLayouts;

    /// <summary>The mod's own meshes rebuilt in Unity's canvas update (<c>ModGraphic.UpdateGeometry</c>).</summary>
    public static int MeshRebuilds;

    /// <summary><c>MapShapeLayer</c> taking a new set of shown received drawings.</summary>
    public static int ReceivedSyncs;

    private static int _mostRings;
    private static RingPath _ringPath;

    /// <summary>Once a frame, from the ring pool: how many rings it drew and which way.</summary>
    public static void CountRings(int drawn, bool gpu)
    {
        if (drawn > _mostRings) _mostRings = drawn;
        _ringPath = gpu ? RingPath.Gpu : RingPath.Cpu;
    }

    /// <summary>The counts since the last call, and starts them from zero.</summary>
    public static ActivityCounts Take()
    {
        var counts = new ActivityCounts(LabelLayouts, MeshRebuilds, ReceivedSyncs, _mostRings, _ringPath);
        LabelLayouts = MeshRebuilds = ReceivedSyncs = _mostRings = 0;
        _ringPath = RingPath.None;
        return counts;
    }
}

/// <summary>One logged window of <see cref="ModActivity"/>.</summary>
public readonly struct ActivityCounts
{
    public ActivityCounts(int labelLayouts, int meshRebuilds, int receivedSyncs, int mostRings, RingPath ringPath)
    {
        LabelLayouts = labelLayouts;
        MeshRebuilds = meshRebuilds;
        ReceivedSyncs = receivedSyncs;
        MostRings = mostRings;
        RingPath = ringPath;
    }

    public int LabelLayouts { get; }
    public int MeshRebuilds { get; }
    public int ReceivedSyncs { get; }

    /// <summary>The most 3D rings drawn in one frame of the window.</summary>
    public int MostRings { get; }

    public RingPath RingPath { get; }
}
