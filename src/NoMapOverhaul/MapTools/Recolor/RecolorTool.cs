using System.Numerics;
using NoMapOverhaul.MapTools.Eraser;

namespace NoMapOverhaul.MapTools.Recolor;

/// <summary>
/// Click one of the player's own drawings to give it the colour picked in the menu; undo puts the old colour back. The
/// drawing a click would change is drawn under the cursor in that colour, the way the eraser highlights what it would
/// take. A teammate's drawing keeps the colour its owner's snapshot gives it: sharing has no request to change it, as
/// it has for erasing.
/// </summary>
public sealed class RecolorTool : MapTool
{
    private const string IdleStatus = "Click your own drawing to recolour it.";

    private readonly ShapeHitTest _hitTest;
    private readonly RecolorCanvas _preview = new();
    private MapShape? _hovered;
    private ShapeColor _previewColor;
    private Vector2? _cursor;

    public RecolorTool(IMapToolContext context, ILabelPlacements? placements = null)
        : base(context) =>
        _hitTest = new ShapeHitTest(context, placements);

    public override string Name => "Recolour";

    public override string Status => IdleStatus;

    public override MapShape? RedrawnShape => Hovered;

    /// <summary>An undo or Clear can take the hovered drawing away before the cursor moves again.</summary>
    private MapShape? Hovered => _hovered is { } shape && Context.Shapes.Contains(shape) ? shape : null;

    public override void OnDeactivate()
    {
        _hovered = null;
        _cursor = null;
    }

    public override void OnClick(MapPointer pointer)
    {
        var color = Context.Color;
        if (FindAt(pointer.Position) is { } shape && shape.Color != color)
        {
            Context.Shapes.Replace(shape, shape.WithColor(color));
        }

        _hovered = null;
        InvalidateOverlay();
    }

    public override void OnPointerMove(MapPointer pointer) => _cursor = pointer.Position;

    /// <summary>
    /// Finds the drawing under the cursor every frame, as the eraser does, since a drawing on a unit moves under a still
    /// cursor. Redraws the preview only when that's a different drawing, or the colour changed some way other than the
    /// menu's swatches, such as in F1, which would otherwise leave it showing a colour a click no longer applies.
    /// </summary>
    public override void OnFrame(IWorldLabels labels)
    {
        if (_cursor is not { } cursor)
        {
            return;
        }

        var hovered = FindAt(cursor);
        var color = Context.Color;
        if (!ReferenceEquals(hovered, _hovered) || color != _previewColor)
        {
            _hovered = hovered;
            _previewColor = color;
            InvalidateOverlay();
        }
    }

    public override void DrawOverlay(IMapCanvas canvas) => Hovered?.Draw(_preview.Over(canvas, Context.Color));

    private MapShape? FindAt(Vector2 position) =>
        _hitTest.Find(Context.Shapes.Shapes, position, EraserTool.Reach * Context.MetersPerIconUnit);
}
