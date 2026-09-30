using System.Collections.Generic;
using BaanishUiImprovements.Drawing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Draws the store's shapes and each tool's overlay on the DynamicMap. Like the runways it is a layer under the map's
/// icon layer, so it shows on the minimap and the full map and inherits their pan, zoom, and rotation. It sits just
/// past the mod's other map layers there, so drawings cover the runways but never hide a unit icon.
/// It is also the <see cref="IMapCanvas"/> the shapes draw into. Each shape keeps its own mesh and labels, rebuilt only
/// when the shape is added, when the zoom or a drawing setting changes, or on the game's 10 Hz map refresh if it drew
/// anything live. Panning moves the layer with the map and rebuilds nothing. Unity re-batches a whole canvas when any
/// graphic in it changes or moves, so the layer is two: the lines in the layer's own canvas, so rebuilding a shape doesn't
/// re-batch the game's icons and icons moving every frame don't re-batch a long pen stroke, and the markers and labels in
/// a canvas of their own, since the minimap turns them upright every frame.
/// Labels are placed after the shapes draw, by <see cref="LabelLayout"/>: on the full map clear of the game's icons and
/// labels and of each other, whenever a drawing redraws and once the map comes to rest after a pan or zoom. While the
/// map moves, and on the minimap, each label keeps its slot.
/// </summary>
internal sealed class MapShapeLayer : IMapCanvas
{
    /// <summary>Half the angle between an arrowhead's two barbs.</summary>
    private const float ArrowBarbDegrees = 25f;

    /// <summary>An arrowhead's length as a multiple of the line width, so the head keeps its shape at any LineWidth.</summary>
    private const float ArrowHeadPerLineWidth = 5f;

    /// <summary>
    /// How the mod's other map layers are named (BaanishRunwayLayer, BaanishAirbaseLabelLayer,
    /// BaanishAirbaseBoundaryLayer). They take the icon layer's first slots, so together they lead its children.
    /// </summary>
    private const string ModLayerPrefix = "Baanish";

    private readonly ModSettings _settings;
    private readonly IMapView _view;
    private readonly Dictionary<MapShape, ShapeGraphics> _graphics = new();
    private readonly HashSet<MapShape> _seen = new();
    private readonly List<MapShape> _stale = new();
    private readonly ShapeGraphics?[] _overlays;
    private readonly LabelLayout _labelLayout = new();
    private readonly LabelObstacles _obstacleFinder = new();
    private readonly List<LabelBox> _obstacles = new();
    private readonly List<PlacedLabel> _placements = new();
    private readonly List<MapLabel> _placing = new();
    private RectTransform? _layer;

    /// <summary>Every marker, then the plates, then every label, after every drawing's lines, in a canvas of their own.</summary>
    private RectTransform? _labelRoot;

    /// <summary>The first child of <see cref="_labelRoot"/>, so markers draw under every label.</summary>
    private RectTransform? _markerRoot;

    /// <summary>Every label's plate and leader as one mesh, after the markers and under every label's text.</summary>
    private FeatheredRectBatch? _plates;
    private GameObject? _markerPrefab;
    private TextMeshProUGUI? _hudStyle;
    private int _storeVersion = -1;
    private float _lastTick = -1f;
    private float _factor;
    private float _inverseScale;
    private (float Line, Color RimColor, float Text, DistanceUnit Units) _style;
    private ShapeGraphics? _target;
    private Quaternion _uprightFor;
    private bool _labelsDrawn;
    private Vector3 _viewPosition;
    private float _viewScale;
    private bool _viewMoving;
    private bool _placedOnFullMap;
    private int _placedForScreenLayout = -1;

    public MapShapeLayer(ModSettings settings, IMapView view, int toolCount)
    {
        _settings = settings;
        _view = view;
        _overlays = new ShapeGraphics?[toolCount];
    }

    public DistanceUnit Units => _view.Units;
    public float MetersPerIconUnit => _factor > 0f ? _inverseScale / _factor : 1f;
    public float TextSize => _settings.MapToolTextSize.Value;

    public MapPoint? OwnAircraft
    {
        get
        {
            MarkLive();
            return _view.OwnAircraft;
        }
    }

