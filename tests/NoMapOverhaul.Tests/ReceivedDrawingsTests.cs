using System.Numerics;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.BearingRange;
using NoMapOverhaul.MapTools.Circle;
using NoMapOverhaul.MapTools.Eraser;
using NoMapOverhaul.MapTools.Pen;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.MapTools.Waypoint;
using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class ReceivedDrawingsTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("received drawings isolate sender IDs and preserve replacement render IDs", Identity),
        ("identical heartbeat shapes retain references and renderer version", StableHeartbeat),
        ("every received shape field invalidates only the changed reference", ChangedContent),
        ("received snapshot order changes preserve instances but invalidate rendering", ChangedOrder),
        ("removed and cleared drawings never reuse stale references", RemovedReferences),
        ("received drawings resolve the original owner and wire ID for edit requests", OwnerLookup),
        ("received drawings remove only the specified owner", RemoveOwner),
        ("teammates' drawings stack in the order the teammates first sent them", PairingOrder),
        ("received drawings enforce thirty-two peers and reuse cleared capacity", PeerBudget),
        ("received drawings split a four-hundred-shape pool evenly between teammates", ShapeBudget),
        ("the performance log counts each teammate's held and shown drawings", CountOwned),
        ("a full teammate roster is reported separately from invalid peer data", CapacityReason),
        ("received drawings split a ten-thousand-point pool evenly between teammates", GeometryBudget),
        ("received drawings enforce one hundred shapes and three thousand points per snapshot", SnapshotBudget),
        ("received drawings reject invalid identity channels and shape kinds without mutation", InvalidSnapshot),
        ("received drawing IDs remain distinct after removal reuse and clear", IdLifecycle),
        ("shared shapes require every anchor to pass recipient visibility", Visibility),
        ("shared fixed shapes need no unit lookup", FixedVisibility),
        ("shared snapshots include every drawing kind, unit-linked or fixed", ShareableSnapshot),
        ("a decoded drawing the store keeps is never decoded into a later snapshot", KeptDecodedShapes),
        ("received BRA map and HUD numbers use recipient tracks instead of sender fallbacks", ReceiverBearing),
        ("received unit circles and notes sit on recipient tracks instead of sender fallbacks", ReceiverAnchoredShapes),
        ("received BRA reads lost at its last known position and hides only for a unit never known", LostBearing),
        ("received BRA collaborative erase respects broadcast permission and preserves owner confirmation", BearingErase),
    };

    private static readonly ShapeColor Color = new(1, 2, 3);

    /// <summary>A teammate who leaves goes to the back on return; one who only changes drawings keeps its place.</summary>
    private static void PairingOrder()
    {
        var store = new ReceivedDrawings();
        MapShape Owned(ulong sender) => store.Shapes.First(shape => store.TryFindOwner(shape, out var owner, out _) && owner == sender);
        string Order() => string.Join(",", store.Shapes.Select(shape => store.TryFindOwner(shape, out var owner, out _) ? owner : 0));
        foreach (var sender in new ulong[] { 30, 10, 20 })
        {
            Expect(store.TryReplace(sender, new[] { Note() }), "accept a teammate");
        }

        ExpectText(Order(), "30,10,20");
        Expect(store.TryReplace(30, new MapShape[] { Stamp(new TextNote(Point(), 0, "changed", Color)) }), "a teammate changes its note");
        ExpectText(Order(), "30,10,20");
        store.Remove(10);
        Expect(store.TryReplace(10, new[] { Note() }), "the teammate re-pairs");
        ExpectText(Order(), "30,20,10");
        Expect(Owned(10) != null, "the returning teammate's drawing shows");
    }
    private static MapPoint Point(uint unit = 0) => new(new Vector2(2, 3), unit);
    private static T Stamp<T>(T shape, int id = 1) where T : MapShape
    {
        shape.Id = id;
        shape.Channel = "a";
        return shape;
    }
    private static TextNote Note(int id = 1) => Stamp(new TextNote(Point(), 0, "note", Color), id);
    private static MapShape[] Notes(int count) => Enumerable.Range(1, count).Select(i => (MapShape)Note(i)).ToArray();
    private static MapShape[] Pen(int points) => new MapShape[] { Stamp(new PenStroke(new Vector2[points], Color)) };

    /// <summary>Shown is whatever the map drew of a teammate's drawings, here half of one teammate's and none of a stranger's.</summary>
    private static void CountOwned()
    {
        var store = new ReceivedDrawings();
        Expect(store.TryReplace(10, Notes(4)) && store.TryReplace(20, Notes(2)), "accept two teammates");
        var shown = new HashSet<MapShape>(store.Shapes.Where(shape => store.TryFindOwner(shape, out var owner, out _) && owner == 10).Take(2));
        Expect(store.CountOwned(10, shown) == (4, 2), $"expected 4 held and 2 shown, got {store.CountOwned(10, shown)}");
        Expect(store.CountOwned(20, shown) == (2, 0), $"expected 2 held and none shown, got {store.CountOwned(20, shown)}");
        Expect(store.CountOwned(30, shown) == (0, 0), "a teammate who sent nothing holds nothing");
    }

    private static void Identity()
    {
        var store = new ReceivedDrawings();
        var a = Note();
        var b = Note();
        Expect(store.TryReplace(10, new[] { a }) && store.TryReplace(20, new[] { b }), "accept two senders");
        Expect(a.Id == -1 && b.Id == -2, "render IDs must be negative and distinct");
        var replacement = Stamp(new TextNote(Point(), 0, "changed", Color));
        Expect(store.TryReplace(10, new[] { replacement }), "replace owner");
        Expect(replacement.Id == -1 && store.Shapes.Count == 2 && b.Id == -2, "keep IDs across replacement");
        Expect(store.Version == 3, "one version per accepted snapshot");
    }

    private static void RemoveOwner()
    {
        var store = new ReceivedDrawings();
        var b = Note();
        Expect(store.TryReplace(10, Notes(2)) && store.TryReplace(20, new[] { b }), "set up owners");
        Expect(store.TryReplace(10, Array.Empty<MapShape>()), "empty snapshot accepted");
        Expect(store.Shapes.Count == 1 && ReferenceEquals(store.Shapes[0], b), "only sender removed");
        var version = store.Version;
        store.Remove(30);
        Expect(store.Version == version, "missing owner is no mutation");
    }

    private static void OwnerLookup()
    {
        var store = new ReceivedDrawings();
        var shape = Note(17);
        Expect(store.TryReplace(10, new[] { shape }), "accept owner drawing");
        Expect(store.TryFindOwner(shape, out var owner, out var sourceId) && owner == 10 && sourceId == 17,
            "render ID is separate from original wire ID");
        var replacement = Stamp(new TextNote(Point(), 0, "changed", Color), 17);
        Expect(store.TryReplace(10, new[] { replacement }), "replace drawing");
        Expect(!store.TryFindOwner(shape, out _, out _) &&
            store.TryFindOwner(replacement, out owner, out sourceId) && owner == 10 && sourceId == 17,
            "stale drawing reference cannot request an edit");
        store.Remove(10);
        Expect(!store.TryFindOwner(replacement, out _, out _), "removed owner cannot be edited");
    }

    private static MapShape[] AllKinds() => new MapShape[]
    {
        Stamp(new BearingRangeShape(Point(1), Point(2), 10, 20, Color), 1),
        Stamp(new CircleShape(Point(3), 100, 30, Color), 2),
        Stamp(new TextNote(Point(4), 40, "note", Color), 3),
        Stamp(new PenStroke(new[] { Vector2.Zero, Vector2.One }, Color), 4),
        Stamp(new WaypointRoute(Color, new RouteWaypoint(Point(5), 50)).With(new RouteWaypoint(Point(6), 60)), 5),
    };

    private static void StableHeartbeat()
    {
        var store = new ReceivedDrawings();
        var original = AllKinds();
        Expect(store.TryReplace(1, original), "accept every shape kind");
        var before = store.Shapes;
        var version = store.Version;
        Expect(store.TryReplace(1, AllKinds()), "accept fresh identical heartbeat instances");
        Expect(ReferenceEquals(before, store.Shapes) && store.Version == version, "unchanged heartbeat must not rebuild the renderer list");
        for (var i = 0; i < original.Length; i++)
        {
            Expect(ReferenceEquals(store.Shapes[i], original[i]), "retain each existing drawing instance");
            Expect(store.TryFindOwner(original[i], out var owner, out var sourceId) && owner == 1 && sourceId == i + 1,
                "preserved instance retains its owner and positive wire ID");
        }
    }

    private static void ChangedContent()
    {
        var moved = new MapPoint(new Vector2(9, 8), 1);
        var changes = new (int Index, MapShape Shape)[]
        {
            (0, new BearingRangeShape(moved, Point(2), 10, 20, Color)),
            (0, new BearingRangeShape(Point(9), Point(2), 10, 20, Color)),
            (0, new BearingRangeShape(Point(1), moved, 10, 20, Color)),
            (0, new BearingRangeShape(Point(1), Point(9), 10, 20, Color)),
            (0, new BearingRangeShape(Point(1), Point(2), 11, 20, Color)),
            (0, new BearingRangeShape(Point(1), Point(2), 10, 21, Color)),
            (1, new CircleShape(new MapPoint(moved.Position, 3), 100, 30, Color)),
            (1, new CircleShape(Point(9), 100, 30, Color)),
            (1, new CircleShape(Point(3), 101, 30, Color)),
            (1, new CircleShape(Point(3), 100, 31, Color)),
            (2, new TextNote(new MapPoint(moved.Position, 4), 40, "note", Color)),
            (2, new TextNote(Point(9), 40, "note", Color)),
            (2, new TextNote(Point(4), 41, "note", Color)),
            (2, new TextNote(Point(4), 40, "changed", Color)),
            (3, new PenStroke(new[] { Vector2.Zero, new Vector2(2, 1) }, Color)),
            (3, new PenStroke(new[] { Vector2.One, Vector2.Zero }, Color)),
            (3, new PenStroke(new[] { Vector2.Zero }, Color)),
            (4, new WaypointRoute(Color, new RouteWaypoint(new MapPoint(moved.Position, 5), 50)).With(new RouteWaypoint(Point(6), 60))),
            (4, new WaypointRoute(Color, new RouteWaypoint(Point(9), 50)).With(new RouteWaypoint(Point(6), 60))),
            (4, new WaypointRoute(Color, new RouteWaypoint(Point(5), 51)).With(new RouteWaypoint(Point(6), 60))),
            (4, new WaypointRoute(Color, new RouteWaypoint(Point(5), 50)).With(new RouteWaypoint(new MapPoint(moved.Position, 6), 60))),
            (4, new WaypointRoute(Color, new RouteWaypoint(Point(5), 50)).With(new RouteWaypoint(Point(9), 60))),
            (4, new WaypointRoute(Color, new RouteWaypoint(Point(5), 50)).With(new RouteWaypoint(Point(6), 61))),
            (4, new WaypointRoute(Color, new RouteWaypoint(Point(6), 60)).With(new RouteWaypoint(Point(5), 50))),
            (4, new WaypointRoute(Color, new RouteWaypoint(Point(5), 50))),
            (0, new TextNote(Point(1), 10, "different kind", Color)),
        };
        foreach (var (index, shape) in changes) ExpectChanged(index, Stamp(shape, index + 1));
        for (var i = 0; i < 5; i++)
        {
            var changed = AllKinds()[i];
            changed.Channel = "b";
            ExpectChanged(i, changed);
        }
        foreach (var color in new[] { new ShapeColor(9, 2, 3), new ShapeColor(1, 9, 3), new ShapeColor(1, 2, 9), new ShapeColor(1, 2, 3, 9) })
            ExpectChanged(2, Stamp(new TextNote(Point(4), 40, "note", color), 3));
    }

    private static void ExpectChanged(int index, MapShape changed)
    {
        var store = new ReceivedDrawings();
        var original = AllKinds();
        Expect(store.TryReplace(1, original), "accept original shapes");
        var version = store.Version;
        var replacement = AllKinds();
        replacement[index] = changed;
        Expect(store.ChangeCost(1, replacement) == 1, "each changed field costs exactly one replacement");
        Expect(store.TryReplace(1, replacement), "accept changed field");
        Expect(store.Version == version + 1 && ReferenceEquals(store.Shapes[index], changed), "changed field replaces reference and increments version once");
        Expect(changed.Id == original[index].Id && !store.TryFindOwner(original[index], out _, out _), "retain render ID but reject stale edit reference");
        for (var i = 0; i < original.Length; i++)
            if (i != index) Expect(ReferenceEquals(store.Shapes[i], original[i]), "unchanged siblings retain their references");
    }

    private static void ChangedOrder()
    {
        var store = new ReceivedDrawings();
        var original = AllKinds();
        Expect(store.TryReplace(1, original), "accept ordered shapes");
        var version = store.Version;
        Expect(store.TryReplace(1, Enumerable.Reverse(AllKinds()).ToArray()), "accept reordered shapes");
        Expect(store.Version == version + 1, "draw order change invalidates renderer");
        for (var i = 0; i < original.Length; i++)
            Expect(ReferenceEquals(store.Shapes[i], original[original.Length - 1 - i]), "reorder retained instances to match the wire order");
        var before = store.Shapes;
        Expect(store.TryReplace(1, Enumerable.Reverse(AllKinds()).ToArray()), "accept reordered heartbeat");
        Expect(ReferenceEquals(before, store.Shapes) && store.Version == version + 1, "repeated new order is stable");
    }

    private static void RemovedReferences()
    {
        foreach (var clear in new[] { false, true })
        {
            var store = new ReceivedDrawings();
            var old = Note();
            Expect(store.TryReplace(1, new[] { old }), "accept original drawing");
            if (clear) store.Clear(); else store.Remove(1);
            var fresh = Note();
            Expect(store.TryReplace(1, new[] { fresh }), "accept re-added owner");
            Expect(ReferenceEquals(store.Shapes[0], fresh) && !store.TryFindOwner(old, out _, out _), "removed owner never preserves its stale reference");
        }
        var partial = new ReceivedDrawings();
        var original = AllKinds();
        Expect(partial.TryReplace(1, original) && partial.TryReplace(1, AllKinds().Skip(1).ToArray()), "remove one drawing from a live owner");
        var readded = AllKinds();
        Expect(partial.TryReplace(1, readded), "re-add removed drawing");
        Expect(ReferenceEquals(partial.Shapes[0], readded[0]) && !partial.TryFindOwner(original[0], out _, out _), "removed source ID never preserves its stale reference");
    }

    private static void PeerBudget()
    {
        Expect(ReceivedDrawings.MaxPeers == 32, "fixed peer budget");
        var store = new ReceivedDrawings();
        for (ulong i = 1; i <= 32; i++) Expect(store.TryReplace(i, Notes(1)), "accept peer within budget");
        Expect(!store.TryReplace(33, Notes(1)) && store.Shapes.Count == 32, "reject thirty-third peer");
        Expect(store.TryReplace(1, Notes(2)), "existing owner can replace at peer cap");
        store.Remove(1);
        Expect(store.TryReplace(33, Notes(1)), "departed owner frees peer capacity");
    }

    private static void ShapeBudget()
    {
        Expect(ReceivedDrawings.MaxShapes == 400, "fixed shape pool");
        var store = new ReceivedDrawings();
        for (ulong i = 1; i <= 4; i++) Expect(store.TryReplace(i, Notes(100)), "four full teammates are accepted");
        Expect(store.Shapes.Count == 400, "up to four teammates show everything");
        Expect(store.TryReplace(5, Notes(1)), "a fifth teammate is accepted, not refused for a full pool");
        Expect(store.Shapes.Count == 4 * 80 + 1, "each teammate shows its oldest drawings up to an equal share");
        Expect(store.TryReplace(6, Notes(100)), "a sixth teammate joins while drawings are hidden");
        store.Remove(5);
        store.Remove(4);
        Expect(store.Shapes.Count == 400, "departures give the others their drawings back");
        Expect(store.Shapes.Select(shape => shape.Id).Distinct().Count() == 400, "revealed drawings never share an ID");
    }

    private static void GeometryBudget()
    {
        Expect(ReceivedDrawings.MaxGeometryPoints == 10000, "fixed point pool");
        var store = new ReceivedDrawings();
        for (ulong i = 1; i <= 4; i++) Expect(store.TryReplace(i, Pen(2500)), "four full pen budgets are accepted");
        Expect(store.Shapes.Count == 4, "10,000 points fit the pool");
        Expect(store.TryReplace(5, Notes(1)) && store.Shapes.Count == 1,
            "past a teammate's point share a whole stroke is hidden, never cut");
    }

    private static void CapacityReason()
    {
        var store = new ReceivedDrawings();
        for (ulong i = 1; i <= 32; i++) Expect(store.TryReplace(i, Notes(1)), "fill the teammate slots");
        Expect(!store.TryReplace(33, Notes(1), out var full) && full, "a thirty-third teammate cannot fit");
        var invalid = Note(); invalid.Channel = "bad!";
        Expect(!store.TryReplace(2, new[] { invalid }, out full) && !full, "invalid peer data is a separate failure");
        store.Remove(1);
        Expect(store.TryReplace(33, Notes(1), out full) && !full, "peer can retry after a slot frees");
    }

    private static void SnapshotBudget()
    {
        Expect(DrawingProtocol.MaxShapes == 100 && DrawingProtocol.MaxGeometryPoints == 3000, "fixed packet budgets");
        var store = new ReceivedDrawings();
        Expect(!store.TryReplace(1, Notes(101)) && !store.TryReplace(1, Pen(3001)), "reject oversized owner snapshot");
        Expect(store.Version == 0 && store.Shapes.Count == 0, "no partial initial snapshot");
        Expect(store.TryReplace(1, Notes(100)) && store.TryReplace(2, Pen(3000)), "accept exact packet budgets");
    }

    private static void InvalidSnapshot()
    {
        var store = new ReceivedDrawings();
        Expect(store.TryReplace(1, Notes(1)), "set up accepted snapshot");
        var before = store.Shapes;
        var version = store.Version;
        Expect(!store.TryReplace(0, Notes(1)), "zero sender rejected");
        foreach (var id in new[] { 0, -1 }) Expect(!store.TryReplace(1, new[] { Note(id) }), "nonpositive wire ID rejected");
        Expect(!store.TryReplace(1, new[] { Note(), Note() }), "duplicate wire IDs rejected");
        foreach (var channel in new[] { "", "A", " a", "a,b", "a!" })
        {
            var shape = Note();
            shape.Channel = channel;
            Expect(!store.TryReplace(1, new[] { shape }) && shape.Id == 1, "invalid channel rejected before ID mutation");
        }
        var good = Note();
        Expect(!store.TryReplace(1, new MapShape[] { good, Stamp(new UnknownShape(), 2) }) && good.Id == 1, "unknown kind rejects whole snapshot");
        Expect(ReferenceEquals(before, store.Shapes) && version == store.Version, "invalid snapshots leave state unchanged");
    }

    private static void IdLifecycle()
    {
        var store = new ReceivedDrawings();
        Expect(store.TryReplace(1, Notes(1)) && store.TryReplace(2, Notes(1)), "set up owners");
        store.Remove(1);
        var reused = Note();
        Expect(store.TryReplace(3, new[] { reused }) && reused.Id == -1, "removed owner IDs can be reused");
        Expect(store.Shapes.Select(s => s.Id).Distinct().Count() == 2, "live identities remain distinct");
        store.Clear();
        var version = store.Version;
        store.Clear();
        Expect(store.Shapes.Count == 0 && store.Version == version, "empty clear is no mutation");
        var fresh = Note();
        Expect(store.TryReplace(1, new[] { fresh }) && fresh.Id == -1, "clear releases all identity mappings");
    }

    private static void Visibility()
    {
        var shapes = new MapShape[]
        {
            new BearingRangeShape(Point(1), Point(2), 0, 0, Color),
            new CircleShape(Point(2), 100, 0, Color),
            new TextNote(Point(2), 0, "note", Color),
            new WaypointRoute(Color, new RouteWaypoint(Point(1), 0)).With(new RouteWaypoint(Point(2), 0)),
        };
        foreach (var shape in shapes)
        {
            Expect(!SharedShapeVisibility.HasKnownAnchors(shape, id => id == 1), "one unknown anchor hides whole shape");
            Expect(SharedShapeVisibility.HasKnownAnchors(shape, id => id is 1 or 2), "every anchor passes recipient visibility");
        }
        Expect(!SharedShapeVisibility.HasKnownAnchors(new UnknownShape(), _ => true), "unknown shape kind hidden");
    }

    private static void FixedVisibility()
    {
        var shapes = new MapShape[]
        {
            new BearingRangeShape(Point(), Point(), 0, 0, Color),
            new CircleShape(Point(), 100, 0, Color),
            Note(), new PenStroke(new Vector2[2], Color),
            new WaypointRoute(Color, new RouteWaypoint(Point(), 0)),
        };
        foreach (var shape in shapes)
            Expect(SharedShapeVisibility.HasKnownAnchors(shape, _ => throw new Exception("fixed point lookup")), "fixed geometry visible");
    }

    private static void ShareableSnapshot()
    {
        var shapes = new MapShape[]
        {
            Stamp(new BearingRangeShape(Point(1), Point(2), 100, 200, Color), 1),
            Stamp(new BearingRangeShape(Point(1), Point(), 100, 200, Color), 2),
            Stamp(new BearingRangeShape(Point(), Point(2), 100, 200, Color), 3),
            Stamp(new BearingRangeShape(Point(), Point(), 100, 200, Color), 4),
            Stamp(new CircleShape(Point(1), 100, 0, Color), 5),
            Stamp(new TextNote(Point(1), 0, "note", Color), 6),
            Stamp(new WaypointRoute(Color, new RouteWaypoint(Point(), 0)).With(new RouteWaypoint(Point(1), 0)), 7),
            Stamp(new CircleShape(Point(), 100, 0, Color), 8),
            Note(9), Stamp(new PenStroke(new[] { Vector2.One }, Color), 10),
            Stamp(new WaypointRoute(Color, new RouteWaypoint(Point(), 0)), 11),
            Stamp(new UnknownShape(), 12)
        };
        var selected = shapes.Where(SharedShapeVisibility.CanShare).ToArray();
        Expect(selected.Select(shape => shape.Id).SequenceEqual(Enumerable.Range(1, 11)),
            "every drawing kind shares, unit-linked or fixed; unknown kinds stay local");
        var packet = new DrawingPacket(DrawingPacketKind.Snapshot, new SharedMissionEpoch(37, 1234.5, 7),
            Guid.NewGuid(), Guid.NewGuid(), 1, Array.Empty<string>(), selected);
        Expect(DrawingProtocol.TryEncode(packet, out var bytes), "eligible snapshot encodes");
        Expect(DrawingProtocol.TryDecode(bytes, out var decoded) && decoded != null, "eligible snapshot decodes");
        var received = new ReceivedDrawings();
        Expect(received.TryReplace(10, decoded!.Shapes) && received.Shapes.Count == 11,
            "recipient accepts every eligible drawing together");
        Expect(received.Shapes.All(shape => shape.Id < 0), "shared drawings retain remote identities");
        Expect(received.TryFindOwner(received.Shapes[0], out var owner, out var sourceId) && owner == 10 && sourceId == 1,
            "unit BRA retains its original erase identity");
    }

    /// <summary>
    /// The decoder hands back a session's unchanged drawings as the objects its last snapshot made. The store renumbers
    /// the shapes it keeps, so a kept one must not come back in the next snapshot.
    /// </summary>
    private static void KeptDecodedShapes()
    {
        Expect(DrawingProtocol.TryEncode(new DrawingPacket(DrawingPacketKind.Snapshot, new SharedMissionEpoch(37, 1234.5, 7),
            Guid.NewGuid(), Guid.NewGuid(), 1, Array.Empty<string>(), new MapShape[] { Note(1), Stamp(new CircleShape(Point(), 100, 0, Color), 2) }),
            out var bytes), "snapshot encodes");
        MapShape[] Decoded()
        {
            Expect(DrawingProtocol.TryDecode(bytes, out var packet) && packet != null, "snapshot decodes");
            return packet!.Shapes;
        }

        var store = new ReceivedDrawings();
        var first = Decoded();
        Expect(store.TryReplace(10, first), "first snapshot applies");
        var second = Decoded();
        Expect(second.All(shape => shape.Id > 0) && !second.Intersect(first).Any(), "kept drawings are not decoded again");
        var version = store.Version;
        Expect(store.TryReplace(10, second) && store.Shapes.SequenceEqual(first) && store.Version == version,
            "an unchanged snapshot keeps the drawings already shown");
        var third = Decoded();
        Expect(third.SequenceEqual(second), "drawings the store didn't keep are reused");
        Expect(store.TryReplace(10, third) && store.Shapes.SequenceEqual(first) && store.Version == version,
            "a reused snapshot keeps the drawings already shown");
    }

    private static BearingRangeShape ReceiveBearing(MapPoint? from = null, MapPoint? to = null)
    {
        var bearing = Stamp(new BearingRangeShape(from ?? new MapPoint(new Vector2(-900000, 800000), 1),
            to ?? new MapPoint(new Vector2(700000, -600000), 2), 90000, -90000, Color), 17);
        var received = new ReceivedDrawings();
        Expect(received.TryReplace(10, new[] { bearing }), "receive unit BRA");
        Expect(bearing.Id < 0, "recipient assigns remote rendering identity");
        return bearing;
    }

    private static void ReceiverBearing()
    {
        var context = new BearingContext();
        var bearing = ReceiveBearing();
        bearing.Draw(context);
        bearing.AddWorldLabel(context, context);
        Expect(context.Arrows.Single() == (new Vector2(1000, 2000), new Vector2(1000, 6000)),
            "map endpoints come from recipient tracks");
        ExpectText(context.MapLabels.Single(), "000° 5.0km 3400 m");
        Expect(context.WorldLabels.Single() == (new Vector3(1000, 3400, 6000), "000° 5.0km 3400 m"),
            "HUD location, straight-line range and altitude come from recipient tracks");
        context.Positions[2] = new Vector3(5000, 400, 2000);
        context.ClearDraws();
        bearing.Draw(context);
        bearing.AddWorldLabel(context, context);
        ExpectText(context.MapLabels.Single(), "090° 4.0km 400 m");
        Expect(context.WorldLabels.Single().Position == context.Positions[2], "shared BRA follows recipient track movement");
    }

    private static void ReceiverAnchoredShapes()
    {
        var context = new BearingContext();
        var circle = Stamp(new CircleShape(new MapPoint(new Vector2(-900000, 800000), 1), 500, 0, Color), 1);
        var note = Stamp(new TextNote(new MapPoint(new Vector2(700000, -600000), 2), 0, "bandit", Color), 2);
        Expect(new ReceivedDrawings().TryReplace(10, new MapShape[] { circle, note }), "receive unit circle and note");
        circle.Draw(context);
        circle.AddWorldRing(context, context);
        note.AddWorldLabel(context, context);
        Expect(context.Circles.Single() == new Vector2(1000, 2000) && context.Rings.Single() == context.Positions[1],
            "unit circle centres on the recipient's track on the map and in the HUD");
        Expect(context.WorldLabels.Single().Position == context.Positions[2], "unit note sits on the recipient's track in the HUD");
    }

    private static void LostBearing()
    {
        foreach (var (from, to, lostUnit) in new[]
        {
            (Point(1), Point(2), 1u), (Point(1), Point(2), 2u),
            (Point(1), Point(), 1u), (Point(), Point(2), 2u)
        })
        {
            var context = new BearingContext();
            var bearing = ReceiveBearing(from, to);
            var hit = new ShapeHitTest(context);
            bool Known(uint id) => context.HaveHeadquarters && context.Positions.ContainsKey(id);
            context.TryResolveWorld(from, out var start);
            context.TryResolveWorld(to, out var end);
            var click = new Vector2((start.X + end.X) / 2, (start.Z + end.Z) / 2);
            Expect(ReferenceEquals(hit.Find(new[] { bearing }, click, 8), bearing), "live shared BRA is selectable");
            context.Live.Remove(lostUnit);
            context.ClearDraws();
            bearing.Draw(context);
            bearing.AddWorldLabel(context, context);
            Expect(context.Arrows.Count == 1 && context.MapLabels.Count == 1 && context.MapLabels[0].EndsWith(MeasureLabel.LostLine) &&
                context.WorldLabels.Count == 1 && context.WorldLabels[0].Text.EndsWith(MeasureLabel.LostLine) &&
                ReferenceEquals(hit.Find(new[] { bearing }, click, 8), bearing),
                "a stale endpoint leaves the arrow where its side last knew it, reading lost on the map and the HUD");
            Expect(SharedShapeVisibility.HasKnownAnchors(bearing, Known), "the recipient list keeps a BRA on a unit its side has known");
            context.Positions.Remove(lostUnit);
            Expect(!SharedShapeVisibility.HasKnownAnchors(bearing, Known), "a unit the recipient's side never knew hides the BRA");
            context.Positions[lostUnit] = lostUnit == 1 ? new Vector3(1000, 400, 2000) : new Vector3(1000, 3400, 6000);
            context.Live.Add(lostUnit);
            context.ClearDraws();
            bearing.Draw(context);
            bearing.AddWorldLabel(context, context);
            Expect(context.Arrows.Count == 1 && context.MapLabels.Count == 1 && !context.MapLabels[0].EndsWith(MeasureLabel.LostLine) &&
                context.WorldLabels.Count == 1 && ReferenceEquals(hit.Find(new[] { bearing }, click, 8), bearing),
                "a reacquired track drops the lost line");
            context.HaveHeadquarters = false;
            Expect(!SharedShapeVisibility.HasKnownAnchors(bearing, Known), "without a faction nothing is known, so nothing shows");
        }
    }

    private static void BearingErase()
    {
        var context = new BearingContext();
        var bearing = ReceiveBearing();
        IReadOnlyList<string> localBroadcast = new[] { "a" };
        IReadOnlyList<string> remoteBroadcast = new[] { "a" };
        var requests = 0;
        var eraser = new EraserTool(context, receivedShapes: () => new[] { bearing },
            canEraseReceived: shape => SharedShapeVisibility.CanShare(shape) &&
                ChannelRouting.CanCollaborate(shape.Channel, localBroadcast, remoteBroadcast),
            requestEraseReceived: shape => { Expect(ReferenceEquals(shape, bearing), "request current remote BRA"); requests++; return true; });
        Expect(eraser.TryErase(bearing) && requests == 1, "shared broadcast BRA accepts collaborative erase");
        Expect(context.Shapes.Shapes.Count == 0 && !context.Shapes.CanUndo && bearing.Id < 0,
            "erase request leaves remote BRA and local undo history intact pending owner confirmation");
        localBroadcast = Array.Empty<string>();
        Expect(!eraser.TryErase(bearing) && requests == 1, "local receive-only permission cannot erase");
        localBroadcast = new[] { "a" };
        remoteBroadcast = Array.Empty<string>();
        Expect(!eraser.TryErase(bearing) && requests == 1, "owner receive-only permission cannot erase");
        remoteBroadcast = new[] { "a" };
        Expect(eraser.TryErase(bearing) && requests == 2, "restoring both broadcast permissions restores erase");
    }

    private sealed class BearingContext : IMapToolContext, IMapCanvas, IWorldLabels
    {
        public Dictionary<uint, Vector3> Positions { get; } = new()
        {
            [1] = new Vector3(1000, 400, 2000), [2] = new Vector3(1000, 3400, 6000)
        };
        public HashSet<uint> Live { get; } = new() { 1, 2 };
        public bool HaveHeadquarters { get; set; } = true;
        public List<(Vector2 From, Vector2 To)> Arrows { get; } = new();
        public List<string> MapLabels { get; } = new();
        public List<(Vector3 Position, string Text)> WorldLabels { get; } = new();
        public List<Vector2> Circles { get; } = new();
        public List<Vector3> Rings { get; } = new();
        public ShapeStore Shapes { get; } = new();
        public ShapeColor Color => ReceivedDrawingsTests.Color;
        public float LineWidth => 1f;
        public DistanceUnit Units => DistanceUnit.Kilometres;
        public float MetersPerIconUnit => 1f;
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
            if (!point.IsAnchored) { position = new Vector3(point.Position.X, 0, point.Position.Y); return true; }
            position = Positions.TryGetValue(point.UnitId, out var known) ? known : new Vector3(point.Position.X, 0, point.Position.Y);
            return HaveHeadquarters && Live.Contains(point.UnitId);
        }
        public void ClearDraws() { Arrows.Clear(); MapLabels.Clear(); WorldLabels.Clear(); }
        public float GroundElevation(Vector2 position) => 0;
        public bool IsUnitGone(uint unitId) => false;
        public void Arrow(Vector2 from, Vector2 to, ShapeColor color) => Arrows.Add((from, to));
        public void Label(LabelAnchor anchor, string text, ShapeColor color) => MapLabels.Add(text);
        public void Add(Vector3 position, string text, ShapeColor color) => WorldLabels.Add((position, text));
        public void Line(Vector2 from, Vector2 to, ShapeColor color) { }
        public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color) { }
        public void Circle(Vector2 center, float radius, ShapeColor color) => Circles.Add(center);
        public void Marker(Vector2 position, ShapeColor color) { }
        public void Ring(Vector3 center, float radius, ShapeColor color, object owner) => Rings.Add(center);
    }

    private sealed class UnknownShape : MapShape
    {
        public UnknownShape() : base(ReceivedDrawingsTests.Color) { }
        public override void Draw(IMapCanvas canvas) { }
    }
}
