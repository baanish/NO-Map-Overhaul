using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingDiscoveryTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("an unconfirmed teammate is probed once per mission", ProbeOnce),
        ("channel changes don't renew an unconfirmed teammate's probe", SurvivesChannelChanges),
        ("a confirmed teammate leaves the sparse schedule", Confirmed),
        ("a new mission or faction starts discovery over", NewEpoch),
        ("a teammate whose Hello arrives after our probe went unanswered still pairs when our acknowledgement is lost", LostConfirmation),
    };

    private static DrawingPacket Reply(DrawingPeer peer, DrawingPacket packet)
    {
        Expect(peer.Receive(packet, out var reply, out _) && reply != null, "handshake accepted with a reply");
        return reply!;
    }

    private static void LostConfirmation()
    {
        var channels = new[] { "command" };
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        discovery.TryProbe(1); // A had sharing off
        var b = new DrawingPeerState(Epoch, Array.Empty<string>(), channels);
        var a = new DrawingPeer(Epoch, Array.Empty<string>(), channels);
        var ack = Reply(b.Protocol, a.CreateHello()); // A enables sharing later and says Hello
        discovery.Accepted(1, DrawingPacketKind.Hello, b.Protocol.IsReady);
        Reply(a, ack); // A pairs; its confirming acknowledgement gets a non-OK send and never arrives
        Expect(a.IsReady && !b.Protocol.IsReady && discovery.IsConfirmed(1), "a Hello confirms the sender runs the mod");
        Expect(b.TryHello(305), "a confirmed teammate gets handshake retries");
        var answer = Reply(a, b.Protocol.CreateHello());
        Expect(b.Protocol.Receive(answer, out _, out _) && b.Protocol.IsReady, "the retry completes the pairing");
    }

    private static readonly SharedMissionEpoch Epoch = new(42, 100.25, 7);

    private static int Probes(DrawingDiscovery discovery, ulong id, int ticks)
    {
        var sent = 0;
        for (var tick = 0; tick < ticks; tick++)
            if (discovery.TryProbe(id)) sent++;
        return sent;
    }

    private static void ProbeOnce()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        ExpectText(Probes(discovery, 1, 10000).ToString(), "1");
        ExpectText(Probes(discovery, 2, 10).ToString(), "1");
    }

    private static void SurvivesChannelChanges()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        var sent = Probes(discovery, 1, 10);
        for (var change = 0; change < 50; change++)
        {
            discovery.Begin(Epoch); // every channel change restarts the session in the same mission
            sent += Probes(discovery, 1, 60);
        }
        ExpectText(sent.ToString(), "1");
    }

    private static void Confirmed()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        Expect(discovery.TryProbe(1), "first probe");
        discovery.Confirm(1);
        Expect(discovery.IsConfirmed(1) && !discovery.TryProbe(1), "a confirmed teammate is not probed");
        discovery.Begin(Epoch);
        Expect(discovery.IsConfirmed(1), "confirmation outlives a channel change");
    }

    private static void NewEpoch()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        Probes(discovery, 1, 10);
        discovery.Confirm(2);
        discovery.Begin(new SharedMissionEpoch(43, 200, 7));
        Expect(!discovery.IsConfirmed(2), "a new mission forgets confirmations");
        Expect(discovery.TryProbe(1), "a new mission probes again");
    }
}
