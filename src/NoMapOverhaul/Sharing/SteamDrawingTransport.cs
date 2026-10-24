using System;
using System.Runtime.InteropServices;
using Mirage;
using Mirage.SteamworksSocket;
using NuclearOption.Networking;
using Steamworks;

namespace NoMapOverhaul.Sharing;

/// <summary>Steam Networking Messages between the players' own Steam accounts: the transport sharing ships with.</summary>
internal sealed class SteamDrawingTransport : IDrawingTransport
{
    private readonly IntPtr[] _native = new IntPtr[DrawingReceiveBudget.MaxMessagesPerTick];
    private Callback<SteamNetworkingMessagesSessionRequest_t>? _request;
    private Callback<SteamNetworkingMessagesSessionFailed_t>? _failure;
    private static ulong _localSteamId;

    public string? Unavailable(NetworkManagerNuclearOption manager) =>
        !SteamConnectionOwnershipPatch.IsApplied ? "Steam connection ownership guard unavailable"
        : !SteamManager.ClientInitialized ? "Steam not initialized or multiplayer mission not ready"
        : manager.Client.SocketFactory is not SteamworksSocketFactory transport || transport.GameServer
            ? "unsupported transport: Steam player transport required"
        : null;

    public bool TryIdentify(NetworkManagerNuclearOption manager, Player local, out ulong server, out string reason)
    {
        server = 0;
        reason = "local authenticated player or faction not ready";
        if (local.SteamID == 0 || local.SteamID != LocalSteamId()) return false;
        if (manager.Server.Active)
        {
            server = local.SteamID;
        }
        else if (manager.Client.Player.ConnectionHandle is SteamConnection connection)
        {
            // The server's ID only marks which game the sharing session belongs to; drawings go between the players'
            // own accounts, so a dedicated server's game-server account serves as well as a player host's.
            reason = "server Steam identity not eligible";
            server = connection.SteamID.m_SteamID;
            if (!connection.SteamID.BIndividualAccount() && !connection.SteamID.BGameServerAccount()) return false;
        }
        else
        {
            reason = "Steam connection not ready";
            return false;
        }

        reason = server != 0 ? "active Steam team session" : "server identity missing";
        return server != 0;
    }

    /// <summary>
    /// The signed-in account, asked of Steam once: it can't change while the game runs, and the sharing checks read it
    /// every frame and for every message, where each Steam call costs time and waits on Steam's lock.
    /// </summary>
    private static ulong LocalSteamId()
    {
        if (_localSteamId == 0) _localSteamId = SteamUser.GetSteamID().m_SteamID;
        return _localSteamId;
    }

    public ulong IdOf(Player player) =>
        player.SteamID != 0 && player.CSteamID.BIndividualAccount() ? player.SteamID : 0;

    /// <summary>
    /// Has Steam fetch its relay network setup for the player's own networking. The game sets it up only for its
    /// game-server connection and its lobbies, so without this the first teammate's session fetches it mid-flight, with
    /// Steam's networking lock held: a 1.4 s freeze was logged that way, inside ReceiveMessagesOnChannel. Steam fetches
    /// in the background, so this call returns at once.
    /// </summary>
    public bool Prepare()
    {
        if (!SteamManager.ClientInitialized) return false;
        SteamNetworkingUtils.InitRelayNetworkAccess();
        return true;
    }

    public int Receive(int channel, DrawingMessage[] into, int max)
    {
        var count = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, _native, max);
        for (var i = 0; i < count; i++)
        {
            if (_native[i] == IntPtr.Zero)
            {
                into[i] = default;
                continue;
            }

            var message = SteamNetworkingMessage_t.FromIntPtr(_native[i]);
            into[i] = new DrawingMessage(message.m_identityPeer.GetSteamID64(), message.m_nChannel, message.m_pData, message.m_cbSize);
        }

        return count;
    }

    public void ReleaseReceived()
    {
        for (var i = 0; i < _native.Length; i++)
        {
            if (_native[i] == IntPtr.Zero) continue;
            SteamNetworkingMessage_t.Release(_native[i]);
            _native[i] = IntPtr.Zero;
        }
    }

    public bool Send(ulong id, int channel, byte[] bytes, out string result)
    {
        var identity = Identity(id);
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            // A probe that found the session broken, by an earlier unanswered probe from either side, would fail at once
            // without the restart flag, and a teammate who enabled sharing late would never pair. DrawingDiscovery's
            // three probes, not the flag, bound the attempts made towards a teammate without the mod.
            var sent = SteamNetworkingMessages.SendMessageToUser(ref identity, pin.AddrOfPinnedObject(), (uint)bytes.Length,
                Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession, channel);
            result = sent.ToString();
            return sent == EResult.k_EResultOK;
        }
        finally { pin.Free(); }
    }

    public string ConnectionState(ulong id)
    {
        var identity = Identity(id);
        var state = SteamNetworkingMessages.GetSessionConnectionInfo(ref identity, out var info, out _);
        return $"state={state}; endReason={info.m_eEndReason}";
    }

    public bool TryGetSessionStatus(ulong id, out DrawingSessionStatus status)
    {
        status = default;
        if (!SteamManager.ClientInitialized) return false;
        var identity = Identity(id);
        var state = SteamNetworkingMessages.GetSessionConnectionInfo(ref identity, out _, out var quick);
        // No session, as with a teammate without the mod once their probe times out: Steam leaves the status zeroed,
        // and a zero quality would read as total packet loss.
        if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None) return false;
        status = new DrawingSessionStatus(StateName(state), quick.m_nPing, quick.m_flConnectionQualityLocal,
            quick.m_cbPendingReliable, quick.m_cbSentUnackedReliable, quick.m_usecQueueTime.m_SteamNetworkingMicroseconds / 1000.0);
        return true;
    }

    private static string StateName(ESteamNetworkingConnectionState state) => state switch
    {
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None => "None",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting => "Connecting",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute => "FindingRoute",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected => "Connected",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer => "ClosedByPeer",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally => "ProblemDetectedLocally",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FinWait => "FinWait",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Linger => "Linger",
        ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Dead => "Dead",
        _ => "Unknown",
    };

    public bool Accept(ulong id)
    {
        var identity = Identity(id);
        return SteamNetworkingMessages.AcceptSessionWithUser(ref identity);
    }

    public void Close(ulong id, int channel)
    {
        if (!SteamManager.ClientInitialized) return;
        var identity = Identity(id);
        SteamNetworkingMessages.CloseChannelWithUser(ref identity, channel);
    }

    public void Listen(Action<ulong> sessionRequested, Action<ulong, string> sessionFailed)
    {
        // An identity that can't be read reaches the handlers as 0, which no teammate has, rather than escaping into
        // Steamworks' callback dispatch.
        _request = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(request =>
            sessionRequested(SteamId(ref request.m_identityRemote)));
        _failure = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(failure =>
            sessionFailed(SteamId(ref failure.m_info.m_identityRemote),
                $"state={failure.m_info.m_eState}; endReason={failure.m_info.m_eEndReason}"));
    }

    public void StopListening()
    {
        _request?.Dispose(); _request = null;
        _failure?.Dispose(); _failure = null;
    }

    public bool Listening => _request != null || _failure != null;

    private static ulong SteamId(ref SteamNetworkingIdentity identity)
    {
        try { return identity.GetSteamID64(); }
        catch (Exception) { return 0; }
    }

    private static SteamNetworkingIdentity Identity(ulong id)
    {
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(id);
        return identity;
    }
}
