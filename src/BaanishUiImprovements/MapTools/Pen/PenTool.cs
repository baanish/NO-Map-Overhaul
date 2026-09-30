using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools.Pen;

/// <summary>
/// Freehand strokes: press and drag on the map to draw in the picked colour. Points closer than
/// <see cref="MinPointGap"/> to the last one are dropped as the pen moves, and the finished stroke is simplified with
/// <see cref="StrokeSimplifier"/>, so a stroke costs few of the MaxPenPoints budget. A stroke that runs out of budget
/// stops where it ran out, is kept up to there, and the menu says why.
/// </summary>
public sealed class PenTool : MapTool
{
    /// <summary>Closest the next kept point may be to the last, in icon units: a few screen pixels.</summary>
    public const float MinPointGap = 3f;

    /// <summary>How far the simplified stroke may stray from the drawn one, in icon units: well under the line width, so it looks the same.</summary>
    public const float Tolerance = 0.75f;

    private const string IdleStatus = "Drag on the map to draw.";
    private const string LimitStatus = "Pen point limit reached: erase or undo a stroke to draw more.";

    private readonly List<Vector2> _points = new();
    private bool _drawing;
    private bool _stopped;
    private bool _squeezed;
    private int? _limitedAt;
    private int _room;
    private float _gapSquared;
    private float _tolerance;

    public PenTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Pen";

    public override bool CapturesDrag => true;

    public override bool InProgress => _drawing;

    /// <summary>
    /// Empty at the shape cap, so the menu shows its own limit message. The pen's own limit message lasts until the
    /// drawings change, since an erase or undo may have made room.
    /// </summary>
    public override string Status => Warning ? LimitStatus : Context.Shapes.IsFull ? string.Empty : IdleStatus;

    public override bool Warning => _stopped || _limitedAt == Context.Shapes.Version;

    public override void OnDeactivate() => Drop();

    public override void OnMissionStart()
    {
        Drop();
        _limitedAt = null;
    }

    public override void OnPointerDown(MapPointer pointer)
    {
        Drop();
        InvalidateOverlay();
        var shapes = Context.Shapes;
        _room = shapes.MaxPoints - shapes.PointCount;
        _limitedAt = _room < 1 ? shapes.Version : null;
        if (_limitedAt != null || shapes.IsFull)
        {
            return;
        }

        // Pan and zoom are blocked while the button is held, so the scale holds for the whole stroke.
        var metersPerUnit = Context.MetersPerIconUnit;
        _gapSquared = MinPointGap * metersPerUnit * (MinPointGap * metersPerUnit);
        _tolerance = Tolerance * metersPerUnit;
        _points.Add(pointer.Position);
        _drawing = true;
    }

    public override void OnPointerDrag(MapPointer pointer)
    {
        if (_drawing)
        {
            Append(pointer.Position);
        }
    }

    /// <summary>A press and release in place leaves a dot.</summary>
    public override void OnPointerUp(MapPointer pointer)
    {
        if (!_drawing)
        {
            return;
        }

        Append(pointer.Position);
        StrokeSimplifier.Simplify(_points, _tolerance);
        var stroke = new PenStroke(_points.ToArray(), Context.Color);
        var limited = _stopped;
        Drop();
        InvalidateOverlay();
        if (!Context.Shapes.Add(stroke) && !Context.Shapes.IsFull)
        {
            limited = true; // a redo during the drag took the room
        }

        if (limited)
        {
            _limitedAt = Context.Shapes.Version;
        }
    }

    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_drawing)
        {
            canvas.Polyline(_points, Context.Color);
        }
    }

    /// <summary>
    /// At the budget, simplifying what's drawn so far may make room; if not, the stroke ends here. A pass that frees less
    /// than an eighth of the budget is the last, and the stroke ends once that room fills, so a long stroke near the cap
    /// isn't simplified again on every drag.
    /// </summary>
    private void Append(Vector2 point)
    {
        if (_stopped || Vector2.DistanceSquared(point, _points[_points.Count - 1]) < _gapSquared)
        {
            return;
        }

        InvalidateOverlay(); // a point is kept, or the stroke is simplified to make room

        if (_points.Count >= _room)
        {
            if (!_squeezed)
            {
                StrokeSimplifier.Simplify(_points, _tolerance);
                _squeezed = _room - _points.Count < _room / 8;
            }

            if (_points.Count >= _room)
            {
                _stopped = true;
                return;
            }
        }

        _points.Add(point);
    }

    private void Drop()
    {
        _points.Clear();
        _drawing = false;
        _stopped = false;
        _squeezed = false;
    }
}
