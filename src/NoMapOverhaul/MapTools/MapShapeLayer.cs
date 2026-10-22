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
    /// <summary>
    /// By shape id, not shape: an edit or a teammate's update replaces the shape under the same id, and its graphics
    /// redraw in place rather than being destroyed and built again.
    /// </summary>
    private readonly Dictionary<int, ShapeGraphics> _graphics = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _stale = new();
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

    /// <summary>Every note, after every label on a plate.</summary>
    private NoteText? _noteText;
    private GameObject? _markerPrefab;
    private TextMeshProUGUI? _hudStyle;
    private int _storeVersion = -1;
    private int _receivedVersion = -1;
    private float _lastTick = -1f;
    private float _factor;
    private float _inverseScale;
    private (float Line, Color RimColor, float Text, DistanceUnit Units, string DrawingChannel, float OtherOpacity, float OtherBrightness) _style;

    /// <summary>
    /// World units per icon unit when every label was last typeset: the text's world scale, which TextMeshPro bakes into
    /// each glyph's SDF edge. It changes without a zoom when the map moves to a canvas of another scale.
    /// </summary>
    private float _typesetUnit;

    /// <summary>The labels' typesetter had woken by the last frame; typesets before it wakes make nothing.</summary>
    private bool _typesetterAwake;

    /// <summary>
    /// The shape drawing now is on a channel other than the one new drawings go to, and draws fainter and darker by the
    /// Other channels settings, so a channel switch shows which drawings it covers. Overlays draw at full unless they
    /// belong to such a drawing (<see cref="MapTool.OverlayChannel"/>); the eraser's highlight always does.
    /// </summary>
    private bool _dimmed;

    private Color32 Shade(ShapeColor color)
    {
        var shade = color.ToColor32();
        if (_dimmed)
        {
            var brightness = Mathf.Clamp01(_settings.OtherChannelsBrightness.Value);
            shade.r = (byte)(shade.r * brightness);
            shade.g = (byte)(shade.g * brightness);
            shade.b = (byte)(shade.b * brightness);
            shade.a = (byte)(shade.a * Mathf.Clamp01(_settings.OtherChannelsOpacity.Value));
        }

        return shade;
    }

    private ShapeGraphics? _target;
    /// <summary>The map's rotation when its labels and markers last stood upright, which the labels are placed for.</summary>
    private Quaternion _uprightFor = Quaternion.identity;
    private Quaternion _placedForRotation;
    private float _spacedHeading;
    private bool _labelsChanged;

    /// <summary>A label only moved with its unit, or changed colour: placed again in the slot it has.</summary>
    private bool _labelsMoved;
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
        IReadOnlyList<RectTransform> screenAreas, int screenLayout, IReadOnlyList<MapShape> received, int receivedVersion)
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
        var layerScale = _layer.lossyScale.x;
        var unit = layerScale * inverseScale;
        var awake = _plateText!.Awake;
        var woke = awake && !_typesetterAwake;
        _typesetterAwake = awake;
        var style = (_settings.MapToolLineWidth.Value, _settings.OutlineColor.Value, _settings.MapToolTextSize.Value, Units, store.DrawingChannel, _settings.OtherChannelsOpacity.Value, _settings.OtherChannelsBrightness.Value);
        var restyle = fresh || woke || factor != _factor || inverseScale != _inverseScale || !style.Equals(_style) ||
                      !ReferenceEquals(hudStyle, _hudStyle) || Mathf.Abs(unit - _typesetUnit) > MapLabel.RetypesetScaleChange * Mathf.Abs(_typesetUnit);
        var synced = store.Version != _storeVersion || receivedVersion != _receivedVersion;
        var redraw = restyle || synced || tick || AnyOverlayInvalid(tools);
        // The view is worked out again only when drawings may redraw or the map was panned well across: the minimap
        // moves and turns every frame, and each transform call on it pays for the moved hierarchy.
        var viewChanged = redraw || (_layer.position - _viewportAt).sqrMagnitude > _viewportSlack * _viewportSlack;
        if (viewChanged)
        {
            UpdateViewport(map, inverseScale);
        }

        if (redraw)
        {
            _factor = factor;
            _inverseScale = inverseScale;
            _style = style;
            _hudStyle = hudStyle;
            _markerPrefab = map.mapWaypoint;
            if (restyle)
            {
                _typesetUnit = unit;
            }

            if (synced)
            {
                Sync(store, received);
                ModActivity.ReceivedSyncs += _receivedVersion >= 0 && receivedVersion != _receivedVersion ? 1 : 0; // not a reset's redraw
                _receivedVersion = receivedVersion;
            }

            // A zoom or a new style moves every label, and a shape gone from the store takes its labels with it.
            _labelsChanged |= restyle || synced;

            foreach (var graphics in _graphics.Values)
            {
                // A drawing on units redraws only when one of them moved or its track changed, not on every refresh: a
                // parked, lost or destroyed unit leaves it as it was, and teammates' drawings on such units pile up.
                // One out of view redraws only once its units could have brought it near.
                if (graphics.Undrawn || restyle ||
                    (tick && graphics.Live && AnchorsMoved(graphics) && (graphics.InView || AnchorsNearView(graphics))))
                {
                    var shape = graphics.Shape!;
                    Begin(graphics);
                    _dimmed = shape.Channel != store.DrawingChannel;
                    shape.Draw(this);
                    _dimmed = false;
                    End();
                }
            }

            for (var i = 0; i < tools.Count; i++)
            {
                var overlay = _overlays[i]!;
                if (restyle || synced || tools[i].OverlayInvalid || (tick && overlay.Live))
                {
                    Begin(overlay);
                    _dimmed = tools[i].OverlayChannel is { } channel && channel != store.DrawingChannel;
                    tools[i].DrawOverlay(this);
                    _dimmed = false;
                    End();
                    tools[i].OverlayInvalid = false;
                    var redrawn = tools[i].RedrawnShape?.Id ?? 0;
                    _labelsChanged |= redrawn != overlay.RedrawnId; // a highlight moving between identical drawings changes no label text
                    overlay.RedrawnId = redrawn;
                }
            }
        }

        if (viewChanged)
        {
            foreach (var graphics in _graphics.Values)
            {
                var inView = !graphics.HasBounds || _viewport.Overlaps(graphics.Bounds);
                if (inView != graphics.InView)
                {
                    graphics.ShowInView(inView);
                    _labelsChanged = true; // the labels to lay out are the ones in view
                }
            }
        }

        ModTimings.Stop(ModSection.ShapeDrawing, drawing);
        var upright = ModTimings.Start();
        KeepUpright();
        ModTimings.Stop(ModSection.UprightTurning, upright);
        var placing = ModTimings.Start();
        PlaceLabels(map, screenAreas, screenLayout, layerScale);
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
    /// <param name="scale">The layer's world scale, read once a frame.</param>
    private void PlaceLabels(DynamicMap map, IReadOnlyList<RectTransform> screenAreas, int screenLayout, float scale)
    {
        var position = _layer!.position;
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
        if (!_labelsChanged && !_labelsMoved && !cameToRest && !turned && fullMap == _placedOnFullMap && screenLayout == _placedForScreenLayout)
        {
            return;
        }

        ModActivity.LabelLayouts++;
        var switchedMap = fullMap != _placedOnFullMap;
        var relaid = screenLayout != _placedForScreenLayout;
        var changed = _labelsChanged;
        _labelsChanged = false;
        _labelsMoved = false;
        _placedForRotation = toUpright;
        _placedOnFullMap = fullMap;
        _placedForScreenLayout = screenLayout;
        _placing.Clear();
        _placements.Clear();
        foreach (var pair in _graphics)
        {
            if (pair.Value.InView)
            {
                pair.Value.AddLabels(_placing, pair.Key, overlay: false);
            }
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
        // Labels that only moved with their units keep their slots (obstacles stay null), so they don't jump about as
        // units move; they're laid out again when one changes, the full map comes to rest, or the minimap turns a step.
        if (fullMap && still && (changed || switchedMap || cameToRest || relaid))
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
        var notes = _noteText!;
        plates.Clear();
        text.Clear();
        notes.Clear();
        foreach (var label in _placing)
        {
            label.AddPlate(plates, toMap, _inverseScale);
            label.AddText(toMap, _inverseScale);
        }

        plates.Apply();
        text.Apply();
        notes.Apply(toMap, _inverseScale);
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
        _noteText = null;
        _placing.Clear();
        _graphics.Clear();
        System.Array.Clear(_overlays, 0, _overlays.Length);
        _storeVersion = -1;
        _receivedVersion = -1;
        _placedForScreenLayout = -1;
        _uprightFor = Quaternion.identity; // how every label and marker is made
    }

    /// <summary>Only on the full map, the one map the tools take clicks on.</summary>
    public bool TryGetBox(MapShape shape, int index, out LabelBox box)
    {
        if (_placedOnFullMap && _graphics.TryGetValue(shape.Id, out var graphics) && ReferenceEquals(graphics.Shape, shape) &&
            graphics.TryGetPlacement(index) is { Slot: >= 0 } placement)
        {
            box = placement.Box;
            return true;
        }

        box = default;
        return false;
    }

    public bool TryResolve(MapPoint point, out FlatVector position)
    {
        if (!point.IsAnchored)
        {
            return _view.TryResolve(point, out position);
        }

        var found = TryResolveWorld(point, out var world);
        position = new FlatVector(world.X, world.Z);
        return found;
    }

    public bool TryResolveWorld(MapPoint point, out WorldVector position)
    {
        var found = _view.TryResolveWorld(point, out position);
        if (point.IsAnchored && _target != null)
        {
            _target.Live = true;
            _target.Anchors.Add((point, position, found));
        }

        return found;
    }

    /// <summary>How far past the map's edge, in icon units, a drawing still counts as in view: about a label's reach from its anchor.</summary>
    private const float LabelReach = 80f;

    private readonly Vector3[] _corners = new Vector3[4];

    /// <summary>
    /// The part of the layer the map shows, in the layer's units, grown by a sixth of its size and by
    /// <see cref="LabelReach"/>: a drawing outside it has its graphics switched off and its labels left out of the layout,
    /// so a pile of teammates' drawings far off costs nothing each frame. Only a whole drawing out of view is left out.
    /// </summary>
    private Rect _viewport;

    /// <summary>Where the layer was when <see cref="_viewport"/> was worked out.</summary>
    private Vector3 _viewportAt;

    /// <summary>
    /// How far, in world units, the layer may move before the view is worked out again between redraws: a 24th of the
    /// map's diagonal, well inside the sixth of its size the view is grown by.
    /// </summary>
    private float _viewportSlack;

    private void UpdateViewport(DynamicMap map, float inverseScale)
    {
        ((RectTransform)map.transform).GetWorldCorners(_corners);
        _viewportAt = _layer!.position;
        _viewportSlack = (_corners[2] - _corners[0]).magnitude / 24f;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var corner in _corners)
        {
            var local = (Vector2)_layer!.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        var margin = (max - min) / 6f + Vector2.one * (LabelReach * inverseScale);
        _viewport = Rect.MinMaxRect(min.x - margin.x, min.y - margin.y, max.x + margin.x, max.y + margin.y);
    }

    /// <summary>Whether the drawing as last drawn, stretched to where its units are now, reaches the map's view.</summary>
    private bool AnchorsNearView(ShapeGraphics graphics)
    {
        var bounds = graphics.Bounds;
        foreach (var (point, _, _) in graphics.Anchors)
        {
            _view.TryResolveWorld(point, out var now);
            var at = Local(new FlatVector(now.X, now.Z));
            bounds.min = Vector2.Min(bounds.min, at);
            bounds.max = Vector2.Max(bounds.max, at);
        }

        return _viewport.Overlaps(bounds);
    }

    /// <summary>Whether any unit the drawing's last draw resolved now resolves elsewhere, or has gained or lost its live track.</summary>
    private bool AnchorsMoved(ShapeGraphics graphics)
    {
        foreach (var (point, world, found) in graphics.Anchors)
        {
            if (_view.TryResolveWorld(point, out var now) != found || now != world)
            {
                return true;
            }
        }

        return false;
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

        var head = MapCanvasMetrics.ArrowHeadPerLineWidth * _settings.MapToolLineWidth.Value * _inverseScale;
        var back = -shaft.normalized * Mathf.Min(head, shaft.magnitude);
        var strokes = _target!.Strokes;
        strokes.AddPoint(tip + (Vector2)(Quaternion.Euler(0f, 0f, MapCanvasMetrics.ArrowBarbDegrees) * back));
        strokes.AddPoint(tip);
        strokes.AddPoint(tip + (Vector2)(Quaternion.Euler(0f, 0f, -MapCanvasMetrics.ArrowBarbDegrees) * back));
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
        _target!.Include(middle - Vector2.one * local);
        _target.Include(middle + Vector2.one * local);
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
            !_target!.AddMarker(_markerPrefab, Local(position), _inverseScale, Shade(color), Quaternion.Inverse(_uprightFor)))
        {
            Circle(position, MapCanvasMetrics.MarkerRadius * MetersPerIconUnit, color);
        }
    }

    /// <summary>
    /// Sets the text now; the label is placed once every shape has drawn (<see cref="PlaceLabels"/>), if it changed. A live
    /// shape draws ten times a second, and its labels mostly haven't moved.
    /// </summary>
    public void Label(LabelAnchor anchor, string text, ShapeColor color)
    {
        Local(anchor.Point);
        var change = _target!.NextLabel().Set(anchor.Scaled(_factor / _inverseScale), text, _settings.MapToolTextSize.Value,
            Shade(color), _settings.OutlineColor.Value, _hudStyle, _inverseScale);
        _labelsChanged |= change == LabelChange.Changed;
        _labelsMoved |= change == LabelChange.Moved;
    }

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
        _noteText = new NoteText(_labelRoot);
        for (var i = 0; i < _overlays.Length; i++)
        {
            _overlays[i] = new ShapeGraphics(_layer, _markerRoot, _plateText, _noteText, "ToolOverlay");
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

    /// <summary>Makes graphics for new ids, drops those of ids gone from the store, and orders them as the store does, overlays on top.</summary>
    private void Sync(ShapeStore store, IReadOnlyList<MapShape> received)
    {
        _seen.Clear();
        var shapes = store.Shapes;
        for (var i = 0; i < shapes.Count + received.Count; i++)
        {
            _seen.Add((i < shapes.Count ? shapes[i] : received[i - shapes.Count]).Id);
        }

        // Gone ones leave first: Destroy waits for the frame's end, and one left in place ahead of the rest would move
        // every later drawing a slot, each move re-sorting the canvas.
        _stale.Clear();
        foreach (var pair in _graphics)
        {
            if (!_seen.Contains(pair.Key))
            {
                pair.Value.Destroy();
                _stale.Add(pair.Key);
            }
        }

        foreach (var id in _stale)
        {
            _graphics.Remove(id);
        }

        for (var i = 0; i < shapes.Count + received.Count; i++)
        {
            var shape = i < shapes.Count ? shapes[i] : received[i - shapes.Count];
            if (!_graphics.TryGetValue(shape.Id, out var graphics))
            {
                graphics = new ShapeGraphics(_layer!, _markerRoot!, _plateText!, _noteText!, "MapShape");
                _graphics.Add(shape.Id, graphics);
            }

            graphics.Shape = shape;
            if (graphics.Rect.GetSiblingIndex() != i)
            {
                graphics.Rect.SetSiblingIndex(i);
            }

            if (graphics.MarkerRect.GetSiblingIndex() != i)
            {
                graphics.MarkerRect.SetSiblingIndex(i);
            }
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
        var fill = Shade(color);
        _target!.Strokes.EndStroke(closed, _settings.MapToolLineWidth.Value * 0.5f * _inverseScale, fill, 0f, fill,
            EdgeProfile.AntiAliasWidth * _inverseScale);
    }

    /// <summary>A point in the layer's units; while a drawing draws, it also grows that drawing's bounds.</summary>
    private Vector2 Local(FlatVector meters)
    {
        var local = new Vector2(meters.X, meters.Y) * _factor;
        _target?.Include(local);
        return local;
    }

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
        private readonly PlateText _plateText;
        private readonly NoteText _noteText;
        private ImageBatch? _markers;
        private MapShape? _shape;
        private int _labelsUsed;

        /// <summary>How many labels the last finished draw used.</summary>
        private int _lastLabelCount;

        public ShapeGraphics(Transform layer, Transform markers, PlateText plateText, NoteText noteText, string name)
        {
            _plateText = plateText;
            _noteText = noteText;
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

        /// <summary>The last draw read a unit's or the aircraft's position, so each map refresh checks whether it moved.</summary>
        public bool Live { get; set; }

        /// <summary>Each unit the last draw resolved, where it was, and whether its track was live.</summary>
        public List<(MapPoint Point, WorldVector World, bool Found)> Anchors { get; } = new();

        /// <summary>Everything the last draw put down, in the layer's units, while <see cref="HasBounds"/>.</summary>
        public UnityEngine.Rect Bounds { get; private set; }

        public bool HasBounds { get; private set; }

        /// <summary>Within the map's view as last checked; out of it, the drawing's graphics are off and its labels skipped.</summary>
        public bool InView { get; private set; } = true;

        public void Include(Vector2 point)
        {
            Bounds = HasBounds ? UnityEngine.Rect.MinMaxRect(Mathf.Min(Bounds.xMin, point.x), Mathf.Min(Bounds.yMin, point.y),
                Mathf.Max(Bounds.xMax, point.x), Mathf.Max(Bounds.yMax, point.y)) : new UnityEngine.Rect(point, Vector2.zero);
            HasBounds = true;
        }

        public void ShowInView(bool inView)
        {
            InView = inView;
            ApplyView();
        }

        private void ApplyView()
        {
            var lines = InView && !Strokes.IsEmpty;
            if (Strokes.enabled != lines)
            {
                Strokes.enabled = lines;
            }

            if (_markers != null && _markers.enabled != InView)
            {
                _markers.enabled = InView;
            }
        }

        public bool Undrawn { get; private set; } = true;

        /// <summary>The stored shape this draws, or null for a tool's overlay. A different one draws on the next render.</summary>
        public MapShape? Shape
        {
            get => _shape;
            set
            {
                if (!ReferenceEquals(_shape, value))
                {
                    _shape = value;
                    Undrawn = true;
                }
            }
        }

        /// <summary>For a tool's overlay, the id of the stored shape it draws again (<see cref="MapTool.RedrawnShape"/>), or 0.</summary>
        public int RedrawnId { get; set; }

        public void Begin()
        {
            Strokes.Clear();
            _markers?.Clear();
            _labelsUsed = 0;
            Live = false;
            Anchors.Clear();
            HasBounds = false;
            Undrawn = false;
        }

        /// <summary>True when this draw used a different number of labels from the last, so the labels need placing again.</summary>
        public bool End()
        {
            _markers?.Apply();
            Strokes.Apply();
            // A note or an idle tool draws no lines, and an enabled graphic is still culled against the map's mask every
            // frame, as is one out of view.
            ApplyView();

            var labelsChanged = _labelsUsed != _lastLabelCount;
            _lastLabelCount = _labelsUsed;
            return labelsChanged;
        }

        public MapLabel NextLabel()
        {
            if (_labelsUsed == _labels.Count)
            {
                _labels.Add(new MapLabel(_plateText, _noteText));
            }

            return _labels[_labelsUsed++];
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

        public void KeepUpright(Quaternion toMap)
        {
            if (_markers != null)
            {
                _markers.Turn = toMap;
            }
        }

        /// <summary>The markers live under the layer's marker container, not this shape's rect, so they go separately.</summary>
        public void Destroy()
        {
            // Out of the hierarchy now, since Destroy waits for the frame's end and the drawings after these move up.
            Rect.SetParent(null, false);
            MarkerRect.SetParent(null, false);
            Object.Destroy(Rect.gameObject);
            Object.Destroy(MarkerRect.gameObject);
        }
    }
}
