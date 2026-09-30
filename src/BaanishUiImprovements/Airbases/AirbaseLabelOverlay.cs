using System.Collections.Generic;
using BaanishUiImprovements.Drawing;
using TMPro;
using UnityEngine;

namespace BaanishUiImprovements.Airbases;

/// <summary>
/// Names the player's side's airbases on the full map, just under the game's icon for each, so a base named on comms
/// is easy to find. Full map only, like the game's airbase icons: it is north-up there, so map-space down is screen
/// down and the labels need no per-frame work.
/// </summary>
internal sealed class AirbaseLabelOverlay
{
    /// <summary>From the airbase centre to the top of its label, clearing the game's icon.</summary>
    private const float IconClearance = 22f;

    private readonly ModSettings _settings;
    private readonly Dictionary<Airbase, OutlinedText> _labels = new();
    private readonly HashSet<Airbase> _seen = new();
    private readonly List<Airbase> _stale = new();
    private RectTransform? _layer;

    public AirbaseLabelOverlay(ModSettings settings) => _settings = settings;

    /// <summary>
    /// The name the game shows for an airbase: its map tooltip and spawn menu read <c>SavedAirbase.DisplayName</c>. A
    /// mission editor airbase whose author cleared that name falls back to its unique name, which the editor fills the
    /// name in from. Null if it has neither.
    /// </summary>
    public static string? NameOf(Airbase airbase)
    {
        var saved = airbase.SavedAirbase;
        return saved == null ? null
            : !string.IsNullOrWhiteSpace(saved.DisplayName) ? saved.DisplayName
            : !string.IsNullOrWhiteSpace(saved.UniqueName) ? saved.UniqueName
            : null;
    }

    public void Render(DynamicMap map, IReadOnlyList<Airbase> airbases, TextMeshProUGUI? hudStyle)
    {
        var layer = EnsureLayer(map);
        var factor = map.mapDisplayFactor;
        var inverseScale = 1f / map.mapImage.transform.localScale.x;

        _seen.Clear();
        if (_settings.ShowAirbaseNames.Value && DynamicMap.mapMaximized)
        {
            foreach (var airbase in airbases)
            {
                if (!_labels.TryGetValue(airbase, out var label))
                {
                    label = new OutlinedText("AirbaseLabel", layer, new Vector2(0.5f, 1f), TextAlignmentOptions.Top);
                    _labels.Add(airbase, label);
                }

                var center = airbase.center.GlobalPosition();
                label.Rect.localPosition = new Vector2(center.x, center.z) * factor + Vector2.down * (IconClearance * inverseScale);
                label.Rect.localScale = Vector3.one * inverseScale;
                label.Set(NameOf(airbase) ?? string.Empty, _settings.AirbaseNameSize.Value, _settings.AirbaseNameColor.Value,
                    _settings.OutlineColor.Value, hudStyle);
                _seen.Add(airbase);
            }
        }

        _stale.Clear();
        foreach (var pair in _labels)
        {
            if (!_seen.Contains(pair.Key))
            {
                pair.Value.Destroy();
                _stale.Add(pair.Key);
            }
        }
        foreach (var airbase in _stale)
        {
            _labels.Remove(airbase);
        }
    }

    public void Reset()
    {
        if (_layer != null)
        {
            Object.Destroy(_layer.gameObject);
        }

        _layer = null;
        _labels.Clear();
    }

    /// <summary>
    /// Takes the icon layer's second slot, after the boundary layer, which keeps the first: over the boundary, under the
    /// runways and the game's unit icons. It moves only when out of place, since each move re-sorts the map's canvas.
    /// </summary>
    private RectTransform EnsureLayer(DynamicMap map)
    {
        if (_layer == null || _layer.parent != map.iconLayer.transform)
        {
            Reset();
            _layer = new GameObject("BaanishAirbaseLabelLayer", typeof(RectTransform)).GetComponent<RectTransform>();
            _layer.SetParent(map.iconLayer.transform, false);
            _layer.gameObject.AddComponent<Canvas>(); // so the game's icons moving every frame don't re-batch the names
        }

        var first = _layer.parent.GetChild(0);
        var slot = first != _layer && first.name == AirbaseBoundaryOverlay.LayerName ? 1 : 0;
        if (_layer.GetSiblingIndex() != slot)
        {
            _layer.SetSiblingIndex(slot);
        }

        return _layer;
    }
}
