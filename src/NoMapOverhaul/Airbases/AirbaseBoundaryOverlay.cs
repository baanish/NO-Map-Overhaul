using System.Collections.Generic;
using NoMapOverhaul.Drawing;
using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace NoMapOverhaul.Airbases;

/// <summary>
/// Shades each friendly airbase's landing zone on the map. The game counts a stopped, landed aircraft as
/// returned rather than crashed when it is within <see cref="Airbase.GetRadius"/> of <c>Airbase.center</c>
/// (<c>FactionHQ.AnyNearAirbase</c>); that radius is the airbase's capture range, so this is also the capture zone.
/// </summary>
internal sealed class AirbaseBoundaryOverlay
{
    /// <summary>The layer's name, which the names layer looks for to take the slot after it.</summary>
    public const string LayerName = "BaanishAirbaseBoundaryLayer";

    private const float EdgeWidth = 1f;

    private readonly ModSettings _settings;
    private readonly Dictionary<Airbase, CircleGraphic> _circles = new();
    private readonly HashSet<Airbase> _seen = new();
    private readonly List<Airbase> _stale = new();
    private RectTransform? _layer;

    public AirbaseBoundaryOverlay(ModSettings settings) => _settings = settings;

    public void Render(DynamicMap map, IReadOnlyList<Airbase> airbases)
    {
        var layer = EnsureLayer(map);
        var factor = map.mapDisplayFactor;
        var inverseScale = 1f / map.mapImage.transform.localScale.x;
        var friendly = ThemeManager.Active?.ColorTheme.MapIconFriendly ?? new Color(0.3f, 0.6f, 1f);
        var fill = friendly.WithAlpha(_settings.BoundaryFillOpacity.Value);
        var edge = friendly.WithAlpha(_settings.BoundaryEdgeOpacity.Value);

        _seen.Clear();
        if (_settings.ShowBoundaries.Value)
        {
            foreach (var airbase in airbases)
            {
                if (airbase.center == null || airbase.GetRadius() <= 0f)
                {
                    continue;
                }

                if (!_circles.TryGetValue(airbase, out var circle))
                {
                    var rect = new GameObject("AirbaseBoundary", typeof(RectTransform)).GetComponent<RectTransform>();
                    rect.SetParent(layer, false);
                    circle = rect.gameObject.AddComponent<CircleGraphic>();
                    circle.raycastTarget = false;
                    _circles.Add(airbase, circle);
                }

                var center = airbase.center.GlobalPosition();
                circle.rectTransform.localPosition = new Vector2(center.x, center.z) * factor;
                circle.Set(airbase.GetRadius() * factor, EdgeWidth * inverseScale, fill, edge, EdgeProfile.AntiAliasWidth * inverseScale);
                _seen.Add(airbase);
            }
        }

        _stale.Clear();
        foreach (var pair in _circles)
        {
            if (!_seen.Contains(pair.Key))
            {
                Object.Destroy(pair.Value.gameObject);
                _stale.Add(pair.Key);
            }
        }
        foreach (var airbase in _stale)
        {
            _circles.Remove(airbase);
        }
    }

    public void Reset()
    {
        if (_layer != null)
        {
            Object.Destroy(_layer.gameObject);
        }

        _layer = null;
        _circles.Clear();
    }

    /// <summary>Kept as the icon layer's first child every frame, so it stays under the runway layer whichever was created first.</summary>
    private RectTransform EnsureLayer(DynamicMap map)
    {
        if (_layer == null || _layer.parent != map.iconLayer.transform)
        {
            Reset();
            _layer = new GameObject(LayerName, typeof(RectTransform)).GetComponent<RectTransform>();
            _layer.SetParent(map.iconLayer.transform, false);
            _layer.gameObject.AddComponent<Canvas>(); // so the game's icons moving every frame don't re-batch the circles
        }

        if (_layer.GetSiblingIndex() != 0)
        {
            _layer.SetAsFirstSibling();
        }

        return _layer;
    }
}
