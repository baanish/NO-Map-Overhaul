using System.Collections.Generic;
using System.Globalization;
using BaanishUiImprovements.MapTools;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.Diagnostics;

/// <summary>
/// The Run perf test button in F1. One press measures the whole game's frame rate in six phases: the minimap with the
/// mod off, on, and on with <see cref="StressDrawings"/>, then the same on the full map. Each phase waits
/// <see cref="SettleSeconds"/> before measuring, so the switch (graphics rebuilt, the map opened) stays out of the numbers.
/// The mod-off phases override General.Enabled through <see cref="ModOn"/>, and the drawings show through
/// <see cref="ModSettings.PerfTestShowsDrawings"/>, instead of writing the settings, so a crash mid-test can't leave
/// any of them saved. Afterwards the player's drawings come back with their undo history, along with the map.
/// Leaving the mission, losing the aircraft, opening or closing the map, or a second press stops it early and puts
/// everything back the same way.
/// </summary>
internal sealed class PerfTest
{
    /// <summary>Time to close F1, whose window costs frames too.</summary>
    private const float FirstSettleSeconds = 5f;

    private const float SettleSeconds = 2f;
    private const float MeasureSeconds = 10f;

    /// <summary>How far ahead of the aircraft <c>DynamicMap.CenterMinimizedMap</c> centres the minimap.</summary>
    private const float MinimapLead = 4000f;

    /// <summary>For a minimap whose size can't be read.</summary>
    private const float FallbackRadius = 8000f;

    private const int MaxAnchors = 40;

    private static readonly (string Name, bool FullMap, bool ModOn, bool Stress, int Baseline)[] Phases =
    {
        ("Minimap, mod off", false, false, false, -1),
        ("Minimap, mod on", false, true, false, 0),
        ("Minimap, mod on, heavy drawings", false, true, true, 0),
        ("Full map, mod off", true, false, false, -1),
        ("Full map, mod on", true, true, false, 3),
        ("Full map, mod on, heavy drawings", true, true, true, 3),
    };

    private readonly ModSettings _settings;
    private readonly PerformanceLog _performance;
    private readonly MapToolContext _context;
    private readonly ManualLogSource _log;
    private readonly FrameStats _stats = new();
    private readonly RenderCounters _render = new();
    private readonly List<PerfPhase> _results = new();
    private List<MapShape>? _stress;
    private int _stressUnits;
    private int _stressShown;
    private ShapeStore.Saved? _savedShapes;
    private DynamicMap? _map;
    private bool _mapWasOpen;
    private int _phase = -1;
    private float _phaseStart;
    private bool _requested;

