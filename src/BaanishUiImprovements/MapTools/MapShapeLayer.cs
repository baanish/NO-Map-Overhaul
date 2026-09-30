using System.Collections.Generic;
using BaanishUiImprovements.Drawing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Draws the store's shapes and each tool's overlay on the DynamicMap. Like the runways it is a layer under the map's
/// icon layer, so it shows on the minimap and the full map and inherits their pan, zoom, and rotation. It is the last
/// child there, so drawings sit over unit icons, as the game's own waypoints do.
/// It is also the <see cref="IMapCanvas"/> the shapes draw into. Each shape keeps its own mesh and labels, rebuilt only
/// when the shape is added, when the zoom or a drawing setting changes, or on the game's 10 Hz map refresh if it drew
/// anything live. Panning moves the layer with the map and rebuilds nothing. The layer is its own canvas, so rebuilding
/// a shape doesn't re-batch the game's icons, and icons moving every frame don't re-batch a long pen stroke.
/// </summary>
internal sealed class MapShapeLayer : IMapCanvas
{
    /// <summary>Half the angle between an arrowhead's two barbs.</summary>
    private const float ArrowBarbDegrees = 25f;

    private readonly ModSettings _settings;
    private readonly IMapView _view;
    private readonly Dictionary<MapShape, ShapeGraphics> _graphics = new();
    private readonly HashSet<MapShape> _seen = new();
    private readonly List<MapShape> _stale = new();
    private readonly ShapeGraphics?[] _overlays;
    private RectTransform? _layer;
    private GameObject? _markerPrefab;
    private TextMeshProUGUI? _hudStyle;
    private int _storeVersion = -1;
    private float _lastTick = -1f;
    private float _factor;
    private float _inverseScale;
    private (float Line, float Rim, Color RimColor, float Text) _style;
    private ShapeGraphics? _target;
    private Quaternion _uprightFor;
    private bool _redrawnSinceUpright;

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
    /// waypoint leg draw stored shapes.
    /// </summary>
    public void Render(DynamicMap map, ShapeStore store, IReadOnlyList<MapTool> tools, TextMeshProUGUI? hudStyle)
    {
        var fresh = EnsureLayer(map);
        var tick = map.mapLastUpdated != _lastTick;
        _lastTick = map.mapLastUpdated;
        if (tick && _layer!.GetSiblingIndex() != _layer.parent.childCount - 1)
        {
            _layer.SetAsLastSibling(); // the game appends each new unit's icon
        }

        var factor = map.mapDisplayFactor;
        var inverseScale = 1f / map.mapImage.transform.localScale.x;
        var style = (_settings.MapToolLineWidth.Value, _settings.OutlineWidth.Value, _settings.OutlineColor.Value, _settings.MapToolTextSize.Value);
        var restyle = fresh || factor != _factor || inverseScale != _inverseScale || !style.Equals(_style) || !ReferenceEquals(hudStyle, _hudStyle);
        var synced = store.Version != _storeVersion;
        if (!restyle && !synced && !tick && !AnyOverlayInvalid(tools))
        {
            return;
        }

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

    /// <summary>
    /// Per frame: the minimap turns every frame, so text and markers reset to upright between redraws. Nothing to do
    /// while the map holds still, as the full map does, and no redraw has placed new ones.
    /// </summary>
    public void KeepUpright()
    {
        if (_layer == null)
        {
            return; // destroyed with the old scene's map; the next render rebuilds everything
        }

        var rotation = _layer.rotation;
        if (!_redrawnSinceUpright && rotation.Equals(_uprightFor))
        {
            return; // exact comparison: Quaternion's == lets a slow turn creep by unnoticed
        }

        _uprightFor = rotation;
        _redrawnSinceUpright = false;

        foreach (var graphics in _graphics.Values)
        {
            graphics.KeepUpright();
        }

        foreach (var overlay in _overlays)
        {
            overlay?.KeepUpright();
        }
    }

    public void Reset()
    {
        if (_layer != null)
        {
            Object.Destroy(_layer.gameObject);
        }

        _layer = null;
        _graphics.Clear();
        System.Array.Clear(_overlays, 0, _overlays.Length);
        _storeVersion = -1;
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

        var back = -shaft.normalized * Mathf.Min(MapCanvasMetrics.ArrowHeadLength * _inverseScale, shaft.magnitude);
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

        marker.rectTransform.localPosition = Local(position);
        marker.rectTransform.localScale = Vector3.one * _inverseScale;
        marker.color = color.ToColor32();
    }

    public void Label(FlatVector position, string text, ShapeColor color, LabelPlacement placement = LabelPlacement.Center)
    {
        var (pivot, alignment, away) = placement switch
        {
            LabelPlacement.Above => (new Vector2(0.5f, 0f), TextAlignmentOptions.Bottom, Vector2.up),
            LabelPlacement.Below => (new Vector2(0.5f, 1f), TextAlignmentOptions.Top, Vector2.down),
            LabelPlacement.Left => (new Vector2(1f, 0.5f), TextAlignmentOptions.Right, Vector2.left),
            LabelPlacement.Right => (new Vector2(0f, 0.5f), TextAlignmentOptions.Left, Vector2.right),
            _ => (new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center, Vector2.zero),
        };
        var label = _target!.NextLabel();
        label.Align(pivot, alignment, away * MapCanvasMetrics.LabelGap);
        label.Rect.localPosition = Local(position);
        label.Rect.localScale = Vector3.one * _inverseScale;
        label.Set(text, _settings.MapToolTextSize.Value, color.ToColor32(), _settings.OutlineColor.Value, _hudStyle);
    }

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
        for (var i = 0; i < _overlays.Length; i++)
        {
            _overlays[i] = new ShapeGraphics(_layer, "ToolOverlay");
        }

        return true;
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
                graphics = new ShapeGraphics(_layer!, "MapShape");
                _graphics.Add(shape, graphics);
            }

