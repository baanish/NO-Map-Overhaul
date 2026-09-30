using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine;

namespace BaanishUiImprovements.Diagnostics;

/// <summary>
/// The LogPerformance setting: times the plugin's per-frame work and its map refreshes with <see cref="Stopwatch"/>,
/// and every <see cref="WindowSeconds"/> of real time logs them next to the game's own frame time
/// (<see cref="FrameSummary.LogLine"/>). The mod's times cover its scripts only; what Unity spends drawing the mod's
/// graphics shows up in the frame time alone, which is why an A/B with General.Enabled compares frame times.
/// Off, it reads no clock and keeps nothing.
/// </summary>
internal sealed class PerformanceLog
{
    private const float WindowSeconds = 5f;

    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    private readonly ModSettings _settings;
    private readonly ManualLogSource _log;
    private readonly FrameStats _window = new();
    private float _windowStart = -1f;

    public PerformanceLog(ModSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>While the perf test measures a phase, it gets every sample too, whether or not LogPerformance is on.</summary>
    public FrameStats? Phase { get; set; }

    /// <summary>A timestamp to pass back to <see cref="AddFrame"/> or <see cref="AddRefresh"/>.</summary>
    public long Start() => _settings.LogPerformance.Value || Phase != null ? Stopwatch.GetTimestamp() : 0L;

    /// <summary>Once per frame, after the plugin's per-frame work.</summary>
    public void AddFrame(long start, bool modOn)
    {
        if (start == 0L)
        {
            _windowStart = -1f; // a later switch-on starts a fresh window
            return;
        }

        var modMs = Elapsed(start);
        var frameMs = Time.unscaledDeltaTime * 1000f;
        Phase?.AddFrame(frameMs, modMs, modOn);
        if (!_settings.LogPerformance.Value)
        {
            _windowStart = -1f;
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (_windowStart < 0f)
        {
            _window.Clear();
            _windowStart = now;
        }

        _window.AddFrame(frameMs, modMs, modOn);
        if (now - _windowStart >= WindowSeconds)
        {
            _log.LogInfo(_window.Summarize().LogLine());
            _window.Clear();
            _windowStart = now;
        }
    }

    /// <summary>After each of the game's map refreshes, which it raises from its own update, outside the plugin's frame.</summary>
    public void AddRefresh(long start)
    {
        if (start == 0L)
        {
            return;
        }

        var ms = Elapsed(start);
        Phase?.AddRefresh(ms);
        if (_windowStart >= 0f)
        {
            _window.AddRefresh(ms);
        }
    }

    private static float Elapsed(long start) => (float)((Stopwatch.GetTimestamp() - start) * MsPerTick);
}
