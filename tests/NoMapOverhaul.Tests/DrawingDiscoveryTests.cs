using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingDiscoveryTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("an unconfirmed teammate is probed at 0, 60 and 300 seconds, then never", ProbeSchedule),
        ("channel changes don't renew an unconfirmed teammate's probes", SurvivesChannelChanges),
        ("a confirmed teammate leaves the sparse schedule", Confirmed),
        ("a new mission or faction starts discovery over", NewEpoch),
        ("a teammate whose Hello arrives after our probes ran out still pairs when our acknowledgement is lost", LostConfirmation),
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
        foreach (var time in DrawingDiscovery.ProbeOffsets) discovery.TryProbe(1, time); // A had sharing off for all three
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

    private static List<double> Probes(DrawingDiscovery discovery, ulong id, double from, double to)
    {
        var sent = new List<double>();
        for (var now = from; now <= to; now += 0.2)
            if (discovery.TryProbe(id, Math.Round(now, 1))) sent.Add(Math.Round(now, 1));
        return sent;
    }

    private static void ProbeSchedule()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        ExpectText(string.Join(",", Probes(discovery, 1, 10, 2000)), "10,70,310");
    }

    private static void SurvivesChannelChanges()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        var sent = Probes(discovery, 1, 10, 20);
        for (var change = 0; change < 50; change++)
        {
            discovery.Begin(Epoch); // every channel change restarts the session in the same mission
            sent.AddRange(Probes(discovery, 1, 20 + change * 12, 32 + change * 12));
        }
        ExpectText(string.Join(",", sent), "10,70,310");
    }

    private static void Confirmed()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        Expect(discovery.TryProbe(1, 0), "first probe");
        discovery.Confirm(1);
        Expect(discovery.IsConfirmed(1) && !discovery.TryProbe(1, 60), "a confirmed teammate is not probed");
        discovery.Begin(Epoch);
        Expect(discovery.IsConfirmed(1), "confirmation outlives a channel change");
    }

    private static void NewEpoch()
    {
        var discovery = new DrawingDiscovery();
        discovery.Begin(Epoch);
        Probes(discovery, 1, 0, 400);
        discovery.Confirm(2);
        discovery.Begin(new SharedMissionEpoch(43, 200, 7));
        Expect(!discovery.IsConfirmed(2), "a new mission forgets confirmations");
        Expect(discovery.TryProbe(1, 500), "a new mission probes again");
    }
}
