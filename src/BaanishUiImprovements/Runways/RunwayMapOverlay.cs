using System.Collections.Generic;
using BaanishUiImprovements.Drawing;
using TMPro;
using UnityEngine;

namespace BaanishUiImprovements.Runways;

/// <summary>
/// Draws runway strips, end numbers, and the approach line on the DynamicMap. The game uses one DynamicMap
/// for both the minimap and the full map, so a layer under its icon layer shows on both and inherits
/// their pan, zoom, and rotation. Sizes are in map units times the inverse map scale, the same convention
/// the game's own icons use, so they read the same at every zoom.
/// </summary>
internal sealed class RunwayMapOverlay
{
    private const float LabelGap = 14f;

    /// <summary>One dash plus its gap spans 500 m of ground, so the dashes double as distance ticks: two per km.</summary>
    private const float DashCycleMeters = 500f;
    private const float DashMeters = 300f;

    private readonly ModSettings _settings;
    private readonly Dictionary<Airbase.Runway, RunwayGraphic> _graphics = new();
    private readonly HashSet<Airbase.Runway> _seen = new();
    private readonly List<Airbase.Runway> _stale = new();
    private RectTransform? _layer;
    private readonly List<FeatheredRect> _approachDashes = new();
    private Quaternion _uprightFor;

    public RunwayMapOverlay(ModSettings settings) => _settings = settings;

    public void Render(DynamicMap map, IReadOnlyList<Airbase.Runway> runways, Airbase.Runway.RunwayUsage? approach, TextMeshProUGUI? hudStyle)
    {
        var layer = EnsureLayer(map);
        var factor = map.mapDisplayFactor;
        var inverseScale = 1f / map.mapImage.transform.localScale.x;

        _seen.Clear();
        if (_settings.MapRunways.Value)
        {
            foreach (var runway in runways)
            {
                if (!_graphics.TryGetValue(runway, out var graphic))
                {
                    graphic = new RunwayGraphic(layer, runway);
                    _graphics.Add(runway, graphic);
                }

                graphic.Update(_settings, factor, inverseScale, hudStyle);
                _seen.Add(runway);
            }
        }

        _stale.Clear();
        foreach (var pair in _graphics)
        {
            if (!_seen.Contains(pair.Key))
            {
                pair.Value.Destroy();
                _stale.Add(pair.Key);
            }
        }
        foreach (var runway in _stale)
        {
            _graphics.Remove(runway);
        }

        UpdateApproachLine(layer, approach, factor, inverseScale);
    }

    /// <summary>
    /// Per frame: the minimap turns every frame, so numbers reset to upright between the 10 Hz map refreshes. Nothing to
    /// do while the map holds still, as the full map does, since each number is made upright when it's created.
    /// </summary>
    public void KeepLabelsUpright()
    {
        if (_layer == null)
        {
            return; // destroyed with the old scene's map; the next refresh rebuilds everything
        }

        var rotation = _layer.rotation;
        if (rotation.Equals(_uprightFor))
        {
            return; // exact comparison: Quaternion's == lets a slow turn creep by unnoticed
        }

        _uprightFor = rotation;
        foreach (var graphic in _graphics.Values)
        {
            graphic.KeepLabelsUpright();
        }
    }

    public void Reset()
    {
        if (_layer != null)
        {
            Object.Destroy(_layer.gameObject);
        }

        _layer = null;
        _approachDashes.Clear();
        _graphics.Clear();
    }

    /// <summary>A scene change destroys the map and our layer with it, so a missing layer means every cached graphic is gone too.</summary>
    private RectTransform EnsureLayer(DynamicMap map)
    {
        if (_layer != null && _layer.parent == map.iconLayer.transform)
        {
            return _layer;
        }

        Reset();
        _layer = NewRect("BaanishRunwayLayer", map.iconLayer.transform);
        // A canvas of its own: Unity re-batches a whole canvas when anything in it moves, and the game's icons move every frame.
        _layer.gameObject.AddComponent<Canvas>();
        _layer.SetAsFirstSibling();
        return _layer;
    }

    private void UpdateApproachLine(RectTransform layer, Airbase.Runway.RunwayUsage? approach, float factor, float inverseScale)
    {
        var lengthMeters = _settings.ApproachLineLengthKm.Value * 1000f;
        var dashCount = approach is null ? 0 : Mathf.CeilToInt(lengthMeters / DashCycleMeters);
        while (_approachDashes.Count < dashCount)
        {
            _approachDashes.Add(NewFeatheredRect("ApproachDash", layer, new Vector2(0.5f, 0f)));
        }

        for (var i = 0; i < _approachDashes.Count; i++)
        {
            _approachDashes[i].enabled = i < dashCount;
        }

        if (approach is not { } usage)
        {
            return;
        }

        var threshold = MapPoint(usage.GetStart(), factor);
        var outward = (threshold - MapPoint(usage.GetEnd(), factor)).normalized;
        var rotation = MapRotation(outward);
        var width = _settings.ApproachLineWidth.Value * inverseScale;
        var color = _settings.ApproachLineColor.Value;
        for (var i = 0; i < dashCount; i++)
        {
            var from = i * DashCycleMeters;
            var dash = _approachDashes[i];
            dash.rectTransform.localPosition = threshold + outward * (from * factor);
            dash.rectTransform.localRotation = rotation;
            dash.rectTransform.sizeDelta = new Vector2(width, Mathf.Min(DashMeters, lengthMeters - from) * factor);
            dash.color = color;
            ApplyEdges(dash, _settings, inverseScale);
        }
    }

