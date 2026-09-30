using System.Collections.Generic;
using System.Globalization;
using NoMapOverhaul.MapTools;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// The Run perf test button in F1. One press measures the whole game's frame rate in eight conditions: the minimap with
/// the mod off, on, on with typical drawings, and on with heavy drawings (both from <see cref="StressDrawings"/>), then
/// the same on the full map. Within each view the four conditions take turns in short slices
/// (<see cref="PerfSchedule"/>), so drift while flying spreads over all four,
/// and each slice waits <see cref="SliceSettleSeconds"/> after a switch so the switch (graphics rebuilt, the map
/// opened) stays out of the numbers. The mod-off conditions override General.Enabled through <see cref="ModOn"/>, and
/// the drawings show through <see cref="ModSettings.PerfTestShowsDrawings"/>, instead of writing the settings, so a
/// crash mid-test can't leave any of them saved. Afterwards the player's drawings come back with their undo history,
/// along with the map. Leaving the mission, losing the aircraft, opening or closing the map, or a second press stops it
/// early and puts everything back the same way.
/// </summary>
internal sealed class PerfTest
{
    /// <summary>Time to close F1, whose window costs frames too.</summary>
    private const float FirstSettleSeconds = 5f;

    /// <summary>After opening the full map.</summary>
    private const float ViewSettleSeconds = 2f;

    private const float SliceSettleSeconds = 0.5f;
    private const float SliceSeconds = 2f;

    /// <summary>How far ahead of the aircraft <c>DynamicMap.CenterMinimizedMap</c> centres the minimap.</summary>
    private const float MinimapLead = 4000f;

    /// <summary>For a minimap whose size can't be read.</summary>
    private const float FallbackRadius = 8000f;

    /// <summary>The most live units the drawings ride on, so runs in busy and quiet places redraw about the same amount.</summary>
    private const int MaxAnchors = 10;

    private static readonly (string Name, bool FullMap, bool ModOn, DrawingSet Drawings)[] Conditions =
    {
        ("Minimap, mod off", false, false, DrawingSet.None),
        ("Minimap, mod on", false, true, DrawingSet.None),
        ("Minimap, mod on, typical drawings", false, true, DrawingSet.Typical),
        ("Minimap, mod on, heavy drawings", false, true, DrawingSet.Heavy),
        ("Full map, mod off", true, false, DrawingSet.None),
        ("Full map, mod on", true, true, DrawingSet.None),
        ("Full map, mod on, typical drawings", true, true, DrawingSet.Typical),
        ("Full map, mod on, heavy drawings", true, true, DrawingSet.Heavy),
    };

    private readonly ModSettings _settings;
    private readonly PerformanceLog _performance;
    private readonly MapToolHost _mapTools;
    private readonly MapToolContext _context;
    private readonly ManualLogSource _log;
    private readonly FrameStats[] _stats = new FrameStats[Conditions.Length];
    private readonly double[][] _sectionMs = new double[Conditions.Length][];
    private readonly double[] _settlingMs = new double[ModTimings.Count];
    private readonly RenderCounters _render = new(Conditions.Length);

    /// <summary>How many of each <see cref="DrawingSet"/>'s shapes the store took, by its value.</summary>
    private readonly int[] _shown = new int[3];

    /// <summary>The live units both sets ride on, picked at the first slice that shows drawings, and kept for the whole test.</summary>
    private List<MapPoint>? _anchors;

    private float _radius;
    private DrawingSet? _showing;
    private ShapeStore.Saved? _savedShapes;
    private DynamicMap? _map;
    private bool _mapWasOpen;
    private int _slice = -1;
    private float _sliceStart;
    private bool _measuring;
    private bool _requested;

    public PerfTest(ModSettings settings, PerformanceLog performance, MapToolHost mapTools, ManualLogSource log)
    {
        _settings = settings;
        _performance = performance;
        _mapTools = mapTools;
        _context = mapTools.Context;
        _log = log;
        for (var i = 0; i < Conditions.Length; i++)
        {
            _stats[i] = new FrameStats();
            _sectionMs[i] = new double[ModTimings.Count];
        }

        settings.PerfTestButton.CustomDrawer = DrawButton;
        settings.PerfTest.SettingChanged += (_, _) =>
        {
            if (settings.PerfTest.Value)
            {
                settings.PerfTest.Value = false;
                _requested = true;
            }
        };
    }

    /// <summary>Whether the mod runs this frame while the test is on, overriding General.Enabled; null otherwise.</summary>
    public bool? ModOn => _slice >= 0 ? Conditions[PerfSchedule.Condition(_slice)].ModOn : null;

