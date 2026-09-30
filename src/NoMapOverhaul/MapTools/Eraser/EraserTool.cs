using System.Collections.Generic;
using System.Numerics;

namespace NoMapOverhaul.MapTools.Eraser;

/// <summary>
/// Click near any drawing to delete it; undo brings it back. The shape a click would take is redrawn in
/// <see cref="HighlightColor"/> under the cursor, so it's clear what goes. Works on every tool's shapes through
/// <see cref="ShapeHitTest"/>.
/// </summary>
public sealed class EraserTool : MapTool
{
    /// <summary>How far from a stroke a click still takes it, in icon units: about a fingertip's slop on screen.</summary>
    public const float Reach = 8f;

    private static readonly ShapeColor HighlightColor = new(255, 64, 64, 200);

    private readonly ShapeHitTest _hitTest;
    private readonly RecolorCanvas _highlight = new();
    private MapShape? _hovered;
    private Vector2? _cursor;

    public EraserTool(IMapToolContext context, ILabelPlacements? placements = null)
        : base(context) =>
        _hitTest = new ShapeHitTest(context, placements);

    public override string Name => "Eraser";

    public override string Status => "Click a drawing to delete it.";

    public override MapShape? RedrawnShape => _hovered;

    public override void OnDeactivate()
    {
        _hovered = null;
        _cursor = null;
    }

    public override void OnClick(MapPointer pointer)
    {
        if (Find(pointer.Position) is { } shape)
        {
            Context.Shapes.Remove(shape);
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

        var hovered = Find(cursor);
        if (!ReferenceEquals(hovered, _hovered))
        {
            _hovered = hovered;
            InvalidateOverlay();
        }
    }

    /// <summary>An undo or Clear can take the hovered shape away before the cursor moves again.</summary>
    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_hovered is { } shape && Context.Shapes.Contains(shape))
        {
            shape.Draw(_highlight.Over(canvas, HighlightColor));
        }
    }

    private MapShape? Find(Vector2 position) => _hitTest.Find(Context.Shapes.Shapes, position, Reach * Context.MetersPerIconUnit);

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
