using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools.Eraser;

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

    public EraserTool(IMapToolContext context)
        : base(context) =>
        _hitTest = new ShapeHitTest(context);

    public override string Name => "Eraser";

    public override void OnDeactivate() => _hovered = null;

    public override void OnClick(MapPointer pointer)
    {
        if (Find(pointer) is { } shape)
        {
            Context.Shapes.Remove(shape);
        }

        _hovered = null;
    }

    public override void OnPointerMove(MapPointer pointer) => _hovered = Find(pointer);

    /// <summary>An undo or Clear can take the hovered shape away before the cursor moves again.</summary>
    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_hovered is { } shape && Context.Shapes.Contains(shape))
        {
            shape.Draw(_highlight.Over(canvas, HighlightColor));
        }
    }

    private MapShape? Find(MapPointer pointer) =>
        _hitTest.Find(Context.Shapes.Shapes, pointer.Position, Reach * Context.MetersPerIconUnit);

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
        public void Line(Vector2 from, Vector2 to, ShapeColor color) => _inner.Line(from, to, _color);
        public void Arrow(Vector2 from, Vector2 to, ShapeColor color) => _inner.Arrow(from, to, _color);
        public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color) => _inner.Polyline(points, _color);
        public void Circle(Vector2 center, float radius, ShapeColor color) => _inner.Circle(center, radius, _color);
        public void Marker(Vector2 position, ShapeColor color) => _inner.Marker(position, _color);

        public void Label(Vector2 position, string text, ShapeColor color, LabelPlacement placement = LabelPlacement.Center) =>
            _inner.Label(position, text, _color, placement);
    }
}
