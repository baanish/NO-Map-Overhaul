using System.Collections.Generic;
using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.Drawing;
using NoMapOverhaul.MapTools.Circle;
using UnityEngine;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Rings in the 3D view, which the Circle tool requests each frame through <see cref="WorldLabelPool"/>. Each ring is
/// drawn on the HUD canvas, beside the 3D labels, so it hides with the HUD and while the map is open. With the
/// <see cref="RingShader"/> loaded, each is the shared <see cref="RingMesh"/> with a material of its own, and the GPU
/// projects, clips, and extrudes it from a few numbers set each frame. Otherwise each is a flat
/// <see cref="StrokeGraphic"/> that <see cref="RingProjection"/> projects and clips on the CPU, and a ring whose mesh
/// was the whole ring in view is moved rather than rebuilt while the camera or its unit only slides it across the
/// screen (see <see cref="Add"/>). Nothing is projected while the HUD's canvas is switched off.
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

    /// <summary>Each drawing's ring, so a ring keeps its mesh from frame to frame however the requests are ordered.</summary>
    private readonly Dictionary<object, Ring> _byOwner = new();

    private readonly NumericsVector2[] _samples = new NumericsVector2[RingProjection.SampleCount];
    private readonly Plane[] _frustum = new Plane[6];
    private Camera? _camera;
    private int _cameraFrame = -1;
    private RectTransform? _root;

    /// <summary>The rings under <see cref="_root"/> are drawn by the <see cref="RingShader"/>, chosen when it was made.</summary>
    private Shader? _shader;

    private Transform? _parent;
    private ScreenCamera _view;
    private float _scale;
    private bool _drawing;
    private int _frame;

    public WorldRingPool(ModSettings settings) => _settings = settings;

    /// <summary>
    /// Starts a frame's requests under the HUD's label parent, or none when there is no HUD or camera. Leaving the
    /// mission destroys the HUD with the rings in it, and switching the RingsOnCpu setting makes them all anew.
    /// </summary>
    public void Begin(Transform? parent, Camera? camera)
    {
        if (!ReferenceEquals(parent, _parent) || (_root is not null && _root == null) || (_root != null && ChosenShader() != _shader))
        {
            Reset();
            _parent = parent;
        }

        _frame++;
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

        _camera = camera;
        _cameraFrame = -1;
    }

    /// <summary>The camera's view for this frame's projections, worked out on the frame's first ring, so a frame without rings costs nothing here.</summary>
    private void TakeCamera()
    {
        var camera = _camera!;
        _cameraFrame = _frame;
        var transform = camera.transform;
        var position = transform.position;
        var rotation = transform.rotation;
        var halfScreen = new NumericsVector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _view = new ScreenCamera(new NumericsVector3(position.x, position.y, position.z),
            new NumericsQuaternion(rotation.x, rotation.y, rotation.z, rotation.w), halfScreen,
            halfScreen.Y / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad), camera.nearClipPlane);
        GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
    }

    /// <summary>
    /// Draws a ring this frame. One whose mesh is the whole ring in view is only moved while it keeps its shape on
    /// screen: its samples, projected afresh from where its centre is now, each land within <see cref="MaxShiftError"/> of where moving the mesh by
    /// their mean shift puts them. A turn of the camera slides a ring across the screen with almost no change of shape,
    /// so a ring far off is rebuilt only now and then in flight, and one the camera nears is rebuilt as it changes
    /// shape. A ring cut by the screen's edge or the near plane is rebuilt whenever the camera moves.
    /// </summary>
    /// <param name="center">Global meters, as <see cref="IWorldLabels.Ring"/> takes it.</param>
    /// <param name="owner">The drawing, which keeps its slot while it stays in view.</param>
    public void Add(NumericsVector3 center, float radius, ShapeColor color, object owner)
    {
        if (!_drawing)
        {
            return;
        }

        if (_cameraFrame != _frame)
        {
            TakeCamera();
        }

        var local = new GlobalPosition(center.X, center.Y, center.Z).ToLocalPosition();
        // Only a ring the camera can see takes one of the slots, so a ring in front of the player always shows.
        if (!GeometryUtility.TestPlanesAABB(_frustum, new Bounds(local, new Vector3(2f * radius, 1f, 2f * radius))) ||
            Claim(owner) is not { } ring)
        {
            return;
        }

        var scene = new NumericsVector3(local.x, local.y, local.z);
        if (ring.Gpu is { } gpu)
        {
            RingProjection.ToCamera(scene, radius, _view, out var middle, out var east, out var north);
            gpu.Draw(middle, east, north, color, _view, _settings.MapToolLineWidth.Value * 0.5f * _scale);
            return;
        }

        var key = new RingKey(scene, radius, color, _settings.MapToolLineWidth.Value, _scale);
        var graphic = ring.Graphic!;
        if (!graphic.enabled)
        {
            graphic.enabled = true;
            // Switched back on, Unity rebuilds the mesh around where the graphic sits, which undoes any move.
            ring.Key = null;
        }

        if (ring.Key is { } last && key.SameShape(last))
        {
            if (ring.View.Equals(_view) && ring.Center == key.Center)
            {
                return;
            }

            ring.View = _view;
            ring.Center = key.Center;
            // A ring on a moving unit shifts like one the camera slides past: its samples, at the new centre, are checked
            // against the mesh as built.
            if (ring.Movable && RingProjection.TrySample(key.Center, radius, _view, _samples) &&
                RingProjection.TryShift(ring.Samples, _samples, MaxShiftError, out var shift))
            {
                var rect = graphic.rectTransform;
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
        ring.Center = key.Center;
        _projection.Project(key.Center, radius, _view);
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

    /// <summary>Hides the rings no circle asked for this frame, freeing their slots.</summary>
    public void End()
    {
        var drawn = 0;
        foreach (var ring in _rings)
        {
            if (ring.Frame == _frame)
            {
                drawn++;
                continue;
            }

            if (ring.Owner != null)
            {
                _byOwner.Remove(ring.Owner);
                ring.Owner = null;
            }

            if (ring.Gpu != null)
            {
                ring.Gpu.Hide();
            }
            else if (ring.Graphic!.enabled)
            {
                ring.Graphic.enabled = false;
            }
        }

        if (_drawing)
        {
            ModActivity.CountRings(drawn, _shader != null);
        }
    }

    public void Reset()
    {
        if (_root != null)
        {
            Object.Destroy(_root.gameObject);
        }

        foreach (var ring in _rings)
        {
            ring.Gpu?.Destroy();
        }

        _root = null;
        _shader = null;
        _parent = null;
        _rings.Clear();
        _byOwner.Clear();
    }

    private RectTransform Root()
    {
        if (_root == null)
        {
            _root = new GameObject("BaanishWorldRings", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(_parent, false);
            _root.SetAsFirstSibling(); // under the 3D labels, so a ring never crosses their text
            // A canvas of its own, so a ring rebuilt or moved re-batches the rings alone, not the game's whole HUD.
            _root.gameObject.AddComponent<Canvas>();
            _shader = ChosenShader();
            // The shader places rings in screen pixels, the world units of a canvas drawn over the screen.
            if (_shader != null && _root.GetComponentInParent<Canvas>().rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                RingShader.FallBack("the HUD's canvas isn't drawn over the screen.");
                _shader = null;
            }
        }

        return _root;
    }

    private Shader? ChosenShader() => _settings.RingsOnCpu.Value ? null : RingShader.Shader;

    /// <summary>
    /// The owner's ring from last frame, else a free one, else a new one up to <see cref="MaxRings"/>; null when every
    /// slot is in use. A slot freed this frame is free again from the next.
    /// </summary>
    private Ring? Claim(object owner)
    {
        if (!_byOwner.TryGetValue(owner, out var ring))
        {
            ring = _rings.Find(free => free.Owner == null);
            if (ring == null)
            {
                if (_rings.Count >= MaxRings)
                {
                    return null;
                }

                var rect = new GameObject("Ring", typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(_root, false);
                if (_shader != null)
                {
                    ring = new Ring(new GpuRing(rect.gameObject, _shader));
                }
                else
                {
                    var graphic = rect.gameObject.AddComponent<StrokeGraphic>();
                    graphic.raycastTarget = false;
                    ring = new Ring(graphic);
                }

                _rings.Add(ring);
            }

            ring.Owner = owner;
            ring.Key = null; // another drawing's mesh
            _byOwner.Add(owner, ring);
        }

        ring.Frame = _frame;
        return ring;
    }

    private sealed class Ring
    {
        public Ring(StrokeGraphic graphic) => Graphic = graphic;

        public Ring(GpuRing gpu) => Gpu = gpu;

        /// <summary>The ring's mesh when it's built on the CPU.</summary>
        public StrokeGraphic? Graphic { get; }

        /// <summary>The ring's renderer when the GPU draws it.</summary>
        public GpuRing? Gpu { get; }

        /// <summary>The drawing that holds this slot, or null while it's free.</summary>
        public object? Owner { get; set; }

        /// <summary>The last frame a drawing asked for this ring.</summary>
        public int Frame { get; set; }

        /// <summary>What the mesh was last built from, or null before the first build.</summary>
        public RingKey? Key { get; set; }

        /// <summary>The camera of the last frame the ring was built or moved for.</summary>
        public ScreenCamera View { get; set; }

        /// <summary>The centre, in scene space, of the last frame the ring was built or moved for.</summary>
        public NumericsVector3 Center { get; set; }

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

        public bool Equals(RingKey other) => Center == other.Center && SameShape(other);

        /// <summary>Everything but the centre: a ring that only moved may keep its mesh, if its shape on screen held.</summary>
        public bool SameShape(RingKey other) =>
            Radius == other.Radius && Color == other.Color && LineWidth == other.LineWidth && Scale == other.Scale;

        public override bool Equals(object? obj) => obj is RingKey other && Equals(other);

        public override int GetHashCode() => System.HashCode.Combine(Center, Radius, Color, LineWidth, Scale);
    }
}
