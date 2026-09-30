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
/// The perf test's rendering columns, per frame over each condition's slices. Unity's own counters come from ProfilerRecorder, looked
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
    private readonly double[,] _sums;
    private readonly bool[] _recorded = new bool[StatNames.Length];
    private readonly double[] _canvasMs;
    private readonly int[] _frames;
    private readonly (int Graphics, int Texts)[] _counts;
    private bool _timed;

    public RenderCounters(int conditions)
    {
        _sums = new double[conditions, StatNames.Length];
        _canvasMs = new double[conditions];
        _frames = new int[conditions];
        _counts = new (int, int)[conditions];
    }

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
        System.Array.Clear(_canvasMs, 0, _canvasMs.Length);
        System.Array.Clear(_frames, 0, _frames.Length);
        System.Array.Clear(_counts, 0, _counts.Length);
        _canvasTicks = 0;
    }

    /// <summary>A slice starts measuring: the canvas time of the frames that settled it is dropped.</summary>
    public void BeginSlice() => _canvasTicks = 0;

    /// <summary>Once per measured frame, into the condition being measured. Reads the frame before, the last one the player loop finished.</summary>
    public void Sample(int condition)
    {
        _frames[condition]++;
        _canvasMs[condition] += _canvasTicks * MsPerTick;
        _canvasTicks = 0;
        for (var i = 0; i < _recorders.Length; i++)
        {
            if (_recorders[i].Valid)
            {
                var value = _recorders[i].LastValue;
                _sums[condition, i] += i < Markers ? value / 1e6 : value;
                _recorded[i] |= value != 0;
            }
        }
    }

    /// <summary>Counts the mod's graphics now on screen or culled by a mask, for the condition showing. Searches the scene, so call it once per slice.</summary>
    public void CountGraphics(int condition) => _counts[condition] = CountModGraphics();

    /// <summary>Averages per frame over the condition's slices, with NaN for a stat this build never recorded.</summary>
    public RenderSummary Summarize(int condition)
    {
        var frames = _frames[condition];
        var averages = new float[StatNames.Length];
        for (var i = 0; i < averages.Length; i++)
        {
            averages[i] = frames > 0 && _recorded[i] ? (float)(_sums[condition, i] / frames) : float.NaN;
        }

        var (graphics, texts) = _counts[condition];
        return new RenderSummary(_timed && frames > 0 ? (float)(_canvasMs[condition] / frames) : float.NaN, averages[0], averages[1],
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
