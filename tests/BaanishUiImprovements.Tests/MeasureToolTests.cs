using System.Numerics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.MapTools.BearingRange;
using BaanishUiImprovements.MapTools.Circle;
using BaanishUiImprovements.MapTools.Eraser;
using BaanishUiImprovements.Tracking;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// The bearing/range and circle tools, driven through their pointer events against a fake map. Positions are meters,
/// X east and Y north. The fake map has 10 m per icon unit, so a click within 60 m of the first point counts as the
/// same point, and its ground is 120 m above sea level everywhere.
/// </summary>
internal static class MeasureToolTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("two clicks measure bearing and range", TwoClicksMeasure),
        ("a measured end follows its unit, freezes while its track is stale, and resumes when spotted", MeasuredEndFollowsUnit),
        ("clicking the start again cancels a measurement", ClickingStartAgainCancels),
        ("a measurement's 3D label sits on its end", WorldLabelSitsOnEnd),
        ("a target overhead reads its height as range, and a level pair its map distance", RangeRunsThroughTheAir),
        ("the label hangs from the arrowhead", LabelHangsFromArrowhead),
        ("measurement text is built only when it changes", MeasureTextBuiltOnlyOnChange),
        ("dragging from the centre draws a circle", DraggingDrawsCircle),
        ("clicking the centre then the edge draws a circle", ClickCenterThenEdge),
        ("clicking the centre again cancels a circle", ClickingCenterAgainCancels),
        ("a preset radius finishes a centre or places the next", PresetRadius),
        ("a circle's centre follows its unit and freezes when lost", CircleFollowsUnit),
        ("a circle's edge stays at the cursor beside a unit", CircleEdgeIgnoresUnits),
        ("a circle's 3D ring sits level with its centre", CircleRingsSitLevelWithCentre),
        ("the eraser and undo take measurements and circles", EraserAndUndoTakeBoth),
        ("previews follow the cursor only once started", PreviewsFollowCursorOnceStarted),
        ("a placed start, centre, or preset is in progress until dropped", PlacedPointsAreInProgress),
    };

    private const float Nm = NavFormat.MetersPerNauticalMile;
    private static readonly ShapeColor White = new(255, 255, 255);

    private static void TwoClicksMeasure()
    {
        var map = new FakeMap();
        var tool = new BearingRangeTool(map);
        tool.OnClick(At(0, 0));
        Expect(map.Shapes.Shapes.Count == 0, "expected no shape after the first click");
        tool.OnClick(At(3 * Nm, 3 * Nm));
        var canvas = Draw(map);
        Expect(canvas.Arrows.Count == 1 && canvas.Arrows[0].To == new Vector2(3 * Nm, 3 * Nm), "expected one arrow to the second click");
        ExpectText(canvas.Labels[0].Text, "045° 4.2nm");
    }

    private static void MeasuredEndFollowsUnit()
    {
        var map = new FakeMap();
        map.Positions[7] = new Vector3(0, 3000, Nm);
        var tool = new BearingRangeTool(map);
        tool.OnClick(At(0, 0));
        tool.OnClick(OnUnit(map, 7));
        ExpectText(Draw(map).Labels[0].Text, "000° 1.8nm 9.8k ft");

        map.Positions[7] = new Vector3(2 * Nm, 3000, 0);
        ExpectText(Draw(map).Labels[0].Text, "090° 2.5nm 9.8k ft");

        map.States[7] = TrackState.Stale;
        map.Positions[7] = new Vector3(3 * Nm, 5000, 0);
        var stale = Draw(map);
        ExpectText(stale.Labels[0].Text, "090° 2.5nm 9.8k ft\nlost");
        Expect(stale.Arrows[0].To == new Vector2(2 * Nm, 0), "expected a stale end to stay where the unit was last known");
        var labels = new RecordingLabels();
        tool.OnFrame(labels);
        Expect(labels.Added[0].Position == new Vector3(2 * Nm, 3000, 0), "expected a stale end's 3D label at the last known altitude");
        ExpectText(labels.Added[0].Text, "090° 2.5nm 9.8k ft\nlost");

        map.States[7] = TrackState.Live;
        ExpectText(Draw(map).Labels[0].Text, "090° 4.0nm 16k ft");
    }

    private static void ClickingStartAgainCancels()
    {
        var map = new FakeMap();
        map.Positions[7] = new Vector3(5000, 0, 5000);
        var tool = new BearingRangeTool(map);
        tool.OnClick(At(0, 0));
        tool.OnClick(At(40, 0));
        tool.OnClick(OnUnit(map, 7));
        tool.OnClick(OnUnit(map, 7));
        Expect(map.Shapes.Shapes.Count == 0, "expected clicking the same point or unit twice to add nothing");
        tool.OnClick(At(0, 1000));
        Expect(map.Shapes.Shapes.Count == 0, "expected the cancelled measurement to start over");
    }

    private static void WorldLabelSitsOnEnd()
    {
        var map = new FakeMap();
        map.Positions[7] = new Vector3(0, 3000, -Nm);
        var tool = new BearingRangeTool(map);
        tool.OnClick(At(0, 0));
        tool.OnClick(At(Nm, 0));
        tool.OnClick(At(0, 0));
        tool.OnClick(OnUnit(map, 7));

        var labels = new RecordingLabels();
        tool.OnFrame(labels);
        Expect(labels.Added.Count == 2, $"expected a 3D label per arrow, got {labels.Added.Count}");
        Expect(labels.Added[0].Position == new Vector3(Nm, 120, 0), "expected a fixed end's label on the ground");
        ExpectText(labels.Added[0].Text, "090° 1.0nm");
        Expect(labels.Added[1].Position == new Vector3(0, 3000, -Nm), "expected a unit's label at its altitude");
        ExpectText(labels.Added[1].Text, "180° 1.8nm 9.8k ft");
    }

    /// <summary>The ground is 120 m up everywhere, so a unit 5120 m above a fixed point is 5 km from it.</summary>
    private static void RangeRunsThroughTheAir()
    {
        var map = new FakeMap { Units = DistanceUnit.Kilometres };
        map.Positions[7] = new Vector3(0, 5120, 0);
        map.Positions[8] = new Vector3(0, 3000, 0);
        map.Positions[9] = new Vector3(3000, 3000, 4000);
        map.Shapes.Add(new BearingRangeShape(new MapPoint(Vector2.Zero), new MapPoint(Vector2.Zero, 7), 120f, 0f, White));

        var tool = new BearingRangeTool(map);
        tool.OnClick(OnUnit(map, 8));
        tool.OnPointerMove(At(3840, 0));
        var preview = new RecordingCanvas(map);
        tool.DrawOverlay(preview);
        ExpectText(preview.Labels[0].Text, "090° 4.8km");
        tool.OnClick(OnUnit(map, 9));

        var canvas = Draw(map);
        ExpectText(canvas.Labels[0].Text, "000° 5.0km 5100 m");
        ExpectText(canvas.Labels[1].Text, "037° 5.0km 3000 m");
    }

    /// <summary>On a unit, the label hangs from just past its icon: the marker radius, 10 units of 10 m, beyond the head.</summary>
    private static void LabelHangsFromArrowhead()
    {
        var map = new FakeMap();
        map.Positions[7] = new Vector3(0, 0, 2000);
        var tool = new BearingRangeTool(map);
        tool.OnClick(At(0, 0));
        tool.OnClick(At(1000, 0));
        tool.OnClick(At(0, 0));
        tool.OnClick(OnUnit(map, 7));
        var canvas = Draw(map);
        Expect(canvas.Labels[0].Anchor.Equals(LabelAnchor.Bearing(Vector2.Zero, new Vector2(1000, 0))), "expected a fixed end's label on the head");
        Expect(canvas.Labels[1].Anchor.Equals(LabelAnchor.Bearing(Vector2.Zero, new Vector2(0, 2100))), "expected a unit's label past its icon");
    }

    private static void MeasureTextBuiltOnlyOnChange()
    {
        var label = new MeasureLabel();
        var first = label.BearingRange(Vector3.Zero, new Vector3(0, 0, 4.21f * Nm), false, DistanceUnit.NauticalMiles, lost: false);
        var same = label.BearingRange(Vector3.Zero, new Vector3(0.1f, 0, 4.24f * Nm), false, DistanceUnit.NauticalMiles, lost: false);
        Expect(ReferenceEquals(first, same), "expected the same text to be reused");
        ExpectText(label.BearingRange(Vector3.Zero, new Vector3(0, 0, 4.21f * Nm), false, DistanceUnit.Kilometres, lost: false), "000° 7.8km");
        ExpectText(label.BearingRange(Vector3.Zero, new Vector3(0, 0, 4.21f * Nm), false, DistanceUnit.Kilometres, lost: true), "000° 7.8km\nlost");

        var northeast = Vector3.Normalize(new Vector3(1, 0, 1));
        var level = new Vector3(0, 5500, 0);
        var climbing = label.BearingRange(level, northeast * 22000f + new Vector3(0, 5480, 0), true, DistanceUnit.Kilometres, lost: false);
        ExpectText(climbing, "045° 22km 5500 m");
        Expect(ReferenceEquals(climbing, label.BearingRange(level, northeast * 22000f + new Vector3(0, 5520, 0), true, DistanceUnit.Kilometres, lost: false)),
               "expected a climb within the shown 100 m to reuse the text");
        ExpectText(label.BearingRange(level, northeast * 22000f + new Vector3(0, 5560, 0), true, DistanceUnit.Kilometres, lost: true), "045° 22km 5600 m\nlost");
        var angels18 = new Vector3(0, 18000 * NavFormat.MetersPerFoot, 0);
        ExpectText(label.BearingRange(angels18, northeast * 12 * Nm + angels18, true, DistanceUnit.NauticalMiles, lost: false), "045° 12nm 18k ft");
        ExpectText(label.BearingRange(angels18, northeast * 12 * Nm + angels18, false, DistanceUnit.NauticalMiles, lost: false), "045° 12nm");

        var radius = new MeasureLabel();
        var ring = radius.Radius(5 * Nm, DistanceUnit.NauticalMiles, lost: false);
        Expect(ReferenceEquals(ring, radius.Radius(5.01f * Nm, DistanceUnit.NauticalMiles, lost: false)), "expected the same radius text to be reused");
        ExpectText(ring, "5.0nm");

        var westSouthwest = new Vector3(MathF.Sin(256f * MathF.PI / 180f), 0, MathF.Cos(256f * MathF.PI / 180f)) * 5 * Nm;
        ExpectText(radius.BearingRange(Vector3.Zero, westSouthwest, false, DistanceUnit.NauticalMiles, lost: false), "256° 5.0nm");
        ExpectText(radius.Radius(5 * Nm, DistanceUnit.NauticalMiles, lost: false), "5.0nm");
    }

    private static void DraggingDrawsCircle()
    {
        var map = new FakeMap();
        var tool = new CircleTool(map);
        tool.OnPointerDown(At(0, 0));
        tool.OnPointerDrag(At(1000, 1000));
        tool.OnPointerUp(At(3000, 4000));
        var canvas = Draw(map);
        Expect(canvas.Circles.Count == 1 && canvas.Circles[0].Radius == 5000f, "expected a 5 km circle");
        Expect(canvas.Lines == 2, "expected a cross on a fixed centre");
        ExpectText(canvas.Labels[0].Text, "2.7nm");
        Expect(canvas.Labels[0].Anchor.Equals(LabelAnchor.Ring(Vector2.Zero, 5000f)), "expected the radius on the ring");
    }

    private static void ClickCenterThenEdge()
    {
        var map = new FakeMap();
        var tool = new CircleTool(map);
        Click(tool, At(0, 0));
        Expect(map.Shapes.Shapes.Count == 0, "expected a click to place only the centre");
        Click(tool, At(1000, 0));
        Expect(Draw(map).Circles is [{ Radius: 1000f }], "expected the second click to set the radius");
    }

    private static void ClickingCenterAgainCancels()
    {
        var map = new FakeMap();
        var tool = new CircleTool(map);
        Click(tool, At(0, 0));
        Click(tool, At(30, 0));
        Expect(map.Shapes.Shapes.Count == 0, "expected a second click on the centre to add nothing");
        Click(tool, At(1000, 0));
        Expect(map.Shapes.Shapes.Count == 0, "expected the cancelled circle to start over from a new centre");
    }

    private static void PresetRadius()
    {
        var map = new FakeMap { Units = DistanceUnit.Kilometres };
        var tool = new CircleTool(map);
        Expect(tool.Options.Count == 3 && tool.Options[1] == "10" && tool.OptionSuffix == "km", "expected presets in kilometres");

        Click(tool, At(0, 0));
        tool.OnOption(1);
        tool.OnOption(2);
        Expect(tool.PickedOption == 2, "expected the preset armed for the next click to show picked");
        Click(tool, At(500, 500));
        Click(tool, At(9000, 500));
        var canvas = Draw(map);
        Expect(canvas.Circles.Count == 2, $"expected a preset to place one circle, got {canvas.Circles.Count}");
        Expect(canvas.Circles[0].Center == Vector2.Zero && canvas.Circles[0].Radius == 10000f, "expected the preset to finish the placed centre");
        Expect(canvas.Circles[1].Center == new Vector2(500, 500) && canvas.Circles[1].Radius == 20000f, "expected the next click to place the preset");

        var fresh = new CircleTool(map);
        fresh.OnOption(0);
        fresh.OnOption(0);
        Click(fresh, At(0, 0));
        Expect(map.Shapes.Shapes.Count == 2, "expected picking a preset twice to drop it");
    }

    private static void CircleFollowsUnit()
    {
        var map = new FakeMap { Units = DistanceUnit.Kilometres };
        map.Positions[9] = new Vector3(1000, 0, 1000);
        var tool = new CircleTool(map);
        tool.OnOption(0);
        Click(tool, OnUnit(map, 9));
        map.Positions[9] = new Vector3(2000, 0, 3000);
        var canvas = Draw(map);
        Expect(canvas.Circles[0].Center == new Vector2(2000, 3000), "expected the ring to follow the unit");
        Expect(canvas.Lines == 0, "expected no cross over a unit's icon");

        map.States[9] = TrackState.Unknown;
        map.Positions.Remove(9);
        var lost = Draw(map);
        ExpectText(lost.Labels[0].Text, "5.0km\nlost");
        Expect(lost.Circles[0].Center == new Vector2(2000, 3000), "expected a destroyed unit's ring to stay where it was last known");
    }

    /// <summary>The cursor is within snapping reach of a unit's icon 100 m past it, which the centre would take but the edge mustn't.</summary>
    private static void CircleEdgeIgnoresUnits()
    {
        var map = new FakeMap();
        map.Positions[9] = new Vector3(3100, 0, 0);
        var besideUnit = new MapPointer(new Vector2(3000, 0), new MapPoint(new Vector2(3100, 0), 9));
        var tool = new CircleTool(map);
        tool.OnPointerDown(At(0, 0));
        tool.OnPointerDrag(besideUnit);
        var preview = new RecordingCanvas(map);
        tool.DrawOverlay(preview);
        Expect(preview.Circles is [{ Radius: 3000f }], "expected the preview's edge at the cursor");
        tool.OnPointerUp(besideUnit);
        Expect(Draw(map).Circles is [{ Radius: 3000f }], "expected the radius to reach the cursor, not the unit");
    }

    private static void CircleRingsSitLevelWithCentre()
    {
        var map = new FakeMap();
        map.Positions[9] = new Vector3(1000, 4000, 1000);
        var tool = new CircleTool(map);
        Click(tool, At(0, 0));
        Click(tool, At(3000, 0));
        Click(tool, OnUnit(map, 9));
        Click(tool, At(1000, 3000));

        var labels = new RecordingLabels();
        tool.OnFrame(labels);
        Expect(labels.Rings.Count == 2, $"expected a 3D ring per circle, got {labels.Rings.Count}");
        Expect(labels.Rings[0] == (new Vector3(1000, 4000, 1000), 2000f), "expected the newest ring first, at its unit's altitude");
        Expect(labels.Rings[1] == (new Vector3(0, 120, 0), 3000f), "expected a fixed centre's ring on the ground");
    }

    private static void EraserAndUndoTakeBoth()
    {
        var map = new FakeMap();
        var measure = new BearingRangeTool(map);
        measure.OnClick(At(0, 0));
        measure.OnClick(At(1000, 0));
        var circle = new CircleTool(map);
        circle.OnPointerDown(At(0, 5000));
        circle.OnPointerUp(At(1000, 5000));
        Expect(map.Shapes.Shapes.Count == 2, "expected an arrow and a circle");

        var eraser = new EraserTool(map);
        eraser.OnClick(At(500, 30));
        eraser.OnClick(At(0, 6000));
        Expect(map.Shapes.Shapes.Count == 0, "expected the eraser to take the arrow and the ring");
        map.Shapes.Undo();
        map.Shapes.Undo();
        Expect(map.Shapes.Shapes.Count == 2, "expected undo to bring both back");
    }

    private static void PreviewsFollowCursorOnceStarted()
    {
        var map = new FakeMap();
        var measure = new BearingRangeTool(map);
        measure.OnPointerMove(At(100, 0));
        Expect(!TakeOverlayInvalid(measure), "expected no measurement redraw before a start is picked");
        measure.OnClick(At(0, 0));
        TakeOverlayInvalid(measure);
        measure.OnPointerMove(At(500, 0));
        Expect(TakeOverlayInvalid(measure), "expected the measurement preview to follow the cursor");

        var circle = new CircleTool(map);
        circle.OnPointerMove(At(100, 0));
        Expect(!TakeOverlayInvalid(circle), "expected no circle redraw before a centre or preset is picked");
        circle.OnOption(0);
        circle.OnPointerMove(At(500, 0));
        Expect(TakeOverlayInvalid(circle), "expected the preset circle preview to follow the cursor");
    }

    /// <summary>What a right-click cancels: the host drops it through OnDeactivate.</summary>
    private static void PlacedPointsAreInProgress()
    {
        var map = new FakeMap();
        var measure = new BearingRangeTool(map);
        measure.OnClick(At(0, 0));
        Expect(measure.InProgress, "expected a placed start to be in progress");
        measure.OnDeactivate();
        Expect(!measure.InProgress, "expected a dropped start to be gone");

        var circle = new CircleTool(map);
        Click(circle, At(0, 0));
        Expect(circle.InProgress, "expected a placed centre to be in progress");
        circle.OnDeactivate();
        circle.OnOption(0);
        Expect(circle.InProgress, "expected an armed preset to be in progress");
        circle.OnDeactivate();
        Expect(!circle.InProgress && circle.PickedOption == -1, "expected a dropped preset to be gone");
    }

    private static MapPointer At(float x, float y) => new(new Vector2(x, y), null);

    /// <summary>A click on a unit's icon, as the input routing reports it: anchored at the icon's current position.</summary>
    private static MapPointer OnUnit(FakeMap map, uint id)
    {
        var at = new Vector2(map.Positions[id].X, map.Positions[id].Z);
        return new MapPointer(at, new MapPoint(at, id));
    }

    private static void Click(MapTool tool, MapPointer pointer)
    {
        tool.OnPointerDown(pointer);
        tool.OnPointerUp(pointer);
    }

    private static RecordingCanvas Draw(FakeMap map)
    {
        var canvas = new RecordingCanvas(map);
        var shapes = map.Shapes.Shapes;
        for (var i = 0; i < shapes.Count; i++)
        {
            shapes[i].Draw(canvas);
        }

        return canvas;
    }

    /// <summary>
    /// Units sit at the global positions their side reports (X east, Y altitude, Z north), live unless
    /// <see cref="States"/> says otherwise. Resolved through <see cref="KnownPositions"/>, as the game side does.
    /// </summary>
    private sealed class FakeMap : IMapToolContext
    {
        private readonly KnownPositions _known = new();

        public Dictionary<uint, Vector3> Positions { get; } = new();
        public Dictionary<uint, TrackState> States { get; } = new();
        public ShapeStore Shapes { get; } = new();
        public ShapeColor Color => White;
        public DistanceUnit Units { get; set; } = DistanceUnit.NauticalMiles;
        public float MetersPerIconUnit => 10f;
        public float TextSize => 10f;
        public MapPoint? OwnAircraft => null;

        public bool TryResolve(MapPoint point, out Vector2 position)
        {
            var found = TryResolveWorld(point, out var world);
            position = new Vector2(world.X, world.Z);
            return found;
        }

        public bool TryResolveWorld(MapPoint point, out Vector3 position)
        {
            if (!point.IsAnchored)
            {
                position = new Vector3(point.Position.X, 0f, point.Position.Y);
                return true;
            }

            var state = !Positions.TryGetValue(point.UnitId, out var reported) ? TrackState.Unknown
                : States.TryGetValue(point.UnitId, out var set) ? set
                : TrackState.Live;
            if (!_known.TryResolve(point.UnitId, state, reported, out position))
            {
                position = new Vector3(point.Position.X, 0f, point.Position.Y);
            }

            return state == TrackState.Live;
        }

        public float GroundElevation(Vector2 position) => 120f;
    }

    private sealed class RecordingCanvas : IMapCanvas
    {
        private readonly IMapView _view;

        public RecordingCanvas(IMapView view) => _view = view;

        public List<(Vector2 From, Vector2 To)> Arrows { get; } = new();
        public List<(Vector2 Center, float Radius)> Circles { get; } = new();
        public List<(LabelAnchor Anchor, string Text)> Labels { get; } = new();
        public int Lines { get; private set; }

        public DistanceUnit Units => _view.Units;
        public float MetersPerIconUnit => _view.MetersPerIconUnit;
        public float TextSize => _view.TextSize;
        public MapPoint? OwnAircraft => _view.OwnAircraft;

        public bool TryResolve(MapPoint point, out Vector2 position) => _view.TryResolve(point, out position);
        public bool TryResolveWorld(MapPoint point, out Vector3 position) => _view.TryResolveWorld(point, out position);
        public void Line(Vector2 from, Vector2 to, ShapeColor color) => Lines++;
        public void Arrow(Vector2 from, Vector2 to, ShapeColor color) => Arrows.Add((from, to));
        public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color) => Lines++;
        public void Circle(Vector2 center, float radius, ShapeColor color) => Circles.Add((center, radius));

        public void Marker(Vector2 position, ShapeColor color)
        {
        }

        public void Label(LabelAnchor anchor, string text, ShapeColor color) => Labels.Add((anchor, text));
    }

    private sealed class RecordingLabels : IWorldLabels
    {
        public List<(Vector3 Position, string Text)> Added { get; } = new();
        public List<(Vector3 Center, float Radius)> Rings { get; } = new();

        public void Add(Vector3 position, string text, ShapeColor color) => Added.Add((position, text));
        public void Ring(Vector3 center, float radius, ShapeColor color) => Rings.Add((center, radius));
    }
}