    /// <summary>A dark rim keeps green graphics readable over the map's own bright green linework (Feldspar, cities).</summary>
    private static void ApplyEdges(FeatheredRect shape, ModSettings settings, float inverseScale) =>
        shape.SetEdges(settings.OutlineWidth.Value * inverseScale, settings.OutlineColor.Value, EdgeProfile.AntiAliasWidth * inverseScale);

    /// <summary>
    /// The numbers for a runway's Start and End, shared by the map labels and the HUD callout. Every number comes
    /// from the runway heading, then the paint table. A runway's <c>name</c> is ignored: missions set names that
    /// disagree with the paint (Ignus free flight calls a runway painted 06 "Runway 07").
    /// </summary>
    internal static (string Start, string End) EndNumbers(Airbase.Runway runway)
    {
        var airbase = runway.airbase?.SavedAirbase?.DisplayName;
        return (RunwayNames.Painted(airbase, HeadingNumber(runway.GetDirection(reverse: false))),
                RunwayNames.Painted(airbase, HeadingNumber(runway.GetDirection(reverse: true))));
    }

    /// <summary>The same arithmetic as <c>Airbase.Runway.GetName</c> for an unnamed runway, so the numbers match its clearance.</summary>
    private static string HeadingNumber(Vector3 direction)
    {
        var number = Mathf.RoundToInt(Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y * 0.1f);
        return (number == 0 ? 36 : number).ToString("00");
    }

    private static Vector2 MapPoint(Transform transform, float factor)
    {
        var global = transform.GlobalPosition();
        return new Vector2(global.x, global.z) * factor;
    }

    /// <summary>Rotates a rect so its up axis points along <paramref name="direction"/> in map space (x east, y north).</summary>
    private static Quaternion MapRotation(Vector2 direction) =>
        Quaternion.Euler(0f, 0f, -Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg);

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static FeatheredRect NewFeatheredRect(string name, Transform parent, Vector2 pivot)
    {
        var shape = NewRect(name, parent).gameObject.AddComponent<FeatheredRect>();
        shape.raycastTarget = false;
        shape.rectTransform.pivot = pivot;
        return shape;
    }

    private sealed class RunwayGraphic
    {
        private readonly Airbase.Runway _runway;
        private readonly FeatheredRect _strip;
        private readonly OutlinedText _startLabel;
        private readonly OutlinedText _endLabel;

        public RunwayGraphic(Transform layer, Airbase.Runway runway)
        {
            _runway = runway;
            _strip = NewFeatheredRect("RunwayStrip", layer, new Vector2(0.5f, 0.5f));
            _startLabel = new OutlinedText("RunwayLabel", layer, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
            _endLabel = new OutlinedText("RunwayLabel", layer, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
            KeepLabelsUpright();
        }

        public void Update(ModSettings settings, float factor, float inverseScale, TextMeshProUGUI? hudStyle)
        {
            var start = MapPoint(_runway.Start, factor);
            var end = MapPoint(_runway.End, factor);
            var axis = (end - start).normalized;

            var rect = _strip.rectTransform;
            rect.localPosition = (start + end) * 0.5f;
            rect.localRotation = MapRotation(axis);
            rect.sizeDelta = new Vector2(
                Mathf.Max(_runway.GetWidth() * factor, settings.RunwayMinWidth.Value * inverseScale),
                Vector2.Distance(start, end));
            _strip.color = settings.RunwayColor.Value;
            ApplyEdges(_strip, settings, inverseScale);

            // A number sits past the end it names: that is where a pilot landing on that number touches down.
            var gap = axis * (LabelGap * inverseScale);
            var (startNumber, endNumber) = EndNumbers(_runway);
            PlaceLabel(_startLabel, startNumber, start - gap, settings, inverseScale, hudStyle);
            PlaceLabel(_endLabel, endNumber, end + gap, settings, inverseScale, hudStyle);
            _endLabel.Visible = _runway.Reversable || settings.RunwayBothEnds.Value;
        }

        public void KeepLabelsUpright()
        {
            _startLabel.Rect.rotation = Quaternion.identity;
            _endLabel.Rect.rotation = Quaternion.identity;
        }

        public void Destroy()
        {
            Object.Destroy(_strip.gameObject);
            _startLabel.Destroy();
            _endLabel.Destroy();
        }

        private static void PlaceLabel(OutlinedText label, string number, Vector2 position, ModSettings settings, float inverseScale, TextMeshProUGUI? hudStyle)
        {
            label.Rect.localPosition = position;
            label.Rect.localScale = Vector3.one * inverseScale;
            label.Set(number, settings.RunwayLabelSize.Value, settings.RunwayLabelColor.Value, settings.OutlineColor.Value, hudStyle);
        }
    }
}
