using System.Numerics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.MapTools.Eraser;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// The map tools' shared logic: the shape store and its history, distance and bearing text, and the eraser's hit test.
/// Positions are meters, X east and Y north. The fake map has 10 m per icon unit, so the eraser reaches 80 m.
/// </summary>
internal static class MapToolTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("undo and redo step through edits", UndoRedoStepThroughEdits),
        ("a new edit drops the redo steps", NewEditDropsRedo),
        ("clear is one undo step", ClearIsOneUndoStep),
        ("the shape cap refuses adds but not undo", ShapeCapRefusesAddsNotUndo),
        ("the point cap counts every shape", PointCapCountsEveryShape),
        ("replace keeps the id and the draw order", ReplaceKeepsIdAndOrder),
        ("reset forgets the history", ResetForgetsHistory),
        ("restore brings back the shapes and the history", RestoreBringsBackShapesAndHistory),
        ("history keeps the last 100 steps", HistoryKeepsLastSteps),
        ("bearings are three digits true", BearingsAreThreeDigitsTrue),
        ("distances round per unit", DistancesRoundPerUnit),
        ("the game's unit system picks the unit", GameUnitSystemPicksUnit),
        ("the hit test measures lines, rings, and labels", HitTestMeasuresShapes),
        ("the topmost of overlapping shapes is hit", TopmostShapeIsHit),
        ("the eraser takes the nearest shape in reach", EraserTakesNearestInReach),
        ("the eraser misses beyond reach", EraserMissesBeyondReach),
        ("an erased shape comes back with undo", ErasedShapeComesBackWithUndo),
        ("the eraser redraws its highlight only for a different shape", EraserHighlightRedrawsOnlyForDifferentShape),
    };

    private static readonly ShapeColor White = new(255, 255, 255);

    private static void UndoRedoStepThroughEdits()
    {
        var store = new ShapeStore();
        var a = Line(0, 0, 100, 0);
        var b = Line(0, 100, 100, 100);
        store.Add(a);
        store.Add(b);
        ExpectShapes(store, a, b);
        Expect(store.Undo() && store.Undo() && !store.Undo(), "expected exactly two undo steps");
        ExpectShapes(store);
        Expect(store.Redo(), "expected a redo step");
        ExpectShapes(store, a);
        Expect(store.Redo() && !store.Redo(), "expected exactly two redo steps");
        ExpectShapes(store, a, b);
    }

    private static void NewEditDropsRedo()
    {
        var store = new ShapeStore();
        var a = Line(0, 0, 100, 0);
        var c = Line(0, 200, 100, 200);
        store.Add(a);
        store.Add(Line(0, 100, 100, 100));
        store.Undo();
        store.Add(c);
        ExpectShapes(store, a, c);
        Expect(!store.CanRedo, "expected the undone shape to be gone for good");
    }

    private static void ClearIsOneUndoStep()
    {
        var store = new ShapeStore();
        store.Clear();
        Expect(!store.CanUndo, "expected clearing nothing to add no step");
        var a = Line(0, 0, 100, 0);
        var b = Line(0, 100, 100, 100);
        store.Add(a);
        store.Add(b);
        store.Clear();
        ExpectShapes(store);
        store.Undo();
        ExpectShapes(store, a, b);
    }

    private static void ShapeCapRefusesAddsNotUndo()
    {
        var store = new ShapeStore { MaxShapes = 2 };
        var a = Line(0, 0, 100, 0);
        var b = Line(0, 100, 100, 100);
        Expect(store.Add(a) && store.Add(b), "expected room for two shapes");
        Expect(store.IsFull && !store.Add(Line(0, 200, 100, 200)), "expected a third shape to be refused");
        ExpectShapes(store, a, b);
        store.Remove(a);
        store.MaxShapes = 1;
        store.Undo();
        ExpectShapes(store, a, b);
    }

    private static void PointCapCountsEveryShape()
    {
        var store = new ShapeStore { MaxPoints = 10 };
        var six = Line(0, 0, 100, 0, points: 6);
        Expect(store.Add(six), "expected 6 points to fit");
        Expect(!store.Add(Line(0, 0, 100, 0, points: 5)), "expected 11 points to be refused");
        Expect(store.Add(Line(0, 0, 100, 0, points: 4)), "expected exactly 10 points to fit");
        Expect(!store.Replace(six, Line(0, 0, 100, 0, points: 7)), "expected growing past the cap to be refused");
        Expect(store.Replace(six, Line(0, 0, 100, 0, points: 2)), "expected shrinking to be allowed");
        Expect(store.PointCount == 6, $"expected 6 points in total, got {store.PointCount}");
    }

    private static void ReplaceKeepsIdAndOrder()
    {
        var store = new ShapeStore();
        var a = Line(0, 0, 100, 0);
        var b = Line(0, 100, 100, 100);
        var c = Line(0, 200, 100, 200);
        var edited = Line(0, 150, 100, 150);
        store.Add(a);
        store.Add(b);
        store.Add(c);
        Expect(store.Replace(b, edited), "expected the replace to succeed");
        ExpectShapes(store, a, edited, c);
        Expect(edited.Id == b.Id && edited.Id != a.Id, "expected the replacement to keep the old id");
        store.Undo();
        ExpectShapes(store, a, b, c);
    }

    private static void ResetForgetsHistory()
    {
        var store = new ShapeStore();
        store.Add(Line(0, 0, 100, 0));
        store.Add(Line(0, 100, 100, 100));
        store.Undo();
        store.Reset();
        ExpectShapes(store);
        Expect(!store.CanUndo && !store.CanRedo, "expected no history after a reset");
    }

    /// <summary>The perf test swaps its own drawings in and must hand back the player's, undo and redo included.</summary>
    private static void RestoreBringsBackShapesAndHistory()
    {
        var store = new ShapeStore();
        var a = Line(0, 0, 100, 0);
        var b = Line(0, 100, 100, 100);
        store.Add(a);
        store.Add(b);
        store.Undo();
        var saved = store.Save();
        store.Reset();
        store.Add(Line(0, 200, 100, 200));
        store.Restore(saved);
        ExpectShapes(store, a);
        Expect(store.Redo(), "expected the redo step back");
        ExpectShapes(store, a, b);
        Expect(store.Undo() && store.Undo() && !store.CanUndo, "expected exactly the saved undo steps");
    }

    private static void HistoryKeepsLastSteps()
    {
        var store = new ShapeStore();
        for (var i = 0; i < ShapeStore.HistoryDepth + 50; i++)
        {
            store.Add(Line(0, i, 100, i));
        }

        var undone = 0;
        while (store.Undo())
        {
            undone++;
        }

        Expect(undone == ShapeStore.HistoryDepth, $"expected {ShapeStore.HistoryDepth} undo steps, got {undone}");
        Expect(store.Shapes.Count == 50, $"expected the 50 oldest shapes to stay, got {store.Shapes.Count}");
    }

    private static void BearingsAreThreeDigitsTrue()
    {
        ExpectText(NavFormat.Bearing(NavFormat.BearingDegrees(Vector2.Zero, new Vector2(0, 1000))), "000°");
        ExpectText(NavFormat.Bearing(NavFormat.BearingDegrees(Vector2.Zero, new Vector2(1000, 1000))), "045°");
        ExpectText(NavFormat.Bearing(NavFormat.BearingDegrees(Vector2.Zero, new Vector2(0, -1000))), "180°");
        ExpectText(NavFormat.Bearing(NavFormat.BearingDegrees(Vector2.Zero, new Vector2(-1000, 0))), "270°");
        ExpectText(NavFormat.Bearing(359.6f), "000°");
        ExpectText(NavFormat.Bearing(7.4f), "007°");
        ExpectText(NavFormat.Bearing(-90f), "270°");
        ExpectText(NavFormat.Bearing(float.NaN), "000°");
    }

    private static void DistancesRoundPerUnit()
    {
        ExpectText(NavFormat.Distance(NavFormat.MetersPerNauticalMile * 4.24f, DistanceUnit.NauticalMiles), "4.2nm");
        ExpectText(NavFormat.Distance(NavFormat.MetersPerNauticalMile * 9.96f, DistanceUnit.NauticalMiles), "10nm");
        ExpectText(NavFormat.Distance(12345f, DistanceUnit.Kilometres), "12km");
        ExpectText(NavFormat.Distance(850f, DistanceUnit.Kilometres), "850m");
        ExpectText(NavFormat.Distance(999.6f, DistanceUnit.Kilometres), "1.0km");
        ExpectText(NavFormat.Distance(NavFormat.MetersPerStatuteMile * 2.5f, DistanceUnit.StatuteMiles), "2.5mi");
        ExpectText(NavFormat.Distance(-5f, DistanceUnit.Kilometres), "0m");
        Expect(NavFormat.DistanceKey(NavFormat.MetersPerNauticalMile * 4.21f, DistanceUnit.NauticalMiles) ==
               NavFormat.DistanceKey(NavFormat.MetersPerNauticalMile * 4.24f, DistanceUnit.NauticalMiles),
            "expected distances that read the same to share a key");
        Expect(NavFormat.DistanceKey(NavFormat.MetersPerNauticalMile * 4.24f, DistanceUnit.NauticalMiles) !=
               NavFormat.DistanceKey(NavFormat.MetersPerNauticalMile * 4.26f, DistanceUnit.NauticalMiles),
            "expected 4.2nm and 4.3nm to differ");
    }

    private static void GameUnitSystemPicksUnit()
    {
        Expect(NavFormat.Resolve(UnitsSetting.Game, gameIsMetric: true) == DistanceUnit.Kilometres, "expected metric to mean kilometres");
        Expect(NavFormat.Resolve(UnitsSetting.Game, gameIsMetric: false) == DistanceUnit.NauticalMiles, "expected imperial to mean nautical miles");
        Expect(NavFormat.Resolve(UnitsSetting.StatuteMiles, gameIsMetric: true) == DistanceUnit.StatuteMiles, "expected the override to win");
    }

    private static void HitTestMeasuresShapes()
    {
        var hitTest = new ShapeHitTest(new FakeContext());
        var line = new[] { Line(0, 0, 1000, 0) };
        Expect(hitTest.Find(line, new Vector2(500, 70), 80f) is not null, "expected a click 70 m off the line to hit");
        Expect(hitTest.Find(line, new Vector2(1100, 0), 80f) is null, "expected a click 100 m past the end to miss");

        var ring = new MapShape[] { new Ring(Vector2.Zero, 1000f) };
        Expect(hitTest.Find(ring, new Vector2(1050, 0), 80f) is not null, "expected a click on the ring to hit");
        Expect(hitTest.Find(ring, Vector2.Zero, 80f) is null, "expected a click in the middle of the ring to miss");

        // "ABCD" in 10-unit text at 10 m per unit, with a 1-unit rim: 130 m either side of the point and 60 m above and below.
        var note = new MapShape[] { new Note(new Vector2(5000, 0), "ABCD") };
        Expect(hitTest.Find(note, new Vector2(5100, 30), 0f) is not null, "expected a click on the text to hit");
        Expect(hitTest.Find(note, new Vector2(5250, 0), 80f) is null, "expected a click 130 m past the text to miss");
    }

    private static void TopmostShapeIsHit()
    {
        var lower = Line(0, 0, 1000, 0);
        var upper = Line(0, 0, 1000, 0);
        var found = new ShapeHitTest(new FakeContext()).Find(new[] { lower, upper }, new Vector2(500, 10), 80f);
        Expect(ReferenceEquals(found, upper), "expected the shape drawn last to win a tie");
    }

    private static void EraserTakesNearestInReach()
    {
        var context = new FakeContext();
        var near = Line(0, 0, 1000, 0);
        var far = Line(0, 100, 1000, 100);
        context.Shapes.Add(near);
        context.Shapes.Add(far);
        new EraserTool(context).OnClick(new MapPointer(new Vector2(500, 40), null));
        ExpectShapes(context.Shapes, far);
    }

    private static void EraserMissesBeyondReach()
    {
        var context = new FakeContext();
        var line = Line(0, 0, 1000, 0);
        context.Shapes.Add(line);
        new EraserTool(context).OnClick(new MapPointer(new Vector2(500, 300), null));
        ExpectShapes(context.Shapes, line);
        Expect(!context.Shapes.CanRedo && context.Shapes.Version == 1, "expected a miss to add no undo step");
    }

    private static void ErasedShapeComesBackWithUndo()
    {
        var context = new FakeContext();
        var line = Line(0, 0, 1000, 0);
        context.Shapes.Add(line);
        new EraserTool(context).OnClick(new MapPointer(new Vector2(500, 0), null));
        ExpectShapes(context.Shapes);
        context.Shapes.Undo();
        ExpectShapes(context.Shapes, line);
    }

    private static void EraserHighlightRedrawsOnlyForDifferentShape()
    {
        var context = new FakeContext();
        context.Shapes.Add(Line(0, 0, 1000, 0));
        var eraser = new EraserTool(context);
        eraser.OnPointerMove(new MapPointer(new Vector2(100, 10), null));
        Expect(TakeOverlayInvalid(eraser), "expected hovering a shape to redraw the highlight");
        eraser.OnPointerMove(new MapPointer(new Vector2(600, 20), null));
        Expect(!TakeOverlayInvalid(eraser), "expected moving along the same shape to redraw nothing");
        eraser.OnPointerMove(new MapPointer(new Vector2(600, 500), null));
        Expect(TakeOverlayInvalid(eraser), "expected leaving the shape to clear the highlight");
    }

    private static LineShape Line(float x1, float y1, float x2, float y2, int points = 0) =>
        new(new Vector2(x1, y1), new Vector2(x2, y2), points);

    private static void ExpectShapes(ShapeStore store, params MapShape[] expected)
    {
        var same = store.Shapes.Count == expected.Length;
        for (var i = 0; same && i < expected.Length; i++)
        {
            same = ReferenceEquals(store.Shapes[i], expected[i]);
        }

        Expect(same, $"expected {expected.Length} shapes in order, got {store.Shapes.Count}");
    }

    private sealed class LineShape : MapShape
    {
        private readonly Vector2 _from;
        private readonly Vector2 _to;
        private readonly int _points;

        public LineShape(Vector2 from, Vector2 to, int points)
            : base(White)
        {
            _from = from;
            _to = to;
            _points = points;
        }

        public override int PointCount => _points;

        public override void Draw(IMapCanvas canvas) => canvas.Line(_from, _to, Color);
    }

    private sealed class Ring : MapShape
    {
        private readonly Vector2 _center;
        private readonly float _radius;

        public Ring(Vector2 center, float radius)
            : base(White)
        {
            _center = center;
            _radius = radius;
        }

        public override void Draw(IMapCanvas canvas) => canvas.Circle(_center, _radius, Color);
    }

    private sealed class Note : MapShape
    {
        private readonly Vector2 _position;
        private readonly string _text;

        public Note(Vector2 position, string text)
            : base(White)
        {
            _position = position;
            _text = text;
        }

        public override void Draw(IMapCanvas canvas) => canvas.Label(LabelAnchor.Note(_position), _text, Color);
    }

    private sealed class FakeContext : IMapToolContext
    {
        public ShapeStore Shapes { get; } = new();
        public ShapeColor Color => White;
        public DistanceUnit Units => DistanceUnit.NauticalMiles;
        public float MetersPerIconUnit => 10f;
        public float TextSize => 10f;
        public MapPoint? OwnAircraft => null;

        public bool TryResolve(MapPoint point, out Vector2 position)
        {
            position = point.Position;
            return true;
        }

        public bool TryResolveWorld(MapPoint point, out Vector3 position)
        {
            position = new Vector3(point.Position.X, 0f, point.Position.Y);
            return true;
        }

        public float GroundElevation(Vector2 position) => 0f;
    }
}
