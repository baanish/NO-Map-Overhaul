using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace NoMapOverhaul.Runways;

/// <summary>
/// Pins "RWY 27" over the threshold of the nearest runway end, so an ATC call can be read
/// straight off the HUD. The label is a clone of the game's own airbase label from <see cref="AirbaseOverlay"/>,
/// so it shares the HUD canvas, font, and visibility (it hides with the HUD and when the map is open).
/// </summary>
internal sealed class RunwayHudCallout
{
    private const float BelowThreshold = 30f;

    private static readonly FieldInfo? AirbaseLabelField = AccessTools.DeclaredField(typeof(AirbaseOverlay), "airbaseLabel");

    private readonly ModSettings _settings;
    private AirbaseOverlay? _overlay;
    private TextMeshProUGUI? _label;
    private float _nextSearchTime;
    private Airbase.Runway? _textRunway;
    private bool _textReverse;
    private bool _textWithName;

    public RunwayHudCallout(ModSettings settings) => _settings = settings;

    /// <summary>False when this game version has no <c>AirbaseOverlay.airbaseLabel</c>: no callout, and map numbers keep TextMeshPro's default font.</summary>
    public static bool LabelFieldFound => AirbaseLabelField is not null;

    /// <summary>The game's HUD label style (font, material, weight), borrowed by the map labels so both read the same.</summary>
    public TextMeshProUGUI? HudStyle => _label;

    public void Render(Airbase.Runway.RunwayUsage? approach)
    {
        if (!EnsureLabel())
        {
            return;
        }

        var camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
        if (!_settings.HudCallout.Value || approach is not { } usage || camera == null || !GearAllowsCallout())
        {
            _label!.enabled = false;
            return;
        }

        var threshold = usage.GetStart();
        if (threshold == null)
        {
            _label!.enabled = false;
            return;
        }

        var screen = camera.WorldToScreenPoint(threshold.position);
        if (screen.z <= 0f)
        {
            _label!.enabled = false;
            return;
        }

        var withName = _settings.HudAirbaseName.Value;
        if (!ReferenceEquals(_textRunway, usage.Runway) || _textReverse != usage.Reverse || _textWithName != withName)
        {
            var (startNumber, endNumber) = RunwayMapOverlay.EndNumbers(usage.Runway);
            var airbaseName = withName ? usage.Runway.airbase?.SavedAirbase?.DisplayName : null;
            _label!.text = RunwayNames.Callout(usage.Reverse ? endNumber : startNumber, airbaseName);
            _textRunway = usage.Runway;
            _textReverse = usage.Reverse;
            _textWithName = withName;
        }

        _label!.transform.position = new Vector3(screen.x, screen.y - BelowThreshold, 0f);
        _label.fontSize = (int)PlayerSettings.overlayTextSize;
        _label.color = _settings.HudColor.Value;
        _label.enabled = true;
    }

    public void Reset()
    {
        if (_label != null)
        {
            Object.Destroy(_label.gameObject);
        }

        _overlay = null;
        _label = null;
        _textRunway = null;
    }

    /// <summary><c>gearDeployed</c> follows the gear lever, so the callout shows as the gear starts extending, not once it locks.</summary>
    private bool GearAllowsCallout()
    {
        if (!_settings.HudGearDownOnly.Value)
        {
            return true;
        }

        var hud = SceneSingleton<CombatHUD>.i;
        return hud != null && hud.aircraft != null && hud.aircraft.gearDeployed;
    }

    private bool EnsureLabel()
    {
        if (_overlay != null && _label != null)
        {
            return true;
        }

        Reset();
        if (Time.unscaledTime < _nextSearchTime)
        {
            return false;
        }

        _nextSearchTime = Time.unscaledTime + 1f;
        // Includes inactive objects: the overlay can be hidden (spawn screen, map open) while the map still needs its font.
        var overlay = System.Array.Find(
            Resources.FindObjectsOfTypeAll<AirbaseOverlay>(), candidate => candidate.gameObject.scene.IsValid());
        if (overlay == null || AirbaseLabelField?.GetValue(overlay) is not TextMeshProUGUI source)
        {
            return false;
        }

        _overlay = overlay;
        _label = Object.Instantiate(source, source.transform.parent);
        _label.name = "BaanishRunwayCallout";
        _label.raycastTarget = false;
        _label.enabled = false;
        return true;
    }
}