    /// <summary>Per frame, first thing in the plugin's guarded update. Between slice switches it only compares a few numbers.</summary>
    public void Update()
    {
        if (_requested)
        {
            _requested = false;
            if (_slice >= 0)
            {
                Stop("cancelled");
            }
            else
            {
                Begin();
            }

            return;
        }

        if (_slice < 0)
        {
            return;
        }

        var map = SceneSingleton<DynamicMap>.i;
        if (map == null || !ReferenceEquals(map, _map))
        {
            Stop("the mission ended");
            return;
        }

        if (PlayerAircraft() == null)
        {
            Stop("the aircraft is gone");
            return;
        }

        var condition = PerfSchedule.Condition(_slice);
        if (DynamicMap.mapMaximized != Conditions[condition].FullMap)
        {
            Stop("the map was opened or closed");
            return;
        }

        var elapsed = Time.realtimeSinceStartup - _sliceStart;
        var settle = SettleSeconds(_slice);
        if (!_measuring)
        {
            if (elapsed >= settle)
            {
                _measuring = true;
                _render.BeginSlice();
                ModTimings.TakeMs(_settlingMs); // the settling frames' times, dropped
                _performance.Phase = _stats[condition];
            }

            return;
        }

        _render.Sample(condition);
        if (elapsed < settle + SliceSeconds)
        {
            return;
        }

        _performance.Phase = null;
        ModTimings.TakeMs(_sectionMs[condition]);
        _render.CountGraphics(condition);
        if (_slice + 1 < PerfSchedule.SliceCount)
        {
            StartSlice(_slice + 1);
        }
        else
        {
            Finish(Conditions.Length, null);
        }
    }

    /// <summary>The plugin is shutting down: drawings, and with <paramref name="moveMap"/> the map, come back.</summary>
    public void Shutdown(bool moveMap) => Restore(moveMap);

    private void DrawButton(ConfigEntryBase entry)
    {
        var label = _slice >= 0
            ? "Cancel perf test (slice " + (_slice + 1).ToString(CultureInfo.InvariantCulture) + " of " +
              PerfSchedule.SliceCount.ToString(CultureInfo.InvariantCulture) + ")"
            : "Run perf test";
        if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
        {
            _requested = true;
        }
    }

