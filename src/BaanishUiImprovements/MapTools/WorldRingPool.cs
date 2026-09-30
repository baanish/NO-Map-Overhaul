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
/// is open. <see cref="RingProjection"/> projects and clips it. A ring whose mesh was the whole ring in view is moved
/// rather than rebuilt while the camera only slides it across the screen (see <see cref="Add"/>). Nothing is projected
/// while the HUD's canvas is switched off.
/// </summary>
internal sealed class WorldRingPool
{
    /// <summary>Rings drawn at once; requests past it are dropped. Each is a mesh of up to 128 chords.</summary>
    private const int MaxRings = 16;

    /// <summary>
    /// How far, in pixels, any of a ring's <see cref="RingProjection.SampleCount"/> samples may land from where moving
    /// the old mesh puts it before the ring is rebuilt. A ring's shape on screen changes smoothly around it, so between
    /// the samples the moved mesh strays barely further from the rebuilt ring. That's about as far as the chords may
    /// stray from the circle, and inside the line's one-pixel anti-aliased edge, so a moved ring looks like a rebuilt one.
    /// </summary>
    private const float MaxShiftError = 0.4f;

    private readonly ModSettings _settings;
    private readonly RingProjection _projection = new();
    private readonly List<Ring> _rings = new();
    private readonly NumericsVector2[] _samples = new NumericsVector2[RingProjection.SampleCount];
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
        // The game switches the HUD's canvas off while the full map is open over the cockpit; rings built then never show.
        _drawing = parent != null && parent.gameObject.activeInHierarchy && camera != null && _settings.MapToolCirclesIn3D.Value;
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

    /// <summary>
    /// Draws a ring this frame. One whose mesh is the whole ring in view is only moved while it keeps its shape on
    /// screen: its samples, projected afresh, each land within <see cref="MaxShiftError"/> of where moving the mesh by
    /// their mean shift puts them. A turn of the camera slides a ring across the screen with almost no change of shape,
    /// so a ring far off is rebuilt only now and then in flight, and one the camera nears is rebuilt as it changes
    /// shape. A ring cut by the screen's edge or the near plane is rebuilt whenever the camera moves.
    /// </summary>
    /// <param name="center">Global meters, as <see cref="IWorldLabels.Ring"/> takes it.</param>
    public void Add(NumericsVector3 center, float radius, ShapeColor color)
    {
        if (!_drawing || _used >= MaxRings)
        {
            return;
        }

        var ring = Next();
        var local = new GlobalPosition(center.X, center.Y, center.Z).ToLocalPosition();
        var key = new RingKey(new NumericsVector3(local.x, local.y, local.z), radius, color, _settings.MapToolLineWidth.Value, _scale);
        if (!ring.Graphic.enabled)
        {
            ring.Graphic.enabled = true;
            // Switched back on, Unity rebuilds the mesh around where the graphic sits, which undoes any move.
            ring.Key = null;
        }

        if (ring.Key is { } last && key.Equals(last))
        {
            if (ring.View.Equals(_view))
            {
                return;
            }

            ring.View = _view;
            if (ring.Movable && RingProjection.TrySample(key.Center, radius, _view, _samples) &&
                RingProjection.TryShift(ring.Samples, _samples, MaxShiftError, out var shift))
            {
                var rect = ring.Graphic.rectTransform;
                var position = ring.BuiltAt + new Vector2(shift.X, shift.Y) / _scale;
                if ((Vector2)rect.localPosition != position)
                {
                    rect.localPosition = position;
                }

                return;
            }
        }

        ring.Key = key;
        ring.View = _view;
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
        ring.BuiltAt = graphic.rectTransform.localPosition;
        ring.Movable = _projection.Closed && RingProjection.TrySample(key.Center, radius, _view, ring.Samples);
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

        /// <summary>The camera of the last frame the ring was built or moved for.</summary>
        public ScreenCamera View { get; set; }

        /// <summary>Where the mesh sat when it was built, before any move.</summary>
        public Vector2 BuiltAt { get; set; }

        /// <summary>The mesh is the whole ring in view, so it may be moved rather than rebuilt.</summary>
        public bool Movable { get; set; }

        /// <summary>Where the ring's samples were on screen when it was built, while <see cref="Movable"/>.</summary>
        public NumericsVector2[] Samples { get; } = new NumericsVector2[RingProjection.SampleCount];
    }

    /// <summary>Everything a ring's mesh depends on but the camera, with the centre in Unity's scene space as the camera is.</summary>
    private readonly struct RingKey : System.IEquatable<RingKey>
    {
        public RingKey(NumericsVector3 center, float radius, ShapeColor color, float lineWidth, float scale)
        {
            Center = center;
            Radius = radius;
            Color = color;
            LineWidth = lineWidth;
            Scale = scale;
        }

        public NumericsVector3 Center { get; }
        public float Radius { get; }
        public ShapeColor Color { get; }
        public float LineWidth { get; }
        public float Scale { get; }

        public bool Equals(RingKey other) =>
            Center == other.Center && Radius == other.Radius && Color == other.Color && LineWidth == other.LineWidth &&
            Scale == other.Scale;

        public override bool Equals(object? obj) => obj is RingKey other && Equals(other);

        public override int GetHashCode() => System.HashCode.Combine(Center, Radius, Color, LineWidth, Scale);
    }
}
