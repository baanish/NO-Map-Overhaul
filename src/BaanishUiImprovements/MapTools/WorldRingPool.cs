using System.Collections.Generic;
using BaanishUiImprovements.Drawing;
using BaanishUiImprovements.MapTools.Circle;
using UnityEngine;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Rings in the 3D view, which the Circle tool requests each frame through <see cref="WorldLabelPool"/>. Each ring is a
/// flat <see cref="StrokeGraphic"/> on the HUD canvas, beside the 3D labels, so it hides with the HUD and while the map
/// is open. <see cref="RingProjection"/> projects and clips it; a ring is rebuilt only when the camera, the circle, or
/// the line width changed since its last frame.
/// </summary>
internal sealed class WorldRingPool
{
    /// <summary>Rings drawn at once; requests past it are dropped. Each is a 128-chord mesh rebuilt as the camera moves.</summary>
    private const int MaxRings = 16;

    private readonly ModSettings _settings;
    private readonly RingProjection _projection = new();
    private readonly List<Ring> _rings = new();
    private RectTransform? _root;
    private Transform? _parent;
    private ScreenCamera _view;
    private float _scale;
    private bool _drawing;
    private int _used;

    public WorldRingPool(ModSettings settings) => _settings = settings;

    /// <summary>
    /// Starts a frame's requests under the HUD's label parent, or none when there is no HUD or camera. Leaving the
    /// mission destroys the HUD with the rings in it.
    /// </summary>
    public void Begin(Transform? parent, Camera? camera)
    {
        if (!ReferenceEquals(parent, _parent) || (_root is not null && _root == null))
        {
            Reset();
            _parent = parent;
        }

        _used = 0;
        _drawing = parent != null && camera != null && _settings.MapToolCirclesIn3D.Value;
        if (!_drawing)
        {
            return;
        }

        var root = Root();
        // The HUD canvas draws over the screen, where world units are pixels: at the world origin, unturned, this
        // container's local units are pixels over its scale.
        if (root.position != Vector3.zero || root.rotation != Quaternion.identity)
        {
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        _scale = root.lossyScale.x;
        if (_scale <= 0f)
        {
            _drawing = false;
            return;
        }

        var transform = camera!.transform;
        var position = transform.position;
        var rotation = transform.rotation;
        var halfScreen = new NumericsVector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _view = new ScreenCamera(new NumericsVector3(position.x, position.y, position.z),
            new NumericsQuaternion(rotation.x, rotation.y, rotation.z, rotation.w), halfScreen,
            halfScreen.Y / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad), camera.nearClipPlane);
    }

    /// <param name="center">Global meters, as <see cref="IWorldLabels.Ring"/> takes it.</param>
    public void Add(NumericsVector3 center, float radius, ShapeColor color)
    {
        if (!_drawing || _used >= MaxRings)
        {
            return;
        }

        var ring = Next();
        var local = new GlobalPosition(center.X, center.Y, center.Z).ToLocalPosition();
        var key = new RingKey(new NumericsVector3(local.x, local.y, local.z), radius, color, _settings.MapToolLineWidth.Value, _scale, _view);
        if (!ring.Graphic.enabled)
        {
            ring.Graphic.enabled = true;
        }

        if (ring.Key is { } last && key.Equals(last))
        {
            return;
        }

        ring.Key = key;
        _projection.Project(key.Center, radius, _view);
        var graphic = ring.Graphic;
        var fill = color.ToColor32();
        graphic.Clear();
        var runs = _projection.Runs;
        for (var run = 0; run < runs.Count; run++)
        {
            var (start, count) = runs[run];
            for (var i = start; i < start + count; i++)
            {
                var point = _projection.Points[i];
                graphic.AddPoint(new Vector2(point.X, point.Y) / _scale);
            }

            graphic.EndStroke(_projection.Closed, key.LineWidth * 0.5f, fill, 0f, fill, EdgeProfile.AntiAliasWidth / _scale);
        }

        graphic.Apply();
    }

    /// <summary>Hides the rings no circle asked for this frame.</summary>
    public void End()
    {
        for (var i = _used; i < _rings.Count; i++)
        {
            if (_rings[i].Graphic.enabled)
            {
                _rings[i].Graphic.enabled = false;
            }
        }
    }

    public void Reset()
    {
        if (_root != null)
        {
            Object.Destroy(_root.gameObject);
        }

        _root = null;
        _parent = null;
        _rings.Clear();
        _used = 0;
    }

    private RectTransform Root()
    {
        if (_root == null)
        {
            _root = new GameObject("BaanishWorldRings", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(_parent, false);
            _root.SetAsFirstSibling(); // under the 3D labels, so a ring never crosses their text
        }

        return _root;
    }

    private Ring Next()
    {
        if (_used == _rings.Count)
        {
            var rect = new GameObject("Ring", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(_root, false);
            var graphic = rect.gameObject.AddComponent<StrokeGraphic>();
            graphic.raycastTarget = false;
            _rings.Add(new Ring(graphic));
        }

        return _rings[_used++];
    }

    private sealed class Ring
    {
        public Ring(StrokeGraphic graphic) => Graphic = graphic;

        public StrokeGraphic Graphic { get; }

        /// <summary>What the mesh was last built from, or null before the first build.</summary>
        public RingKey? Key { get; set; }
    }

    /// <summary>Everything a ring's mesh depends on, with the centre in Unity's scene space as the camera is.</summary>
    private readonly struct RingKey : System.IEquatable<RingKey>
    {
        public RingKey(NumericsVector3 center, float radius, ShapeColor color, float lineWidth, float scale, ScreenCamera view)
        {
            Center = center;
            Radius = radius;
            Color = color;
            LineWidth = lineWidth;
            Scale = scale;
            View = view;
        }

        public NumericsVector3 Center { get; }
        public float Radius { get; }
        public ShapeColor Color { get; }
        public float LineWidth { get; }
        public float Scale { get; }
        public ScreenCamera View { get; }

        public bool Equals(RingKey other) =>
            Center == other.Center && Radius == other.Radius && Color == other.Color && LineWidth == other.LineWidth &&
            Scale == other.Scale && View.Equals(other.View);

        public override bool Equals(object? obj) => obj is RingKey other && Equals(other);

        public override int GetHashCode() => System.HashCode.Combine(Center, Radius, Color, LineWidth, Scale, View);
    }
}