    /// <summary>
    /// Per frame, cheap when nothing changed. Redraws what the store, the zoom, or the settings changed, the overlays
    /// their tools invalidated, and on each of the game's 10 Hz map refreshes (<c>DynamicMap.mapLastUpdated</c>) the
    /// live shapes and overlays. A store change redraws every overlay too, since the eraser's highlight and the
    /// waypoint leg draw stored shapes. Then places the labels if anything was drawn or the map came to rest.
    /// </summary>
    /// <param name="screenAreas">Menu areas fixed on screen that labels keep clear of.</param>
    /// <param name="screenLayout">Changes whenever <paramref name="screenAreas"/> change.</param>
    public void Render(DynamicMap map, ShapeStore store, IReadOnlyList<MapTool> tools, TextMeshProUGUI? hudStyle,
        IReadOnlyList<RectTransform> screenAreas, int screenLayout)
    {
        var fresh = EnsureLayer(map);
        var shown = DynamicMap.mapMaximized || _settings.MapToolShowOnMinimap.Value || _settings.PerfTestShowsDrawings;
        if (_layer!.gameObject.activeSelf != shown)
        {
            _layer.gameObject.SetActive(shown);
            fresh = true; // hidden, it skips every redraw, so it redraws everything when it shows again
        }

        if (!shown)
        {
            return;
        }

        var tick = map.mapLastUpdated != _lastTick;
        _lastTick = map.mapLastUpdated;
        if (fresh || tick)
        {
            KeepUnderGameIcons();
        }

        var factor = map.mapDisplayFactor;
        var inverseScale = 1f / map.mapImage.transform.localScale.x;
        var style = (_settings.MapToolLineWidth.Value, _settings.OutlineColor.Value, _settings.MapToolTextSize.Value, Units);
        var restyle = fresh || factor != _factor || inverseScale != _inverseScale || !style.Equals(_style) || !ReferenceEquals(hudStyle, _hudStyle);
        var synced = store.Version != _storeVersion;
        if (restyle || synced || tick || AnyOverlayInvalid(tools))
        {
            _factor = factor;
            _inverseScale = inverseScale;
            _style = style;
            _hudStyle = hudStyle;
            _markerPrefab = map.mapWaypoint;
            if (synced)
            {
                Sync(store);
            }

            foreach (var pair in _graphics)
            {
                var graphics = pair.Value;
                if (graphics.Undrawn || restyle || (tick && graphics.Live))
                {
                    Begin(graphics);
                    pair.Key.Draw(this);
                    End();
                }
            }

            for (var i = 0; i < tools.Count; i++)
            {
                var overlay = _overlays[i]!;
                if (restyle || synced || tools[i].OverlayInvalid || (tick && overlay.Live))
                {
                    Begin(overlay);
                    tools[i].DrawOverlay(this);
                    End();
                    tools[i].OverlayInvalid = false;
                }
            }
        }

        PlaceLabels(map, screenAreas, screenLayout);
    }

    /// <summary>
    /// Places every label when one was drawn, the map came to rest, the full map opened or closed, or the menu changed
    /// shape. On the full map at rest, labels step clear of the game's icons and labels; while it moves they keep
    /// their slots, and on the turning minimap, where nothing is avoided, each takes its first.
    /// </summary>
    private void PlaceLabels(DynamicMap map, IReadOnlyList<RectTransform> screenAreas, int screenLayout)
    {
        var position = _layer!.position;
        var scale = _layer.lossyScale.x;
        var moving = position != _viewPosition || scale != _viewScale;
        var cameToRest = _viewMoving && !moving;
        _viewPosition = position;
        _viewScale = scale;
        _viewMoving = moving;
        var fullMap = DynamicMap.mapMaximized;
        if (!_labelsDrawn && !cameToRest && fullMap == _placedOnFullMap && screenLayout == _placedForScreenLayout)
        {
            return;
        }

        _labelsDrawn = false;
        _placedOnFullMap = fullMap;
        _placedForScreenLayout = screenLayout;
        _placing.Clear();
        _placements.Clear();
        foreach (var pair in _graphics)
        {
            pair.Value.AddLabels(_placing, pair.Key.Id, overlay: false);
        }

        foreach (var overlay in _overlays)
        {
            overlay!.AddLabels(_placing, 0, overlay: true);
        }

        foreach (var label in _placing)
        {
            if (!fullMap)
            {
                label.Placement.Slot = -1;
            }

            _placements.Add(label.Placement);
        }

        var avoid = fullMap && !moving;
        if (avoid)
        {
            _obstacleFinder.Collect(map, _layer, _inverseScale, screenAreas, _obstacles);
        }

        _labelLayout.Place(_placements, avoid ? _obstacles : null);
        foreach (var label in _placing)
        {
            label.Apply(_inverseScale);
        }

        DrawPlates();
    }

