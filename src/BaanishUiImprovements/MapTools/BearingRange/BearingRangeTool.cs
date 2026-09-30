using System.Numerics;

namespace BaanishUiImprovements.MapTools.BearingRange;

/// <summary>
/// Measures bearing and range: click where to measure from, then where to. Either end snaps to a unit clicked on or
/// just beside its icon and follows it. An arrow previews the measurement until the second click, and clicking the
/// start again drops it. Each arrow's label also shows in the 3D view.
/// </summary>
public sealed class BearingRangeTool : MapTool
{
    /// <summary>A second click this close to the first, in icon units, is the same point: it cancels rather than measuring nothing.</summary>
    public const float SamePointReach = 6f;

    private const string PickStart = "Click where to measure from.";
    private const string PickEnd = "Click where to measure to, or the start again to cancel.";

    private readonly MeasureLabel _previewLabel = new();
    private MapPoint? _start;
    private MapPoint? _hover;

    public BearingRangeTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Bearing/range";

    /// <summary>Empty at the shape cap, so the menu says why nothing is added.</summary>
    public override string Status => Context.Shapes.IsFull ? string.Empty : _start is null ? PickStart : PickEnd;

    public override void OnDeactivate() => Forget();

    public override void OnMissionStart() => Forget();

    public override void OnPointerMove(MapPointer pointer) => _hover = pointer.Point;

    public override void OnClick(MapPointer pointer)
    {
        _hover = pointer.Point;
        if (_start is not { } start)
        {
            _start = pointer.Point;
            return;
        }

        var end = pointer.Point;
        _start = null;
        Context.TryResolve(start, out var from);
        Context.TryResolve(end, out var to);
        if (Vector2.Distance(from, to) <= SamePointReach * Context.MetersPerIconUnit)
        {
            return;
        }

        var elevation = end.IsAnchored ? 0f : Context.GroundElevation(end.Position);
        Context.Shapes.Add(new BearingRangeShape(start, end, elevation, Context.Color));
    }

    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_start is not { } start || _hover is not { } hover)
        {
            return;
        }

        var startFound = canvas.TryResolve(start, out var from);
        var hoverFound = canvas.TryResolve(hover, out var to);
        BearingRangeShape.DrawMeasurement(canvas, _previewLabel, from, to, hover.IsAnchored, !startFound || !hoverFound, Context.Color);
    }

    public override void OnFrame(IWorldLabels labels)
    {
        var shapes = Context.Shapes.Shapes;
        for (var i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] is BearingRangeShape shape)
            {
                shape.AddWorldLabel(Context, labels);
            }
        }
    }

    private void Forget()
    {
        _start = null;
        _hover = null;
    }
}
