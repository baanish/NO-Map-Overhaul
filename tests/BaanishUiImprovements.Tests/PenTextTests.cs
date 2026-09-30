using System.Numerics;
using BaanishUiImprovements.MapTools;
using BaanishUiImprovements.MapTools.Eraser;
using BaanishUiImprovements.MapTools.Pen;
using BaanishUiImprovements.MapTools.Text;
using BaanishUiImprovements.Tracking;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// The pen and text tools. Positions are meters, X east and Y north. The fake map has 10 m per icon unit, so the pen
/// keeps points 30 m apart and simplifies within 7.5 m, and the ground is 120 m above sea level everywhere.
/// </summary>
internal static class PenTextTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("simplifying drops straight runs and keeps corners", SimplifyingKeepsCorners),
        ("simplifying keeps wiggles past the tolerance", SimplifyingKeepsWiggles),
        ("the pen drops points closer than the gap", PenDropsClosePoints),
        ("a pen click leaves a dot", PenClickLeavesDot),
        ("a pen stroke stops at the point cap and says so", PenStopsAtPointCap),
        ("a straight stroke past the budget simplifies and keeps going", StraightStrokeKeepsGoing),
        ("a spent point budget refuses the next stroke", SpentBudgetRefusesStroke),
        ("a pen stroke undoes and erases", PenStrokeUndoesAndErases),
        ("switching tools drops a half-drawn stroke", SwitchingToolsDropsStroke),
        ("the pen redraws only for a kept point", PenRedrawsOnlyForKeptPoint),
        ("typed text is placed on enter", TypedTextPlacedOnEnter),
        ("escape drops the text", EscapeDropsText),
        ("backspace deletes and the length is capped", BackspaceAndLengthCap),
        ("blank text places nothing", BlankTextPlacesNothing),
        ("clicking elsewhere places the note and starts the next", ClickElsewherePlacesNote),
        ("a note labels the 3D view at ground height", NoteLabelsThreeDView),
        ("a text note undoes and erases", TextNoteUndoesAndErases),
        ("a held stroke and typed text are in progress until dropped", StrokeAndTextAreInProgress),
        ("a click on a unit puts the note on it, and one on the map where it lands", NoteAnchorsToClickedUnit),
        ("a note on a unit follows it, says lost while stale, and resumes when spotted", NoteOnUnitFollowsIt),
        ("a note on a destroyed unit goes, and undo and redo can't bring it back", NoteGoesWithDestroyedUnit),
        ("clear then undo doesn't bring back a note on a destroyed unit", ClearAndUndoSkipDestroyedUnitNote),
        ("the eraser takes a note on a unit", EraserTakesUnitNote),
    };

    /// <summary>A unit 3000 m up, over (1000, 2000).</summary>
    private static readonly Vector3 Bomber = new(1000, 3000, 2000);

    private static readonly ShapeColor Yellow = new(255, 221, 51);

    private static void SimplifyingKeepsCorners()
    {
        var points = new List<Vector2>();
        for (var x = 0; x <= 100; x += 10)
        {
            points.Add(new Vector2(x, 0));
        }

        for (var y = 10; y <= 100; y += 10)
        {
            points.Add(new Vector2(100, y));
        }

        StrokeSimplifier.Simplify(points, 1f);
        ExpectPoints(points, new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100));
    }

    private static void SimplifyingKeepsWiggles()
    {
        var points = new List<Vector2> { new(0, 0), new(10, 5), new(20, 0), new(30, 0.5f), new(40, 0) };
        StrokeSimplifier.Simplify(points, 1f);
        ExpectPoints(points, new Vector2(0, 0), new Vector2(10, 5), new Vector2(20, 0), new Vector2(40, 0));
    }

    /// <summary>A straight drag in 10 m steps: every third point survives the drag, and only the ends the release.</summary>
    private static void PenDropsClosePoints()
    {
        var context = new FakeContext();
        var pen = new PenTool(context);
        pen.OnPointerDown(At(0, 0));
        for (var x = 10; x <= 180; x += 10)
        {
            pen.OnPointerDrag(At(x, 0));
        }

        var canvas = new RecordingCanvas();
        pen.DrawOverlay(canvas);
        Expect(canvas.PolylinePoints == 7, $"expected 7 points while drawing, got {canvas.PolylinePoints}");
        pen.OnPointerUp(At(180, 0));
        var stroke = OnlyStroke(context);
        ExpectPoints(stroke.Points, new Vector2(0, 0), new Vector2(180, 0));
        Expect(stroke.Color == Yellow, "expected the stroke in the picked colour");
    }

    private static void PenClickLeavesDot()
    {
        var context = new FakeContext();
        var pen = new PenTool(context);
        pen.OnPointerDown(At(500, 500));
        pen.OnPointerUp(At(500, 500));
        ExpectPoints(OnlyStroke(context).Points, new Vector2(500, 500));
    }

    private static void PenStopsAtPointCap()
    {
        var context = new FakeContext();
        context.Shapes.MaxPoints = 5;
        var pen = new PenTool(context);
        DrawZigzag(pen, 0);
        Expect(OnlyStroke(context).PointCount == 5, $"expected the stroke cut at 5 points, got {OnlyStroke(context).PointCount}");
        Expect(pen.Status.Contains("limit"), $"expected a limit message, got '{pen.Status}'");
        context.Shapes.Undo();
        Expect(!pen.Status.Contains("limit"), $"expected the limit message gone after an undo, got '{pen.Status}'");
    }

    private static void StraightStrokeKeepsGoing()
    {
        var context = new FakeContext();
        context.Shapes.MaxPoints = 16;
        var pen = new PenTool(context);
        pen.OnPointerDown(At(0, 0));
        for (var x = 30; x <= 3000; x += 30)
        {
            pen.OnPointerDrag(At(x, 0));
        }

        pen.OnPointerUp(At(3000, 0));
        ExpectPoints(OnlyStroke(context).Points, new Vector2(0, 0), new Vector2(3000, 0));
        Expect(!pen.Status.Contains("limit"), $"expected no limit message, got '{pen.Status}'");
    }

    private static void SpentBudgetRefusesStroke()
    {
        var context = new FakeContext();
        context.Shapes.MaxPoints = 11;
        var pen = new PenTool(context);
        DrawZigzag(pen, 0);
        Expect(OnlyStroke(context).PointCount == 11 && !pen.Status.Contains("limit"), "expected the first stroke to fit exactly");
        DrawZigzag(pen, 1000);
        Expect(context.Shapes.Shapes.Count == 1, $"expected the second stroke refused, got {context.Shapes.Shapes.Count} strokes");
        Expect(pen.Status.Contains("limit"), $"expected a limit message, got '{pen.Status}'");
        context.Shapes.Undo();
        DrawZigzag(pen, 1000);
        Expect(context.Shapes.Shapes.Count == 1 && !pen.Status.Contains("limit"), "expected room again after an undo");
    }

    private static void PenStrokeUndoesAndErases()
    {
        var context = new FakeContext();
        var pen = new PenTool(context);
        DrawZigzag(pen, 0);
        var stroke = OnlyStroke(context);
        Expect(context.Shapes.Undo() && context.Shapes.Shapes.Count == 0, "expected undo to take the stroke");
        Expect(context.Shapes.Redo() && context.Shapes.Contains(stroke), "expected redo to bring it back");
        new EraserTool(context).OnClick(At(30, 45));
        Expect(context.Shapes.Shapes.Count == 0, "expected the eraser to take the stroke");
    }

    private static void SwitchingToolsDropsStroke()
    {
        var context = new FakeContext();
        var pen = new PenTool(context);
        pen.OnPointerDown(At(0, 0));
        pen.OnPointerDrag(At(100, 0));
        var canvas = new RecordingCanvas();
        pen.DrawOverlay(canvas);
        Expect(canvas.PolylinePoints == 2, $"expected the half-drawn stroke in the overlay, got {canvas.PolylinePoints} points");
        pen.OnDeactivate();
        pen.OnPointerUp(At(200, 0));
        canvas = new RecordingCanvas();
        pen.DrawOverlay(canvas);
        Expect(context.Shapes.Shapes.Count == 0 && canvas.PolylinePoints == 0, "expected nothing kept or drawn");
    }

    private static void TypedTextPlacedOnEnter()
    {
        var context = new FakeContext();
        var text = new TextTool(context);
        Expect(!text.CapturesKeyboard, "expected the keyboard free before a click");
        text.OnClick(At(500, 700));
        Expect(text.CapturesKeyboard, "expected the keyboard taken while typing");
        Type(text, "CAP 1\n");
        Expect(!text.CapturesKeyboard, "expected the keyboard back after Enter");
        var note = OnlyNote(context);
        ExpectText(note.Text, "CAP 1");
        Expect(note.At.Position == new Vector2(500, 700) && note.Color == Yellow, "expected the note at the click in the picked colour");
    }

    private static void EscapeDropsText()
    {
        var context = new FakeContext();
        var text = new TextTool(context);
        text.OnClick(At(0, 0));
        Type(text, "SAM\u001b");
        Expect(!text.CapturesKeyboard && context.Shapes.Shapes.Count == 0, "expected nothing placed after Escape");
        var canvas = new RecordingCanvas();
        text.DrawOverlay(canvas);
        Expect(canvas.Label is null, "expected no preview after Escape");
    }

    private static void BackspaceAndLengthCap()
    {
        var context = new FakeContext();
        var text = new TextTool(context);
        text.OnClick(At(0, 0));
        Type(text, "AB\bC\t");
        var canvas = new RecordingCanvas();
        text.DrawOverlay(canvas);
        ExpectText(canvas.Label ?? "none", "AC_");
        Type(text, new string('X', TextTool.MaxLength) + "\n");
        Expect(OnlyNote(context).Text.Length == TextTool.MaxLength, $"expected the note cut at {TextTool.MaxLength} characters");
    }

    private static void BlankTextPlacesNothing()
    {
        var context = new FakeContext();
        var text = new TextTool(context);
        text.OnClick(At(0, 0));
        Type(text, "   \n");
        Expect(context.Shapes.Shapes.Count == 0 && !context.Shapes.CanUndo, "expected no note and no undo step");
    }

    private static void ClickElsewherePlacesNote()
    {
        var context = new FakeContext();
        var text = new TextTool(context);
        text.OnClick(At(0, 0));
        Type(text, "BULLSEYE");
        text.OnClick(At(5000, 0));
        ExpectText(OnlyNote(context).Text, "BULLSEYE");
        Expect(text.CapturesKeyboard, "expected typing to go on at the new point");
        Type(text, "\n");
        Expect(context.Shapes.Shapes.Count == 1, "expected nothing typed at the new point to place nothing");
    }

    private static void NoteLabelsThreeDView()
    {
        var context = new FakeContext();
        var text = new TextTool(context);
        text.OnClick(At(500, 700));
        Type(text, "FARP\n");
        var labels = new RecordingLabels();
        text.OnFrame(labels);
        Expect(labels.Count == 1 && labels.Text == "FARP" && labels.Position == new Vector3(500, 120, 700),
            $"expected one 'FARP' label at the ground, got {labels.Count} at {labels.Position}");
        context.Shapes.Undo();
        labels = new RecordingLabels();
        text.OnFrame(labels);
        Expect(labels.Count == 0, "expected no label once the note is undone");
    }

    private static void TextNoteUndoesAndErases()
    {
        var context = new FakeContext();
        var text = new TextTool(context);
        text.OnClick(At(5000, 0));
        Type(text, "ABCD\n");
        var note = OnlyNote(context);
        Expect(context.Shapes.Undo() && context.Shapes.Shapes.Count == 0, "expected undo to take the note");
        Expect(context.Shapes.Redo() && context.Shapes.Contains(note), "expected redo to bring it back");
        new EraserTool(context).OnClick(At(5100, 30));
        Expect(context.Shapes.Shapes.Count == 0, "expected the eraser to take the note");
    }

    private static void PenRedrawsOnlyForKeptPoint()
    {
        var pen = new PenTool(new FakeContext());
        pen.OnPointerDown(At(0, 0));
        Expect(TakeOverlayInvalid(pen), "expected a new stroke to redraw");
        pen.OnPointerDrag(At(10, 0));
        Expect(!TakeOverlayInvalid(pen), "expected a dropped point to redraw nothing");
        pen.OnPointerDrag(At(40, 0));
        Expect(TakeOverlayInvalid(pen), "expected a kept point to redraw");
        pen.OnPointerUp(At(40, 0));
        Expect(TakeOverlayInvalid(pen), "expected the finished stroke's preview to clear");
    }

    /// <summary>Up and down 50 m every 30 m for 300 m: 11 points that all survive thinning and simplifying.</summary>
    /// <summary>What a right-click cancels: the host drops it through OnDeactivate.</summary>
    private static void StrokeAndTextAreInProgress()
    {
        var context = new FakeContext();
        var pen = new PenTool(context);
        pen.OnPointerDown(At(0, 0));
        Expect(pen.InProgress, "expected a held stroke to be in progress");
        pen.OnDeactivate();
        Expect(!pen.InProgress, "expected a dropped stroke to be gone");

        var text = new TextTool(context);
        text.OnClick(At(0, 0));
        Expect(text.InProgress, "expected typing to be in progress");
        text.OnDeactivate();
        Expect(!text.InProgress && !text.CapturesKeyboard, "expected dropped text to give the keyboard back");
    }

    private static void NoteAnchorsToClickedUnit()
    {
        var context = new FakeContext();
        context.Positions[7] = Bomber;
        var text = new TextTool(context);
        text.OnClick(OnUnit(context, 7));
        var preview = new RecordingCanvas(context);
        text.DrawOverlay(preview);
        Expect(preview.Anchor is { Kind: LabelKind.UnitNote }, "expected the preview beside the unit");
        Type(text, "BOMBERS\n");
        Expect(OnlyNote(context).At.UnitId == 7, "expected the note on unit 7");

        text.OnClick(At(500, 700));
        Type(text, "IP\n");
        var note = (TextNote)context.Shapes.Shapes[1];
        Expect(!note.At.IsAnchored && note.At.Position == new Vector2(500, 700) && note.Elevation == 120f, "expected a fixed note on the ground at the click");
        var canvas = new RecordingCanvas(context);
        note.Draw(canvas);
        Expect(canvas.Anchor is { Kind: LabelKind.Note } anchor && anchor.Point == new Vector2(500, 700), "expected the fixed note centred on its point");
    }

    private static void NoteOnUnitFollowsIt()
    {
        var context = new FakeContext();
        context.Positions[7] = Bomber;
        var text = new TextTool(context);
        text.OnClick(OnUnit(context, 7));
        Type(text, "BOMBERS\n");
        ExpectNoteAt(context, text, new Vector2(1000, 2000), Bomber, "BOMBERS");

        context.Positions[7] = new Vector3(1500, 3200, 2000);
        ExpectNoteAt(context, text, new Vector2(1500, 2000), new Vector3(1500, 3200, 2000), "BOMBERS");

        context.States[7] = TrackState.Stale;
        context.Positions[7] = new Vector3(4000, 5000, 0);
        ExpectNoteAt(context, text, new Vector2(1500, 2000), new Vector3(1500, 3200, 2000), "BOMBERS\nlost");

        context.States.Remove(7);
        ExpectNoteAt(context, text, new Vector2(4000, 0), new Vector3(4000, 5000, 0), "BOMBERS");
    }

    /// <summary>The destroyed unit's note goes as if it had never been placed, and the step that added it with it.</summary>
    private static void NoteGoesWithDestroyedUnit()
    {
        var context = TwoNotes(out var text);
        context.Gone.Add(7);
        var labels = new RecordingLabels();
        text.OnFrame(labels);
        Expect(labels.Count == 1 && labels.Text == "IP", $"expected only the fixed note's 3D label, got {labels.Count}");
        ExpectText(OnlyNote(context).Text, "IP");
        Expect(context.Shapes.Undo() && context.Shapes.Shapes.Count == 0 && !context.Shapes.CanUndo, "expected one undo to take the fixed note, and no more");
        Expect(context.Shapes.Redo() && !context.Shapes.CanRedo, "expected one redo to bring back the fixed note, and no more");
        ExpectText(OnlyNote(context).Text, "IP");
    }

    /// <summary>The unit goes before the Clear, then after it, when an undo brings its note back until the next frame.</summary>
    private static void ClearAndUndoSkipDestroyedUnitNote()
    {
        var context = TwoNotes(out var text);
        context.Gone.Add(7);
        text.OnFrame(new RecordingLabels());
        context.Shapes.Clear();
        Expect(context.Shapes.Undo(), "expected Clear to undo");
        ExpectText(OnlyNote(context).Text, "IP");

        context = TwoNotes(out text);
        context.Shapes.Clear();
        context.Gone.Add(7);
        context.Shapes.Undo();
        text.OnFrame(new RecordingLabels());
        ExpectText(OnlyNote(context).Text, "IP");
        Expect(context.Shapes.Redo() && context.Shapes.Shapes.Count == 0, "expected redo to clear again");
    }

    private static void EraserTakesUnitNote()
    {
        var context = new FakeContext();
        context.Positions[7] = Bomber;
        var text = new TextTool(context);
        text.OnClick(OnUnit(context, 7));
        Type(text, "BOMBERS\n");
        new EraserTool(context).OnClick(At(1300, 2000));
        Expect(context.Shapes.Shapes.Count == 0, "expected the eraser to take the note beside the unit");
    }

    /// <summary>A fixed note "IP", then "BOMBERS" on unit 7.</summary>
    private static FakeContext TwoNotes(out TextTool text)
    {
        var context = new FakeContext();
        context.Positions[7] = Bomber;
        text = new TextTool(context);
        text.OnClick(At(0, 0));
        Type(text, "IP\n");
        text.OnClick(OnUnit(context, 7));
        Type(text, "BOMBERS\n");
        return context;
    }

    /// <summary>The only note's map label beside the unit at <paramref name="unit"/>, and its 3D label at <paramref name="world"/>.</summary>
    private static void ExpectNoteAt(FakeContext context, TextTool text, Vector2 unit, Vector3 world, string expected)
    {
        var canvas = new RecordingCanvas(context);
        OnlyNote(context).Draw(canvas);
        Expect(canvas.Anchor is { Kind: LabelKind.UnitNote } anchor && anchor.Point == unit, $"expected the label beside the unit at {unit}, got {canvas.Anchor?.Point}");
        ExpectText(canvas.Label ?? "none", expected);
        var labels = new RecordingLabels();
        text.OnFrame(labels);
        Expect(labels.Count == 1 && labels.Position == world, $"expected the 3D label at {world}, got {labels.Position}");
        ExpectText(labels.Text ?? "none", expected);
    }

    private static void DrawZigzag(PenTool pen, float y)
    {
        pen.OnPointerDown(At(0, y));
        for (var x = 30; x <= 300; x += 30)
        {
            pen.OnPointerDrag(At(x, y + (x % 60 == 30 ? 50 : 0)));
        }

        pen.OnPointerUp(At(300, y));
    }

    private static void Type(TextTool tool, string keys)
    {
        foreach (var key in keys)
        {
            tool.OnTextInput(key);
        }
    }

    private static MapPointer At(float x, float y) => new(new Vector2(x, y), null);

    private static MapPointer OnUnit(FakeContext context, uint id)
    {
        var at = new Vector2(context.Positions[id].X, context.Positions[id].Z);
        return new MapPointer(at, new MapPoint(at, id));
    }

    private static PenStroke OnlyStroke(FakeContext context)
    {
        Expect(context.Shapes.Shapes.Count == 1 && context.Shapes.Shapes[0] is PenStroke, $"expected one stroke, got {context.Shapes.Shapes.Count} shapes");
        return (PenStroke)context.Shapes.Shapes[0];
    }

    private static TextNote OnlyNote(FakeContext context)
    {
        Expect(context.Shapes.Shapes.Count == 1 && context.Shapes.Shapes[0] is TextNote, $"expected one note, got {context.Shapes.Shapes.Count} shapes");
        return (TextNote)context.Shapes.Shapes[0];
    }

    private static void ExpectPoints(IReadOnlyList<Vector2> actual, params Vector2[] expected) =>
        Expect(actual.SequenceEqual(expected), $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");

    /// <summary>
    /// Units sit at the global positions their side reports (X east, Y altitude, Z north), live unless
    /// <see cref="States"/> says otherwise, until they're <see cref="Gone"/>. Resolved through <see cref="KnownPositions"/>,
    /// as the game side does.
    /// </summary>
    private sealed class FakeContext : IMapToolContext
    {
        private readonly KnownPositions _known = new();

        public Dictionary<uint, Vector3> Positions { get; } = new();
        public Dictionary<uint, TrackState> States { get; } = new();
        public HashSet<uint> Gone { get; } = new();
        public ShapeStore Shapes { get; } = new();
        public ShapeColor Color => Yellow;
        public DistanceUnit Units => DistanceUnit.NauticalMiles;
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

        public bool IsUnitGone(uint unitId) => Gone.Contains(unitId);
    }

    /// <summary>Keeps the last label's anchor and text and counts polyline points; the tools here draw nothing else.</summary>
    private sealed class RecordingCanvas : IMapCanvas
    {
        private readonly IMapView? _view;

        /// <param name="view">Resolves points on units; without it every point is where it was placed.</param>
        public RecordingCanvas(IMapView? view = null) => _view = view;

        public int PolylinePoints { get; private set; }
        public string? Label { get; private set; }
        public LabelAnchor? Anchor { get; private set; }

        public DistanceUnit Units => DistanceUnit.NauticalMiles;
        public float MetersPerIconUnit => 10f;
        public float TextSize => 10f;
        public MapPoint? OwnAircraft => null;

        public bool TryResolve(MapPoint point, out Vector2 position)
        {
            if (_view != null)
            {
                return _view.TryResolve(point, out position);
            }

            position = point.Position;
            return true;
        }

        public bool TryResolveWorld(MapPoint point, out Vector3 position)
        {
            if (_view != null)
            {
                return _view.TryResolveWorld(point, out position);
            }

            position = new Vector3(point.Position.X, 0f, point.Position.Y);
            return true;
        }

        public void Line(Vector2 from, Vector2 to, ShapeColor color)
        {
        }

        public void Arrow(Vector2 from, Vector2 to, ShapeColor color)
        {
        }

        public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color) => PolylinePoints += points.Count;

        public void Circle(Vector2 center, float radius, ShapeColor color)
        {
        }

        public void Marker(Vector2 position, ShapeColor color)
        {
        }

        void IMapCanvas.Label(LabelAnchor anchor, string text, ShapeColor color)
        {
            Anchor = anchor;
            Label = text;
        }
    }

    private sealed class RecordingLabels : IWorldLabels
    {
        public int Count { get; private set; }
        public string? Text { get; private set; }
        public Vector3 Position { get; private set; }

        public void Add(Vector3 position, string text, ShapeColor color)
        {
            Count++;
            Text = text;
            Position = position;
        }

        public void Ring(Vector3 center, float radius, ShapeColor color)
        {
        }
    }
}
