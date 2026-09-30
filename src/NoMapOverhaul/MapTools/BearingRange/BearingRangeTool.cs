using System.Numerics;

namespace NoMapOverhaul.MapTools.BearingRange;

/// <summary>
/// Measures bearing and range: click where to measure from, then where to. Either end snaps to a unit clicked on or
/// just beside its icon and follows it. An arrow previews the measurement until the second click, and clicking the
/// start again drops it. Pressing the tool's key twice starts from the player's aircraft instead (see
/// <see cref="StartAtOwnAircraft"/>). Each arrow's label also shows in the 3D view.
/// </summary>
public sealed class BearingRangeTool : MapTool
{
    /// <summary>
    /// A second fixed point this close to a fixed first one, in icon units, is the same point: it cancels rather than
    /// measuring nothing. A unit is the same point only as itself, so a unit stacked above another, or above the first
    /// point, still measures through the air.
    /// </summary>
    public const float SamePointReach = 6f;

    private const string PickStart = "Click where to measure from.";
    private const string PickEnd = "Click where to measure to, or the start again to cancel.";
    private const string PickEndFromAircraft = "From your aircraft: click where to measure to.";
    private const string NoAircraft = "No aircraft to measure from.";

    private readonly MeasureLabel _previewLabel = new();
    private MapPoint? _start;
    private MapPoint? _hover;
    private float _startElevation;
    private float _hoverElevation;
    private bool _startOnAircraft;
    private bool _noAircraft;

    public BearingRangeTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Bearing/range";

    /// <summary>Empty at the shape cap, so the menu says why nothing is added.</summary>
    public override string Status =>
        Context.Shapes.IsFull ? string.Empty
        : _noAircraft ? NoAircraft
        : _start is null ? PickStart
        : _startOnAircraft ? PickEndFromAircraft
        : PickEnd;

    public override bool Warning => _noAircraft;

    public override bool InProgress => _start is not null;

    /// <summary>
    /// The tool's key pressed twice: measure from the player's own aircraft, anchored to it as a click on its icon is,
    /// so the arrow follows it. Replaces a start already placed. With no aircraft, as when dead or spectating, the strip
    /// says so and nothing else changes.
    /// </summary>
    public void StartAtOwnAircraft()
    {
        if (Context.OwnAircraft is not { } aircraft)
        {
            _noAircraft = true;
            return;
        }

        _noAircraft = false;
        _start = aircraft;
        _startOnAircraft = true;
        _startElevation = Elevation(aircraft);
        _hoverElevation = _hover is { } hover ? Elevation(hover) : 0f;
        InvalidateOverlay();
    }

    public override void OnDeactivate() => Forget();

    public override void OnMissionStart() => Forget();

    /// <summary>The preview follows the cursor once a start is picked.</summary>
    public override void OnPointerMove(MapPointer pointer)
    {
        _hover = pointer.Point;
        if (_start is not null)
        {
            _hoverElevation = Elevation(pointer.Point);
            InvalidateOverlay();
        }
    }

    public override void OnClick(MapPointer pointer)
    {
        _hover = pointer.Point;
        _hoverElevation = Elevation(pointer.Point);
        _noAircraft = false;
        InvalidateOverlay();
        if (_start is not { } start)
        {
            _start = pointer.Point;
            _startElevation = _hoverElevation;
            return;
        }

        var end = pointer.Point;
        _start = null;
        _startOnAircraft = false;
        var same = start.IsAnchored || end.IsAnchored
            ? start.UnitId == end.UnitId
            : Vector2.Distance(start.Position, end.Position) <= SamePointReach * Context.MetersPerIconUnit;
        if (same)
        {
            return;
        }

        Context.Shapes.Add(new BearingRangeShape(start, end, _startElevation, _hoverElevation, Context.Color));
    }

    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_start is not { } start || _hover is not { } hover)
        {
            return;
        }

        var startFound = BearingRangeShape.TryResolveEnd(canvas, start, _startElevation, out var from);
        var hoverFound = BearingRangeShape.TryResolveEnd(canvas, hover, _hoverElevation, out var to);
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

    /// <summary>Ground height under a fixed point, which the range starts or ends at; a unit's altitude is read as it moves.</summary>
    private float Elevation(MapPoint point) => point.IsAnchored ? 0f : Context.GroundElevation(point.Position);

    private void Forget()
    {
        _start = null;
        _hover = null;
        _startOnAircraft = false;
        _noAircraft = false;
    }
}
