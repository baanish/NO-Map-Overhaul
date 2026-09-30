using System.Collections.Generic;
using System.Diagnostics;
using TMPro;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaanishUiImprovements.Diagnostics;

/// <summary>
/// The perf test's rendering columns, per frame over a phase. Unity's own counters come from ProfilerRecorder, looked
/// up by name at runtime, since a release build of the game records only some of them. Canvas time the mod measures
/// itself, with timestamps around the two player loop steps where Unity rebuilds and batches every canvas
/// (<c>PostLateUpdate.PlayerUpdateCanvases</c> and <c>PlayerEmitCanvasGeometry</c>), which works in any build. That
/// time covers the game's canvases too, so like the frame rate it is compared with the mod off.
/// </summary>
internal sealed class RenderCounters
{
    /// <summary>In <see cref="RenderSummary"/>'s order after the canvas time. Markers first, then render counters.</summary>
    private static readonly string[] StatNames =
    {
        "Canvas.BuildBatch", "Canvas.SendWillRenderCanvases", "Batches Count", "SetPass Calls Count", "Draw Calls Count",
        "Vertices Count",
    };

    /// <summary>The first <see cref="StatNames"/> entries that are markers, recorded in nanoseconds.</summary>
    private const int Markers = 2;

    private const string ModPrefix = "Baanish";

    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;
    private static long _stepStart;
    private static long _canvasTicks;

    private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[StatNames.Length];
    private readonly double[] _sums = new double[StatNames.Length];
    private readonly bool[] _recorded = new bool[StatNames.Length];
    private double _canvasMs;
    private bool _timed;
    private int _frames;

    /// <summary>Starts the recorders and the canvas timer. Found once per test, since listing every stat is slow.</summary>
    public void Start()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var handle in handles)
        {
            var index = System.Array.IndexOf(StatNames, ProfilerRecorderHandle.GetDescription(handle).Name);
            if (index >= 0 && !_recorders[index].Valid)
            {
                _recorders[index] = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default);
                _recorders[index].Start();
            }
        }

        _timed = TimeCanvases(true);
        Clear();
    }

    public void Stop()
    {
        for (var i = 0; i < _recorders.Length; i++)
        {
            _recorders[i].Dispose();
            _recorders[i] = default;
        }

        TimeCanvases(false);
        _timed = false;
    }

    public void Clear()
    {
        System.Array.Clear(_sums, 0, _sums.Length);
        System.Array.Clear(_recorded, 0, _recorded.Length);
        _canvasMs = 0;
        _canvasTicks = 0;
        _frames = 0;
    }

    /// <summary>Once per measured frame. Reads the frame before, the last one the player loop finished.</summary>
    public void Sample()
    {
        _frames++;
        _canvasMs += _canvasTicks * MsPerTick;
        _canvasTicks = 0;
        for (var i = 0; i < _recorders.Length; i++)
        {
            if (_recorders[i].Valid)
            {
                var value = _recorders[i].LastValue;
                _sums[i] += i < Markers ? value / 1e6 : value;
                _recorded[i] |= value != 0;
            }
        }
    }

    /// <summary>
    /// Averages per frame, with NaN for a stat this build never recorded, plus a count of the mod's graphics now on
    /// screen or culled by a mask. Searches the scene, so call it once per phase.
    /// </summary>
    public RenderSummary Summarize()
    {
        var averages = new float[StatNames.Length];
        for (var i = 0; i < averages.Length; i++)
        {
            averages[i] = _frames > 0 && _recorded[i] ? (float)(_sums[i] / _frames) : float.NaN;
        }

        var (graphics, texts) = CountModGraphics();
        return new RenderSummary(_timed && _frames > 0 ? (float)(_canvasMs / _frames) : float.NaN, averages[0], averages[1],
            averages[2], averages[3], averages[4], averages[5], graphics, texts);
    }

    /// <summary>Enabled graphics under an object named Baanish…, which every root the mod creates is.</summary>
    private static (int Graphics, int Texts) CountModGraphics()
    {
        var graphics = 0;
        var texts = 0;
        foreach (var graphic in Object.FindObjectsOfType<Graphic>())
        {
            if (!graphic.enabled)
            {
                continue;
            }

            for (var parent = graphic.transform; parent != null; parent = parent.parent)
            {
                if (parent.name.StartsWith(ModPrefix, System.StringComparison.Ordinal))
                {
                    graphics++;
                    texts += graphic is TMP_Text ? 1 : 0;
                    break;
                }
            }
        }

        return (graphics, texts);
    }

    /// <summary>
    /// Adds or removes timestamps around Unity's canvas steps in the current player loop, keeping whatever else other
    /// mods put there. False when neither step was found.
    /// </summary>
    private static bool TimeCanvases(bool on)
    {
        var root = PlayerLoop.GetCurrentPlayerLoop();
        var systems = root.subSystemList;
        var wrapped = 0;
        for (var i = 0; i < systems.Length; i++)
        {
            if (systems[i].type != typeof(PostLateUpdate))
            {
                continue;
            }

            var steps = new List<PlayerLoopSystem>(systems[i].subSystemList);
            steps.RemoveAll(step => step.type == typeof(CanvasStepStart) || step.type == typeof(CanvasStepEnd));
            if (on)
            {
                wrapped += Wrap(steps, typeof(PostLateUpdate.PlayerUpdateCanvases));
                wrapped += Wrap(steps, typeof(PostLateUpdate.PlayerEmitCanvasGeometry));
            }

            systems[i].subSystemList = steps.ToArray();
        }

        PlayerLoop.SetPlayerLoop(root);
        return wrapped > 0;
    }

    private static int Wrap(List<PlayerLoopSystem> steps, System.Type step)
    {
        var at = steps.FindIndex(system => system.type == step);
        if (at < 0)
        {
            return 0;
        }

        steps.Insert(at + 1, new PlayerLoopSystem { type = typeof(CanvasStepEnd), updateDelegate = () => _canvasTicks += Stopwatch.GetTimestamp() - _stepStart });
        steps.Insert(at, new PlayerLoopSystem { type = typeof(CanvasStepStart), updateDelegate = () => _stepStart = Stopwatch.GetTimestamp() });
        return 1;
    }

    /// <summary>Names the timestamps in the player loop, so they can be found and removed again.</summary>
    private struct CanvasStepStart
    {
    }

    private struct CanvasStepEnd
    {
    }
}
