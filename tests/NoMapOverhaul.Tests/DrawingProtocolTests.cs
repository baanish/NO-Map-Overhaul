using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.BearingRange;
using NoMapOverhaul.MapTools.Circle;
using NoMapOverhaul.MapTools.Pen;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.MapTools.Waypoint;
using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingProtocolTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("drawing protocol preserves shapes but redacts anchored fallback geometry", RoundTrip),
        ("drawing notes reject separators and bidi controls on send and receive", NoteLayoutControls),
        ("drawing protocol puts no anchored fallback coordinates on the wire", AnchoredWire),
        ("shared BRA wire keeps unit IDs and fixed geometry while rejecting sender track data", BearingWire),
        ("drawing protocol shares single-click pen dots and rejects empty strokes", PenDots),
        ("drawing protocol preserves handshake subscriptions and sessions", Handshakes),
        ("drawing protocol rejects other versions on every packet kind and tells them from malformed data", PreviousVersion),
        ("native drawing header reads only the fixed prefix before version filtering", NativeHeader),
        ("drawing protocol fits maximum canonical control frames", ControlPacketBounds),
        ("drawing protocol bounds and validates erase requests", DeleteRequests),
        ("drawing protocol rejects every truncated prefix and trailing bytes", Truncation),
        ("drawing protocol contains mutated peer frames without partial state", MutatedFrames),
        ("drawing protocol rejects malformed headers counts types and UTF8", Malformed),
        ("drawing protocol enforces finite coordinate elevation and radius bounds", Geometry),
        ("drawing protocol enforces canonical channels and plain bounded notes", TextPolicy),
        ("drawing protocol rejects format-only notes and preserves visible Unicode", NoteVisibility),
        ("drawing protocol caps total geometry including fixed anchors", GeometryBudget),
        ("drawing protocol caps shapes and rejects duplicate ids", ShapeBudget),
        ("reused encode and receive buffers keep each packet's bytes apart", ReusedBuffers),
        ("a readdressed snapshot is byte for byte the one encoded for its new teammate", Readdressed),
        ("an unchanged record decodes to the object its session's last snapshot made", ReusedRecords),
        ("records reused from a session's last snapshot never change whether a frame decodes", ReusedRecordValidation),
    };

    private static DrawingPacket Snapshot(Guid recipient, params MapShape[] shapes) => new(DrawingPacketKind.Snapshot,
        new SharedMissionEpoch(37, 1234.5, 7), Sender, recipient, 91, Array.Empty<string>(), shapes);

    private static void ReusedRecords()
    {
        var receiver = CacheReceiver();
        var session = receiver.LocalSession;
        var circle = Stamp(new CircleShape(new MapPoint(new Vector2(1, 2), 0), 900f, 3f, Color), 2);
        var pen = Stamp(new PenStroke(new[] { Vector2.Zero, Vector2.One }, Color), 3);
        var first = PrimeCache(receiver, Encode(Snapshot(session, Note("before"), circle, pen)), 1).Shapes;
        var again = Decode(Encode(Snapshot(session, Note("after"), circle, pen))).Shapes;
        Expect(again[0] is TextNote { Text: "after" } && !ReferenceEquals(first[0], again[0]), "a changed drawing is built afresh");
        Expect(ReferenceEquals(first[1], again[1]) && ReferenceEquals(first[2], again[2]), "unchanged drawings are the same objects");
        var other = Decode(Encode(Snapshot(Guid.NewGuid(), Note("after"), circle, pen))).Shapes;
        Expect(other.Zip(again).All(pair => !ReferenceEquals(pair.First, pair.Second)), "another session's snapshot shares none of them");
    }

    /// <summary>
    /// A record identical to one in the session's last snapshot isn't decoded again, so each damaged frame must decode,
    /// or be rejected, exactly as it is for a session with nothing to reuse.
    /// </summary>
    private static void ReusedRecordValidation()
    {
        const int records = 60; // the header and the shape count
        var receiver = CacheReceiver();
        var session = receiver.LocalSession;
        var route = new WaypointRoute(Color, new RouteWaypoint(new MapPoint(new Vector2(21, 22), 0), 23))
            .With(new RouteWaypoint(new MapPoint(Vector2.Zero, 9), 0));
        var source = Encode(Snapshot(session, Note(), Stamp(new CircleShape(new MapPoint(new Vector2(1, 2), 0), 900f, 3f, Color), 2),
            Stamp(new PenStroke(new[] { Vector2.Zero, Vector2.One, new Vector2(5, 6) }, Color), 3), Stamp(route, 4),
            Stamp(new BearingRangeShape(new MapPoint(Vector2.Zero, 5), new MapPoint(new Vector2(7, 8), 0), 0, 9, Color), 5)));
        var primed = PrimeCache(receiver, source, 1).Shapes;
        for (var i = 0; i < source.Length; i++) Reject(source.Take(i).ToArray());
        Expect(Decode(source).Shapes.SequenceEqual(primed), "rejected frames leave the session's records as they were");

        var random = new Random(611);
        var accepted = 0;
        for (var frame = 0; frame < 2048; frame++)
        {
            Decode(source);
            var bytes = (byte[])source.Clone();
            var changes = random.Next(1, 5);
            for (var i = 0; i < changes; i++) bytes[random.Next(records, bytes.Length)] = (byte)random.Next(256);
            var elsewhere = (byte[])bytes.Clone();
            DrawingProtocol.Readdress(elsewhere, Sender, Guid.NewGuid(), 91);
            var reused = DrawingProtocol.TryDecode(bytes, out var packet);
            Expect(reused == DrawingProtocol.TryDecode(elsewhere, out var fresh), "reuse never changes whether a frame decodes");
            if (!reused) continue;
            accepted++;
            Expect(Encode(Snapshot(session, packet!.Shapes)).SequenceEqual(Encode(Snapshot(session, fresh!.Shapes))),
                "reuse never changes what a frame decodes to");
        }
        Expect(accepted > 100, "enough damaged frames decode to compare them");

        // Records from separate snapshots spliced into one frame, which the encoder would refuse to write.
        byte[] Spliced(params byte[][] snapshots)
        {
            var frame = snapshots[0].Take(records).Concat(snapshots.SelectMany(snapshot => snapshot.Skip(records))).ToArray();
            BitConverter.GetBytes((ushort)snapshots.Length).CopyTo(frame, records - 2);
            return frame;
        }
        byte[] Stroke(int id, int points) => Encode(Snapshot(session, Stamp(new PenStroke(Enumerable.Repeat(Vector2.One, points).ToArray(), Color), id)));
        var big = Stroke(1, 2000);
        var kept = PrimeCache(receiver, big, 2).Shapes[0];
        Reject(Spliced(Stroke(2, 1001), big));
        Reject(Spliced(big, big));
        var full = Decode(Spliced(Stroke(2, 1000), big)).Shapes;
        Expect(ReferenceEquals(full[1], kept), "the spliced frame reused the stroke, so the rejections above came from the reused record");
    }

    private static DrawingPeer CacheReceiver()
    {
        var receiver = new DrawingPeer(new SharedMissionEpoch(37, 1234.5, 7), Array.Empty<string>());
        Expect(receiver.Receive(new DrawingPacket(DrawingPacketKind.Acknowledgement, new SharedMissionEpoch(37, 1234.5, 7),
            Sender, receiver.LocalSession, 0, Array.Empty<string>(), Array.Empty<MapShape>()), out _, out _),
            "cache fixture completes a compatible handshake");
        return receiver;
    }

    private static DrawingPacket PrimeCache(DrawingPeer receiver, byte[] source, uint sequence)
    {
        var bytes = (byte[])source.Clone();
        DrawingProtocol.Readdress(bytes, Sender, receiver.LocalSession, sequence);
        var packet = Decode(bytes);
        Expect(receiver.Receive(packet, out _, out _), "only an accepted snapshot primes the record cache");
        return packet;
    }

    /// <summary>One encoding serves every teammate on the same channels, so readdressing must equal encoding afresh.</summary>
    private static void Readdressed()
    {
        var shapes = new MapShape[] { Note(), Stamp(new CircleShape(new MapPoint(new Vector2(1, 2), 0), 900f, 3f, Color), 2) };
        var bytes = Encode(Packet(shapes));
        var other = new Guid("11111111-2222-3333-4444-555555555555");
        var otherRecipient = new Guid("66666666-7777-8888-9999-000000000000");
        DrawingProtocol.Readdress(bytes, other, otherRecipient, 4_000_000_000);
        var fresh = Encode(new DrawingPacket(DrawingPacketKind.Snapshot, new SharedMissionEpoch(37, 1234.5, 7), other, otherRecipient,
            4_000_000_000, Array.Empty<string>(), shapes));
        Expect(bytes.SequenceEqual(fresh), "readdressing changes exactly the sessions and the sequence");
        var packet = Decode(bytes);
        Expect(packet.SenderSession == other && packet.RecipientSession == otherRecipient && packet.Sequence == 4_000_000_000,
            "the receiver reads the new address");
    }

    /// <summary>The encoder writes into one buffer and the receiver decodes from one: neither may leak bytes between packets.</summary>
    private static void ReusedBuffers()
    {
        var long_ = Encode(Packet(Note("a longer note than the next one")));
        var copy = (byte[])long_.Clone();
        var short_ = Encode(Packet(Note("short")));
        Expect(long_.SequenceEqual(copy) && short_.Length < long_.Length, "a later encode leaves earlier bytes as they were");

        var scratch = new byte[DrawingProtocol.MaxPacketBytes];
        Array.Copy(long_, scratch, long_.Length);
        Array.Copy(short_, scratch, short_.Length); // the longer packet's tail stays behind the shorter one
        Expect(DrawingProtocol.TryDecode(scratch, short_.Length, out var packet) && packet!.Shapes[0] is TextNote { Text: "short" },
            "a buffer decodes only the length it was given");
        Expect(!DrawingProtocol.TryDecode(scratch, short_.Length + 1, out _), "one stale byte past the packet is rejected");
        Expect(!DrawingProtocol.TryDecode(scratch, scratch.Length + 1, out _) && !DrawingProtocol.TryDecode(scratch, -1, out _),
            "a length outside the buffer is rejected");
    }

    private static readonly ShapeColor Color = new(1, 2, 3, 4);
    private static void NativeHeader()
    {
        var prefix = Marshal.AllocHGlobal(6);
        try
        {
            Marshal.WriteInt32(prefix, unchecked((int)DrawingProtocol.Magic));
            Marshal.WriteByte(prefix, 4, DrawingProtocol.Version);
            foreach (var kind in Enum.GetValues<DrawingPacketKind>())
            {
                Marshal.WriteByte(prefix, 5, (byte)kind);
                Expect(DrawingProtocol.TryReadHeader(prefix, DrawingProtocol.MaxPacketBytes, out var version, out var readKind) &&
                    version == 6 && readKind == kind, "a declared full frame needs only six native bytes for version and kind");
                Marshal.WriteByte(prefix, 4, 5);
                Expect(DrawingProtocol.TryReadHeader(prefix, DrawingProtocol.MaxPacketBytes, out version, out readKind) &&
                    version == 5 && readKind == kind, "another version is recognized before allocating or copying a payload");
                Marshal.WriteByte(prefix, 4, DrawingProtocol.Version);
            }
            for (var size = 0; size < 6; size++)
                Expect(!DrawingProtocol.TryReadHeader(prefix, size, out _, out _), "truncated prefix is not read");
            Expect(!DrawingProtocol.TryReadHeader(IntPtr.Zero, 6, out _, out _), "null native pointer is not read");
            Marshal.WriteInt32(prefix, 0);
            Expect(!DrawingProtocol.TryReadHeader(prefix, 6, out _, out _), "wrong magic is rejected before copying");
        }
        finally { Marshal.FreeHGlobal(prefix); }
    }

    private static readonly Guid Sender = new("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly Guid Recipient = new("fedcba98-7654-3210-fedc-ba9876543210");
    private static DrawingPacket Packet(params MapShape[] shapes) => new(DrawingPacketKind.Snapshot,
        new SharedMissionEpoch(37, 1234.5, 7), Sender, Recipient, 91, Array.Empty<string>(), shapes);

    private static T Stamp<T>(T shape, int id = 1) where T : MapShape { shape.Id = id; shape.Channel = "a"; return shape; }
    private static TextNote Note(string text = "Target α") => Stamp(new TextNote(new MapPoint(new Vector2(4, -5), 42), 6, text, Color));
    private static byte[] Encode(DrawingPacket packet)
    {
        Expect(DrawingProtocol.TryEncode(packet, out var bytes), "encoding valid packet"); return bytes;
    }
    private static DrawingPacket Decode(byte[] bytes)
    {
        Expect(DrawingProtocol.TryDecode(bytes, out var packet) && packet != null, "decoding valid packet"); return packet!;
    }
    private static void Reject(byte[] bytes)
    {
        Expect(!DrawingProtocol.TryDecode(bytes, out var packet) && packet == null, "invalid packet must not expose partial state");
    }
    private static void Reject(DrawingPacket packet) => Expect(!DrawingProtocol.TryEncode(packet, out var bytes) && bytes.Length == 0, "invalid encode");
    private static byte[] Replace(byte[] bytes, int offset, byte[] value)
    {
        var copy = (byte[])bytes.Clone(); Array.Copy(value, 0, copy, offset, value.Length); return copy;
    }
    private static void Point(MapPoint actual, Vector2 position, uint unitId) =>
        Expect(actual.Position == position && actual.UnitId == unitId, "point identity and wire position");

    private static void RoundTrip()
    {
        var route = new WaypointRoute(Color, new RouteWaypoint(new MapPoint(new Vector2(21, 22), 333), 23))
            .With(new RouteWaypoint(new MapPoint(new Vector2(-24, 25)), -26));
        var original = Packet(
            Stamp(new BearingRangeShape(new MapPoint(new Vector2(-10, 11), 71), new MapPoint(new Vector2(12, -13), 72), -14, 15, Color), 5),
            Stamp(new CircleShape(new MapPoint(new Vector2(16, -17), 73), 1800, 19, Color), 2),
            Stamp(new PenStroke(new[] { new Vector2(-1, 2), new Vector2(3, -4), new Vector2(5, 6) }, Color), 3),
            Note(), Stamp(route, 4));
        var copy = Decode(Encode(original));
        Expect(copy.Epoch.Equals(original.Epoch) && copy.SenderSession == Sender && copy.RecipientSession == Recipient && copy.Sequence == 91, "header preserved");
        Expect(copy.Shapes.Length == 5, "all five kinds");
        for (var i = 0; i < 5; i++) Expect(copy.Shapes[i].Id == original.Shapes[i].Id && copy.Shapes[i].Channel == "a" && copy.Shapes[i].Color == Color, "shape metadata preserved");
        var bearing = (BearingRangeShape)copy.Shapes[0];
        Point(bearing.From, Vector2.Zero, 71); Point(bearing.To, Vector2.Zero, 72);
        Expect(bearing.FromElevation == 0 && bearing.ToElevation == 0, "anchored bearing elevations redacted");
        var circle = (CircleShape)copy.Shapes[1]; Point(circle.Center, Vector2.Zero, 73);
        Expect(circle.Radius == 1800 && circle.CenterElevation == 0, "anchored circle elevation redacted");
        var pen = (PenStroke)copy.Shapes[2];
        Expect(pen.Points.SequenceEqual(((PenStroke)original.Shapes[2]).Points), "every pen point");
        var note = (TextNote)copy.Shapes[3]; Point(note.At, Vector2.Zero, 42);
        Expect(note.Elevation == 0 && note.Text == "Target α", "anchored note elevation redacted");
        var waypoints = (WaypointRoute)copy.Shapes[4];
        Expect(waypoints.Count == 2 && waypoints[0].Elevation == 0 && waypoints[1].Elevation == -26, "route elevations");
        Point(waypoints[0].Point, Vector2.Zero, 333); Point(waypoints[1].Point, new Vector2(-24, 25), 0);
    }

    private static void AnchoredWire()
    {
        var anchored = Encode(Packet(Note()));
        Expect(BitConverter.ToSingle(anchored, 72) == 0f && BitConverter.ToSingle(anchored, 76) == 0f &&
            BitConverter.ToUInt32(anchored, 80) == 42 && BitConverter.ToSingle(anchored, 84) == 0f,
            "anchored note contains only its unit id");
        Reject(Replace(anchored, 72, BitConverter.GetBytes(4f)));
        Reject(Replace(anchored, 84, BitConverter.GetBytes(6f)));
        var fixedNote = Stamp(new TextNote(new MapPoint(new Vector2(4, -5)), 6, "Target", Color));
        var fixedBytes = Encode(Packet(fixedNote));
        Expect(BitConverter.ToSingle(fixedBytes, 72) == 4f && BitConverter.ToSingle(fixedBytes, 76) == -5f &&
            BitConverter.ToUInt32(fixedBytes, 80) == 0 && BitConverter.ToSingle(fixedBytes, 84) == 6f,
            "fixed note keeps its position and elevation");
    }

    private static void PenDots()
    {
        var bytes = Encode(Packet(Stamp(new PenStroke(new[] { new Vector2(12, -34) }, Color))));
        var pen = (PenStroke)Decode(bytes).Shapes[0];
        Expect(pen.Points.Count == 1 && pen.Points[0] == new Vector2(12, -34), "single-click dot preserved");
        Reject(Packet(Stamp(new PenStroke(Array.Empty<Vector2>(), Color))));
        Reject(Replace(bytes, 72, BitConverter.GetBytes((ushort)0)));
    }

    private static void BearingWire()
    {
        foreach (var (fromUnit, toUnit) in new[] { (71u, 72u), (71u, 0u), (0u, 72u), (0u, 0u) })
        {
            var bearing = Stamp(new BearingRangeShape(new MapPoint(new Vector2(-10, 11), fromUnit),
                new MapPoint(new Vector2(12, -13), toUnit), 1400, 1500, Color));
            var bytes = Encode(Packet(bearing));
            Expect(bytes.Length == 104, "one-channel BRA has a fixed wire layout");
            foreach (var (offset, unit, x, y, elevation) in new[]
            {
                (72, fromUnit, -10f, 11f, 1400f), (84, toUnit, 12f, -13f, 1500f)
            })
            {
                Expect(BitConverter.ToSingle(bytes, offset) == (unit == 0 ? x : 0) &&
                    BitConverter.ToSingle(bytes, offset + 4) == (unit == 0 ? y : 0) &&
                    BitConverter.ToUInt32(bytes, offset + 8) == unit,
                    "unit references never transmit sender coordinates; fixed points retain exact coordinates");
                var elevationOffset = offset == 72 ? 96 : 100;
                Expect(BitConverter.ToSingle(bytes, elevationOffset) == (unit == 0 ? elevation : 0),
                    "unit references never transmit sender altitude; fixed points retain ground height");
                if (unit == 0) continue;
                Reject(Replace(bytes, offset, BitConverter.GetBytes(x)));
                Reject(Replace(bytes, offset + 4, BitConverter.GetBytes(y)));
                Reject(Replace(bytes, elevationOffset, BitConverter.GetBytes(elevation)));
            }
            var copy = (BearingRangeShape)Decode(bytes).Shapes.Single();
            Expect(copy.From.UnitId == fromUnit && copy.To.UnitId == toUnit,
                "redaction preserves both anchor identities");
        }
    }

    private static void Handshakes()
    {
        foreach (var kind in new[] { DrawingPacketKind.Hello, DrawingPacketKind.Acknowledgement })
        {
            var recipient = kind == DrawingPacketKind.Hello ? Guid.Empty : Recipient;
            var packet = new DrawingPacket(kind, new SharedMissionEpoch(1, 2, 7), Sender, recipient, 0,
                new[] { "flight one", "bra_2" }, Array.Empty<MapShape>(), new[] { "command", "medusa" });
            var copy = Decode(Encode(packet));
            Expect(copy.Kind == kind && copy.RecipientSession == recipient && copy.Channels.SequenceEqual(packet.Channels) &&
                copy.BroadcastChannels.SequenceEqual(packet.BroadcastChannels), "both direction lists in handshake");
        }
        Reject(new DrawingPacket(DrawingPacketKind.Snapshot, new SharedMissionEpoch(1, 2, 7), Sender, Guid.Empty, 0, Array.Empty<string>(), Array.Empty<MapShape>()));
        Reject(new DrawingPacket(DrawingPacketKind.Hello, new SharedMissionEpoch(1, 2, 7), Sender, Guid.Empty, 0, Array.Empty<string>(), new MapShape[] { Note() }));
        Reject(new DrawingPacket(DrawingPacketKind.Hello, new SharedMissionEpoch(1, 2, 0), Sender, Guid.Empty, 0, Array.Empty<string>(), Array.Empty<MapShape>()));
    }

    private static void ControlPacketBounds()
    {
        var receiveOnly = Enumerable.Range(0, DrawingChannels.MaxChannels)
            .Select(i => new string((char)('a' + i), DrawingChannels.MaxNameLength)).ToArray();
        var broadcast = Enumerable.Range(0, DrawingChannels.MaxChannels)
            .Select(i => new string((char)('k' + i), DrawingChannels.MaxNameLength)).ToArray();
        var epoch = new SharedMissionEpoch(1, 2, 7);
        foreach (var kind in new[] { DrawingPacketKind.Hello, DrawingPacketKind.Acknowledgement })
        {
            var packet = new DrawingPacket(kind, epoch, Sender, Recipient, 0,
                receiveOnly, Array.Empty<MapShape>(), broadcast);
            var bytes = Encode(packet);
            Expect(bytes.Length == 740, "maximum handshake wire size");
            var copy = Decode(bytes);
            Expect(copy.Channels.SequenceEqual(receiveOnly) && copy.BroadcastChannels.SequenceEqual(broadcast) &&
                copy.Channels.Concat(copy.BroadcastChannels).Distinct().Count() == 20,
                "ten disjoint receive-only and ten broadcast channels are preserved without merging the wire lists");
            var empty = new DrawingPacket(kind, epoch, Sender, Recipient, 0,
                Array.Empty<string>(), Array.Empty<MapShape>());
            Expect(Encode(empty).Length == 60, "empty handshake wire size");
            Reject(new DrawingPacket(kind, epoch, Sender, Recipient, 0, receiveOnly, Array.Empty<MapShape>(),
                broadcast.Concat(new[] { "extra" }).ToArray()));
            Reject(new DrawingPacket(kind, epoch, Sender, Recipient, 0,
                receiveOnly.Concat(new[] { "extra" }).ToArray(), Array.Empty<MapShape>(), broadcast));
        }
        var request = new DrawingPacket(DrawingPacketKind.DeleteRequest, epoch, Sender, Recipient, 0,
            Array.Empty<string>(), Array.Empty<MapShape>(), targetShapeId: int.MaxValue, targetChannel: broadcast[0]);
        var requestBytes = Encode(request);
        Expect(requestBytes.Length == 96, "maximum erase request wire size");
        var decoded = Decode(requestBytes);
        Expect(decoded.TargetShapeId == int.MaxValue && decoded.TargetChannel == broadcast[0],
            "maximum erase target preserved");
    }

    private static void PreviousVersion()
    {
        var epoch = new SharedMissionEpoch(1, 2, 7);
        var packets = new[]
        {
            new DrawingPacket(DrawingPacketKind.Hello, epoch, Sender, Guid.Empty, 0,
                new[] { "reader" }, Array.Empty<MapShape>(), new[] { "command" }),
            new DrawingPacket(DrawingPacketKind.Acknowledgement, epoch, Sender, Recipient, 0,
                new[] { "reader" }, Array.Empty<MapShape>(), new[] { "command" }),
            Packet(Note()),
            new DrawingPacket(DrawingPacketKind.DeleteRequest, epoch, Sender, Recipient, 1,
                Array.Empty<string>(), Array.Empty<MapShape>(), targetShapeId: 42, targetChannel: "command"),
        };
        foreach (var packet in packets)
        {
            var bytes = Encode(packet);
            Expect(bytes[4] == 6, "unit-linked circles, notes and routes need protocol version six");
            Expect(BitConverter.ToUInt32(bytes, DrawingProtocol.SequenceOffset) == packet.Sequence,
                "the receiver reads a snapshot's sequence before decoding it");
            Decode(bytes);
            Reject(Replace(bytes, 4, new byte[] { 5 }));
            Reject(Replace(bytes, 4, new byte[] { 4 }));
            Reject(Replace(bytes, 4, new byte[] { 3 }));
            Reject(Replace(bytes, 4, new byte[] { 2 }));
            Expect(!DrawingProtocol.IsOtherVersion(bytes) && DrawingProtocol.IsOtherVersion(Replace(bytes, 4, new byte[] { 3 })) &&
                DrawingProtocol.IsOtherVersion(Replace(bytes, 4, new byte[] { 5 })) &&
                DrawingProtocol.IsOtherVersion(Replace(bytes, 4, new byte[] { 7 })), "another release's frames are told apart from malformed data");
            Expect(!DrawingProtocol.IsOtherVersion(Replace(bytes, 0, new byte[] { 0 })), "a frame without the magic is malformed, not another version");
        }
    }

    private static void DeleteRequests()
    {
        var request = new DrawingPacket(DrawingPacketKind.DeleteRequest, new SharedMissionEpoch(37, 1234.5, 7),
            Sender, Recipient, 8, Array.Empty<string>(), Array.Empty<MapShape>(),
            targetShapeId: 42, targetChannel: "command");
        var copy = Decode(Encode(request));
        Expect(copy.Kind == DrawingPacketKind.DeleteRequest && copy.TargetShapeId == 42 &&
            copy.TargetChannel == "command" && copy.Sequence == 8, "erase target preserved");
        Reject(new DrawingPacket(DrawingPacketKind.DeleteRequest, request.Epoch, Sender, Recipient, 8,
            Array.Empty<string>(), Array.Empty<MapShape>(), targetShapeId: 0, targetChannel: "command"));
        Reject(new DrawingPacket(DrawingPacketKind.DeleteRequest, request.Epoch, Sender, Recipient, 8,
            Array.Empty<string>(), Array.Empty<MapShape>(), targetShapeId: 42, targetChannel: "bad!"));
        Reject(new DrawingPacket(DrawingPacketKind.DeleteRequest, request.Epoch, Sender, Recipient, 8,
            new[] { "command" }, Array.Empty<MapShape>(), targetShapeId: 42, targetChannel: "command"));
        var bytes = Encode(request);
        Reject(Replace(bytes, 58, BitConverter.GetBytes(0)));
        Reject(bytes.Concat(new byte[] { 0 }).ToArray());
    }

    private static void Truncation()
    {
        var bytes = Encode(Packet(Note()));
        for (var i = 0; i < bytes.Length; i++) Reject(bytes.Take(i).ToArray());
        Reject(bytes.Concat(new byte[] { 0 }).ToArray());
        Reject(new byte[DrawingProtocol.MaxPacketBytes + 1]);
    }

    private static void MutatedFrames()
    {
        var source = Encode(Packet(Note()));
        var random = new Random(610);
        for (var frame = 0; frame < 2048; frame++)
        {
            var bytes = (byte[])source.Clone();
            var changes = random.Next(1, 9);
            for (var i = 0; i < changes; i++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
            var accepted = DrawingProtocol.TryDecode(bytes, out var packet);
            Expect(accepted == (packet != null), "mutated frame is either complete or rejected");
        }
    }

    private static void Malformed()
    {
        var bytes = Encode(Packet(Note()));
        Reject(Replace(bytes, 0, new byte[] { 0 }));
        Reject(Replace(bytes, 4, new byte[] { 255 }));
        Reject(Replace(bytes, 5, new byte[] { 255 }));
        Reject(Replace(bytes, 6, BitConverter.GetBytes(0u)));
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, 0, -1 }) Reject(Replace(bytes, 10, BitConverter.GetBytes(value)));
        Reject(Replace(bytes, 18, BitConverter.GetBytes(0u)));
        Reject(Replace(bytes, 22, new byte[16])); Reject(Replace(bytes, 38, new byte[16]));
        Reject(Replace(bytes, 58, BitConverter.GetBytes((ushort)201)));
        Reject(Replace(bytes, 60, new byte[] { 99 }));
        Reject(Replace(bytes, 61, BitConverter.GetBytes(-1)));
        Reject(Replace(bytes, 61, BitConverter.GetBytes(0)));
        Reject(Replace(bytes, 65, BitConverter.GetBytes(ushort.MaxValue)));
        Reject(Replace(bytes, 67, new byte[] { 255 }));
        // One-byte channel ends at 68; RGBA follows, then X/Y/unit/elevation and the text length.
        Reject(Replace(bytes, 88, BitConverter.GetBytes(ushort.MaxValue)));
        Reject(Replace(bytes, 90, new byte[] { 255 }));
        Reject(Replace(bytes, 72, BitConverter.GetBytes(float.NaN)));
    }

    private static void Geometry()
    {
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 2000001f, -2000001f })
            Reject(Packet(Stamp(new CircleShape(new MapPoint(new Vector2(value, 0)), 1, 0, Color))));
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, 100001f, -100001f })
            Reject(Packet(Stamp(new TextNote(new MapPoint(Vector2.Zero), value, "note", Color))));
        foreach (var value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 2000001f })
            Reject(Packet(Stamp(new CircleShape(new MapPoint(Vector2.Zero), value, 0, Color))));
        Decode(Encode(Packet(Stamp(new CircleShape(new MapPoint(new Vector2(-2000000, 2000000)), 2000000, -100000, Color)))));
    }

    private static void TextPolicy()
    {
        foreach (var name in new[] { "", "A", " a", "a,b", "<b>", "é", new string('a', 33) })
        { var note = Note(); note.Channel = name; Reject(Packet(note)); }
        foreach (var text in new[] { "", " ", "a\nb", "a\0b", new string('a', 65), "\ud800" }) Reject(Packet(Note(text)));
        foreach (var text in new[] { "alt < 2000", "A > B", "<size=9999>text</size>", "\U0001f6eb" })
            Expect(((TextNote)Decode(Encode(Packet(Note(text)))).Shapes[0]).Text == text, "literal note text preserved");
        Decode(Encode(Packet(Note(new string('α', 64)))));
        var duplicate = new DrawingPacket(DrawingPacketKind.Hello, new SharedMissionEpoch(1, 1, 7), Sender, Guid.Empty, 0, new[] { "a", "a" }, Array.Empty<MapShape>());
        Reject(duplicate);
        var manyChannels = Enumerable.Range(1, 10).Select(i => "channel-" + i).ToArray();
        var hello = new DrawingPacket(DrawingPacketKind.Hello, new SharedMissionEpoch(1, 1, 7), Sender, Guid.Empty, 0, manyChannels, Array.Empty<MapShape>());
        Expect(Decode(Encode(hello)).Channels.Length == 10, "ten subscriptions accepted");
        Reject(new DrawingPacket(DrawingPacketKind.Hello, hello.Epoch, Sender, Guid.Empty, 0, manyChannels.Concat(new[] { "eleven" }).ToArray(), Array.Empty<MapShape>()));
        Reject(Replace(Encode(hello), 58, new byte[] { 11 }));
    }

    private static void NoteLayoutControls()
    {
        foreach (var control in "\u2028\u2029\u202a\u202b\u202c\u202d\u202e\u2066\u2067\u2068\u2069")
        {
            var text = "A" + control + "B";
            var payload = Encoding.UTF8.GetBytes(text);
            var bytes = Encode(Packet(Note(new string('a', payload.Length))));
            var crafted = Replace(bytes, bytes.Length - payload.Length, payload);
            Expect(!DrawingProtocol.TryEncode(Packet(Note(text)), out var output) && output.Length == 0,
                $"U+{(int)control:X4} must be rejected before sending");
            Expect(!DrawingProtocol.TryDecode(crafted, out var packet) && packet == null,
                $"U+{(int)control:X4} must be rejected when received");
        }
    }

    private static void NoteVisibility()
    {
        foreach (var text in new[] { "\u200b", " \u200c\u200d\u2060\ufeff ", "\U000e0001", " \U000e0020 " })
        {
            var note = Stamp(new TextNote(new MapPoint(Vector2.Zero), 0, text, Color));
            var payload = Encoding.UTF8.GetBytes(text);
            var valid = Stamp(new TextNote(note.At, 0, new string('a', payload.Length), Color));
            var bytes = Encode(Packet(valid));
            var crafted = Replace(bytes, bytes.Length - payload.Length, payload);
            var encoded = DrawingProtocol.TryEncode(Packet(note), out var output);
            var decoded = DrawingProtocol.TryDecode(crafted, out var packet);
            Expect(!encoded && output.Length == 0 && !decoded && packet == null,
                $"format-only note must be rejected without partial state: encoded={encoded}, decoded={decoded}");
        }

        foreach (var text in new[] { "\u200bTarget\u2060", "\U0001f469\u200d\U0001f4bb", "می\u200cروم", "旗\U000e0020" })
            Expect(((TextNote)Decode(Encode(Packet(Note(text)))).Shapes[0]).Text == text,
                "visible Unicode text with format characters preserved");
    }

    private static void GeometryBudget()
    {
        var positions = Enumerable.Repeat(new Vector2(1, 2), 2999).ToArray();
        var pen = Stamp(new PenStroke(positions, Color), 2);
        Decode(Encode(Packet(pen, Note())));
        var bearing = Stamp(new BearingRangeShape(new MapPoint(Vector2.Zero), new MapPoint(Vector2.One), 0, 0, Color), 3);
        Reject(Packet(pen, bearing));
        Reject(Packet(Stamp(new PenStroke(new Vector2[3001], Color))));
        var bytes = Encode(Packet(Stamp(new PenStroke(new[] { Vector2.Zero, Vector2.One }, Color))));
        Reject(Replace(bytes, 72, BitConverter.GetBytes((ushort)3001)));
        WaypointRoute Route(int waypoints)
        {
            var route = new WaypointRoute(Color, new RouteWaypoint(new MapPoint(Vector2.Zero), 0));
            for (var i = 1; i < waypoints; i++) route = route.With(new RouteWaypoint(new MapPoint(Vector2.One), i));
            return route;
        }
        Decode(Encode(Packet(Stamp(Route(50)))));
        Reject(Packet(Stamp(Route(51))));
        var routes = Enumerable.Range(1, 6).Select(i => (MapShape)Stamp(Route(50), i)).ToArray();
        Decode(Encode(Packet(routes.Take(5).ToArray())));
        Reject(Packet(routes));
    }

    private static void ShapeBudget()
    {
        var shapes = Enumerable.Range(1, 100).Select(i => (MapShape)Stamp(Note(), i)).ToArray();
        Expect(Decode(Encode(Packet(shapes))).Shapes.Length == 100, "100 accepted");
        Reject(Packet(shapes.Concat(new[] { Stamp(Note(), 101) }).ToArray()));
        Reject(Packet(Note(), Note()));
        var negative = Note(); negative.Id = -1; Reject(Packet(negative));
        var zero = Note(); zero.Id = 0; Reject(Packet(zero));
        var oversized = Enumerable.Range(1, 99).Select(i => (MapShape)Stamp(Note(new string('界', 64)), i)).ToList();
        oversized.Add(Stamp(new PenStroke(new Vector2[2950], Color), 100));
        Reject(Packet(oversized.ToArray()));
        var leftOut = DrawingProtocol.EncodeFitting(Packet(oversized.ToArray()), out var sent, out var bytes);
        Expect(leftOut == 1 && sent.Shapes.Length == 99 && Decode(bytes).Shapes.Length == 99,
            "the oldest drawings that fit are still shared");
        // The local pen budget counts only pen points; the wire also counts a note's position.
        var atPenLimit = new MapShape[] { Stamp(new PenStroke(new Vector2[3000], Color), 1), Stamp(Note(), 2) };
        Expect(DrawingProtocol.EncodeFitting(Packet(atPenLimit), out sent, out _) == 1 && sent.Shapes.Length == 1,
            "a drawing past the geometry limit is left out alone");
        Expect(DrawingProtocol.EncodeFitting(Packet(shapes), out sent, out _) == 0 && sent.Shapes.Length == 100,
            "a snapshot within the limits is sent whole");
    }
}
