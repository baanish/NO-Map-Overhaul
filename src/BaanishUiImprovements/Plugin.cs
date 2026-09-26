using System.Collections.Generic;
using BaanishUiImprovements.Airbases;
using BaanishUiImprovements.Runways;
using BepInEx;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements;

/// <summary>
/// Client-side HUD and map additions. Nothing is patched: the plugin reads the game's own singletons
/// (DynamicMap, CombatHUD, the local faction's airbases) and draws on top, so a game update that moves
/// something fails loudly in the log instead of corrupting game state.
/// Map work runs on the game's own 10 Hz map refresh (<see cref="DynamicMap.onMapChanged"/>), the same
/// cadence as its airbase icons. Only what must track the camera or the turning minimap runs every frame.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.baanish.nuclearoption.uiimprovements";
    public const string PluginName = "Baanish UI Improvements";
    public const string PluginVersion = "0.1.0";

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
    private readonly List<Airbase> _namedAirbases = new();
    private readonly HashSet<string> _airbaseNames = new();
    private Airbase.Runway.RunwayUsage? _approach;
    private Aircraft? _checkedAircraft;
    private bool _checkedIsHelicopter;

    private void Awake()
    {
        _settings = new ModSettings(Config);
        _mapOverlay = new RunwayMapOverlay(_settings);
        _hudCallout = new RunwayHudCallout(_settings);
        _boundaryOverlay = new AirbaseBoundaryOverlay(_settings);
        _labelOverlay = new AirbaseLabelOverlay(_settings);
        DynamicMap.onMapChanged += OnMapChanged;
        if (!RunwayHudCallout.LabelFieldFound)
        {
            Logger.LogWarning("AirbaseOverlay.airbaseLabel is missing in this game version: no HUD runway callout, and map numbers use the default font.");
        }

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    /// <summary>Per frame: the HUD label follows the camera and the numbers counter-rotate the heading-up minimap.</summary>
    private void LateUpdate() => Guard(() =>
    {
        if (SceneSingleton<DynamicMap>.i == null)
        {
            _approach = null; // the runways it points at went with the scene
        }

        _hudCallout.Render(_approach);
        _mapOverlay.KeepLabelsUpright();
    });

    /// <summary>Raised from inside the game's DynamicMap.Update, so it must never throw back into the game.</summary>
    private void OnMapChanged() => Guard(RefreshMap);

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
            OnDestroy();
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        DynamicMap.onMapChanged -= OnMapChanged;
        _mapOverlay.Reset();
        _hudCallout.Reset();
        _boundaryOverlay.Reset();
        _labelOverlay.Reset();
        _approach = null;
    }

    private void RefreshMap()
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null)
        {
            return;
        }

        var hud = SceneSingleton<CombatHUD>.i;
        var aircraft = hud != null ? hud.aircraft : null;
        var rotary = IsHelicopter(aircraft);

        CollectFriendlyAirbases(map.HQ, includeRunways: !rotary);
        _approach = SelectApproach(aircraft, rotary);
        _mapOverlay.Render(map, _runways, _approach, _hudCallout.HudStyle);

        // Both layers take the first slot each refresh, so the one rendered last draws lowest: boundary, then names.
        CollectNamedAirbases();
        _labelOverlay.Render(map, _namedAirbases, _hudCallout.HudStyle);
        _boundaryOverlay.Render(map, _airbases);
    }

    /// <summary>
    /// Every faction's airbases, not just ours, one per name: Ignus free flight stacks three airbases named
    /// "Feldspar International Airport" around one field. Friendly ones claim their name first, so the label lands
    /// under the game's icon. Skips carriers, airbases the mission switched off, and any without a centre to place a label at,
    /// so an unplaceable airbase never claims a name another could show.
    /// </summary>
    private void CollectNamedAirbases()
    {
        _namedAirbases.Clear();
        _airbaseNames.Clear();
        AddNamed(_airbases);
        AddNamed(FactionRegistry.airbaseLookup.Values);
    }

    private void AddNamed(IEnumerable<Airbase> airbases)
    {
        foreach (var airbase in airbases)
        {
            if (airbase != null && !airbase.AttachedAirbase && !airbase.disabled && airbase.center != null &&
                airbase.SavedAirbase != null && _airbaseNames.Add(airbase.SavedAirbase.DisplayName))
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
