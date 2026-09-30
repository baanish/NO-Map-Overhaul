using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace BaanishUiImprovements.Missiles;

/// <summary>
/// One arrow on the screen edge per incoming missile outside the view, so it can be found and shot down.
/// Only missiles the game already warns you about count (<see cref="MissileWarning.knownMissiles"/>), so the arrows
/// reveal nothing the missile warning hasn't. Each arrow is a copy of the game's own off-screen target arrow from
/// <see cref="CombatHUD"/>, so it shares the HUD canvas, sprite, and visibility.
/// </summary>
internal sealed class IncomingMissileArrows
{
    private static readonly FieldInfo? TargetArrowField = AccessTools.DeclaredField(typeof(CombatHUD), "targetArrow");

    private readonly ModSettings _settings;
    private readonly List<Image> _arrows = new();
    private CombatHUD? _hud;
    private Image? _source;

    public IncomingMissileArrows(ModSettings settings) => _settings = settings;

    /// <summary>False when this game version has no <c>CombatHUD.targetArrow</c> to copy: no missile arrows.</summary>
    public static bool ArrowFieldFound => TargetArrowField is not null;

    public void Render()
    {
        var hud = SceneSingleton<CombatHUD>.i;
        if (hud == null || !EnsureSource(hud))
        {
            // Leaving the mission destroys the HUD, and our arrows with it: touching one now would throw.
            Reset();
            return;
        }

        var shown = 0;
        var camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
        var aircraft = hud.aircraft;
        if (_settings.ShowMissileArrows.Value && aircraft != null && !aircraft.disabled && camera != null &&
            aircraft.GetMissileWarningSystem() is { } warning)
        {
            var halfScreen = new NumericsVector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var focalPixels = halfScreen.Y / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (var missile in warning.knownMissiles)
            {
                if (missile == null || missile.disabled)
                {
                    continue;
                }

                var local = camera.transform.InverseTransformPoint(missile.transform.position);
                if (ScreenEdge.Pin(new NumericsVector3(local.x, local.y, local.z), halfScreen, focalPixels) is not { } pin)
                {
                    continue;
                }

                var arrow = Arrow(shown++);
                arrow.transform.position = new Vector3(halfScreen.X + pin.X, halfScreen.Y + pin.Y, 0f);
                arrow.transform.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(pin.Y, pin.X) * Mathf.Rad2Deg - 90f);
                arrow.color = _settings.MissileArrowColor.Value;
                arrow.enabled = true;
            }
        }

        for (var i = shown; i < _arrows.Count; i++)
        {
            _arrows[i].enabled = false;
        }
    }

    public void Reset()
    {
        foreach (var arrow in _arrows)
        {
            if (arrow != null)
            {
                Object.Destroy(arrow.gameObject);
            }
        }

        _arrows.Clear();
        _hud = null;
        _source = null;
    }

    /// <summary>
    /// A new scene brings a new CombatHUD, and the old one took our arrows with it. The arrows share one parent,
    /// so if the first one is gone, they all are.
    /// </summary>
    private bool EnsureSource(CombatHUD hud)
    {
        if (ReferenceEquals(_hud, hud) && _source != null && (_arrows.Count == 0 || _arrows[0] != null))
        {
            return true;
        }

        Reset();
        if (TargetArrowField?.GetValue(hud) is not Image source)
        {
            return false;
        }

        _hud = hud;
        _source = source;
        return true;
    }

    private Image Arrow(int index)
    {
        if (index < _arrows.Count)
        {
            return _arrows[index];
        }

        var arrow = Object.Instantiate(_source!, _source!.transform.parent);
        arrow.name = "BaanishMissileArrow";
        arrow.raycastTarget = false;
        // The copy takes the target arrow's children too; any label among them would show the target's name.
        foreach (Transform child in arrow.transform)
        {
            Object.Destroy(child.gameObject);
        }

        _arrows.Add(arrow);
        return arrow;
    }
}