    public PerfTest(ModSettings settings, PerformanceLog performance, MapToolContext context, ManualLogSource log)
    {
        _settings = settings;
        _performance = performance;
        _context = context;
        _log = log;
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
    public bool? ModOn => _phase >= 0 ? Phases[_phase].ModOn : null;

    /// <summary>Per frame, first thing in the plugin's guarded update. Between phase switches it only compares a few numbers.</summary>
    public void Update()
    {
        if (_requested)
        {
            _requested = false;
            if (_phase >= 0)
            {
                Stop("cancelled");
            }
            else
            {
                Begin();
            }

            return;
        }

        if (_phase < 0)
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

        var phase = Phases[_phase];
        if (DynamicMap.mapMaximized != phase.FullMap)
        {
            Stop("the map was opened or closed");
            return;
        }

        var elapsed = Time.realtimeSinceStartup - _phaseStart;
        var settle = _phase == 0 ? FirstSettleSeconds : SettleSeconds;
        if (_performance.Phase == null)
        {
            if (elapsed >= settle)
            {
                _stats.Clear();
                _render.Clear();
                _performance.Phase = _stats;
            }

            return;
        }

        _render.Sample();
        if (elapsed < settle + MeasureSeconds)
        {
            return;
        }

        _performance.Phase = null;
        _results.Add(new PerfPhase(phase.Name, _stats.Summarize(), phase.Baseline, _render.Summarize()));
        if (_phase + 1 < Phases.Length)
        {
            StartPhase(_phase + 1);
        }
        else
        {
            Finish(null);
        }
    }

    /// <summary>The plugin is shutting down: drawings, and with <paramref name="moveMap"/> the map, come back.</summary>
    public void Shutdown(bool moveMap) => Restore(moveMap);

    private void DrawButton(ConfigEntryBase entry)
    {
        var label = _phase >= 0
            ? "Cancel perf test (phase " + (_phase + 1).ToString(CultureInfo.InvariantCulture) + " of " + Phases.Length.ToString(CultureInfo.InvariantCulture) + ")"
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
        _savedShapes = _context.Shapes.Save();
        _stress = null;
        _results.Clear();
        _render.Start();
        _log.LogInfo("Perf test started.");
        Tell("Perf test running for about 75 seconds. Close F1, and hold the view still.");
        StartPhase(0);
    }

    private void StartPhase(int index)
    {
        _phase = index;
        _phaseStart = Time.realtimeSinceStartup;
        var phase = Phases[index];
        if (phase.FullMap != DynamicMap.mapMaximized)
        {
            if (phase.FullMap)
            {
                _map!.Maximize();
            }
            else
            {
                _map!.Minimize();
            }
        }

        if (phase.FullMap && !DynamicMap.mapMaximized)
        {
            Finish("The game didn't open the full map (DynamicMap.AllowedToOpen was off), so only the minimap was measured.");
            return;
        }

        _context.Shapes.Reset();
        if (phase.Stress)
        {
            _stress ??= BuildStress(_map!);
            foreach (var shape in _stress)
            {
                _context.Shapes.Add(shape); // the store's caps turn away what doesn't fit
            }

            _stressShown = _context.Shapes.Shapes.Count;
        }
    }

    private void Finish(string? note)
    {
        var drawings = string.Format(CultureInfo.InvariantCulture, "{0} shapes, anchored to {1} live units", _stressShown, _stressUnits);
        var table = PerfReport.Table(_results, SettleSeconds, MeasureSeconds, drawings);
        _log.LogInfo(note == null ? table : table + "\n" + note);
        Restore(moveMap: true);
        Tell(PerfReport.Summary(_results));
    }

    private void Stop(string reason)
    {
        Restore(moveMap: true);
        _log.LogInfo("Perf test stopped: " + reason + ".");
        Tell("Perf test stopped: " + reason + ".");
    }

    private void Restore(bool moveMap)
    {
        if (_phase < 0)
        {
            return;
        }

        _phase = -1;
        _performance.Phase = null;
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
        _stress = null;
    }

    /// <summary>Centred where the minimap is, and sized to fit it, so every drawing is on screen on both maps.</summary>
    private List<MapShape> BuildStress(DynamicMap map)
    {
        var aircraft = PlayerAircraft()!;
        var global = aircraft.GlobalPosition();
        var nose = aircraft.transform.forward;
        var forward = new FlatVector(nose.x, nose.z);
        var center = new FlatVector(global.x, global.z) +
                     (forward.LengthSquared() > 0f ? FlatVector.Normalize(forward) * MinimapLead : FlatVector.Zero);
        var radius = MinimapRadius(map);
        var units = NearbyUnits(center, radius, aircraft.persistentID.Id);
        _stressUnits = units.Count;
        var shapes = StressDrawings.Build(center, radius, units, _settings.MapToolMaxPenPoints.Value, _context.GroundElevation);
        _log.LogInfo(string.Format(CultureInfo.InvariantCulture,
            "Perf test drawings: {0} shapes in {1:0.0} km around the minimap's centre, {2} live units anchored.",
            shapes.Count, radius / 1000f, units.Count));
        return shapes;
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
