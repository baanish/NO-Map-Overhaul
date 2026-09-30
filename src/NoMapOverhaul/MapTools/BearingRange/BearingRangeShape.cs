using System.Numerics;

namespace NoMapOverhaul.MapTools.BearingRange;

/// <summary>
/// An arrow from one point to another with the bearing and straight-line range at its head, and the altitude of a unit
/// at the head. Either end may be anchored to a unit and follow it; a lost unit leaves its end where it was last seen,
/// at the altitude it was last seen at, and the label says so.
/// </summary>
public sealed class BearingRangeShape : MapShape
{
    private readonly MeasureLabel _label = new();

    /// <param name="fromElevation">Ground height under a fixed start, for the range. An anchored end uses the unit's altitude.</param>
    /// <param name="toElevation">Ground height under a fixed end, for the range and its 3D label.</param>
    public BearingRangeShape(MapPoint from, MapPoint to, float fromElevation, float toElevation, ShapeColor color)
        : base(color)
    {
        From = from;
        To = to;
        FromElevation = fromElevation;
        ToElevation = toElevation;
    }

    public MapPoint From { get; }

    public MapPoint To { get; }

    public float FromElevation { get; }

    public float ToElevation { get; }

    /// <summary>
    /// Where an end is now in global meters (X east, Y up from sea level, Z north): a unit at the altitude its side
    /// knows, a fixed point at <paramref name="elevation"/>. False as <see cref="IMapView.TryResolve"/> is.
    /// </summary>
    public static bool TryResolveEnd(IMapView view, MapPoint point, float elevation, out Vector3 position)
    {
        var found = view.TryResolveWorld(point, out position);
        if (!point.IsAnchored)
        {
            position.Y = elevation;
        }

        return found;
    }

    /// <summary>
    /// The arrow and its label, shared with the tool's preview while the end is still being picked. The label hangs
    /// from the head, or from just past a unit's icon at the head, so it clears the icon even where the map doesn't know
    /// where the icons are.
    /// </summary>
    public static void DrawMeasurement(IMapCanvas canvas, MeasureLabel label, Vector3 from, Vector3 to, bool toOnUnit, bool lost, ShapeColor color)
    {
        var tail = new Vector2(from.X, from.Z);
        var head = new Vector2(to.X, to.Z);
        canvas.Arrow(tail, head, color);
        var labelAt = head;
        if (toOnUnit && head != tail)
        {
            labelAt += Vector2.Normalize(head - tail) * (MapCanvasMetrics.MarkerRadius * canvas.MetersPerIconUnit);
        }

        canvas.Label(LabelAnchor.Bearing(tail, labelAt), label.BearingRange(from, to, toOnUnit, canvas.Units, lost), color);
    }

    public override void Draw(IMapCanvas canvas)
    {
        var fromFound = TryResolveEnd(canvas, From, FromElevation, out var from);
        var toFound = TryResolveEnd(canvas, To, ToElevation, out var to);
        DrawMeasurement(canvas, _label, from, to, To.IsAnchored, !fromFound || !toFound, Color);
    }

    /// <summary>The label at the arrowhead again, in the 3D view: on the unit at its altitude, or on the ground.</summary>
    public void AddWorldLabel(IMapToolContext context, IWorldLabels labels)
    {
        var fromFound = TryResolveEnd(context, From, FromElevation, out var from);
        var toFound = TryResolveEnd(context, To, ToElevation, out var to);
        labels.Add(to, _label.BearingRange(from, to, To.IsAnchored, context.Units, !fromFound || !toFound), Color);
    }
}
