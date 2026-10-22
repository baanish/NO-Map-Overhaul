using System.Numerics;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.Eraser;
using NoMapOverhaul.MapTools.Pen;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class CollaborativeEraseTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("collaborative hit testing picks the nearest drawing across both lists", NearestAcrossLists),
        ("collaborative hit testing gives exact ties to the last received drawing", ReceivedWinsTies),
        ("invisible drawings do not block visible hits or local erasure", InvisibleDoesNotBlockLocal),
        ("a read-only received drawing blocks local erase and highlight below it", ReadOnlyBlocksLocal),
        ("revoking erase permission clears the highlight under a still cursor", RevokePermission),
        ("a replaced received reference cannot be erased or highlighted", ReplacedReference),
        ("a removed received reference cannot be erased or highlighted", RemovedReference),
        ("accepted and rejected remote erases preserve received drawings and local history", RequestsPreserveHistory),
        ("missing collaborative erase callbacks default to no remote action", MissingCallbacks),
        ("local erases still undo and never call the remote callback", LocalEraseUndoes),
    };

    private static PenStroke Stroke(float y = 0f, byte alpha = 255) =>
        new(new[] { new Vector2(-10, y), new Vector2(10, y) }, new ShapeColor(255, 221, 51, alpha));

    private static void NearestAcrossLists()
    {
        var context = new Context();
        var local = Stroke(2);
        var remote = Stroke(5);
        var hit = new ShapeHitTest(context);
        Expect(ReferenceEquals(hit.Find(new[] { local }, new[] { remote }, Vector2.Zero, 8), local),
            "expected the closer local drawing");
        Expect(ReferenceEquals(hit.Find(new[] { remote }, new[] { local }, Vector2.Zero, 8), local),
            "expected the closer received drawing");
        Expect(hit.Find(new[] { local }, new[] { remote }, new Vector2(0, 20), 8) is null,
            "expected no drawing beyond reach");
    }

    private static void ReceivedWinsTies()
    {
        var context = new Context();
        var firstLocal = Stroke();
        var lastLocal = Stroke();
        var firstRemote = Stroke();
        var lastRemote = Stroke();
        var hit = new ShapeHitTest(context);
        Expect(ReferenceEquals(hit.Find(new[] { firstLocal, lastLocal }, new[] { firstRemote, lastRemote }, Vector2.Zero, 8), lastRemote),
            "expected the last received drawing to win an exact tie");
        Expect(ReferenceEquals(hit.Find(new[] { firstLocal, lastLocal }, Array.Empty<MapShape>(), Vector2.Zero, 8), lastLocal),
            "expected the last local drawing when no received drawing overlaps");
    }

    private static void InvisibleDoesNotBlockLocal()
    {
        var context = new Context();
        var local = Stroke();
        var invisible = Stroke(alpha: 0);
        var hit = new ShapeHitTest(context);
        Expect(hit.Find(new[] { invisible }, Vector2.Zero, 8) is null,
            "expected an invisible local drawing to be unselectable");
        Expect(hit.Find(Array.Empty<MapShape>(), new[] { invisible }, Vector2.Zero, 8) is null,
            "expected an invisible received drawing to be unselectable");
        Expect(ReferenceEquals(hit.Find(new[] { local }, new[] { invisible }, Vector2.Zero, 8), local),
            "expected an invisible received drawing not to win an overlapping hit");
        Expect(context.Shapes.Add(local), "expected local setup to succeed");
        var requests = 0;
        var eraser = new EraserTool(context, receivedShapes: () => new[] { invisible },
            canEraseReceived: _ => false, requestEraseReceived: _ => { requests++; return true; });
        Hover(eraser, context);
        Expect(ReferenceEquals(eraser.RedrawnShape, local), "expected the visible local drawing highlighted");
        eraser.DrawOverlay(context);
        Expect(context.Draws == 1, "expected the local highlight drawn");
        eraser.OnClick(new MapPointer(Vector2.Zero, null));
        Expect(!context.Shapes.Contains(local) && requests == 0, "expected only the visible local drawing erased");
        Expect(context.Shapes.Undo() && context.Shapes.Contains(local), "expected the local erase to undo");
    }

    private static void ReadOnlyBlocksLocal()
    {
        foreach (var alpha in new byte[] { 1, 255 }) ReadOnlyBlocksLocal(alpha);
    }

    private static void ReadOnlyBlocksLocal(byte alpha)
    {
        var context = new Context();
        var local = Stroke();
        var remote = Stroke(alpha: alpha);
        Expect(context.Shapes.Add(local), "expected local setup to succeed");
        var requests = 0;
        var eraser = new EraserTool(context, receivedShapes: () => new[] { remote },
            canEraseReceived: _ => false, requestEraseReceived: _ => { requests++; return true; });
        Expect(ReferenceEquals(eraser.FindAt(Vector2.Zero), remote), "expected the raw received hit even though it is read-only");
        Hover(eraser, context);
        eraser.DrawOverlay(context);
        Expect(eraser.RedrawnShape is null && context.Draws == 0, "expected no read-only highlight");
        eraser.OnClick(new MapPointer(Vector2.Zero, null));
        Expect(context.Shapes.Contains(local) && requests == 0, "expected no fallback erase of the local drawing");
    }

    private static void RevokePermission()
    {
        var context = new Context();
        var remote = Stroke();
        var allowed = true;
        var requests = 0;
        var eraser = new EraserTool(context, receivedShapes: () => new[] { remote },
            canEraseReceived: _ => allowed, requestEraseReceived: _ => { requests++; return true; });
        Hover(eraser, context);
        Expect(ReferenceEquals(eraser.RedrawnShape, remote), "expected an editable received highlight");
        eraser.DrawOverlay(context);
        Expect(context.Draws == 1, "expected one highlight draw");
        TakeOverlayInvalid(eraser);
        allowed = false;
        eraser.DrawOverlay(context);
        Expect(context.Draws == 1, "expected drawing to recheck permission before the next frame");
        eraser.OnFrame(context);
        Expect(eraser.RedrawnShape is null && TakeOverlayInvalid(eraser), "expected the still-cursor frame to clear and invalidate the highlight");
        Expect(!eraser.TryErase(remote) && requests == 0, "expected revoked permission to block the request");
        allowed = true;
        eraser.OnFrame(context);
        Expect(ReferenceEquals(eraser.RedrawnShape, remote) && TakeOverlayInvalid(eraser), "expected restored permission to highlight without moving the cursor");
    }

    private static void ReplacedReference() => StaleReference(replace: true);

    private static void RemovedReference() => StaleReference(replace: false);

    private static void StaleReference(bool replace)
    {
        var context = new Context();
        var old = Stroke();
        var replacement = Stroke();
        old.Id = replacement.Id = 7;
        IReadOnlyList<MapShape> received = new[] { old };
        MapShape? requested = null;
        var eraser = new EraserTool(context, receivedShapes: () => received,
            canEraseReceived: _ => true, requestEraseReceived: shape => { requested = shape; return true; });
        Hover(eraser, context);
        Expect(ReferenceEquals(eraser.RedrawnShape, old), "expected the original drawing highlighted");
        received = replace ? new[] { replacement } : Array.Empty<MapShape>();
        eraser.DrawOverlay(context);
        Expect(context.Draws == 0, "expected no stale highlight before the next frame");
        Expect(!eraser.TryErase(old) && requested is null, "expected the stale reference rejected even if its id is reused");
        eraser.OnFrame(context);
        Expect(ReferenceEquals(eraser.RedrawnShape, replace ? replacement : null), "expected hover to follow the current received list");
        if (replace)
        {
            Expect(eraser.TryErase(replacement) && ReferenceEquals(requested, replacement), "expected only the current replacement to be requested");
        }
    }

    private static void RequestsPreserveHistory()
    {
        foreach (var accepted in new[] { false, true })
        {
            var context = new Context();
            var local = Stroke(20);
            var undone = Stroke(30);
            Expect(context.Shapes.Add(local) && context.Shapes.Add(undone) && context.Shapes.Undo(), "expected a local undo and redo history");
            var remote = Stroke();
            var received = new List<MapShape> { remote };
            var version = context.Shapes.Version;
            var requests = 0;
            var eraser = new EraserTool(context, receivedShapes: () => received, canEraseReceived: _ => true,
                requestEraseReceived: shape => { Expect(ReferenceEquals(shape, remote), "expected the received reference"); requests++; return accepted; });
            Expect(eraser.TryErase(remote) == accepted && requests == 1, "expected the callback result to determine success");
            Expect(received.Count == 1 && ReferenceEquals(received[0], remote), "expected the received drawing to remain until owner confirmation");
            Expect(context.Shapes.Version == version && context.Shapes.Shapes.Count == 1 && context.Shapes.Contains(local), "expected no local store mutation");
            Expect(context.Shapes.Redo() && context.Shapes.Contains(undone), "expected the existing redo step preserved");
            Expect(context.Shapes.Undo() && context.Shapes.Undo() && context.Shapes.Shapes.Count == 0 && !context.Shapes.CanUndo,
                "expected the original two local undo steps and no remote undo step");
        }
    }

    private static void MissingCallbacks()
    {
        var context = new Context();
        var local = Stroke();
        var remote = Stroke();
        Expect(context.Shapes.Add(local), "expected local setup to succeed");
        var requests = 0;
        var cases = new[]
        {
            new EraserTool(context),
            new EraserTool(context, receivedShapes: () => new[] { remote }),
            new EraserTool(context, receivedShapes: () => new[] { remote }, canEraseReceived: _ => true),
            new EraserTool(context, receivedShapes: () => new[] { remote }, requestEraseReceived: _ => { requests++; return true; }),
            new EraserTool(context, canEraseReceived: _ => true, requestEraseReceived: _ => { requests++; return true; }),
        };
        foreach (var eraser in cases)
        {
            Expect(!eraser.TryErase(remote), "expected incomplete collaborative wiring to reject remote erase");
        }

        foreach (var eraser in cases.Skip(1).Take(3))
        {
            eraser.OnClick(new MapPointer(Vector2.Zero, null));
            Hover(eraser, context);
            Expect(eraser.RedrawnShape is null, "expected no highlight without all remote callbacks");
        }

        Expect(requests == 0 && context.Shapes.Contains(local), "expected no callback or fallback local deletion");
    }

    private static void LocalEraseUndoes()
    {
        var context = new Context();
        var local = Stroke();
        Expect(context.Shapes.Add(local), "expected local setup to succeed");
        var requests = 0;
        var eraser = new EraserTool(context, receivedShapes: () => Array.Empty<MapShape>(),
            canEraseReceived: _ => false, requestEraseReceived: _ => { requests++; return false; });
        Hover(eraser, context);
        Expect(ReferenceEquals(eraser.RedrawnShape, local), "expected local drawings to remain editable");
        eraser.OnClick(new MapPointer(Vector2.Zero, null));
        Expect(context.Shapes.Shapes.Count == 0 && requests == 0, "expected only the local drawing removed");
        Expect(!eraser.TryErase(local), "expected a stale local reference rejected");
        Expect(context.Shapes.Undo() && context.Shapes.Contains(local), "expected local erase to undo");
        Expect(context.Shapes.Redo() && context.Shapes.Shapes.Count == 0, "expected local erase to redo");
    }

    private static void Hover(EraserTool eraser, Context context)
    {
        eraser.OnPointerMove(new MapPointer(Vector2.Zero, null));
        eraser.OnFrame(context);
    }

    private sealed class Context : IMapToolContext, IMapCanvas, IWorldLabels
    {
        public ShapeStore Shapes { get; } = new();
        public ShapeColor Color => new(255, 221, 51);
        public float LineWidth => 1f;
        public DistanceUnit Units => DistanceUnit.NauticalMiles;
        public float MetersPerIconUnit => 1f;
        public float TextSize => 10f;
        public MapPoint? OwnAircraft => null;
        public int Draws { get; private set; }
        public bool TryResolve(MapPoint point, out Vector2 position) { position = point.Position; return true; }
        public bool TryResolveWorld(MapPoint point, out Vector3 position) { position = new Vector3(point.Position.X, 0, point.Position.Y); return true; }
        public float GroundElevation(Vector2 position) => 0f;
        public bool IsUnitGone(uint unitId) => false;
        public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color) => Draws++;
        public void Line(Vector2 from, Vector2 to, ShapeColor color) { }
        public void Arrow(Vector2 from, Vector2 to, ShapeColor color) { }
        public void Circle(Vector2 center, float radius, ShapeColor color) { }
        public void Marker(Vector2 position, ShapeColor color) { }
        public void Label(LabelAnchor anchor, string text, ShapeColor color) { }
        public void Add(Vector3 position, string text, ShapeColor color) { }
        public void Ring(Vector3 center, float radius, ShapeColor color, object owner) { }
    }
}
