using System;
using System.Numerics;

namespace BaanishUiImprovements.MapTools.BearingRange;

/// <summary>
/// An arrow from one point to another with the bearing and distance at its head. Either end may be anchored to a unit
/// and follow it; a lost unit leaves its end where it was last seen, and the label says so.
/// </summary>
public sealed class BearingRangeShape : MapShape
{
    private readonly MeasureLabel _label = new();

    /// <param name="toElevation">Ground height under a fixed end, for its 3D label. An anchored end uses the unit's altitude.</param>
    public BearingRangeShape(MapPoint from, MapPoint to, float toElevation, ShapeColor color)
        : base(color)
    {
        From = from;
        To = to;
        ToElevation = toElevation;
    }

    public MapPoint From { get; }

    public MapPoint To { get; }

    public float ToElevation { get; }

    /// <summary>Past the arrowhead, on whichever side the arrow points, so the text doesn't sit on the line.</summary>
    public static LabelPlacement PlacementBeyond(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        if (MathF.Abs(delta.X) >= MathF.Abs(delta.Y))
        {
            return delta.X < 0f ? LabelPlacement.Left : LabelPlacement.Right;
        }

        return delta.Y < 0f ? LabelPlacement.Below : LabelPlacement.Above;
    }

    /// <summary>The arrow and its label, shared with the tool's preview while the end is still being picked.</summary>
    public static void DrawMeasurement(IMapCanvas canvas, MeasureLabel label, Vector2 from, Vector2 to, bool toOnUnit, bool lost, ShapeColor color)
    {
        canvas.Arrow(from, to, color);
        var labelAt = to;
        if (toOnUnit && to != from) // clear the unit's icon under the arrowhead
        {
            labelAt += Vector2.Normalize(to - from) * (MapCanvasMetrics.MarkerRadius * canvas.MetersPerIconUnit);
        }

        canvas.Label(labelAt, label.BearingRange(from, to, canvas.Units, lost), color, PlacementBeyond(from, to));
    }

    public override void Draw(IMapCanvas canvas)
    {
        var fromFound = canvas.TryResolve(From, out var from);
        var toFound = canvas.TryResolve(To, out var to);
        DrawMeasurement(canvas, _label, from, to, To.IsAnchored, !fromFound || !toFound, Color);
    }

    /// <summary>The label at the arrowhead again, in the 3D view: on the unit at its altitude, or on the ground.</summary>
    public void AddWorldLabel(IMapToolContext context, IWorldLabels labels)
    {
        var fromFound = context.TryResolve(From, out var from);
        var toFound = context.TryResolveWorld(To, out var world);
        if (!To.IsAnchored)
        {
            world.Y = ToElevation;
        }

        var to = new Vector2(world.X, world.Z);
        labels.Add(world, _label.BearingRange(from, to, context.Units, !fromFound || !toFound), Color);
    }
}
