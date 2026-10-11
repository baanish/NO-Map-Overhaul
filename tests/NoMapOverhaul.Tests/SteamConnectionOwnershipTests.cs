using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class SteamConnectionOwnershipTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("Steam client callbacks require the gameplay connection even for the same peer", ClientConnections),
        ("Steam callbacks reject invalid connection and listen socket handles", InvalidHandles),
        ("Steam server ignores foreign outgoing and incoming connection lifecycles", ServerForeignConnections),
        ("Steam server accepts its own incoming lifecycle before and after registration", ServerIncomingLifecycle),
        ("Steam server terminal callbacks retain ownership when listen metadata is absent", TerminalMetadata),
        ("Steam server cannot adopt an unknown terminal connection", UnknownTerminalConnection),
    };

    private static void ClientConnections()
    {
        const uint gameplayConnection = 10;
        const uint drawingConnectionToSamePeer = 11;
        var callbacks = new[] { drawingConnectionToSamePeer, gameplayConnection, drawingConnectionToSamePeer };
        var delivered = callbacks.Where(callback => SteamConnectionOwnership.ClientOwns(gameplayConnection, callback)).ToArray();
        Expect(delivered.SequenceEqual(new[] { gameplayConnection }),
            "foreign connecting, connected, and terminal notifications cannot advance or disconnect gameplay");
        Expect(!SteamConnectionOwnership.ClientOwns(12, gameplayConnection), "a prior session handle cannot affect a reconnected client");
    }

    private static void InvalidHandles()
    {
        Expect(!SteamConnectionOwnership.ClientOwns(0, 0), "an uninitialized client does not own the invalid handle");
        Expect(!SteamConnectionOwnership.ClientOwns(10, 0), "invalid callback handle is never gameplay");
        Expect(!SteamConnectionOwnership.ServerOwns(0, 0, 10, false, false), "two invalid listen sockets do not establish ownership");
        Expect(!SteamConnectionOwnership.ServerOwns(20, 20, 0, false, false), "an owned listen socket cannot make an invalid connection valid");
        Expect(!SteamConnectionOwnership.ServerOwns(0, 0, 0, true, true), "known-connection fallback cannot admit an invalid connection");
    }

    private static void ServerForeignConnections()
    {
        foreach (var terminal in new[] { false, true })
        {
            Expect(!SteamConnectionOwnership.ServerOwns(20, 0, 11, false, terminal), "foreign outgoing connection stays outside gameplay");
            Expect(!SteamConnectionOwnership.ServerOwns(20, 21, 12, false, terminal), "another listener's incoming connection stays outside gameplay");
        }
    }

    private static void ServerIncomingLifecycle()
    {
        Expect(SteamConnectionOwnership.ServerOwns(20, 20, 10, false, false), "own listener permits accepting a new incoming connection");
        Expect(SteamConnectionOwnership.ServerOwns(20, 20, 10, true, false), "registration preserves connected notification");
        Expect(SteamConnectionOwnership.ServerOwns(20, 20, 10, true, true), "own listener preserves disconnect notification");
    }

    private static void TerminalMetadata()
    {
        Expect(SteamConnectionOwnership.ServerOwns(20, 0, 10, true, true), "registered gameplay handle permits terminal cleanup without listen metadata");
        Expect(SteamConnectionOwnership.ServerOwns(20, 21, 10, true, true), "known handle establishes ownership even if terminal metadata differs");
        Expect(!SteamConnectionOwnership.ServerOwns(20, 0, 10, true, false), "missing listen metadata cannot cause a duplicate accept or registration");
    }

    private static void UnknownTerminalConnection()
    {
        Expect(!SteamConnectionOwnership.ServerOwns(20, 0, 11, false, true), "unregistered foreign terminal handle cannot close gameplay or enter its poll group");
        Expect(SteamConnectionOwnership.ServerOwns(20, 20, 10, false, true),
            "own listener preserves a pre-registration terminal event for Mirage's existing unknown-handle cleanup");
    }
}
