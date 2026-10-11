using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingPeerQuarantineTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("drawing quarantine survives timeout and roster rejoin until local reset", RosterRejoin),
        ("drawing quarantine has a fixed fail-closed identity budget", Capacity),
    };

    private static void RosterRejoin()
    {
        var quarantine = new DrawingPeerQuarantine();
        var epoch = new SharedMissionEpoch(42, 100.25, 7);
        var channels = new[] { "command" };
        var roster = new Dictionary<ulong, DrawingPeerState> { [7] = new(epoch, channels, channels) };
        Expect(!quarantine.Reject(7), "one rejected ID does not stop unrelated sharing");
        roster[7].Restart();
        Expect(!quarantine.Allows(7) && quarantine.Allows(8), "timeout recovery does not admit the rejected ID");
        roster.Remove(7);
        roster.Add(7, new(epoch, channels, channels));
        roster[7].RecordSend(true, 1000);
        Expect(!quarantine.Allows(7), "a recreated roster entry and successful send cannot clear quarantine");
        quarantine.Clear();
        Expect(quarantine.Allows(7) && roster[7].TryHello(1000), "local sharing reset permits a fresh capability handshake");
    }

    private static void Capacity()
    {
        var quarantine = new DrawingPeerQuarantine();
        Expect(DrawingPeerQuarantine.MaxRejectedPeers == 64, "quarantine retains at most 64 distinct IDs before stopping sharing");
        for (ulong id = 1; id < 64; id++) Expect(!quarantine.Reject(id), "below the rejection cap");
        Expect(quarantine.Allows(100), "unrelated peer remains allowed below the cap");
        Expect(quarantine.Reject(64), "64th distinct rejected ID stops all sharing");
        Expect(!quarantine.Allows(100) && !quarantine.Allows(64), "capacity exhaustion fails closed for every ID");
        Expect(quarantine.Reject(65) && !quarantine.Allows(65), "further IDs cannot evade the bounded record");
        quarantine.Clear();
        Expect(quarantine.Allows(1) && quarantine.Allows(65), "explicit local reset starts an empty rejection record");
    }
}
