using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoMapOverhaul.MapTools.Eraser;

/// <summary>
/// Click near a local drawing to delete it; undo brings it back. Shared erases ask the owner to publish removal.
/// The shape a click would take is redrawn in
/// <see cref="HighlightColor"/> under the cursor, so it's clear what goes. Works on every tool's shapes through
/// <see cref="ShapeHitTest"/>.
/// </summary>
public sealed class EraserTool : MapTool
{
    /// <summary>How far from a stroke a click still takes it, in icon units: about a fingertip's slop on screen.</summary>
    public const float Reach = 8f;

    private static readonly ShapeColor HighlightColor = new(255, 64, 64, 200);

    private readonly ShapeHitTest _hitTest;
    private readonly Func<IReadOnlyList<MapShape>>? _receivedShapes;
    private readonly Func<MapShape, bool>? _canEraseReceived;
    private readonly Func<MapShape, bool>? _requestEraseReceived;
    private readonly RecolorCanvas _highlight = new();
    private MapShape? _hovered;
    private Vector2? _cursor;
    private string _status = "Click a drawing to delete it.";

    public EraserTool(IMapToolContext context, ILabelPlacements? placements = null,
        Func<IReadOnlyList<MapShape>>? receivedShapes = null, Func<MapShape, bool>? canEraseReceived = null,
        Func<MapShape, bool>? requestEraseReceived = null) : base(context)
    {
        _hitTest = new ShapeHitTest(context, placements);
        _receivedShapes = receivedShapes;
        _canEraseReceived = canEraseReceived;
        _requestEraseReceived = requestEraseReceived;
    }

    public override string Name => "Eraser";

    public override string Status => _status;

    public override MapShape? RedrawnShape => _hovered is { } shape && CanErase(shape) ? shape : null;

    public override void OnActivate() => _status = "Click a drawing to delete it.";

    public override void OnDeactivate()
    {
        _hovered = null;
        _cursor = null;
        _status = "Click a drawing to delete it.";
    }

    public override void OnClick(MapPointer pointer)
    {
        if (FindAt(pointer.Position) is { } shape)
        {
            TryErase(shape);
        }

        _hovered = null;
        InvalidateOverlay();
    }

    public override void OnPointerMove(MapPointer pointer) => _cursor = pointer.Position;

    /// <summary>
    /// Finds the shape under the cursor every frame, not only when the cursor moves: a drawing on a unit moves under a
    /// still cursor, and the highlight must show what a click would take. Redraws the highlight only when that's a
    /// different shape, since a long pen stroke is costly to redraw.
    /// </summary>
    public override void OnFrame(IWorldLabels labels)
    {
        if (_cursor is not { } cursor)
        {
            return;
        }

        var candidate = FindAt(cursor);
        var hovered = candidate != null && CanErase(candidate) ? candidate : null;
        if (!ReferenceEquals(hovered, _hovered))
        {
            _hovered = hovered;
            InvalidateOverlay();
        }
    }

    /// <summary>An undo or Clear can take the hovered shape away before the cursor moves again.</summary>
    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_hovered is { } shape && CanErase(shape))
        {
            shape.Draw(_highlight.Over(canvas, HighlightColor));
        }
    }

    public MapShape? FindAt(Vector2 position) => _hitTest.Find(Context.Shapes.Shapes,
        _receivedShapes?.Invoke() ?? Array.Empty<MapShape>(), position, Reach * Context.MetersPerIconUnit);

    public bool TryErase(MapShape shape)
    {
        _hovered = null;
        InvalidateOverlay();
        if (Context.Shapes.Contains(shape))
        {
            _status = "Click a drawing to delete it.";
            return Context.Shapes.Remove(shape);
        }

        var requested = CanErase(shape) && _requestEraseReceived!(shape);
        _status = requested ? "Erase requested; waiting for the owner's update."
            : "Erase not sent. Check sharing and channel membership, or try again shortly.";
        return requested;
    }

    private bool CanErase(MapShape shape)
    {
        if (Context.Shapes.Contains(shape)) return true;
        if (_receivedShapes == null || _canEraseReceived == null || _requestEraseReceived == null) return false;
        var received = _receivedShapes();
        for (var i = 0; i < received.Count; i++)
            if (ReferenceEquals(received[i], shape)) return _canEraseReceived(shape);
        return false;
    }

    /// <summary>Passes draw calls through with one colour, to draw a shape again as a highlight.</summary>
    private sealed class RecolorCanvas : IMapCanvas
    {
        private IMapCanvas _inner = null!;
        private ShapeColor _color;

        public DistanceUnit Units => _inner.Units;
        public float MetersPerIconUnit => _inner.MetersPerIconUnit;
        public float TextSize => _inner.TextSize;
        public MapPoint? OwnAircraft => _inner.OwnAircraft;

        public RecolorCanvas Over(IMapCanvas inner, ShapeColor color)
        {
            _inner = inner;
            _color = color;
            return this;
        }

        public bool TryResolve(MapPoint point, out Vector2 position) => _inner.TryResolve(point, out position);
        public bool TryResolveWorld(MapPoint point, out Vector3 position) => _inner.TryResolveWorld(point, out position);
        public void Line(Vector2 from, Vector2 to, ShapeColor color) => _inner.Line(from, to, _color);
        public void Arrow(Vector2 from, Vector2 to, ShapeColor color) => _inner.Arrow(from, to, _color);
        public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color) => _inner.Polyline(points, _color);
        public void Circle(Vector2 center, float radius, ShapeColor color) => _inner.Circle(center, radius, _color);
        public void Marker(Vector2 position, ShapeColor color) => _inner.Marker(position, _color);

        public void Label(LabelAnchor anchor, string text, ShapeColor color) => _inner.Label(anchor, text, _color);
    }
}