    private void Begin()
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null || PlayerAircraft() == null)
        {
            Tell("Perf test: fly an aircraft in a mission first.");
            return;
        }

        _map = map;
        _mapWasOpen = DynamicMap.mapMaximized;
        _settings.PerfTestShowsDrawings = true;
        _mapTools.TrackMission(map); // with the mod off the store may still hold a mission that has ended
        _savedShapes = _context.Shapes.Save();
        _anchors = null;
        _showing = null;
        foreach (var stats in _stats)
        {
            stats.Clear();
        }

        foreach (var sections in _sectionMs)
        {
            System.Array.Clear(sections, 0, sections.Length);
        }

        _render.Start();
        ModTimings.On = true;
        _log.LogInfo("Perf test started.");
        Tell("Perf test running for about 85 seconds. Close F1, and hold the view still.");
        StartSlice(0);
    }

    /// <summary>Long enough for F1 to close or the full map to open, and none when the condition is the one just measured.</summary>
    private static float SettleSeconds(int slice)
    {
        if (slice == 0)
        {
            return FirstSettleSeconds;
        }

        if (PerfSchedule.View(slice) != PerfSchedule.View(slice - 1))
        {
            return ViewSettleSeconds;
        }

        return PerfSchedule.Condition(slice) == PerfSchedule.Condition(slice - 1) ? 0f : SliceSettleSeconds;
    }

    private void StartSlice(int index)
    {
        _slice = index;
        _sliceStart = Time.realtimeSinceStartup;
        _measuring = false;
        var condition = Conditions[PerfSchedule.Condition(index)];
        if (condition.FullMap != DynamicMap.mapMaximized)
        {
            if (condition.FullMap)
            {
                _map!.Maximize();
            }
            else
            {
                _map!.Minimize();
            }
        }

        if (condition.FullMap && !DynamicMap.mapMaximized)
        {
            Finish(PerfSchedule.ConditionsPerView,
                "The game didn't open the full map (DynamicMap.AllowedToOpen was off), so only the minimap was measured.");
            return;
        }

        if (condition.Drawings != _showing)
        {
            _showing = condition.Drawings;
            _context.Shapes.Reset();
            if (condition.Drawings != DrawingSet.None)
            {
                foreach (var shape in BuildDrawings(_map!, condition.Drawings))
                {
                    _context.Shapes.Add(shape); // the store's caps turn away what doesn't fit
                }

                _shown[(int)condition.Drawings] = _context.Shapes.Shapes.Count;
            }
        }
    }

    /// <summary>Reports the first <paramref name="measured"/> conditions, each over all its slices.</summary>
    private void Finish(int measured, string? note)
    {
        var results = new List<PerfPhase>();
        for (var i = 0; i < measured; i++)
        {
            var summary = _stats[i].Summarize();
            var sections = new float[ModTimings.Count];
            for (var s = 0; s < sections.Length; s++)
            {
                sections[s] = summary.Frames > 0 ? (float)(_sectionMs[i][s] / summary.Frames) : 0f;
            }

            results.Add(new PerfPhase(Conditions[i].Name, summary, PerfSchedule.Baseline(i), _render.Summarize(i), sections));
        }

        var method = string.Format(CultureInfo.InvariantCulture,
            "each condition measured in {0} slices of {1:0.0} s, taking turns with the others in its view, {2:0.0} s to settle after each switch",
            PerfSchedule.Cycles, SliceSeconds, SliceSettleSeconds);
        var units = _anchors?.Count ?? 0;
        var drawings = string.Format(CultureInfo.InvariantCulture,
            "typical {0} shapes on {1} live units, heavy {2} shapes on {3} live units (at most {4})",
            _shown[(int)DrawingSet.Typical], System.Math.Min(units, StressDrawings.TypicalAnchors), _shown[(int)DrawingSet.Heavy], units, MaxAnchors);
        var table = PerfReport.Table(results, method, drawings);
        _log.LogInfo(note == null ? table : table + "\n" + note);
        Restore(moveMap: true);
        Tell(PerfReport.Summary(results));
    }

    private void Stop(string reason)
    {
        Restore(moveMap: true);
        _log.LogInfo("Perf test stopped: " + reason + ".");
        Tell("Perf test stopped: " + reason + ".");
    }

    private void Restore(bool moveMap)
    {
        if (_slice < 0)
        {
            return;
        }

        _slice = -1;
        _measuring = false;
        _performance.Phase = null;
        ModTimings.On = false;
        _render.Stop();
        _settings.PerfTestShowsDrawings = false;
        var map = SceneSingleton<DynamicMap>.i;
        if (map != null && ReferenceEquals(map, _map)) // a new mission starts with no drawings of its own
        {
            _context.Shapes.Restore(_savedShapes!);
            if (moveMap && _mapWasOpen != DynamicMap.mapMaximized)
            {
                if (_mapWasOpen)
                {
                    map.Maximize();
                }
                else
                {
                    map.Minimize();
                }
            }
        }

        _map = null;
        _savedShapes = null;
        _anchors = null;
    }

    /// <summary>
    /// The set, centred where the minimap is now and sized to fit it, so every drawing is on screen on both maps. It's
    /// built again at each switch to it, since the minimap follows the aircraft and fixed drawings built once would slide
    /// out of it, leaving later slices less to draw. The units it rides on are the ones picked for the first set.
    /// </summary>
    private List<MapShape> BuildDrawings(DynamicMap map, DrawingSet set)
    {
        var aircraft = PlayerAircraft()!;
        var global = aircraft.GlobalPosition();
        var nose = aircraft.transform.forward;
        var forward = new FlatVector(nose.x, nose.z);
        var center = new FlatVector(global.x, global.z) +
                     (forward.LengthSquared() > 0f ? FlatVector.Normalize(forward) * MinimapLead : FlatVector.Zero);
        if (_anchors == null)
        {
            _radius = MinimapRadius(map);
            _anchors = NearbyUnits(center, _radius, aircraft.persistentID.Id);
            _log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Perf test drawings in {0:0.0} km around the minimap's centre, on {1} live units.", _radius / 1000f, _anchors.Count));
        }

        return set == DrawingSet.Typical
            ? StressDrawings.BuildTypical(center, _radius, _anchors, _context.GroundElevation)
            : StressDrawings.BuildHeavy(center, _radius, _anchors, _settings.MapToolMaxPenPoints.Value, _context.GroundElevation);
    }

    /// <summary>What a condition draws, and the index of its count in <see cref="_shown"/>.</summary>
    private enum DrawingSet
    {
        None,
        Typical,
        Heavy,
    }

    /// <summary>Four fifths of half the minimap's width, in meters: its rect in world units over one map meter in world units.</summary>
    private static float MinimapRadius(DynamicMap map)
    {
        var rect = (RectTransform)map.transform;
        var halfWidth = rect.rect.width * 0.5f * rect.lossyScale.x;
        var meter = map.mapDisplayFactor * map.iconLayer.transform.lossyScale.x;
        var radius = meter > 0f ? 0.8f * halfWidth / meter : 0f;
        return radius is > 1000f and < 100000f ? radius : FallbackRadius;
    }

    /// <summary>Units the map shows within the radius, nearest first, at the positions the map shows them.</summary>
    private List<MapPoint> NearbyUnits(FlatVector center, float radius, uint ownId)
    {
        var found = new List<(float Distance, MapPoint Point)>();
        foreach (var unit in UnitRegistry.allUnits)
        {
            if (unit == null || unit.disabled || unit.persistentID.Id == ownId)
            {
                continue;
            }

            var global = unit.GlobalPosition();
            if (_context.TryResolve(new MapPoint(new FlatVector(global.x, global.z), unit.persistentID.Id), out var shown) &&
                FlatVector.Distance(shown, center) < radius)
            {
                found.Add((FlatVector.Distance(shown, center), new MapPoint(shown, unit.persistentID.Id)));
            }
        }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var units = new List<MapPoint>();
        for (var i = 0; i < found.Count && i < MaxAnchors; i++)
        {
            units.Add(found[i].Point);
        }

        return units;
    }

    private static Aircraft? PlayerAircraft()
    {
        var hud = SceneSingleton<CombatHUD>.i;
        var aircraft = hud != null ? hud.aircraft : null;
        return aircraft != null && !aircraft.disabled ? aircraft : null;
    }

    private static void Tell(string message)
    {
        var ui = SceneSingleton<GameplayUI>.i;
        if (ui != null)
        {
            ui.GameMessage(message);
        }
    }
}