    /// <summary>The placed labels' plates and leaders, turned as the labels are: upright against the layer's rotation.</summary>
    private void DrawPlates()
    {
        var plates = _plates!;
        var upright = Quaternion.Inverse(_labelRoot!.rotation);
        plates.Clear();
        foreach (var label in _placing)
        {
            label.AddPlate(plates, upright, _inverseScale);
        }

        plates.Apply();
    }

    /// <summary>
    /// Per frame: the minimap turns every frame, so text and markers reset to upright. Nothing to do while the map holds
    /// still, as the full map does: each label and marker is made upright when it's created, and stays so until the map turns.
    /// </summary>
    public void KeepUpright()
    {
        if (_layer == null || !_layer.gameObject.activeSelf)
        {
            return; // destroyed with the old scene's map, or hidden from the minimap; the next render rebuilds everything
        }

        var rotation = _layer.rotation;
        if (rotation.Equals(_uprightFor))
        {
            return; // exact comparison: Quaternion's == lets a slow turn creep by unnoticed
        }

        _uprightFor = rotation;
        foreach (var graphics in _graphics.Values)
        {
            graphics.KeepUpright();
        }

        foreach (var overlay in _overlays)
        {
            overlay?.KeepUpright();
        }

        DrawPlates();
    }

    public void Reset()
    {
        if (_layer != null)
        {
            Object.Destroy(_layer.gameObject);
        }

        _layer = null;
        _labelRoot = null;
        _markerRoot = null;
        _plates = null;
        _placing.Clear();
        _graphics.Clear();
        System.Array.Clear(_overlays, 0, _overlays.Length);
        _storeVersion = -1;
        _placedForScreenLayout = -1;
    }

    public bool TryResolve(MapPoint point, out FlatVector position)
    {
        if (point.IsAnchored)
        {
            MarkLive();
        }

        return _view.TryResolve(point, out position);
    }

    public void Line(FlatVector from, FlatVector to, ShapeColor color)
    {
        var strokes = _target!.Strokes;
        strokes.AddPoint(Local(from));
        strokes.AddPoint(Local(to));
        EndStroke(color, closed: false);
    }

    public void Arrow(FlatVector from, FlatVector to, ShapeColor color)
    {
        Line(from, to, color);
        var tip = Local(to);
        var shaft = tip - Local(from);
        if (shaft.sqrMagnitude < 1e-10f)
        {
            return;
        }

        var head = ArrowHeadPerLineWidth * _settings.MapToolLineWidth.Value * _inverseScale;
        var back = -shaft.normalized * Mathf.Min(head, shaft.magnitude);
        var strokes = _target!.Strokes;
        strokes.AddPoint(tip + (Vector2)(Quaternion.Euler(0f, 0f, ArrowBarbDegrees) * back));
        strokes.AddPoint(tip);
        strokes.AddPoint(tip + (Vector2)(Quaternion.Euler(0f, 0f, -ArrowBarbDegrees) * back));
        EndStroke(color, closed: false);
    }

    public void Polyline(IReadOnlyList<FlatVector> points, ShapeColor color)
    {
        var strokes = _target!.Strokes;
        for (var i = 0; i < points.Count; i++)
        {
            strokes.AddPoint(Local(points[i]));
        }

        EndStroke(color, closed: false);
    }

