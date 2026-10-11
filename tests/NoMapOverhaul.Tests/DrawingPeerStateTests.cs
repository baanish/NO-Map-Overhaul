using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingPeerStateTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("late drawing hello restores exhausted initial attempts", LateHello),
        ("restarted drawing handshake recovers a dropped confirming ack", DroppedConfirmation),
        ("drawing transport failures back off and rotate sessions before retry", SendRecovery),
        ("queued hello failure recovers without restarting duplicate callbacks", QueuedFailure),
        ("unknown drawing peers retain three probes across failed transports", UnknownProbeBudget),
        ("compatible drawing hello during backoff restores probes without shortening it", HelloDuringBackoff),
        ("known drawing peers can reconnect after asynchronous transport failure", CompatibleRecovery),
        ("drawing session restart retains receive and nonce churn budgets", RestartBudgets),
        ("a stall's backlog is received until the flood limit, which flags the peer once per second", FloodLimit),
        ("erase eligibility respects handshake, request cooldown, sequence, and transport retry", DeleteEligibility),
    };

    private static readonly SharedMissionEpoch Epoch = new(42, 100.25, 7);
    private static DrawingPeerState State() => new(Epoch, Array.Empty<string>(), new[] { "command" });
    private static DrawingPeer Peer() => new(Epoch, Array.Empty<string>(), new[] { "command" });

    private static DrawingPacket Reply(DrawingPeer peer, DrawingPacket packet)
    {
        Expect(peer.Receive(packet, out var reply, out var snapshot), "handshake must be accepted");
        Expect(reply != null && snapshot == null, "handshake must reply without a snapshot");
        return reply!;
    }

    private static void Connect(DrawingPeer first, DrawingPeer second)
    {
        var ack = Reply(first, second.CreateHello());
        var confirmation = Reply(second, ack);
        var echo = Reply(first, confirmation);
        Expect(second.Receive(echo, out var response, out _) && response == null, "acks stop echoing");
        Expect(first.IsReady && second.IsReady, "both peers are ready");
    }

    private static void Exhaust(DrawingPeerState state)
    {
        Expect(state.TryHello(0) && state.TryHello(5) && state.TryHello(10), "three initial capability attempts");
        Expect(!state.TryHello(15) && !state.TryHello(100), "unmodified peers get no endless capability probes");
    }

    private static void LateHello()
    {
        var state = State();
        Exhaust(state);
        var other = Peer();
        var before = state.Protocol.Revision;
        var ack = Reply(state.Protocol, other.CreateHello());
        Expect(state.Protocol.Revision == before + 1, "first incoming candidate starts a new handshake revision");
        var confirmation = Reply(other, ack);
        Expect(!state.Protocol.IsReady && other.IsReady, "confirming ack is still in transit");
        Expect(state.TryHello(101), "new incoming candidate restores outgoing retries");
        var retryAck = Reply(other, state.Protocol.CreateHello());
        var final = Reply(state.Protocol, retryAck);
        Expect(other.Receive(final, out var response, out _) && response == null, "ready peer accepts final ack");
        Expect(state.Protocol.IsReady, "late handshake converges even when original confirmation was lost");
        Expect(state.Protocol.Receive(confirmation, out response, out _) && response == null, "delayed same-session confirmation remains harmless");
    }

    private static void DroppedConfirmation()
    {
        var state = State();
        Exhaust(state);
        var old = Peer();
        Connect(state.Protocol, old);
        for (var now = 105; now <= 130; now += 5) Expect(state.TryHello(now), "ready heartbeats keep running");
        var oldSession = state.Protocol.LocalSession;
        var replacement = Peer();
        var ack = Reply(state.Protocol, replacement.CreateHello());
        Reply(replacement, ack); // The confirming ack is deliberately absent from the receiver.
        Expect(!state.Protocol.IsReady && replacement.IsReady && state.Protocol.LocalSession != oldSession, "restart has a fresh half-open session");
        var heartbeatAck = Reply(state.Protocol, replacement.CreateHello());
        Expect(replacement.Receive(heartbeatAck, out var response, out _) && response == null, "ready heartbeats alone cannot finish the other side");
        state.ObserveHandshake();
        Expect(state.TryHello(131), "restart re-enables the responder's own hello");
        var retryAck = Reply(replacement, state.Protocol.CreateHello());
        var final = Reply(state.Protocol, retryAck);
        Expect(replacement.Receive(final, out response, out _) && response == null, "confirmation does not loop");
        Expect(state.Protocol.IsReady && replacement.IsReady, "dropped-confirmation restart converges");
    }

    private static void SendRecovery()
    {
        var state = State();
        var oldSession = state.Protocol.LocalSession;
        Expect(!state.RecordSend(false, 0) && !state.RecordSend(false, 1), "two failures do not restart");
        Expect(!state.RecordSend(true, 2), "success clears the failure streak");
        Expect(!state.RecordSend(false, 3) && !state.RecordSend(false, 4), "fresh streak begins after success");
        state.Sequence = 9;
        state.DeleteSequence = 8;
        Expect(state.RecordSend(false, 5), "third consecutive failure requires native channel closure");
        Expect(state.Protocol.LocalSession != oldSession && !state.Protocol.IsReady, "failure recovery rotates the recipient nonce");
        Expect(state.Sequence == 0 && state.DeleteSequence == 0 && state.SentVersion == -1, "old snapshot and delete state are discarded");
        Expect(!state.CanTransmit(34.999) && !state.TryHello(34.999), "30 second cooldown is enforced");
        Expect(state.CanTransmit(35) && state.TryHello(35), "cooldown ends at its exact deadline");
        var replacement = Peer();
        Connect(state.Protocol, replacement);
        Expect(!state.RecordSend(true, 35), "successful retry stays connected");
        Expect(state.Protocol.IsReady && replacement.IsReady, "never-ready peer can recover without a user toggle");
    }

    private static void FloodLimit()
    {
        var state = State();
        var floods = 0;
        // Thirty seconds of drawing during a stall: two snapshots a second, a heartbeat every five, and a few erases.
        for (var i = 0; i < 36; i++)
        {
            Expect(state.CountReceive(20, out var flooded) && !flooded, "a stall's backlog is received");
        }
        for (var i = 0; i < 10; i++)
        {
            var accepted = state.CountReceive(20.5, out var flooded);
            Expect(accepted == (i < 4), "messages over the limit are dropped");
            if (flooded) floods++;
        }
        Expect(floods == 1, "the flood is reported once per window");
        Expect(state.CountReceive(21, out var later) && !later, "the next second starts a new window");
    }

    private static void RestartBudgets()
    {
        var state = State();
        var nonce = Guid.NewGuid();
        state.ReceiveCount = 4;
        state.ReceiveWindow = 12;
        state.LastHelloSession = nonce;
        state.HelloSessionChanges = 2;
        state.HelloWindow = 11;
        state.Restart();
        Expect(state.ReceiveCount == 4 && state.ReceiveWindow == 12, "transport restart cannot buy another receive budget");
        Expect(state.LastHelloSession == nonce && state.HelloSessionChanges == 2 && state.HelloWindow == 11,
            "transport restart cannot buy more distinct remote nonces");
    }

    private static void QueuedFailure()
    {
        var state = State();
        Expect(state.TryHello(0) && !state.RecordSend(true, 0), "first hello is queued successfully");
        Expect(state.TryHello(5) && !state.RecordSend(false, 5), "second hello fails synchronously");
        Expect(state.TryHello(10) && !state.RecordSend(false, 10), "two direct failures never reach the three-failure threshold");
        Expect(!state.TryHello(15), "initial hello budget is exhausted");
        var oldSession = state.Protocol.LocalSession;
        Expect(state.RecordSessionFailure(16), "asynchronous failure requires native transport cleanup even without three failed sends");
        var replacementSession = state.Protocol.LocalSession;
        Expect(replacementSession != oldSession, "failed transport rotates the drawing session");
        Expect(!state.RecordSessionFailure(20) && !state.RecordSessionFailure(46), "duplicate callbacks cannot restart before or after the deadline without new sends");
        Expect(state.Protocol.LocalSession == replacementSession, "duplicate callbacks retain the recovery session");
        Expect(!state.CanTransmit(45.999) && state.CanTransmit(46), "duplicate callbacks preserve the exact 30-second deadline");
        var other = Peer();
        Reply(state.Protocol, other.CreateHello());
        Expect(state.TryHello(46), "compatible inbound hello restores recovery after exhausted initial sends");
        Connect(state.Protocol, other);
        Expect(state.Protocol.IsReady && other.IsReady, "the logged failure sequence can reconnect without a settings reset");
    }

    private static void UnknownProbeBudget()
    {
        var state = State();
        Expect(state.TryHello(0), "first unknown peer probe");
        state.RecordSend(true, 0);
        Expect(state.RecordSessionFailure(1), "queued initial hello can fail asynchronously");
        Expect(!state.TryHello(30.999) && state.TryHello(31), "second initial probe waits exactly 30 seconds");
        state.RecordSend(true, 31);
        Expect(state.RecordSessionFailure(32), "second transport failure starts a new paced recovery");
        Expect(!state.TryHello(61.999) && state.TryHello(62), "third initial probe waits for the next deadline");
        state.RecordSend(true, 62);
        Expect(state.RecordSessionFailure(63), "final failed transport is still cleaned up");
        Expect(!state.TryHello(93) && !state.TryHello(1000), "native failure callbacks cannot buy endless probes of unmodified peers");
        Expect(state.HelloAttempts == 3, "initial unknown peer budget is exactly three across all transports");

        state = State();
        foreach (var now in new[] { 0, 5, 10 })
        {
            Expect(state.TryHello(now), "three synchronous failure probes remain five seconds apart");
            Expect(state.RecordSend(false, now) == (now == 10), "only third synchronous failure restarts");
        }
        Expect(!state.TryHello(40) && !state.TryHello(1000), "synchronous failure restart also retains the unknown peer budget");
    }

    private static void HelloDuringBackoff()
    {
        var state = State();
        Exhaust(state);
        Expect(state.RecordSessionFailure(101), "exhausted peer transport enters recovery");
        var other = Peer();
        Reply(state.Protocol, other.CreateHello());
        state.ObserveHandshake();
        Expect(state.HelloAttempts == 0, "validated incoming hello replenishes the bounded handshake budget");
        Expect(!state.CanTransmit(130.999) && !state.TryHello(130.999), "compatible traffic cannot clear transport backoff");
        Expect(state.CanTransmit(131) && state.TryHello(131), "first restored probe waits for the existing deadline");
        Expect(!state.TryHello(135.999) && state.TryHello(136), "restored probes keep five-second spacing");
        Expect(state.TryHello(141) && state.TryHello(146), "a teammate that sent a valid hello keeps retrying until paired");
    }

    private static void CompatibleRecovery()
    {
        var state = State();
        Connect(state.Protocol, Peer());
        state.ObserveHandshake();
        state.RecordSend(true, 0);
        Expect(state.RecordSessionFailure(1), "asynchronous failure restarts a previously ready peer");
        Expect(!state.TryHello(30.999) && state.TryHello(31), "known peer reconnect starts after exactly 30 seconds");
        state.RecordSend(true, 31);
        Expect(state.RecordSessionFailure(32), "new send arms failure recovery for the replacement transport");
        Expect(!state.TryHello(61.999) && state.TryHello(62), "known peer can retry the replacement transport after backoff");
        Connect(state.Protocol, Peer());
        Expect(state.Protocol.IsReady, "previously compatible peer reconnects after repeated transport failures");
    }

    private static void DeleteEligibility()
    {
        var state = State();
        Expect(!state.CanRequestDelete(0), "an incomplete handshake cannot offer shared erase");
        Connect(state.Protocol, Peer());
        Expect(state.CanRequestDelete(0), "a ready owner can accept a first request");
        state.NextDelete = 1;
        Expect(!state.CanRequestDelete(0.999) && state.CanRequestDelete(1), "hover and sends share the exact one-second cooldown");
        state.DeleteSequence = uint.MaxValue;
        Expect(!state.CanRequestDelete(1), "an exhausted sequence cannot offer an unsendable request");
        state.Restart(31);
        Connect(state.Protocol, Peer());
        Expect(!state.CanRequestDelete(30.999) && state.CanRequestDelete(31), "a ready protocol cannot bypass transport backoff");
    }
}
