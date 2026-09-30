using System;
using System.Globalization;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// Frame and mod timings over one measuring window: sums and worsts as samples come in, plus the last
/// <see cref="Capacity"/> frame times in a ring for the 1% low. Everything is preallocated, so adding a sample never
/// allocates. Unity-free, so the math is unit-tested.
/// </summary>
public sealed class FrameStats
{
    /// <summary>Frame times kept for the 1% low: 10 seconds at 400 fps. A longer window uses its latest frames.</summary>
    public const int Capacity = 4096;

    private readonly float[] _frameMs = new float[Capacity];
    private readonly float[] _sorted = new float[Capacity];
    private int _frames;
    private int _modOnFrames;
    private int _refreshes;
    private double _frameTotal;
    private double _modTotal;
    private double _refreshTotal;
    private float _frameWorst;
    private float _modWorst;
    private float _refreshWorst;

    public int Frames => _frames;

    /// <summary>One frame: the game's frame time, the mod's own per-frame work, and whether the mod was on.</summary>
    public void AddFrame(float frameMs, float modMs, bool modOn)
    {
        _frameMs[_frames % Capacity] = frameMs;
        _frames++;
        _modOnFrames += modOn ? 1 : 0;
        _frameTotal += frameMs;
        _modTotal += modMs;
        _frameWorst = Math.Max(_frameWorst, frameMs);
        _modWorst = Math.Max(_modWorst, modMs);
    }

    /// <summary>One of the game's map refreshes, which the mod handles outside its per-frame work.</summary>
    public void AddRefresh(float ms)
    {
        _refreshes++;
        _refreshTotal += ms;
        _refreshWorst = Math.Max(_refreshWorst, ms);
    }

    public void Clear()
    {
        _frames = _modOnFrames = _refreshes = 0;
        _frameTotal = _modTotal = _refreshTotal = 0;
        _frameWorst = _modWorst = _refreshWorst = 0f;
    }

    /// <summary>Sorts a copy of the ring, so call it once per window, not per frame.</summary>
    public FrameSummary Summarize()
    {
        var kept = Math.Min(_frames, Capacity);
        Array.Copy(_frameMs, _sorted, kept);
        Array.Sort(_sorted, 0, kept);
        var slowest = kept == 0 ? 0f : _sorted[Math.Max(0, (int)Math.Ceiling(kept * 0.99) - 1)];
        return new FrameSummary(
            _frames,
            _modOnFrames,
            (float)_frameTotal,
            _frames == 0 ? 0f : (float)(_frameTotal / _frames),
            _frameWorst,
            slowest > 0f ? 1000f / slowest : 0f,
            _frames == 0 ? 0f : (float)(_modTotal / _frames),
            _modWorst,
            _refreshes,
            _refreshes == 0 ? 0f : (float)(_refreshTotal / _refreshes),
            _refreshWorst);
    }
}

/// <summary>One window of <see cref="FrameStats"/>. Times in milliseconds.</summary>
public readonly struct FrameSummary
{
    public FrameSummary(int frames, int modOnFrames, float totalMs, float frameAvgMs, float frameWorstMs, float lowFps,
        float modAvgMs, float modWorstMs, int refreshes, float refreshAvgMs, float refreshWorstMs)
    {
        Frames = frames;
        ModOnFrames = modOnFrames;
        TotalMs = totalMs;
        FrameAvgMs = frameAvgMs;
        FrameWorstMs = frameWorstMs;
        LowFps = lowFps;
        ModAvgMs = modAvgMs;
        ModWorstMs = modWorstMs;
        Refreshes = refreshes;
        RefreshAvgMs = refreshAvgMs;
        RefreshWorstMs = refreshWorstMs;
    }

    public int Frames { get; }

    /// <summary>Frames with the mod on, so a window where it was switched shows as mixed.</summary>
    public int ModOnFrames { get; }

    /// <summary>The frame times summed: the window's length in real time.</summary>
    public float TotalMs { get; }

    public float FrameAvgMs { get; }

    public float FrameWorstMs { get; }

    /// <summary>The 1% low: the frame rate that 99% of frames beat, from the 99th percentile frame time.</summary>
    public float LowFps { get; }

    public float AvgFps => FrameAvgMs > 0f ? 1000f / FrameAvgMs : 0f;

    /// <summary>The mod's per-frame work, averaged over every frame.</summary>
    public float ModAvgMs { get; }

    public float ModWorstMs { get; }

    public int Refreshes { get; }

    public float RefreshAvgMs { get; }

    public float RefreshWorstMs { get; }

    public string ModState => ModOnFrames == Frames ? "on" : ModOnFrames == 0 ? "off" : "on and off";

    /// <summary>The LogPerformance line.</summary>
    public string LogLine() => string.Format(CultureInfo.InvariantCulture,
        "Perf {0:0.0} s, mod {1}: {2} frames, {3:0.0} fps avg ({4:0.00} ms), 1% low {5:0.0} fps, worst frame {6:0.0} ms. " +
        "Mod per frame {7:0.000} ms avg, {8:0.000} ms worst. Map refresh {9}x, {10:0.000} ms avg, {11:0.000} ms worst.",
        TotalMs / 1000f, ModState, Frames, AvgFps, FrameAvgMs, LowFps, FrameWorstMs, ModAvgMs, ModWorstMs, Refreshes,
        RefreshAvgMs, RefreshWorstMs);
}