            if (graphics.Rect.GetSiblingIndex() != i)
            {
                graphics.Rect.SetSiblingIndex(i);
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
        }

        _storeVersion = store.Version;
    }

    private void Begin(ShapeGraphics graphics)
    {
        graphics.Begin();
        _target = graphics;
        _redrawnSinceUpright = true;
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

    private void EndStroke(ShapeColor color, bool closed) =>
        _target!.Strokes.EndStroke(closed, _settings.MapToolLineWidth.Value * 0.5f * _inverseScale, color.ToColor32(),
            _settings.OutlineWidth.Value * _inverseScale, _settings.OutlineColor.Value, EdgeProfile.AntiAliasWidth * _inverseScale);

    private Vector2 Local(FlatVector meters) => new Vector2(meters.X, meters.Y) * _factor;

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>One shape's graphics: its lines as one mesh, then its markers and labels on top, reused from draw to draw.</summary>
    private sealed class ShapeGraphics
    {
        private readonly List<OutlinedText> _labels = new();
        private readonly List<Image> _markers = new();
        private int _labelsUsed;
        private int _markersUsed;

        public ShapeGraphics(Transform layer, string name)
        {
            Rect = NewRect(name, layer);
            Strokes = NewRect("Strokes", Rect).gameObject.AddComponent<StrokeGraphic>();
            Strokes.raycastTarget = false;
        }

        public RectTransform Rect { get; }

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

        public OutlinedText NextLabel()
        {
            if (_labelsUsed == _labels.Count)
            {
                _labels.Add(new OutlinedText("Label", Rect, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center));
            }

            var label = _labels[_labelsUsed++];
            label.Visible = true;
            return label;
        }

        /// <summary>Null if the game's marker has no image to tint.</summary>
        public Image? NextMarker(GameObject prefab)
        {
            if (_markersUsed == _markers.Count)
            {
                var copy = Object.Instantiate(prefab, Rect);
                if (!copy.TryGetComponent<Image>(out var image))
                {
                    Object.Destroy(copy);
                    return null;
                }

                copy.name = "Marker";
                image.raycastTarget = false;
                _markers.Add(image);
            }

            var marker = _markers[_markersUsed++];
            marker.enabled = true;
            return marker;
        }

        public void KeepUpright()
        {
            for (var i = 0; i < _labelsUsed; i++)
            {
                _labels[i].Rect.rotation = Quaternion.identity;
            }

            for (var i = 0; i < _markersUsed; i++)
            {
                _markers[i].rectTransform.rotation = Quaternion.identity;
            }
        }

        public void Destroy() => Object.Destroy(Rect.gameObject);
    }
}