    /// <summary>About one segment per 6 icon units of circumference, so the ring looks round at any zoom.</summary>
    public void Circle(FlatVector center, float radius, ShapeColor color)
    {
        var middle = Local(center);
        var local = radius * _factor;
        var segments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * local / (6f * _inverseScale)), 16, 256);
        var strokes = _target!.Strokes;
        for (var i = 0; i < segments; i++)
        {
            var angle = i * 2f * Mathf.PI / segments;
            strokes.AddPoint(middle + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * local);
        }

        EndStroke(color, closed: true);
    }

    /// <summary>A copy of the game's waypoint marker (<c>DynamicMap.mapWaypoint</c>), sized as the game sizes it. A ring stands in if the game has none.</summary>
    public void Marker(FlatVector position, ShapeColor color)
    {
        if (_markerPrefab == null || _target!.NextMarker(_markerPrefab) is not { } marker)
        {
            Circle(position, MapCanvasMetrics.MarkerRadius * MetersPerIconUnit, color);
            return;
        }

        // Unity counts any transform write as a change that re-batches the canvas, and a live route redraws ten times a second.
        var rect = marker.rectTransform;
        var local = (Vector3)Local(position);
        if (rect.localPosition != local)
        {
            rect.localPosition = local;
        }

        var scale = Vector3.one * _inverseScale;
        if (rect.localScale != scale)
        {
            rect.localScale = scale;
        }

        marker.color = color.ToColor32();
    }

    /// <summary>Sets the text now; the label is placed once every shape has drawn (<see cref="PlaceLabels"/>).</summary>
    public void Label(LabelAnchor anchor, string text, ShapeColor color) =>
        _target!.NextLabel().Set(anchor.Scaled(_factor / _inverseScale), text, _settings.MapToolTextSize.Value, color.ToColor32(),
            _settings.OutlineColor.Value, _hudStyle, _inverseScale);

    /// <summary>A scene change destroys the map and our layer with it, so a missing layer means every cached graphic is gone too.</summary>
    private bool EnsureLayer(DynamicMap map)
    {
        if (_layer != null && _layer.parent == map.iconLayer.transform)
        {
            return false;
        }

        Reset();
        _layer = NewRect("BaanishMapToolsLayer", map.iconLayer.transform);
        _layer.gameObject.AddComponent<Canvas>();
        _labelRoot = NewRect("Labels", _layer);
        _labelRoot.gameObject.AddComponent<Canvas>();
        _markerRoot = NewRect("Markers", _labelRoot);
        _plates = NewRect("Plates", _labelRoot).gameObject.AddComponent<FeatheredRectBatch>();
        _plates.raycastTarget = false;
        for (var i = 0; i < _overlays.Length; i++)
        {
            _overlays[i] = new ShapeGraphics(_layer, _markerRoot, _labelRoot, "ToolOverlay");
        }

        return true;
    }

    /// <summary>
    /// Takes the slot just past the mod's other layers, so drawings cover the runways, airbase names, and boundary, and
    /// everything the game adds after them (unit icons, waypoints, radar pings) covers the drawings.
    /// </summary>
    private void KeepUnderGameIcons()
    {
        var parent = _layer!.parent;
        var slot = 0;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (ReferenceEquals(child, _layer))
            {
                continue;
            }

            if (!child.name.StartsWith(ModLayerPrefix, System.StringComparison.Ordinal))
            {
                break;
            }

            slot++;
        }

        if (_layer.GetSiblingIndex() != slot)
        {
            _layer.SetSiblingIndex(slot);
        }
    }

    /// <summary>Makes graphics for new shapes, drops those of shapes gone from the store, and orders them as the store does, overlays on top.</summary>
    private void Sync(ShapeStore store)
    {
        _seen.Clear();
        var shapes = store.Shapes;
        for (var i = 0; i < shapes.Count; i++)
        {
            var shape = shapes[i];
            if (!_graphics.TryGetValue(shape, out var graphics))
            {
                graphics = new ShapeGraphics(_layer!, _markerRoot!, _labelRoot!, "MapShape");
                _graphics.Add(shape, graphics);
            }

            if (graphics.Rect.GetSiblingIndex() != i)
            {
                graphics.Rect.SetSiblingIndex(i);
            }

            if (graphics.MarkerRect.GetSiblingIndex() != i)
            {
                graphics.MarkerRect.SetSiblingIndex(i);
            }

            _seen.Add(shape);
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

        foreach (var shape in _stale)
        {
            _graphics.Remove(shape);
        }

        foreach (var overlay in _overlays)
        {
            overlay!.Rect.SetAsLastSibling();
            overlay.MarkerRect.SetAsLastSibling();
        }

        _labelRoot!.SetAsLastSibling();
        _storeVersion = store.Version;
    }

    private void Begin(ShapeGraphics graphics)
    {
        graphics.Begin();
        _target = graphics;
        _labelsDrawn = true;
    }

    private void End()
    {
        _target!.End();
        _target = null;
    }

    private static bool AnyOverlayInvalid(IReadOnlyList<MapTool> tools)
    {
        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i].OverlayInvalid)
            {
                return true;
            }
        }

        return false;
    }

    private void MarkLive()
    {
        if (_target != null)
        {
            _target.Live = true;
        }
    }

    /// <summary>
    /// Flat: one colour across the width and a one-pixel anti-aliased edge. No dark rim, unlike the runways: on a line
    /// this thin the fade from fill to rim to clear takes up most of the width and reads as a bevel.
    /// </summary>
    private void EndStroke(ShapeColor color, bool closed)
    {
        var fill = color.ToColor32();
        _target!.Strokes.EndStroke(closed, _settings.MapToolLineWidth.Value * 0.5f * _inverseScale, fill, 0f, fill,
            EdgeProfile.AntiAliasWidth * _inverseScale);
    }

    private Vector2 Local(FlatVector meters) => new Vector2(meters.X, meters.Y) * _factor;

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>
    /// One shape's graphics, reused from draw to draw: its lines as one mesh, and its markers and labels, which sit in
    /// the layer's marker and label containers so every marker and label draws over every drawing's lines.
    /// </summary>
    private sealed class ShapeGraphics
    {
        private readonly List<MapLabel> _labels = new();
        private readonly List<Image> _markers = new();
        private readonly Transform _labelParent;
        private int _labelsUsed;
        private int _markersUsed;

        public ShapeGraphics(Transform layer, Transform markers, Transform labels, string name)
        {
            _labelParent = labels;
            Rect = NewRect(name, layer);
            Strokes = NewRect("Strokes", Rect).gameObject.AddComponent<StrokeGraphic>();
            Strokes.raycastTarget = false;
            MarkerRect = NewRect(name, markers);
        }

        /// <summary>Holds the lines.</summary>
        public RectTransform Rect { get; }

        /// <summary>Holds the markers, in the same order among the shapes as <see cref="Rect"/>.</summary>
        public RectTransform MarkerRect { get; }

        public StrokeGraphic Strokes { get; }

        /// <summary>The last draw read a unit's or the aircraft's position, so it redraws on every map refresh.</summary>
        public bool Live { get; set; }

        public bool Undrawn { get; private set; } = true;

        public void Begin()
        {
            Strokes.Clear();
            _labelsUsed = 0;
            _markersUsed = 0;
            Live = false;
            Undrawn = false;
        }

        public void End()
        {
            for (var i = _labelsUsed; i < _labels.Count; i++)
            {
                _labels[i].Visible = false;
            }

            for (var i = _markersUsed; i < _markers.Count; i++)
            {
                _markers[i].enabled = false;
            }

            Strokes.Apply();
        }

        public MapLabel NextLabel()
        {
            if (_labelsUsed == _labels.Count)
            {
                var created = new MapLabel(_labelParent);
                created.Rect.rotation = Quaternion.identity; // upright from the start, see KeepUpright
                _labels.Add(created);
            }

            var label = _labels[_labelsUsed++];
            label.Visible = true;
            return label;
        }

        /// <summary>This draw's labels, for the layer to place.</summary>
        public void AddLabels(List<MapLabel> into, int order, bool overlay)
        {
            for (var i = 0; i < _labelsUsed; i++)
            {
                var label = _labels[i];
                label.Placement.Order = order;
                label.Placement.Overlay = overlay;
                into.Add(label);
            }
        }

        /// <summary>Null if the game's marker has no image to tint.</summary>
        public Image? NextMarker(GameObject prefab)
        {
            if (_markersUsed == _markers.Count)
            {
                var copy = Object.Instantiate(prefab, MarkerRect);
                if (!copy.TryGetComponent<Image>(out var image))
                {
                    Object.Destroy(copy);
                    return null;
                }

                copy.name = "Marker";
                copy.transform.rotation = Quaternion.identity;
                image.raycastTarget = false;
                // The game's prefab ships with maskable off, so without this the marker draws past the minimap's edge.
                image.maskable = true;
                _markers.Add(image);
            }

            var marker = _markers[_markersUsed++];
            marker.enabled = true;
            return marker;
        }

        /// <summary>Hidden ones too, so one shown again on a map that has stopped turning is already upright.</summary>
        public void KeepUpright()
        {
            foreach (var label in _labels)
            {
                label.Rect.rotation = Quaternion.identity;
            }

            foreach (var marker in _markers)
            {
                marker.rectTransform.rotation = Quaternion.identity;
            }
        }

        /// <summary>The markers and labels live under the layer's containers, not this shape's rect, so they go separately.</summary>
        public void Destroy()
        {
            Object.Destroy(Rect.gameObject);
            Object.Destroy(MarkerRect.gameObject);
            foreach (var label in _labels)
            {
                Object.Destroy(label.Rect.gameObject);
            }
        }
    }
}
