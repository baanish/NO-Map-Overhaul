using System.Collections.Generic;
using System.Reflection;
using BaanishUiImprovements.Airbases;
using BaanishUiImprovements.Diagnostics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.Missiles;
using BaanishUiImprovements.Runways;
using BepInEx;
using BepInEx.Configuration;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements;

/// <summary>
/// Client-side HUD and map additions. Nothing in the game is patched: the plugin reads the game's own singletons
/// (DynamicMap, CombatHUD, the local faction's airbases) and draws on top, so a game update that moves
/// something fails loudly in the log instead of corrupting game state. Its one patch is on NOAutopilot
/// (<see cref="AutopilotRightClickPatch"/>), which it soft-depends on so that NOAutopilot is loaded before Awake looks for it.
/// Map work runs on the game's own 10 Hz map refresh (<see cref="DynamicMap.onMapChanged"/>), the same
/// cadence as its airbase icons. Only what must track the camera or the turning minimap runs every frame.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(AutopilotRightClickPatch.AutopilotGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.baanish.nuclearoption.uiimprovements";
    public const string PluginName = "Baanish UI Improvements";
    public const string PluginVersion = "0.5.0";

    /// <summary>Below this radar altitude the aircraft is on the ground: the game's own landing guide treats touchdown the same way.</summary>
    private const float AirborneRadarAlt = 1f;

    private readonly List<Airbase> _airbases = new();
    private readonly List<Airbase.Runway> _runways = new();
    private readonly List<RunwayLine> _runwayLines = new();
    private ModSettings _settings = null!;
    private RunwayMapOverlay _mapOverlay = null!;
    private RunwayHudCallout _hudCallout = null!;
    private AirbaseBoundaryOverlay _boundaryOverlay = null!;
    private AirbaseLabelOverlay _labelOverlay = null!;
    private IncomingMissileArrows _missileArrows = null!;
    private MapToolHost _mapTools = null!;
    private PerformanceLog _performance = null!;
    private PerfTest _perfTest = null!;
    private readonly List<Airbase> _namedAirbases = new();
    private readonly HashSet<string> _airbaseNames = new();
    private Airbase.Runway.RunwayUsage? _approach;
    private Aircraft? _checkedAircraft;
    private bool _checkedIsHelicopter;

    private void Awake()
    {
        MoveSavedSettings();
        _settings = new ModSettings(Config);
        _mapOverlay = new RunwayMapOverlay(_settings);
        _hudCallout = new RunwayHudCallout(_settings);
        _boundaryOverlay = new AirbaseBoundaryOverlay(_settings);
        _labelOverlay = new AirbaseLabelOverlay(_settings);
        _missileArrows = new IncomingMissileArrows(_settings);
        _mapTools = new MapToolHost(_settings);
        _performance = new PerformanceLog(_settings, Logger);
        _perfTest = new PerfTest(_settings, _performance, _mapTools, Logger);
        DynamicMap.onMapChanged += OnMapChanged;
        if (!RunwayHudCallout.LabelFieldFound)
        {
            Logger.LogWarning("AirbaseOverlay.airbaseLabel is missing in this game version: no HUD runway callout, and map numbers use the default font.");
        }

        if (!IncomingMissileArrows.ArrowFieldFound)
        {
            Logger.LogWarning("CombatHUD.targetArrow is missing in this game version: no missile arrows.");
        }

        AutopilotRightClickPatch.Apply(_mapTools, Logger);

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    /// <summary>
    /// Runs before anything is bound, while BepInEx still holds every saved value as an unclaimed ("orphaned") entry, so
    /// each moved setting then picks up its value from the new place as it binds. BepInEx keeps that dictionary private.
    /// </summary>
    private void MoveSavedSettings()
    {
        var orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Config);
        if (orphans is not IDictionary<ConfigDefinition, string> saved)
        {
            Logger.LogWarning("Can't read the saved settings in this BepInEx version, so settings saved by 0.4.0 go back to their defaults.");
            return;
        }

        var moved = SettingsMigration.MoveSavedValues(saved, (section, key) => new ConfigDefinition(section, key));
        if (moved > 0)
        {
            Logger.LogInfo($"Moved {moved} saved settings to their new sections.");
        }
    }

    /// <summary>
    /// Per frame: the HUD label and missile arrows follow the camera, the numbers counter-rotate the heading-up minimap,
    /// and the map tools take input and redraw what changed.
    /// </summary>
    private void LateUpdate()
    {
        var start = _performance.Start();
        Guard(() =>
        {
            _perfTest.Update();
            if (!ModOn)
            {
                // A no-op once everything is gone, so the switch takes effect the frame it flips in F1.
                RemoveOverlays();
                return;
            }

            if (SceneSingleton<DynamicMap>.i == null)
            {
                _approach = null; // the runways it points at went with the scene
            }

            var runways = ModTimings.Start();
            _hudCallout.Render(_approach);
            _mapOverlay.KeepLabelsUpright();
            ModTimings.Stop(ModSection.RunwayOverlays, runways);
            var missiles = ModTimings.Start();
            _missileArrows.Render();
            ModTimings.Stop(ModSection.MissileArrows, missiles);
            _mapTools.Update(_hudCallout.HudStyle);
        });
        _performance.AddFrame(start, ModOn);
    }

    /// <summary>General.Enabled, unless the perf test is switching the mod on and off.</summary>
    private bool ModOn => _perfTest.ModOn ?? _settings.Enabled.Value;

    /// <summary>Raised from inside the game's DynamicMap.Update, so it must never throw back into the game.</summary>
    private void OnMapChanged()
    {
        var start = _performance.Start();
        Guard(RefreshMap);
        _performance.AddRefresh(start);
    }

    /// <summary>A throw disables the plugin rather than repeating every frame or breaking the game's map.</summary>
    private void Guard(System.Action action)
    {
        if (!enabled)
        {
            return;
        }

        try
        {
            action();
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Disabled after an error; the game is unaffected. {exception}");
            enabled = false;
            try
            {
                Shutdown(gameQuitting: false);
            }
            catch (System.Exception cleanup)
            {
                Logger.LogError($"Couldn't undo everything after that error. {cleanup}");
            }
        }
    }

    private void OnDestroy() => Shutdown(gameQuitting: true);

    /// <summary>As the game quits, its scene may be half torn down, so the perf test leaves the map alone.</summary>
    private void Shutdown(bool gameQuitting)
    {
        DynamicMap.onMapChanged -= OnMapChanged;
        AutopilotRightClickPatch.Remove();
        RemoveOverlays();
        _perfTest.Shutdown(moveMap: !gameQuitting);
    }

    /// <summary>Each overlay rebuilds itself on its next render, so turning the mod back on needs nothing more.</summary>
    private void RemoveOverlays()
    {
        _mapOverlay.Reset();
        _hudCallout.Reset();
        _boundaryOverlay.Reset();
        _labelOverlay.Reset();
        _missileArrows.Reset();
        _mapTools.Reset();
        _approach = null;
    }

    private void RefreshMap()
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null || !ModOn)
        {
            return;
        }

        var hud = SceneSingleton<CombatHUD>.i;
        var aircraft = hud != null ? hud.aircraft : null;
        var rotary = IsHelicopter(aircraft);

        CollectFriendlyAirbases(map.HQ, includeRunways: !rotary);
        _approach = SelectApproach(aircraft, rotary);
        _mapOverlay.Render(map, _runways, _approach, _hudCallout.HudStyle);

        // The boundary keeps the icon layer's first slot and the names the one after it, so both draw under the runways.
        CollectNamedAirbases();
        _labelOverlay.Render(map, _namedAirbases, _hudCallout.HudStyle);
        _boundaryOverlay.Render(map, _airbases);
    }

    /// <summary>
    /// Our airbases, carriers left out: every one the game marks with an icon on the full map, since
    /// <c>DynamicMap.RefreshAirbases</c> reads the same list. That's the map's own, ones the mission editor placed, and
    /// ones captured. The game marks one the mission switched off too, so it's named, but one in use claims a shared
    /// name first. One label per name: Ignus free flight stacks three airbases named "Feldspar International Airport"
    /// around one field. Skips any without a centre to place a label at or a name to show, so it never claims a name
    /// another could show. Other sides' airbases are never named: the game doesn't mark them.
    /// </summary>
    private void CollectNamedAirbases()
    {
        _namedAirbases.Clear();
        _airbaseNames.Clear();
        AddNamed(disabled: false);
        AddNamed(disabled: true);
    }

    private void AddNamed(bool disabled)
    {
        foreach (var airbase in _airbases)
        {
            if (airbase.disabled == disabled && airbase.center != null && AirbaseLabelOverlay.NameOf(airbase) is { } name &&
                _airbaseNames.Add(name))
            {
                _namedAirbases.Add(airbase);
            }
        }
    }

    /// <summary>
    /// Re-read on every map refresh so captured or lost airbases need no event wiring. Carriers are skipped: a ship has
    /// one deck, its unit icon already marks it, and its runways carry names rather than headings.
    /// Helicopters still get airbases, for the landing-zone boundary, but no runways. Takeoff-only runways are
    /// skipped too: the one in the stock game (Harmony Sands' "Runway14L Short") lies on top of a landing runway.
    /// </summary>
    private void CollectFriendlyAirbases(FactionHQ? hq, bool includeRunways)
    {
        _airbases.Clear();
        _runways.Clear();
        if (hq == null)
        {
            return;
        }

        foreach (var airbase in hq.GetAirbases())
        {
            if (airbase.AttachedAirbase)
            {
                continue;
            }

            _airbases.Add(airbase);
            if (!includeRunways || airbase.runways == null)
            {
                continue;
            }

            foreach (var runway in airbase.runways)
            {
                if (runway?.Start != null && runway.End != null && runway.Landing)
                {
                    _runways.Add(runway);
                }
            }
        }
    }

    /// <summary>
    /// Helicopters fly with <see cref="HeloControlsFilter"/>. So does the Tarantula tiltrotor, which also lands on
    /// runways, so an aircraft with a <see cref="TiltWingController"/> counts as a plane. Cached per aircraft.
    /// </summary>
    private bool IsHelicopter(Aircraft? aircraft)
    {
        if (aircraft == null)
        {
            return false;
        }

        if (!ReferenceEquals(aircraft, _checkedAircraft))
        {
            _checkedAircraft = aircraft;
            _checkedIsHelicopter = aircraft.GetControlsFilter() is HeloControlsFilter &&
                                   aircraft.GetComponentInChildren<TiltWingController>(true) == null;
        }

        return _checkedIsHelicopter;
    }

    private Airbase.Runway.RunwayUsage? SelectApproach(Aircraft? aircraft, bool rotary)
    {
        if (aircraft == null || rotary || aircraft.disabled || aircraft.radarAlt < AirborneRadarAlt)
        {
            return null;
        }

        _runwayLines.Clear();
        foreach (var runway in _runways)
        {
            _runwayLines.Add(new RunwayLine(Flat(runway.Start), Flat(runway.End)));
        }

        var choice = ApproachSelector.Select(_runwayLines, Flat(aircraft.transform), _settings.ApproachRangeKm.Value * 1000f);
        return choice is { } c ? new Airbase.Runway.RunwayUsage(_runways[c.RunwayIndex], c.Reverse) : null;
    }

    private static FlatVector Flat(UnityEngine.Transform transform)
    {
        var global = transform.GlobalPosition();
        return new FlatVector(global.x, global.z);
    }
}
