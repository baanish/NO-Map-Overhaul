using NoMapOverhaul.MapTools;
using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingPeerTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("one hello completes both drawing peers without an ack loop", OneHello),
        ("simultaneous and repeated drawing hellos converge", Simultaneous),
        ("drawing snapshots require readiness matching sessions and increasing sequences", Snapshots),
        ("wrong epoch and recipient handshakes leave drawing peer state unchanged", WrongHandshake),
        ("reconnecting drawing peers reject old acknowledgements and snapshots", Reconnect),
        ("expired drawing receiver requires a fresh nonce before accepting retries", ExpiredReceiver),
        ("replayed old hello requires a fresh acknowledgement before any snapshot", OldHello),
        ("a heartbeat hello crossing a one-sided restart still pairs on the next hellos", StaleHeartbeat),
        ("a heartbeat's acknowledgement reports the newest snapshot the teammate holds", HeartbeatSnapshot),
        ("replacement drawing subscriptions rotate sessions and reject stale snapshots", Subscriptions),
        ("drawing peer copies subscriptions and rejects in-session subscription mutation", SubscriptionOwnership),
        ("drawing edit requests require broadcast membership and reject replay", DeleteRequests),
        ("receive-only readers receive drawings but cannot erase them", ReceiveOnlyReader),
        ("snapshot session and replay headers are rejected before shape decoding or cache changes", SnapshotHeaderRejection),
        ("decoded snapshots enter the record cache only after peer acceptance", SnapshotCacheAcceptance),
    };

    private static readonly SharedMissionEpoch Epoch = new(42, 100.25, 7);
    private static DrawingPeer Peer(params string[] channels) => new(Epoch, channels);
    private static DrawingPacket Snapshot(DrawingPeer sender, DrawingPeer receiver, uint sequence) =>
        new(DrawingPacketKind.Snapshot, Epoch, sender.LocalSession, receiver.LocalSession, sequence,
            Array.Empty<string>(), Array.Empty<MapShape>());

    private static System.Collections.IDictionary SnapshotCache() => (System.Collections.IDictionary)
        typeof(DrawingProtocol).GetField("LastSnapshots", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)!.GetValue(null)!;

    private static byte[] SnapshotBytes(DrawingPeer sender, DrawingPeer receiver, uint sequence, string text = "accepted")
    {
        var note = new NoMapOverhaul.MapTools.Text.TextNote(new MapPoint(System.Numerics.Vector2.Zero), 0,
            text, new ShapeColor(1, 2, 3)) { Id = 1, Channel = "command" };
        Expect(DrawingProtocol.TryEncode(new DrawingPacket(DrawingPacketKind.Snapshot, Epoch, sender.LocalSession,
            receiver.LocalSession, sequence, Array.Empty<string>(), new MapShape[] { note }), out var bytes),
            "snapshot fixture encodes");
        return bytes;
    }

    private static void SnapshotHeaderRejection()
    {
        DrawingProtocol.ForgetSnapshots();
        var sender = Peer("command");
        var receiver = Peer("command");
        Connect(sender, receiver);
        var bytes = SnapshotBytes(sender, receiver, 2);
        Expect(receiver.TryDecode(bytes, bytes.Length, out var initial, out var malformed) && !malformed &&
            receiver.Receive(initial!, out _, out _), "a valid snapshot decodes and is accepted");
        var cached = SnapshotCache()[receiver.LocalSession];
        var cases = new List<byte[]>();
        foreach (var address in new[]
        {
            (Sender: sender.LocalSession, Recipient: Guid.NewGuid(), Sequence: 3U),
            (Sender: Guid.NewGuid(), Recipient: receiver.LocalSession, Sequence: 3U),
            (Sender: sender.LocalSession, Recipient: receiver.LocalSession, Sequence: 2U),
            (Sender: sender.LocalSession, Recipient: receiver.LocalSession, Sequence: 1U),
            (Sender: sender.LocalSession, Recipient: receiver.LocalSession, Sequence: 0U),
        })
        {
            var rejected = (byte[])bytes.Clone();
            DrawingProtocol.Readdress(rejected, address.Sender, address.Recipient, address.Sequence);
            cases.Add(rejected);
        }
        foreach (var offset in new[] { 6, 10, 18 })
        {
            var wrongEpoch = SnapshotBytes(sender, receiver, 3);
            wrongEpoch[offset] ^= 1;
            cases.Add(wrongEpoch);
        }
        foreach (var rejected in cases)
        {
            foreach (var corruptShapes in new[] { false, true })
            {
                var frame = (byte[])rejected.Clone();
                if (corruptShapes) frame[60] = 0;
                Expect(!receiver.TryDecode(frame, frame.Length, out var packet, out malformed) && !malformed && packet == null,
                    "an ineligible header is rejected without reading even an invalid shape type");
                Expect(SnapshotCache().Count == 1 && ReferenceEquals(SnapshotCache()[receiver.LocalSession], cached),
                    "wrong-session and stale snapshots neither add nor replace cache entries");
            }
        }
        var corrupt = SnapshotBytes(sender, receiver, 3);
        corrupt[60] = 0;
        Expect(!receiver.TryDecode(corrupt, corrupt.Length, out _, out malformed) && malformed,
            "the same invalid shape type is decoded and rejected when its header is eligible");
        var next = SnapshotBytes(sender, receiver, 3);
        Expect(receiver.TryDecode(next, next.Length, out var accepted, out malformed) && !malformed &&
            ReferenceEquals(accepted!.Shapes[0], initial!.Shapes[0]) && receiver.Receive(accepted, out _, out _),
            "a later valid sequence still decodes, reuses the accepted cache and is accepted");
    }

    private static void SnapshotCacheAcceptance()
    {
        DrawingProtocol.ForgetSnapshots();
        var sender = Peer("command");
        var receiver = Peer("command");
        var unpaired = SnapshotBytes(sender, receiver, 1);
        Expect(DrawingProtocol.TryDecode(unpaired, out var decoded) && SnapshotCache().Count == 0,
            "decoding alone does not create a cache entry");
        Expect(!receiver.Receive(decoded!, out _, out _) && SnapshotCache().Count == 0,
            "an unpaired receiver rejects a decoded snapshot without caching it");
        Connect(sender, receiver);
        var first = SnapshotBytes(sender, receiver, 1);
        Expect(receiver.TryDecode(first, first.Length, out decoded, out _) && SnapshotCache().Count == 0 &&
            receiver.Receive(decoded!, out _, out _) && SnapshotCache().Count == 1,
            "a paired receiver caches only after accepting the fully decoded snapshot");
        var cached = SnapshotCache()[receiver.LocalSession];
        var second = SnapshotBytes(sender, receiver, 2, "changed");
        Expect(receiver.TryDecode(second, second.Length, out var changed, out _) &&
            ReferenceEquals(SnapshotCache()[receiver.LocalSession], cached), "a decoded replacement leaves the accepted cache unchanged");
        Expect(receiver.Receive(changed!, out _, out _) && !ReferenceEquals(SnapshotCache()[receiver.LocalSession], cached),
            "accepting the replacement publishes its records");
        var third = SnapshotBytes(sender, receiver, 3, "changed");
        Expect(receiver.TryDecode(third, third.Length, out var repeated, out _) &&
            ReferenceEquals(repeated!.Shapes[0], changed!.Shapes[0]), "valid unchanged drawings retain object reuse");
        Expect(!receiver.Receive(changed!, out _, out _), "a duplicate sequence cannot publish its records again");
        DrawingProtocol.ForgetSnapshots();
    }

    private static DrawingPacket ReceiveReply(DrawingPeer peer, DrawingPacket packet)
    {
        Expect(peer.Receive(packet, out var reply, out var snapshot), "expected handshake acceptance");
        Expect(snapshot == null, "handshake must not expose a snapshot");
        Expect(reply != null, "expected handshake reply");
        return reply!;
    }

    private static void Connect(DrawingPeer first, DrawingPeer second)
    {
        var acknowledgement = ReceiveReply(second, first.CreateHello());
        var echo = ReceiveReply(first, acknowledgement);
        var confirmation = ReceiveReply(second, echo);
        Expect(first.Receive(confirmation, out var reply, out var snapshot), "expected final confirmation");
        Expect(reply == null && snapshot == null, "acknowledgements must stop echoing");
        Expect(first.IsReady && second.IsReady, "expected both peers ready");
    }

    private static void Reject(DrawingPeer peer, DrawingPacket packet)
    {
        var before = (peer.LocalSession, peer.RemoteSession, peer.IsReady, peer.Revision,
            string.Join(",", peer.RemoteChannels), string.Join(",", peer.RemoteBroadcastChannels));
        Expect(!peer.Receive(packet, out var reply, out var snapshot), "expected packet rejection");
        Expect(reply == null && snapshot == null, "rejected packet must produce no outputs");
        var after = (peer.LocalSession, peer.RemoteSession, peer.IsReady, peer.Revision,
            string.Join(",", peer.RemoteChannels), string.Join(",", peer.RemoteBroadcastChannels));
        Expect(before == after, "rejected packet changed peer state");
    }

    private static DrawingPacket Rewrite(DrawingPacket packet, SharedMissionEpoch? epoch = null, Guid? sender = null,
        Guid? recipient = null, uint? sequence = null, string[]? channels = null, string[]? broadcast = null) =>
        new(packet.Kind, epoch ?? packet.Epoch, sender ?? packet.SenderSession, recipient ?? packet.RecipientSession,
            sequence ?? packet.Sequence, channels ?? packet.Channels, packet.Shapes,
            broadcast ?? packet.BroadcastChannels, packet.TargetShapeId, packet.TargetChannel);

    private static void OneHello()
    {
        var first = Peer("one");
        var second = Peer("two", "three");
        var hello = first.CreateHello();
        Expect(hello.Kind == DrawingPacketKind.Hello && hello.RecipientSession == Guid.Empty && hello.Sequence == 0,
            "hello header must carry only the sender nonce");
        Connect(first, second);
        Expect(first.RemoteSession == second.LocalSession && second.RemoteSession == first.LocalSession, "expected bound sessions");
        ExpectText(string.Join(",", first.RemoteChannels), "two,three");
        Expect(first.Revision == 1 && second.Revision == 2, "initiator completes; responder begins and completes");
    }

    private static void Simultaneous()
    {
        var first = Peer("one");
        var second = Peer("two");
        var firstHello = first.CreateHello();
        var secondHello = second.CreateHello();
        var secondAck = ReceiveReply(second, firstHello);
        var firstAck = ReceiveReply(first, secondHello);
        ReceiveReply(first, secondAck);
        ReceiveReply(second, firstAck);
        var firstSession = first.LocalSession;
        var secondSession = second.LocalSession;
        var repeatAck = ReceiveReply(second, firstHello);
        Expect(first.Receive(repeatAck, out var reply, out _) && reply == null, "repeat acknowledgement should terminate");
        ReceiveReply(first, secondHello);
        Expect(first.LocalSession == firstSession && second.LocalSession == secondSession, "repeated hello must not rotate current sessions");
        Expect(first.IsReady && second.IsReady && first.Revision == 2 && second.Revision == 2, "repeated hellos must keep readiness and revision");
    }

    private static void Snapshots()
    {
        var first = Peer("one");
        var second = Peer("one");
        Reject(second, Snapshot(first, second, 1));
        Connect(first, second);
        var snapshot = Snapshot(first, second, 1);
        Reject(second, Rewrite(snapshot, sequence: 0));
        Reject(second, Rewrite(snapshot, sender: Guid.NewGuid()));
        Reject(second, Rewrite(snapshot, recipient: Guid.NewGuid()));
        Reject(second, Rewrite(snapshot, epoch: new SharedMissionEpoch(42, 101, 7)));
        Reject(second, Rewrite(snapshot, epoch: new SharedMissionEpoch(42, 100.25, 8)));
        Expect(second.Receive(snapshot, out var reply, out var accepted) && ReferenceEquals(snapshot, accepted) && reply == null,
            "accepted snapshot must be returned alone");
        Reject(second, snapshot);
        Expect(second.Receive(Snapshot(first, second, 3), out _, out _), "new sequence should pass");
        Reject(second, Snapshot(first, second, 2));
        Expect(second.Receive(Snapshot(first, second, uint.MaxValue), out _, out _), "maximum sequence should pass");
        Reject(second, Snapshot(first, second, 1));
    }

    private static void WrongHandshake()
    {
        var first = Peer("one");
        var second = Peer("two");
        var hello = first.CreateHello();
        Reject(second, Rewrite(hello, epoch: new SharedMissionEpoch(43, 100.25, 7)));
        Reject(second, Rewrite(hello, epoch: new SharedMissionEpoch(42, 100.25, 8)));
        Reject(second, Rewrite(hello, recipient: Guid.NewGuid()));
        Reject(second, Rewrite(hello, sender: Guid.Empty));
        Reject(second, Rewrite(hello, channels: new[] { "Invalid" }));
        var ack = ReceiveReply(second, hello);
        Reject(first, Rewrite(ack, recipient: Guid.NewGuid()));
        Reject(first, Rewrite(ack, epoch: new SharedMissionEpoch(42, 100.5, 7)));
        Reject(first, Rewrite(ack, epoch: new SharedMissionEpoch(42, 100.25, 8)));
        var firstAck = ReceiveReply(first, ack);
        Reject(second, Rewrite(firstAck, sender: Guid.NewGuid()));
        Expect(!second.IsReady, "invalid acknowledgement must not complete a candidate handshake");
    }

    private static void Reconnect()
    {
        var first = Peer("one");
        var second = Peer("two");
        Connect(first, second);
        var oldSnapshot = Snapshot(second, first, 100);
        var oldAck = ReceiveReply(second, first.CreateHello());
        var firstSession = first.LocalSession;
        var replacement = Peer("two");
        var challenge = ReceiveReply(first, replacement.CreateHello());
        Expect(!first.IsReady && first.LocalSession != firstSession && first.RemoteChannels.Count == 0, "reconnect must clear and challenge");
        Reject(first, oldAck);
        Reject(first, oldSnapshot);
        var freshAck = ReceiveReply(replacement, challenge);
        var confirmation = ReceiveReply(first, freshAck);
        Expect(replacement.Receive(confirmation, out _, out _), "expected reconnect confirmation");
        Expect(first.IsReady && replacement.IsReady, "new sessions must connect");
        Reject(first, oldSnapshot);
        Expect(first.Receive(Snapshot(replacement, first, 1), out _, out _), "new session starts at sequence one");
    }

    private static void ExpiredReceiver()
    {
        var sender = Peer("one");
        var receiver = Peer("one");
        Connect(sender, receiver);
        var delayed = Snapshot(sender, receiver, 1);
        var delayedAck = ReceiveReply(sender, receiver.CreateHello());
        var oldSession = receiver.LocalSession;
        receiver = Peer("one");
        Expect(receiver.LocalSession != oldSession, "expiry must rotate the recipient nonce");
        Reject(receiver, delayed);
        Reject(receiver, delayedAck);
        var challenge = ReceiveReply(sender, receiver.CreateHello());
        var answer = ReceiveReply(receiver, challenge);
        var confirmation = ReceiveReply(sender, answer);
        Expect(receiver.Receive(confirmation, out _, out _), "fresh receiver confirms");
        Reject(receiver, delayed);
        // A rate-limited first snapshot is absent; the periodic full retry has a fresh sequence.
        var retry = Snapshot(sender, receiver, 2);
        Expect(receiver.Receive(retry, out _, out var snapshot) && ReferenceEquals(snapshot, retry), "full retry converges after a dropped snapshot");
    }

    private static void OldHello()
    {
        var first = Peer("one");
        var old = Peer("two");
        var oldHello = old.CreateHello();
        Connect(first, old);
        var oldSnapshot = Snapshot(old, first, 1);
        var oldAck = ReceiveReply(old, first.CreateHello());
        var current = Peer("two");
        var challenge = ReceiveReply(first, current.CreateHello());
        var reply = ReceiveReply(current, challenge);
        ReceiveReply(first, reply);
        var completedNonce = first.LocalSession;
        ReceiveReply(first, oldHello);
        Expect(!first.IsReady && first.LocalSession != completedNonce, "old hello must require a new challenge response");
        Reject(first, oldSnapshot);
        Reject(first, oldAck);
        Reject(first, Snapshot(old, first, 2));
    }

    private static void StaleHeartbeat()
    {
        var first = Peer("one");
        var second = Peer("one");
        Connect(first, second);
        var heartbeat = second.CreateHello();
        first = Peer("one"); // a channel Apply restarts one side while the other's heartbeat is in flight
        var lostAck = ReceiveReply(first, heartbeat);
        var challenge = ReceiveReply(second, first.CreateHello());
        Reject(second, lostAck);
        Reject(first, challenge);
        var answer = ReceiveReply(first, second.CreateHello());
        var confirmation = ReceiveReply(second, answer);
        ReceiveReply(first, confirmation);
        Expect(first.IsReady && second.IsReady, "the next hello pair must complete the handshake");
        Expect(first.Receive(Snapshot(second, first, 1), out _, out var snapshot) && snapshot != null, "paired peers exchange snapshots");
    }

    private static void HeartbeatSnapshot()
    {
        var owner = Peer("one");
        var reader = Peer("one");
        Connect(owner, reader);
        Expect(reader.Receive(Snapshot(owner, reader, 1), out _, out _) && reader.Receive(Snapshot(owner, reader, 2), out _, out _),
            "snapshots apply");
        var heartbeat = owner.CreateHello(3); // snapshot 3 was sent but never applied
        Expect(heartbeat.Sequence == 3, "the heartbeat names the last snapshot sent");
        var ack = ReceiveReply(reader, heartbeat);
        Expect(ack.Sequence == 2 && reader.IsReady, "the reader reports what it holds and stays paired");
        Expect(owner.Receive(ack, out var reply, out _) && reply == null && owner.AcknowledgedSnapshot == 2 && owner.IsReady,
            "the owner learns the reader is one snapshot behind");
    }

    private static void Subscriptions()
    {
        var first = Peer("one");
        var old = Peer("old");
        Connect(first, old);
        var stale = Snapshot(old, first, 1);
        var replacement = Peer("new");
        var challenge = ReceiveReply(first, replacement.CreateHello());
        var acknowledgement = ReceiveReply(replacement, challenge);
        ReceiveReply(first, acknowledgement);
        ExpectText(string.Join(",", first.RemoteChannels), "new");
        Reject(first, stale);
    }

    private static void SubscriptionOwnership()
    {
        var channels = new[] { "one" };
        var broadcast = new[] { "command" };
        var first = new DrawingPeer(Epoch, channels, broadcast);
        channels[0] = "changed";
        broadcast[0] = "changed";
        ExpectText(first.CreateHello().Channels[0], "one");
        ExpectText(first.CreateHello().BroadcastChannels[0], "command");
        var hello = first.CreateHello();
        hello.Channels[0] = "changed";
        hello.BroadcastChannels[0] = "changed";
        ExpectText(first.CreateHello().Channels[0], "one");
        ExpectText(first.CreateHello().BroadcastChannels[0], "command");
        var second = Peer("two");
        Connect(first, second);
        Reject(second, Rewrite(first.CreateHello(), channels: new[] { "changed" }));
        Reject(second, Rewrite(first.CreateHello(), broadcast: new[] { "changed" }));
        var ack = ReceiveReply(first, second.CreateHello());
        Reject(second, Rewrite(ack, channels: new[] { "changed" }));
        ack.Channels[0] = "changed";
        ExpectText(second.RemoteChannels[0], "one");
        ExpectText(second.RemoteBroadcastChannels[0], "command");
    }

    private static void DeleteRequests()
    {
        var owner = new DrawingPeer(Epoch, Array.Empty<string>(), new[] { "command" });
        var editor = new DrawingPeer(Epoch, Array.Empty<string>(), new[] { "command" });
        Connect(owner, editor);
        DrawingPacket Request(uint sequence, string channel = "command") =>
            new(DrawingPacketKind.DeleteRequest, Epoch, editor.LocalSession, owner.LocalSession, sequence,
                Array.Empty<string>(), Array.Empty<MapShape>(), targetShapeId: 7, targetChannel: channel);
        Expect(!owner.TryReceiveDelete(Request(1)), "unsent drawing cannot be erased by a guessed ID");
        owner.RecordSentSnapshot(new MapShape[] { new NoMapOverhaul.MapTools.Text.TextNote(new MapPoint(System.Numerics.Vector2.Zero), 0, "test", new ShapeColor(1, 2, 3)) { Id = 7, Channel = "command" } });
        Expect(!owner.TryReceiveDelete(Request(0)), "zero sequence rejected");
        Expect(!owner.TryReceiveDelete(Rewrite(Request(1), sender: Guid.NewGuid())), "wrong sender rejected");
        Expect(!owner.TryReceiveDelete(Rewrite(Request(1), recipient: Guid.NewGuid())), "wrong recipient rejected");
        Expect(!owner.TryReceiveDelete(Rewrite(Request(1), epoch: new SharedMissionEpoch(43, 100.25, 7))), "wrong mission rejected");
        Expect(!owner.TryReceiveDelete(Rewrite(Request(1), epoch: new SharedMissionEpoch(42, 100.25, 8))), "wrong faction rejected");
        Expect(!owner.TryReceiveDelete(Request(1, "other")), "unadvertised channel rejected");
        Expect(owner.TryReceiveDelete(Request(1)), "member edit accepted");
        Expect(!owner.TryReceiveDelete(Request(1)), "replay rejected");
        Expect(owner.TryReceiveDelete(Request(2)), "next edit accepted");
        owner.RecordSentSnapshot(Array.Empty<MapShape>());
        Expect(!owner.TryReceiveDelete(Request(3)), "retracted drawing cannot be erased");
    }

    private static void ReceiveOnlyReader()
    {
        var receiveOnly = new DrawingPeer(Epoch, new[] { "command" });
        Expect(receiveOnly.CreateHello().BroadcastChannels.Length == 0,
            "omitting broadcast channels must not grant publishing or erase permission");
        var nextOwner = new DrawingPeer(Epoch, Array.Empty<string>(), new[] { "command" });
        Connect(nextOwner, receiveOnly);
        var drawing = new NoMapOverhaul.MapTools.Text.TextNote(new MapPoint(System.Numerics.Vector2.Zero), 0,
            "test", new ShapeColor(1, 2, 3)) { Id = 7, Channel = "command" };
        Expect(ChannelRouting.CanSend(drawing.Channel, nextOwner.CreateHello().BroadcastChannels,
            receiveOnly.CreateHello().BroadcastChannels,
            receiveOnly.CreateHello().Channels), "read-only reader is a valid drawing recipient");
        var snapshot = new DrawingPacket(DrawingPacketKind.Snapshot, Epoch, nextOwner.LocalSession,
            receiveOnly.LocalSession, 1, Array.Empty<string>(), new MapShape[] { drawing });
        Expect(receiveOnly.Receive(snapshot, out var reply, out var received) && reply == null &&
            ReferenceEquals(snapshot, received), "read-only peer accepts the owner snapshot after handshake");
        Expect(ChannelRouting.CanReceive(drawing.Channel, receiveOnly.CreateHello().BroadcastChannels,
            receiveOnly.CreateHello().Channels), "read-only reader retains the received drawing");
        nextOwner.RecordSentSnapshot(new MapShape[] { drawing });
        var forbidden = new DrawingPacket(DrawingPacketKind.DeleteRequest, Epoch, receiveOnly.LocalSession,
            nextOwner.LocalSession, 1, Array.Empty<string>(), Array.Empty<MapShape>(),
            targetShapeId: 7, targetChannel: "command");
        Expect(!nextOwner.TryReceiveDelete(forbidden), "receive-only peer cannot edit command");

        foreach (var nonCollaborativeOwner in new[]
        {
            new DrawingPeer(Epoch, new[] { "command" }, Array.Empty<string>()),
            new DrawingPeer(Epoch, new[] { "command" }, new[] { "other" })
        })
        {
            var collaborator = new DrawingPeer(Epoch, Array.Empty<string>(), new[] { "command" });
            Connect(nonCollaborativeOwner, collaborator);
            nonCollaborativeOwner.RecordSentSnapshot(new MapShape[] { drawing });
            var request = new DrawingPacket(DrawingPacketKind.DeleteRequest, Epoch, collaborator.LocalSession,
                nonCollaborativeOwner.LocalSession, 1, Array.Empty<string>(), Array.Empty<MapShape>(),
                targetShapeId: 7, targetChannel: "command");
            Expect(!nonCollaborativeOwner.TryReceiveDelete(request), "receive-only owner cannot accept edits to that channel");
        }
    }
}
