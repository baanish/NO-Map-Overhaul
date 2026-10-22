using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Profiling;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// The LogPerformance setting: times the plugin's per-frame work and its map refreshes with <see cref="Stopwatch"/>,
/// and every <see cref="WindowSeconds"/> of real time logs them next to the game's own frame time
/// (<see cref="FrameSummary.LogLine"/>), with what the mod allocated, the game's garbage collections, and
/// <see cref="Counts"/>. The mod's times cover its scripts only; what Unity spends drawing the mod's
/// graphics shows up in the frame time alone, which is why an A/B with General.Enabled compares frame times. A second
/// line (<see cref="PerfLogLines.Detail"/>) adds Unity's canvas time and render counters from <see cref="RenderCounters"/>,
/// sampled each frame, how often the mod rebuilt things (<see cref="ModActivity"/>), and the <see cref="Context"/>; a
/// third, each teammate's sharing (<see cref="Teammates"/>). Off, it reads no clock and keeps nothing.
/// </summary>
internal sealed class PerformanceLog
{
    private const float WindowSeconds = 5f;

    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    private readonly ModSettings _settings;
    private readonly ManualLogSource _log;
    private readonly FrameStats _window = new();
    private readonly RenderCounters _render = new(1);
    private bool _rendering;

    /// <summary>The perf test, which takes the canvas time while it runs, ran during this window, so it has no render counts.</summary>
    private bool _renderTaken;
    private float _windowStart = -1f;
    private long _monoAtStart;
    private long _frameAllocated;
    private int _collections;
    private int _frameCollections;

