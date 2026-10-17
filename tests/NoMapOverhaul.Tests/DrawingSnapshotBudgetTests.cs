using System.Numerics;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingSnapshotBudgetTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("received snapshot cadence retains only the newest until due for each peer", Cadence),
        ("received snapshot change cost charges additions content and rotated source IDs", ChangeCost),
        ("peer change tokens refill and defer the newest snapshot without charging failed attempts", PeerRefill),
        ("global change tokens bound all peers and retry their newest snapshots", GlobalRefill),
        ("ordinary edits full pairing and collaborative erase bypass snapshot congestion", OrdinarySharing),
        ("restarting a drawing peer clears pending snapshots but preserves cadence and change tokens", Restart),
        ("rotating one hundred notes at five hertz stays within receive cadence and change budgets", AdversarialNotes),
    };

    private static readonly SharedMissionEpoch Epoch = new(42, 100.25, 7);
    private static MapShape[] Notes(int count, int firstId = 1, string text = "note") =>
        Enumerable.Range(firstId, count).Select(id => (MapShape)new TextNote(new MapPoint(Vector2.Zero), 0, text,
            new ShapeColor(1, 2, 3)) { Id = id, Channel = "a" }).ToArray();
    private static DrawingPacket Snapshot(uint sequence, MapShape[] shapes) =>
        new(DrawingPacketKind.Snapshot, Epoch, Guid.NewGuid(), Guid.NewGuid(), sequence, Array.Empty<string>(), shapes);

    private static DrawingPacket? Apply(DrawingSnapshotBudget budget, DrawingSnapshotBudget.Peer peer,
        ReceivedDrawings received, double now, ulong owner = 1)
    {
        var packet = budget.TryTake(peer, owner, received, now);
        if (packet != null) Expect(received.TryReplace(owner, packet.Shapes), "admitted snapshot applies atomically");
        return packet;
    }

    private static void Cadence()
    {
        Expect(DrawingNetworkSchedule.SnapshotInterval == 0.5, "sender and receiver share the half-second minimum");
        var budget = new DrawingSnapshotBudget();
        var peer = new DrawingSnapshotBudget.Peer { Pending = Snapshot(1, Notes(1)) };
        var received = new ReceivedDrawings();
        Expect(Apply(budget, peer, received, 0)?.Sequence == 1, "first pairing snapshot is immediate");
        peer.Pending = Snapshot(2, Notes(1, text: "second"));
        Expect(Apply(budget, peer, received, 0.2) == null, "next network tick cannot replace drawings");
        peer.Pending = Snapshot(3, Notes(1, text: "newest"));
        Expect(Apply(budget, peer, received, 0.499) == null, "deadline is enforced between ticks too");
        var other = new DrawingSnapshotBudget.Peer { Pending = Snapshot(1, Notes(1)) };
        Expect(Apply(budget, other, received, 0.2, 2) != null, "one owner's cadence does not delay another");
        Expect(Apply(budget, peer, received, 0.5)?.Sequence == 3 && peer.Pending == null,
            "only newest applies exactly when due");
        peer.Pending = Snapshot(4, Notes(1, text: "newest"));
        Expect(Apply(budget, peer, received, 0.6) == null && Apply(budget, peer, received, 1) != null,
            "zero-cost snapshots still obey receive cadence");
    }

    private static void ChangeCost()
    {
        var received = new ReceivedDrawings();
        Expect(received.ChangeCost(1, Notes(100)) == 100, "a full first pairing costs one hundred");
        Expect(received.TryReplace(1, Notes(100)), "store original wire IDs separately from render IDs");
        Expect(received.ChangeCost(1, Notes(100)) == 0, "identical decoded shapes cost zero");
        Expect(received.ChangeCost(1, Notes(50)) == 0 && received.ChangeCost(1, Array.Empty<MapShape>()) == 0,
            "removals are free");
        Expect(received.ChangeCost(1, Notes(100, 101)) == 100, "rotating every source ID costs one hundred even with identical content");
        Expect(received.ChangeCost(1, Notes(100, text: "edited")) == 100, "changing every note costs one hundred");
        var mixed = Notes(99);
        mixed[0] = Notes(1, 101)[0];
        mixed[1] = Notes(1, 2, "edited")[0];
        Expect(received.ChangeCost(1, mixed) == 2, "one add and one change cost two while removals remain free");
        Expect(received.ChangeCost(2, Notes(100)) == 100, "another owner cannot borrow existing source IDs");
        Expect(received.ChangeCost(1, Notes(100).Reverse().ToArray()) == 0, "draw order alone does not replace shapes");
        for (ulong owner = 2; owner <= 5; owner++) Expect(received.TryReplace(owner, Notes(100)), "fill display shares");
        Expect(received.ChangeCost(1, Notes(100)) == 0, "hidden shapes are compared against the whole received owner set");
    }

    private static void PeerRefill()
    {
        Expect(DrawingSnapshotBudget.PeerCapacity == 200 && DrawingSnapshotBudget.PeerRefillPerSecond == 100,
            "peer policy permits an initial burst of two hundred and one hundred changes per second thereafter");
        var budget = new DrawingSnapshotBudget();
        var peer = new DrawingSnapshotBudget.Peer();
        var received = new ReceivedDrawings();
        for (uint i = 0; i < 3; i++)
        {
            peer.Pending = Snapshot(i + 1, Notes(100, text: i.ToString()));
            Expect(Apply(budget, peer, received, i * 0.5) != null, "capacity plus refill admits three initial full snapshots");
        }
        peer.Pending = Snapshot(4, Notes(100, text: "deferred"));
        Expect(Apply(budget, peer, received, 1.5) == null, "fifty tokens cannot admit one hundred changes");
        peer.Pending = Snapshot(5, Notes(100, text: "newest"));
        Expect(Apply(budget, peer, received, 1.75) == null && peer.Pending.Sequence == 5, "failed retries preserve newest state");
        Expect(Apply(budget, peer, received, 2)?.Sequence == 5, "refill eventually delivers newest without partial charges");
        peer.Pending = Snapshot(6, Notes(100, text: "newest"));
        Expect(Apply(budget, peer, received, 2.5) != null, "unchanged snapshots need no change tokens");
        peer.Pending = Snapshot(7, Array.Empty<MapShape>());
        Expect(Apply(budget, peer, received, 3) != null && received.Shapes.Count == 0, "owner deletion costs zero");
        peer.Pending = Snapshot(8, Notes(100));
        Expect(Apply(budget, peer, received, 100) != null, "idle refill caps at capacity and permits another full snapshot");
        for (uint i = 0; i < 2; i++)
        {
            peer.Pending = Snapshot(9 + i, Notes(100, text: i.ToString()));
            Expect(Apply(budget, peer, received, 100.5 + i * 0.5) != null, "two hundred burst tokens plus refill remain available after idle");
        }
        peer.Pending = Snapshot(11, Notes(100, text: "over cap"));
        Expect(Apply(budget, peer, received, 101.5) == null, "long idle cannot bank tokens above two hundred");
    }

    private static void GlobalRefill()
    {
        Expect(DrawingSnapshotBudget.GlobalCapacity == 400 && DrawingSnapshotBudget.GlobalRefillPerSecond == 200,
            "global policy permits four hundred initial changes and two hundred per second thereafter");
        var budget = new DrawingSnapshotBudget();
        var received = new ReceivedDrawings();
        for (ulong owner = 1; owner <= 4; owner++)
        {
            var peer = new DrawingSnapshotBudget.Peer { Pending = Snapshot(1, Notes(100)) };
            Expect(Apply(budget, peer, received, 0, owner) != null, "four simultaneous full pairings fit global burst");
        }
        var waiting = new DrawingSnapshotBudget.Peer { Pending = Snapshot(1, Notes(100)) };
        Expect(Apply(budget, waiting, received, 0, 5) == null && Apply(budget, waiting, received, 0.4, 5) == null,
            "global exhaustion defers another owner without consuming its tokens");
        waiting.Pending = Snapshot(2, Notes(100, text: "latest"));
        Expect(Apply(budget, waiting, received, 0.5, 5)?.Sequence == 2, "one hundred global tokens refill in half a second");
        waiting.Pending = Snapshot(3, Notes(100, text: "next"));
        Expect(Apply(budget, waiting, received, 1, 5)?.Sequence == 3, "global rejection never spent peer tokens");
    }

    private static void OrdinarySharing()
    {
        var state = new DrawingPeerState(Epoch, Array.Empty<string>(), new[] { "a" });
        var other = new DrawingPeer(Epoch, Array.Empty<string>(), new[] { "a" });
        Expect(state.Protocol.Receive(other.CreateHello(), out var ack, out _) && ack != null, "hello accepted");
        Expect(other.Receive(ack!, out var confirmation, out _) && confirmation != null, "ack accepted");
        Expect(state.Protocol.Receive(confirmation!, out _, out _), "pairing completed");
        state.ObserveHandshake();
        var budget = new DrawingSnapshotBudget();
        var received = new ReceivedDrawings();
        var shapes = Notes(100);
        state.Protocol.RecordSentSnapshot(shapes);
        state.ReceivedSnapshots.Pending = Snapshot(1, shapes);
        Expect(Apply(budget, state.ReceivedSnapshots, received, 0) != null, "full new pairing has no delay");
        for (uint i = 1; i <= 20; i++)
        {
            var edits = Notes(100);
            edits[0] = Notes(1, text: i.ToString())[0];
            state.ReceivedSnapshots.Pending = Snapshot(i + 1, edits);
            Expect(Apply(budget, state.ReceivedSnapshots, received, i * 0.5) != null,
                "a few edits per second apply at sender cadence without extra latency");
        }
        state.ReceivedSnapshots.Pending = Snapshot(22, Notes(100, text: "pending"));
        Expect(Apply(budget, state.ReceivedSnapshots, received, 10.2) == null, "snapshot is waiting for cadence");
        Expect(state.Protocol.Receive(other.CreateHello(), out var heartbeatAck, out _) && heartbeatAck != null &&
            heartbeatAck.Kind == DrawingPacketKind.Acknowledgement, "heartbeat and acknowledgement bypass pending snapshot");
        var erase = new DrawingPacket(DrawingPacketKind.DeleteRequest, Epoch, other.LocalSession,
            state.Protocol.LocalSession, 1, Array.Empty<string>(), Array.Empty<MapShape>(), targetShapeId: 1, targetChannel: "a");
        Expect(state.Protocol.TryReceiveDelete(erase), "collaborative erase accepted while snapshot waits");
        Expect(state.ReceivedSnapshots.Pending.Sequence == 22 && received.Shapes.Count == 100,
            "erase request does not prematurely replace received owner drawings");
        state.ReceivedSnapshots.Pending = Snapshot(23, Array.Empty<MapShape>());
        Expect(Apply(budget, state.ReceivedSnapshots, received, 10.5)?.Sequence == 23 && received.Shapes.Count == 0,
            "owner confirmation replaces pending content and applies on time");
    }

    private static void Restart()
    {
        var state = new DrawingPeerState(Epoch, Array.Empty<string>(), new[] { "a" });
        var budget = new DrawingSnapshotBudget();
        var received = new ReceivedDrawings();
        state.ReceivedSnapshots.Pending = Snapshot(1, Notes(100));
        Expect(Apply(budget, state.ReceivedSnapshots, received, 0) != null, "initial state applied");
        state.ReceivedSnapshots.Pending = Snapshot(2, Notes(100, text: "stale"));
        state.Restart();
        received.Remove(1);
        Expect(state.ReceivedSnapshots.Pending == null, "transport restart discards old-session pending state");
        state.ReceivedSnapshots.Pending = Snapshot(1, Notes(100));
        Expect(Apply(budget, state.ReceivedSnapshots, received, 0.2) == null, "restart does not reset cadence");
        Expect(Apply(budget, state.ReceivedSnapshots, received, 0.5) != null, "first full snapshot after new pairing still fits");
        received.Remove(1);
        state.Restart();
        state.ReceivedSnapshots.Pending = Snapshot(1, Notes(100));
        Expect(Apply(budget, state.ReceivedSnapshots, received, 1) != null, "refill admits next full pairing");
        state.Restart();
        received.Remove(1);
        state.ReceivedSnapshots.Pending = Snapshot(1, Notes(100));
        Expect(Apply(budget, state.ReceivedSnapshots, received, 1.5) == null, "restarts cannot refill the change bucket");
        state.ObserveHandshake();
        Expect(state.ReceivedSnapshots.Pending == null, "handshake revision also discards pending state");
    }

    private static void AdversarialNotes()
    {
        var budget = new DrawingSnapshotBudget();
        var peer = new DrawingSnapshotBudget.Peer();
        var received = new ReceivedDrawings();
        var applied = new List<double>();
        var changes = 0;
        for (var tick = 0; tick < 50; tick++)
        {
            var now = tick / 5.0;
            peer.Pending = Snapshot((uint)tick + 1, Notes(100, tick * 100 + 1, tick.ToString()));
            var cost = received.ChangeCost(1, peer.Pending.Shapes);
            Expect(cost == 100, "changing and rotating every note always costs one hundred");
            if (Apply(budget, peer, received, now) == null) continue;
            changes += cost;
            applied.Add(now);
            Expect(changes <= 200 + 100 * now && changes <= 400 + 200 * now, "all applications respect both cumulative token budgets");
        }
        Expect(applied.Count == 11 && changes == 1100, "ten seconds of five-hertz churn applies exactly eleven snapshots and eleven hundred changes");
        Expect(peer.Pending?.Sequence == 50, "final rotating-ID state is retained after sender stops");
        Expect(Apply(budget, peer, received, 10)?.Sequence == 50, "first due refilled tick delivers final state");
        changes += 100;
        applied.Add(10);
        Expect(changes == 1200 && changes <= 200 + 100 * 10 && changes <= 400 + 200 * 10,
            "final delivery also fits both cumulative change budgets");
        foreach (var start in applied)
            Expect(applied.Count(time => time >= start && time < start + 1) <= 2, "every half-open one-second interval applies at most two snapshots");
        Expect(received.Shapes.Cast<TextNote>().All(note => note.Text == "49") && peer.Pending == null,
            "final newest state is fully visible and no superseded update remains");
        Expect(Apply(budget, peer, received, 10.2) == null, "final state applies exactly once");
    }
}
