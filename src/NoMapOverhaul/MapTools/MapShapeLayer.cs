using System.Collections.Generic;
using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.Drawing;
using TMPro;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;
using WorldVector = System.Numerics.Vector3;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Draws the store's shapes and each tool's overlay on the DynamicMap. Like the runways it is a layer under the map's
/// icon layer, so it shows on the minimap and the full map and inherits their pan, zoom, and rotation. It sits just
/// past the mod's other map layers there, so drawings cover the runways but never hide a unit icon.
/// It is also the <see cref="IMapCanvas"/> the shapes draw into. Each shape keeps its own mesh and labels, rebuilt only
/// when the shape is added, when the zoom or a drawing setting changes, or on the game's 10 Hz map refresh if it drew
/// anything live. Panning moves the layer with the map and rebuilds nothing. Unity re-batches a whole canvas when any
/// graphic in it changes or moves, so the layer is two: the lines in the layer's own canvas, so rebuilding a shape doesn't
/// re-batch the game's icons and icons moving every frame don't re-batch a long pen stroke, and the markers and labels in
/// a canvas of their own, since the minimap turns them upright as it turns.
/// Labels are placed after the shapes draw, by <see cref="LabelLayout"/>: on the full map clear of the game's icons and
/// labels and of each other, whenever a drawing's labels change and once the map comes to rest after a pan or zoom, and
/// while it moves each label keeps its slot. On the minimap they keep clear of each other only, and move with it as it
/// turns.
/// </summary>
internal sealed class MapShapeLayer : IMapCanvas, ILabelPlacements
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

    /// <summary>
    /// How far the full map may move in a frame, in icon units, and still hold still. While it follows the aircraft the
    /// game moves it every frame, by a small fraction of a unit zoomed out; a pan moves it by more.
    /// </summary>
    private const float StillStep = 0.5f;

    /// <summary>How long the full map holds still after a pan or zoom before its labels step clear of what moved under them.</summary>
    private const float RestSeconds = 0.1f;

    /// <summary>
    /// How far the full map may drift while it holds still before its labels are placed again. Only the menu and the
    /// grid labels stay put on screen; the icons and every other label move with the map.
    /// </summary>
    private const float RestDrift = 8f;

    /// <summary>The minimap's obstacles: its labels keep clear of each other, but the game's labels there aren't collected.</summary>
    private static readonly LabelBox[] NoObstacles = System.Array.Empty<LabelBox>();

    private readonly ModSettings _settings;
    private readonly IMapView _view;
    private readonly Dictionary<MapShape, ShapeGraphics> _graphics = new();
    private readonly HashSet<MapShape> _seen = new();
    private readonly List<MapShape> _stale = new();
    private ShapeGraphics?[] _overlays = System.Array.Empty<ShapeGraphics?>();
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

    /// <summary>The text of every label on a plate, after the plates and under the notes.</summary>
    private PlateText? _plateText;
    private GameObject? _markerPrefab;
    private TextMeshProUGUI? _hudStyle;
    private int _storeVersion = -1;
    private float _lastTick = -1f;
    private float _factor;
    private float _inverseScale;
    private (float Line, Color RimColor, float Text, DistanceUnit Units) _style;
    private ShapeGraphics? _target;
    /// <summary>The map's rotation when its labels and markers last stood upright, which the labels are placed for.</summary>
    private Quaternion _uprightFor = Quaternion.identity;
    private Quaternion _placedForRotation;
    private float _spacedHeading;
    private bool _labelsChanged;
    private Vector3 _viewPosition;
    private float _viewScale;
    private float _viewMovedAt;

    /// <summary>Where the full map was when its labels last stepped clear of the obstacles.</summary>
    private Vector3 _restPosition;
    private float _restScale;
    private bool _placedOnFullMap;
    private int _placedForScreenLayout = -1;

    public MapShapeLayer(ModSettings settings, IMapView view)
    {
        _settings = settings;
        _view = view;
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
    /// waypoint leg draw stored shapes. Then places the labels if a label changed or the full map came to rest.
    /// </summary>
    /// <param name="screenAreas">Menu areas fixed on screen that labels keep clear of.</param>
    /// <param name="screenLayout">Changes whenever <paramref name="screenAreas"/> change.</param>
    public void Render(DynamicMap map, ShapeStore store, IReadOnlyList<MapTool> tools, TextMeshProUGUI? hudStyle,
        IReadOnlyList<RectTransform> screenAreas, int screenLayout)
    {
        var fresh = EnsureLayer(map, tools.Count);
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

        var drawing = ModTimings.Start();
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

            // A zoom or a new style moves every label, and a shape gone from the store takes its labels with it.
            _labelsChanged |= restyle || synced;

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
                    var redrawn = tools[i].RedrawnShape?.Id ?? 0;
                    _labelsChanged |= redrawn != overlay.RedrawnId; // a highlight moving between identical drawings changes no label text
                    overlay.RedrawnId = redrawn;
                }
            }
        }

        ModTimings.Stop(ModSection.ShapeDrawing, drawing);
        var upright = ModTimings.Start();
        KeepUpright();
        ModTimings.Stop(ModSection.UprightTurning, upright);
        var placing = ModTimings.Start();
        PlaceLabels(map, screenAreas, screenLayout);
        ModTimings.Stop(ModSection.LabelPlacement, placing);
    }

    /// <summary>
    /// Places every label when one changed, the full map came to rest, the labels were turned upright again, the full
    /// map opened or closed, or the menu changed shape. On the full map at rest, labels step clear of the game's icons
    /// and labels and of each other; while it moves they keep their slots. It comes to rest once it has held still for
    /// <see cref="RestSeconds"/> after a pan or zoom, or has drifted <see cref="RestDrift"/> while holding still. On the
    /// minimap they step clear of each other only, when one changed and every
    /// <see cref="MinimapHeading.RespaceStepDegrees"/> of turn. The layout works in the frame the labels stand upright
    /// in, which the heading-up minimap turns against the map, so each upright step moves every label even when it
    /// keeps its slot.
    /// </summary>
    private void PlaceLabels(DynamicMap map, IReadOnlyList<RectTransform> screenAreas, int screenLayout)
    {
        var position = _layer!.position;
        var scale = _layer.lossyScale.x;
        var unit = scale * _inverseScale; // world units per icon unit
        if (scale != _viewScale || (position - _viewPosition).magnitude > StillStep * unit)
        {
            _viewMovedAt = Time.unscaledTime;
        }

        _viewPosition = position;
        _viewScale = scale;
        var fullMap = DynamicMap.mapMaximized;
        var still = Time.unscaledTime - _viewMovedAt >= RestSeconds;
        var cameToRest = fullMap && still && (scale != _restScale || (position - _restPosition).magnitude > RestDrift * unit);
        var toUpright = _uprightFor;
        var turned = !toUpright.Equals(_placedForRotation); // exact: KeepUpright steps it
        if (!_labelsChanged && !cameToRest && !turned && fullMap == _placedOnFullMap && screenLayout == _placedForScreenLayout)
        {
            return;
        }

        var switchedMap = fullMap != _placedOnFullMap;
        var changed = _labelsChanged;
        _labelsChanged = false;
        _placedForRotation = toUpright;
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
            overlay!.AddLabels(_placing, overlay.RedrawnId, overlay: true);
        }

        var across = toUpright * Vector3.right;
        foreach (var label in _placing)
        {
            if (switchedMap)
            {
                label.Placement.Slot = -1; // a slot clear of the full map's icons means nothing on the minimap, and back
            }

            label.TurnUpright(across.x, across.y);
            _placements.Add(label.Placement);
        }

        IReadOnlyList<LabelBox>? obstacles = null;
        if (fullMap && still)
        {
            _obstacleFinder.Collect(map, _layer, _inverseScale, screenAreas, _obstacles);
            obstacles = _obstacles;
            _restPosition = position;
            _restScale = scale;
        }
        else if (!fullMap && (changed || switchedMap ||
                              MinimapHeading.Turned(_spacedHeading, toUpright.eulerAngles.z, MinimapHeading.RespaceStepDegrees)))
        {
            obstacles = NoObstacles;
            _spacedHeading = toUpright.eulerAngles.z;
        }

        _labelLayout.Place(_placements, obstacles);
        var toMap = Quaternion.Inverse(toUpright);
        var plates = _plates!;
        var text = _plateText!;
        plates.Clear();
        text.Clear();
        foreach (var label in _placing)
        {
            label.Apply(_inverseScale, toMap);
            label.AddPlate(plates, toMap, _inverseScale);
            label.AddText(toMap, _inverseScale);
        }

        plates.Apply();
        text.Apply();
    }

    /// <summary>
    /// Stands text and markers upright again once the heading-up minimap has turned <see cref="MinimapHeading.UprightStepDegrees"/>
    /// since they last were, and the labels are then placed for that turn. In between they turn with the map, by less
    /// than a degree, and nothing moves: every transform written would have Unity re-batch the canvas. Each label and
    /// marker is made upright when it's created. The full map holds still, so there any change counts, such as the
    /// minimap's last turn when the full map opens.
    /// </summary>
    private void KeepUpright()
    {
        var rotation = _layer!.rotation;
        var step = DynamicMap.mapMaximized ? 0f : MinimapHeading.UprightStepDegrees;
        if (!MinimapHeading.Turned(_uprightFor.eulerAngles.z, rotation.eulerAngles.z, step))
        {
            return;
        }

        _uprightFor = rotation;
        var toMap = Quaternion.Inverse(rotation);
        foreach (var graphics in _graphics.Values)
        {
            graphics.KeepUpright(toMap);
        }

        foreach (var overlay in _overlays)
        {
            overlay?.KeepUpright(toMap);
        }
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
        _plateText = null;
        _placing.Clear();
        _graphics.Clear();
        System.Array.Clear(_overlays, 0, _overlays.Length);
        _storeVersion = -1;
        _placedForScreenLayout = -1;
        _uprightFor = Quaternion.identity; // how every label and marker is made
    }

    /// <summary>Only on the full map, the one map the tools take clicks on.</summary>
    public bool TryGetBox(MapShape shape, int index, out LabelBox box)
    {
        if (_placedOnFullMap && _graphics.TryGetValue(shape, out var graphics) && graphics.TryGetPlacement(index) is { Slot: >= 0 } placement)
        {
            box = placement.Box;
            return true;
        }

        box = default;
        return false;
    }

    public bool TryResolve(MapPoint point, out FlatVector position)
    {
        if (point.IsAnchored)
        {
            MarkLive();
        }

        return _view.TryResolve(point, out position);
    }

    public bool TryResolveWorld(MapPoint point, out WorldVector position)
    {
        if (point.IsAnchored)
        {
            MarkLive();
        }

        return _view.TryResolveWorld(point, out position);
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
        if (_markerPrefab == null ||
            !_target!.AddMarker(_markerPrefab, Local(position), _inverseScale, color.ToColor32(), Quaternion.Inverse(_uprightFor)))
        {
            Circle(position, MapCanvasMetrics.MarkerRadius * MetersPerIconUnit, color);
        }
    }

    /// <summary>
    /// Sets the text now; the label is placed once every shape has drawn (<see cref="PlaceLabels"/>), if it changed. A live
    /// shape draws ten times a second, and its labels mostly haven't moved.
    /// </summary>
    public void Label(LabelAnchor anchor, string text, ShapeColor color) =>
        _labelsChanged |= _target!.NextLabel().Set(anchor.Scaled(_factor / _inverseScale), text, _settings.MapToolTextSize.Value,
            color.ToColor32(), _settings.OutlineColor.Value, _hudStyle, _inverseScale);

    /// <summary>A scene change destroys the map and our layer with it, so a missing layer means every cached graphic is gone too.</summary>
    private bool EnsureLayer(DynamicMap map, int toolCount)
    {
        if (_layer != null && _layer.parent == map.iconLayer.transform)
        {
            return false;
        }

        Reset();
        if (_overlays.Length != toolCount)
        {
            _overlays = new ShapeGraphics?[toolCount];
        }

        _layer = NewRect("BaanishMapToolsLayer", map.iconLayer.transform);
        _layer.gameObject.AddComponent<Canvas>();
        _labelRoot = NewRect("Labels", _layer);
        _labelRoot.gameObject.AddComponent<Canvas>();
        _markerRoot = NewRect("Markers", _labelRoot);
        _plates = NewRect("Plates", _labelRoot).gameObject.AddComponent<FeatheredRectBatch>();
        _plates.raycastTarget = false;
        _plateText = new PlateText(_labelRoot);
        for (var i = 0; i < _overlays.Length; i++)
        {
            _overlays[i] = new ShapeGraphics(_layer, _markerRoot, _labelRoot, _plateText, "ToolOverlay");
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
                graphics = new ShapeGraphics(_layer!, _markerRoot!, _labelRoot!, _plateText!, "MapShape");
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
    }

    private void End()
    {
        _labelsChanged |= _target!.End();
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
    /// One shape's graphics, reused from draw to draw: its lines as one mesh, its markers as one mesh, and its labels.
    /// The markers and labels sit in the layer's marker and label containers, so every marker and label draws over
    /// every drawing's lines.
    /// </summary>
    private sealed class ShapeGraphics
    {
        private readonly List<MapLabel> _labels = new();
        private readonly Transform _labelParent;
        private readonly PlateText _plateText;
        private ImageBatch? _markers;
        private int _labelsUsed;

        /// <summary>How many labels the last finished draw used.</summary>
        private int _lastLabelCount;

        public ShapeGraphics(Transform layer, Transform markers, Transform labels, PlateText plateText, string name)
        {
            _labelParent = labels;
            _plateText = plateText;
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

        /// <summary>For a tool's overlay, the id of the stored shape it draws again (<see cref="MapTool.RedrawnShape"/>), or 0.</summary>
        public int RedrawnId { get; set; }

        public void Begin()
        {
            Strokes.Clear();
            _markers?.Clear();
            _labelsUsed = 0;
            Live = false;
            Undrawn = false;
        }

        /// <summary>True when this draw used a different number of labels from the last, so the labels need placing again.</summary>
        public bool End()
        {
            for (var i = _labelsUsed; i < _labels.Count; i++)
            {
                _labels[i].Visible = false;
            }

            _markers?.Apply();
            Strokes.Apply();
            // A note or an idle tool draws no lines, and an enabled graphic is still culled against the map's mask every frame.
            var drawn = !Strokes.IsEmpty;
            if (Strokes.enabled != drawn)
            {
                Strokes.enabled = drawn;
            }

            var labelsChanged = _labelsUsed != _lastLabelCount;
            _lastLabelCount = _labelsUsed;
            return labelsChanged;
        }

        public MapLabel NextLabel()
        {
            if (_labelsUsed == _labels.Count)
            {
                _labels.Add(new MapLabel(_labelParent, _plateText));
            }

            var label = _labels[_labelsUsed++];
            label.Visible = true;
            return label;
        }

        /// <summary>Where this draw's label at <paramref name="index"/> was placed, or null if the draw had fewer labels.</summary>
        public PlacedLabel? TryGetPlacement(int index) => index < _labelsUsed ? _labels[index].Placement : null;

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

        /// <summary>
        /// A copy of the marker at <paramref name="local"/>, <paramref name="scale"/> times its size, turned upright by
        /// <paramref name="toMap"/>. False if the game's marker has no image to copy. A route's markers are one graphic,
        /// where each used to be a copy of the game's prefab: a hundred graphics for Unity to cull every frame.
        /// </summary>
        public bool AddMarker(GameObject prefab, Vector2 local, float scale, Color32 color, Quaternion toMap)
        {
            if (_markers == null)
            {
                // Maskable, unlike the game's prefab, so markers don't draw past the minimap's edge.
                _markers = MarkerRect.gameObject.AddComponent<ImageBatch>();
                _markers.raycastTarget = false;
            }

            if (!_markers.Use(prefab))
            {
                return false;
            }

            _markers.Turn = toMap;
            _markers.Add(local, scale, color);
            return true;
        }

        /// <summary>Hidden labels too, so one shown again on a map that has stopped turning is already upright.</summary>
        public void KeepUpright(Quaternion toMap)
        {
            foreach (var label in _labels)
            {
                label.StandUpright();
            }

            if (_markers != null)
            {
                _markers.Turn = toMap;
            }
        }

        /// <summary>The markers and labels live under the layer's containers, not this shape's rect, so they go separately.</summary>
        public void Destroy()
        {
            Object.Destroy(Rect.gameObject);
            Object.Destroy(MarkerRect.gameObject);
            foreach (var label in _labels)
            {
                label.Destroy();
            }
        }
    }
}