    public PerformanceLog(ModSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>While the perf test measures a phase, it gets every sample too, whether or not LogPerformance is on.</summary>
    public FrameStats? Phase { get; set; }

    /// <summary>What the mod holds now, such as its drawings and graphics, for the end of each logged window.</summary>
    public System.Func<string>? Counts { get; set; }

    /// <summary>Where the player is, read at the end of each logged window.</summary>
    public System.Func<PerfContext>? Context { get; set; }

    /// <summary>Each teammate's sharing since the last call, read at the end of each logged window.</summary>
    public System.Func<IReadOnlyList<TeammateWindow>>? Teammates { get; set; }

    /// <summary>Drops each teammate's traffic from before a window starts, without asking Steam for their status.</summary>
    public System.Action? DropTeammates { get; set; }

    /// <summary>A timestamp to pass back to <see cref="AddFrame"/> or <see cref="AddRefresh"/>.</summary>
    public long Start()
    {
        if (!_settings.LogPerformance.Value && Phase == null)
        {
            return 0L;
        }

        _monoAtStart = Profiler.GetMonoUsedSizeLong();
        return Stopwatch.GetTimestamp();
    }

    /// <summary>Once per frame, after the plugin's per-frame work.</summary>
    public void AddFrame(long start, bool modOn)
    {
        ModTimings.Logging = _settings.LogPerformance.Value;
        if (ModTimings.On)
        {
            // A frame runs from here to here, with the canvas update's mesh rebuilds in between.
            var collections = System.GC.CollectionCount(0);
            ModTimings.EndFrame(collections != _frameCollections);
            _frameCollections = collections;
        }

        if (start == 0L)
        {
            _windowStart = -1f; // a later switch-on starts a fresh window
            StopRender();
            return;
        }

        var modMs = Elapsed(start);
        CountAllocation();
        var allocated = _frameAllocated;
        _frameAllocated = 0;
        var frameMs = Time.unscaledDeltaTime * 1000f;
        Phase?.AddFrame(frameMs, modMs, modOn, allocated);
        if (!_settings.LogPerformance.Value)
        {
            _windowStart = -1f;
            StopRender();
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (_windowStart < 0f)
        {
            _window.Clear();
            _windowStart = now;
            _collections = System.GC.CollectionCount(0);
            if (!ModTimings.Testing)
            {
                ModTimings.TakeMs(_sectionMs); // from before the window, dropped
                ModTimings.TakeWorstMs(_sectionMs);
            }

            ModActivity.Take();
            DropTeammates?.Invoke();
            StartWindowRender();
        }

        _window.AddFrame(frameMs, modMs, modOn, allocated);
        SampleRender();
        if (now - _windowStart >= WindowSeconds)
        {
            var summary = _window.Summarize();
            var collections = System.GC.CollectionCount(0);
            var gcs = collections - _collections;
            var render = _rendering && !_renderTaken ? _render.Summarize(0) : (RenderSummary?)null;
            var canvasWorstMs = _render.CanvasWorstMs(0);
            var activity = ModActivity.Take();
            var sections = Sections(summary.Frames);
            // The next window starts before the lines are written: this runs outside the plugin's guard, so a throw
            // below costs this window's lines and not every later frame's.
            _window.Clear();
            _windowStart = now;
            _collections = collections;
            StartWindowRender();
            try
            {
                _log.LogInfo(summary.LogLine() + string.Format(CultureInfo.InvariantCulture,
                    " Mod allocated {0:0.0} KB per frame. GC {1}x, heap {2:0} of {3:0} MB. {4}",
                    summary.AllocKbPerFrame, gcs,
                    Profiler.GetMonoUsedSizeLong() / 1048576.0, Profiler.GetMonoHeapSizeLong() / 1048576.0, Counts?.Invoke() ?? string.Empty)
                    + sections);
                _log.LogInfo(PerfLogLines.Detail(render, canvasWorstMs, activity, Context?.Invoke() ?? default));
                if (Teammates != null && PerfLogLines.Teammates(Teammates()) is { } teammates)
                {
                    _log.LogInfo(teammates);
                }
            }
            catch (System.Exception e)
            {
                _log.LogWarning($"Log performance couldn't write this window: {e}");
            }
        }
    }

    /// <summary>
    /// Starts the render counters, or clears them for a new window. Neither while the perf test runs: clearing would
    /// drop canvas time it has yet to read, and finding the counters stalls a frame.
    /// </summary>
    private void StartWindowRender()
    {
        _renderTaken = ModTimings.Testing;
        if (_renderTaken)
        {
            return;
        }

        if (_rendering)
        {
            _render.Clear();
            return;
        }

        _render.Start();
        _rendering = true;
    }

    /// <summary>Reads the frame before's canvas time and render counters: the last frame the player loop finished.</summary>
    private void SampleRender()
    {
        _renderTaken |= ModTimings.Testing;
        if (_rendering && !_renderTaken)
        {
            _render.Sample(0);
        }
    }

    private void StopRender()
    {
        if (_rendering)
        {
            _render.Stop();
            _rendering = false;
        }
    }

    private readonly double[] _sectionMs = new double[ModTimings.Count];
    private readonly double[] _worstMs = new double[ModTimings.Count];

    /// <summary>
    /// The window's mod time per frame by part, as the perf test's third table splits it, so a real session shows which
    /// part a slowdown is in. Empty while the perf test runs, which takes these times itself.
    /// </summary>
    private string Sections(int frames)
    {
        if (ModTimings.Testing || frames == 0)
        {
            return string.Empty;
        }

        System.Array.Clear(_sectionMs, 0, _sectionMs.Length);
        ModTimings.TakeMs(_sectionMs);
        System.Array.Clear(_worstMs, 0, _worstMs.Length);
        ModTimings.TakeWorstMs(_worstMs);
        var text = new System.Text.StringBuilder(" Mod ms per frame by part, average/worst frame:");
        for (var i = 0; i < _sectionMs.Length; i++)
        {
            text.Append(i == 0 ? " " : ", ").Append(PerfReport.SectionHeads[i]).Append(' ')
                .Append((_sectionMs[i] / frames).ToString("0.000", CultureInfo.InvariantCulture)).Append('/')
                .Append(_worstMs[i].ToString("0.00", CultureInfo.InvariantCulture));
        }

        return text.Append('.').ToString();
    }

    /// <summary>After each of the game's map refreshes, which it raises from its own update, outside the plugin's frame.</summary>
    public void AddRefresh(long start)
    {
        if (start == 0L)
        {
            return;
        }

        var ms = Elapsed(start);
        CountAllocation();
        Phase?.AddRefresh(ms);
        if (_windowStart >= 0f)
        {
            _window.AddRefresh(ms);
        }
    }

    /// <summary>The managed heap's growth since <see cref="Start"/>. A collection in between shrinks it, and counts nothing.</summary>
    private void CountAllocation()
    {
        var grown = Profiler.GetMonoUsedSizeLong() - _monoAtStart;
        if (grown > 0)
        {
            _frameAllocated += grown;
        }
    }

    private static float Elapsed(long start) => (float)((Stopwatch.GetTimestamp() - start) * MsPerTick);
}
