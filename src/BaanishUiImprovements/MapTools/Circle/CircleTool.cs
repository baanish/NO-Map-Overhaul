using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using BaanishUiImprovements.MapTools.BearingRange;

namespace BaanishUiImprovements.MapTools.Circle;

/// <summary>
/// Circles on the map, such as a threat ring around a SAM, drawn like a compass: press on the centre and drag out to
/// the radius, or click the centre and then the edge. A preset radius in the menu finishes a placed centre, or with no
/// centre yet places one circle of that size at the next click. The centre snaps to a unit and follows it. Dragging
/// draws instead of panning while this tool is picked.
/// </summary>
public sealed class CircleTool : MapTool
{
    /// <summary>
    /// A press and release closer than this, in icon units, is a click rather than a drag, and an edge clicked this near
    /// the centre cancels the circle rather than drawing a dot.
    /// </summary>
    public const float ClickReach = 6f;

    private const string PickCenter = "Click the centre, or drag from it to the edge.";
    private const string PickEdge = "Click the edge, or pick a radius. Click the centre again to cancel.";

    /// <summary>Preset radii in whichever unit distances show in.</summary>
    private static readonly int[] PresetValues = { 5, 10, 20 };

    private static readonly string[][] PresetLabels = BuildPresetTexts("{0}{1}");
    private static readonly string[][] PresetStatus = BuildPresetTexts("Click the centre of a {0}{1} circle.");

    private readonly MeasureLabel _previewLabel = new();
    private MapPoint? _center;
    private MapPoint? _cursor;
    private Vector2 _pressAt;
    private bool _pressPlacedCenter;
    private int _preset = -1;

    public CircleTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Circle";

    public override bool CapturesDrag => true;

    public override IReadOnlyList<string> Options => PresetLabels[(int)Context.Units];

    /// <summary>Empty at the shape cap, so the menu says why nothing is added.</summary>
    public override string Status =>
        Context.Shapes.IsFull ? string.Empty
        : _center is not null ? PickEdge
        : _preset >= 0 ? PresetStatus[(int)Context.Units][_preset]
        : PickCenter;

    /// <summary>Meters in a preset radius.</summary>
    public static float PresetMeters(int index, DistanceUnit unit) => PresetValues[index] * unit switch
    {
        DistanceUnit.Kilometres => 1000f,
        DistanceUnit.StatuteMiles => NavFormat.MetersPerStatuteMile,
        _ => NavFormat.MetersPerNauticalMile,
    };

    public override void OnDeactivate() => Forget();

    public override void OnMissionStart() => Forget();

    /// <summary>With a centre placed, finishes the circle at that radius; otherwise the next click places one of this size. Clicking the same preset again drops it.</summary>
    public override void OnOption(int index)
    {
        if (index < 0 || index >= PresetValues.Length)
        {
            return;
        }

        if (_center is { } center)
        {
            Add(center, PresetMeters(index, Context.Units));
            return;
        }

        _preset = _preset == index ? -1 : index;
    }

    /// <summary>The preview follows the cursor once a centre or a preset is picked.</summary>
    public override void OnPointerMove(MapPointer pointer)
    {
        _cursor = pointer.Point;
        if (_center is not null || _preset >= 0)
        {
            InvalidateOverlay();
        }
    }

    public override void OnPointerDown(MapPointer pointer)
    {
        _cursor = pointer.Point;
        InvalidateOverlay();
        _pressAt = pointer.Position;
        _pressPlacedCenter = _center is null;
        _center ??= pointer.Point;
    }

    public override void OnPointerDrag(MapPointer pointer)
    {
        _cursor = pointer.Point;
        InvalidateOverlay();
    }

    public override void OnPointerUp(MapPointer pointer)
    {
        _cursor = pointer.Point;
        InvalidateOverlay();
        if (_center is not { } center)
        {
            return;
        }

        var reach = ClickReach * Context.MetersPerIconUnit;
        if (_pressPlacedCenter && Vector2.Distance(_pressAt, pointer.Position) <= reach)
        {
            if (_preset >= 0)
            {
                Add(center, PresetMeters(_preset, Context.Units));
            }

            return; // a click on the centre: the edge comes next
        }

        Context.TryResolve(center, out var middle);
        var radius = Vector2.Distance(middle, pointer.Point.Position);
        if (radius <= reach)
        {
            _center = null;
            return;
        }

        Add(center, radius);
    }

    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_cursor is not { } cursor)
        {
            return;
        }

        var cursorFound = canvas.TryResolve(cursor, out var edge);
        if (_center is { } center)
        {
            var centerFound = canvas.TryResolve(center, out var middle);
            var radius = Vector2.Distance(middle, edge);
            if (radius > ClickReach * canvas.MetersPerIconUnit)
            {
                canvas.Line(middle, edge, Context.Color);
                CircleShape.DrawRing(canvas, _previewLabel, middle, radius, center.IsAnchored, !centerFound, Context.Color);
            }
        }
        else if (_preset >= 0)
        {
            CircleShape.DrawRing(canvas, _previewLabel, edge, PresetMeters(_preset, canvas.Units), cursor.IsAnchored, !cursorFound, Context.Color);
        }
    }

    /// <summary>The cursor stays, so a picked preset's preview shows where the mouse last was on the map.</summary>
    private void Add(MapPoint center, float radius)
    {
        Context.Shapes.Add(new CircleShape(center, radius, Context.Color));
        _center = null;
        _preset = -1;
    }

    private void Forget()
    {
        _center = null;
        _cursor = null;
        _preset = -1;
    }

    /// <summary>One text per unit and preset, built once so the menu reads the same strings every frame.</summary>
    private static string[][] BuildPresetTexts(string format)
    {
        var suffixes = new[] { "nm", "km", "mi" }; // DistanceUnit order
        var texts = new string[suffixes.Length][];
        for (var unit = 0; unit < suffixes.Length; unit++)
        {
            texts[unit] = new string[PresetValues.Length];
            for (var i = 0; i < PresetValues.Length; i++)
            {
                texts[unit][i] = string.Format(CultureInfo.InvariantCulture, format, PresetValues[i], suffixes[unit]);
            }
        }

        return texts;
    }
}
