using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Mirage;
using Mirage.SteamworksSocket;
using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.MapTools;
using NuclearOption.Networking;
using Steamworks;
using UnityEngine;

namespace NoMapOverhaul.Sharing;

internal sealed class SteamDrawingSharing : IDisposable
{
    // A separate Messages channel never enters the game's Mirage message decoder.
    private const int Channel = 0x4E4D4F;
    private const int MaxPeers = ReceivedDrawings.MaxPeers;
    private readonly MapToolHost _host;
    private readonly ReceivedDrawings _received;
    private readonly ManualLogSource _log;
    private readonly Dictionary<ulong, DrawingPeerState> _peers = new();
    private readonly DrawingPeerQuarantine _quarantine = new();
    private readonly DrawingDiscovery _discovery = new();
    private readonly List<ulong> _roster = new(MaxPeers);
    private readonly List<ulong> _removed = new(MaxPeers);
    private readonly Dictionary<ulong, (int Index, uint Sequence)> _newestSnapshot = new(MaxPeers);
    private static readonly string[] ReadyStatuses = CreateReadyStatuses();
    private readonly IntPtr[] _messages = new IntPtr[DrawingReceiveBudget.MaxMessagesPerTick];
    private readonly DrawingReceiveBudget _receiveBudget;
    private readonly DrawingNetworkSchedule _schedule = new();
    private readonly DrawingSnapshotBudget _snapshotBudget = new();
    private readonly DrawingNetworkLog _networkLog;
    private Callback<SteamNetworkingMessagesSessionRequest_t>? _request;
    private Callback<SteamNetworkingMessagesSessionFailed_t>? _failure;
    private SharedMissionEpoch _epoch;
    private object? _mission;
    private INetworkPlayer? _connection;
    private FactionHQ? _hq;
    private ulong _server;
    private IReadOnlyList<string>? _broadcastConfiguration;
    private IReadOnlyList<string>? _receiveConfiguration;
    private bool _active;
    private bool _faulted;
    private bool _wanted;
    private int _nextDiagnosticPeer;
    private string? _diagnosticState;
    private double _nextDiagnosticSummary;
    private double _lastUpdate;

    /// <summary>
    /// A frame that took this long, or a Steam call that blocked past <see cref="SlowCallSeconds"/>, is logged even with
    /// network logging off: a client once froze for nearly a minute while sharing, long enough for the server to drop it.
    /// </summary>
    private const double FrameGapSeconds = 5;
    private const double SlowCallSeconds = 0.25;
    private static ManualLogSource? _slowCallLog;

    private static void LogSlowCall(string call, long start)
    {
        var seconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (seconds > SlowCallSeconds) _slowCallLog?.LogWarning($"Steam {call} blocked the game for {seconds:F2}s.");
    }

    public SteamDrawingSharing(MapToolHost host, ReceivedDrawings received, ManualLogSource log)
    {
        _host = host; _received = received; _log = log;
        _slowCallLog = log;
        _networkLog = new DrawingNetworkLog(message => _log.LogInfo(message));
        _receiveBudget = new DrawingReceiveBudget(count =>
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var received = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _messages, count);
            LogSlowCall("ReceiveMessagesOnChannel", start);
            return received;
        });
    }

    public void Update(bool enabled, bool logNetwork)
    {
        var now = Time.unscaledTimeAsDouble;
        if (_lastUpdate > 0 && now - _lastUpdate > FrameGapSeconds)
            _log.LogWarning($"No frame for {now - _lastUpdate:F1}s; sharing active={_active}.");
        _lastUpdate = now;
        var wasLogging = _networkLog.Enabled;
        _networkLog.Update(logNetwork, now);
        if (!wasLogging && _networkLog.Enabled)
        {
            _diagnosticState = null;
            _nextDiagnosticSummary = 0;
            _networkLog.Write(now, $"protocol={DrawingProtocol.Version}; build={typeof(SteamDrawingSharing).Assembly.ManifestModule.ModuleVersionId}; polling=5Hz.");
        }
        var wanted = enabled && _host.SharingEligible;
        if (!wanted) _faulted = false;
        _wanted = wanted;
        try
        {
            var reason = !enabled ? "mod or map tools disabled, or performance test active"
                : !wanted ? "sharing off, invalid channels, or empty channel lists" : "faulted";
            if (!wanted || _faulted || !TryContext(out var manager, out var local, out var epoch, out var server, out reason))
            {
                LogState(reason);
                Reset(reason);
                _host.SharingStatus = _faulted ? "Sharing unavailable; toggle sharing to retry."
                    : !SteamConnectionOwnershipPatch.IsApplied ? "Sharing unavailable on this game build."
                    : "Waiting for a Steam team session.";
                return;
            }
            var broadcast = _host.Channels.Names;
            var receive = _host.ReceiveChannels.Names;
            if (!_active || !_epoch.Equals(epoch) || _mission != MissionManager.CurrentMission ||
                !ReferenceEquals(_connection, manager.Client.Player) || _hq != local.HQ || _server != server ||
                !ReferenceEquals(_broadcastConfiguration, broadcast) || !ReferenceEquals(_receiveConfiguration, receive))
            {
                var resetReason = _active && (!ReferenceEquals(_broadcastConfiguration, broadcast) ||
                    !ReferenceEquals(_receiveConfiguration, receive)) ? "channel configuration changed" : "mission or connection changed";
                Reset(resetReason);
                _epoch = epoch; _mission = MissionManager.CurrentMission; _connection = manager.Client.Player;
                _hq = local.HQ; _server = server;
                _broadcastConfiguration = broadcast; _receiveConfiguration = receive;
                _discovery.Begin(epoch);
                _active = true;
                _request = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
                _failure = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
                if (_networkLog.Enabled) _networkLog.Write(now, $"session started: role={(manager.Server.Active ? "host" : "client")}; broadcast={broadcast.Count}; receiveOnly={receive.Count}.");
            }
            LogState("active Steam team session");
            if (!_schedule.TryTick(now)) return;
            RefreshRoster(local);
            foreach (var entry in _peers)
            {
                var peer = entry.Value;
                if (_quarantine.Allows(entry.Key) && peer.CanTransmit(now) && peer.Protocol.IsReady && now - peer.LastReceived > 15)
                {
                    if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: expired after {now - peer.LastReceived:F1}s without an accepted packet; restarting.");
                    _received.Remove(entry.Key);
                    CloseChannel(entry.Key);
                    peer.Restart();
                }
            }
            Receive();
            var ready = 0;
            var unshared = 0;
            var otherVersion = 0;
            foreach (var entry in _peers)
            {
                var peer = entry.Value;
                if (!Authorized(entry.Key)) continue;
                if (peer.OtherVersion) otherVersion++;
                if (!peer.CanTransmit(now)) continue;
                peer.ObserveHandshake();
                if (_discovery.IsConfirmed(entry.Key) ? peer.TryHello(now)
                    : !peer.Protocol.IsReady && _discovery.TryProbe(entry.Key, now))
                {
                    Send(entry.Key, peer, peer.Protocol.CreateHello(peer.Sequence));
                    peer.Advertised = peer.Sequence;
                }
                if (!peer.Protocol.IsReady) continue;
                ready++;
                if (now >= peer.NextSnapshot && peer.SentVersion != _host.Context.Shapes.Version)
                {
                    peer.NextSnapshot = now + DrawingNetworkSchedule.SnapshotInterval;
                    SendSnapshot(entry.Key, peer);
                }
                unshared = Math.Max(unshared, peer.Unshared);
            }
            _host.SharingStatus = _faulted ? "Sharing unavailable; toggle sharing to retry."
                : unshared > 0 ? $"Sharing limit reached: your newest {unshared} drawing(s) aren't shared."
                : otherVersion > 0 ? $"{ReadyStatuses[ready]} {otherVersion} teammate(s) on another NO Map Overhaul version."
                : ReadyStatuses[ready];
            if (_networkLog.Enabled && now >= _nextDiagnosticSummary)
            {
                _nextDiagnosticSummary = now + DrawingNetworkLog.WindowSeconds;
                _networkLog.Write(now, $"summary: eligible={_roster.Count}; ready={ready}; ownedShapes={_host.Context.Shapes.Shapes.Count}; receivedShapes={_received.Shapes.Count}.");
                foreach (var peer in _peers.Values) LogPeerState(peer, now);
            }
        }
        catch (Exception error)
        {
            StopSharing(error);
        }
    }

    private void StopSharing(Exception error)
    {
        if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, "sharing exception: " + error.GetType().Name + ".");
        if (!_faulted) _log.LogWarning("Drawing sharing stopped: " + error.GetType().Name);
        _faulted = true;
        Reset("exception");
        _host.SharingStatus = "Sharing unavailable; toggle sharing to retry.";
    }

    private void LogState(string reason)
    {
        if (!_networkLog.Enabled || _diagnosticState == reason) return;
        _diagnosticState = reason;
        _networkLog.Write(Time.unscaledTimeAsDouble, "context: " + reason + ".");
    }

    private void LogPeerState(DrawingPeerState peer, double now)
    {
        var incoming = 0;
        foreach (var channel in peer.Protocol.RemoteBroadcastChannels)
            if (ChannelRouting.CanSend(channel, peer.Protocol.RemoteBroadcastChannels, _host.Channels.Names, _host.ReceiveChannels.Names)) incoming++;
        var outgoing = 0;
        foreach (var channel in _host.Channels.Names)
            if (ChannelRouting.CanSend(channel, _host.Channels.Names, peer.Protocol.RemoteBroadcastChannels, peer.Protocol.RemoteChannels)) outgoing++;
        _networkLog.Write(now, $"peer {peer.DiagnosticId}: state; ready={peer.Protocol.IsReady}; helloAttempts={peer.HelloAttempts}; retryAllowed={peer.CanTransmit(now)}; receivedAny={peer.LastReceived > 0}; remoteBroadcast={peer.Protocol.RemoteBroadcastChannels.Count}; remoteReceiveOnly={peer.Protocol.RemoteChannels.Count}; incomingChannelMatches={incoming}; outgoingChannelMatches={outgoing}.");
    }

    private static bool TryContext(out NetworkManagerNuclearOption manager, out Player local, out SharedMissionEpoch epoch, out ulong server, out string reason)
    {
        manager = NetworkManagerNuclearOption.i; local = null!; epoch = default; server = 0;
        reason = "Steam connection ownership guard unavailable";
        if (!SteamConnectionOwnershipPatch.IsApplied) return false;
        reason = "Steam not initialized or multiplayer mission not ready";
        if (!SteamManager.ClientInitialized || GameManager.gameState != GameState.Multiplayer || !MissionManager.IsRunning ||
            NetworkManagerNuclearOption.IsLoadingScene || manager == null || !manager.Client.Active) return false;
        reason = "unsupported transport: Steam player transport required";
        if (manager.Client.SocketFactory is not SteamworksSocketFactory transport || transport.GameServer) return false;
        reason = "local authenticated player or faction not ready";
        if (manager.Client.Player == null || !manager.Client.Player.IsAuthenticated || !manager.Client.Player.HasCharacter ||
            !GameManager.GetLocalPlayer<Player>(out local) || local == null || local.HQ == null || local.HQ.Identity == null ||
            local.HQ.Identity.NetId == 0 || local.SteamID == 0 ||
            local.SteamID != SteamUser.GetSteamID().m_SteamID) return false;
        reason = "map faction does not match local player";
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null || map.HQ != local.HQ) return false;
        reason = "synchronized mission epoch not ready";
        var mission = NetworkSceneSingleton<MissionManager>.i;
        if (mission == null || mission.Identity == null || mission.Identity.NetId == 0 ||
            !(mission.multiplayerStartTime > 0) || double.IsInfinity(mission.multiplayerStartTime)) return false;
        if (manager.Server.Active)
        {
            reason = "host scene not ready";
            if (manager.Server.LocalPlayer == null || !manager.Server.LocalPlayer.SceneIsReady) return false;
            server = local.SteamID;
        }
        else if (manager.Client.Player.ConnectionHandle is SteamConnection connection)
        {
            reason = "server Steam identity not eligible";
            server = connection.SteamID.m_SteamID;
            if (!connection.SteamID.BIndividualAccount()) return false;
        }
        else { reason = "Steam connection not ready"; return false; }
        epoch = new SharedMissionEpoch(mission.Identity.NetId, mission.multiplayerStartTime, local.HQ.Identity.NetId);
        reason = server != 0 ? "active Steam team session" : "server identity missing";
        return server != 0;
    }

    private void RefreshRoster(Player local)
    {
        _roster.Clear();
        foreach (var player in UnitRegistry.playerLookup.Values)
        {
            if (player != null && player != local && player.HQ == local.HQ && player.SteamID != 0 &&
                player.CSteamID.BIndividualAccount())
            {
                var index = _roster.BinarySearch(player.SteamID);
                if (index >= 0 || ~index >= MaxPeers) continue;
                if (_roster.Count == MaxPeers) _roster.RemoveAt(MaxPeers - 1);
                _roster.Insert(~index, player.SteamID);
            }
        }
        _removed.Clear();
        foreach (var entry in _peers) if (!_roster.Contains(entry.Key)) _removed.Add(entry.Key);
        foreach (var id in _removed)
        {
            if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {_peers[id].DiagnosticId}: left eligible roster.");
            CloseChannel(id); _peers.Remove(id); _received.Remove(id);
        }
        foreach (var id in _roster)
            if (!_peers.ContainsKey(id))
            {
                var peer = new DrawingPeerState(_epoch, _host.ReceiveChannels.Names, _host.Channels.Names, ++_nextDiagnosticPeer);
                _peers.Add(id, peer);
                if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: joined eligible roster.");
            }
    }

    private bool Authorized(ulong id)
    {
        if (!_active || _faulted || !_wanted || !_host.SharingEligible || !_quarantine.Allows(id) || !_roster.Contains(id) ||
            !TryContext(out var manager, out var local, out var epoch, out var server, out _) || !_epoch.Equals(epoch) ||
            server != _server || _hq != local.HQ || !ReferenceEquals(_connection, manager.Client.Player) ||
            _mission != MissionManager.CurrentMission) return false;
        foreach (var player in UnitRegistry.playerLookup.Values)
            if (player != null && player != local && player.SteamID == id && player.HQ == local.HQ) return true;
        return false;
    }

    private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t request)
    {
        try
        {
            var id = request.m_identityRemote.GetSteamID64();
            if (Authorized(id) && _peers.TryGetValue(id, out var peer) && Time.unscaledTimeAsDouble >= peer.NextAccept)
            {
                peer.NextAccept = Time.unscaledTimeAsDouble + 1;
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var accepted = SteamNetworkingMessages.AcceptSessionWithUser(ref request.m_identityRemote);
                LogSlowCall("AcceptSessionWithUser", start);
                if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: native session request; accepted={accepted}.");
            }
            else if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, "native session request ignored: sender ineligible or acceptance cooldown.");
        }
        catch (Exception error)
        {
            if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, "session-request exception: " + error.GetType().Name + ".");
            _faulted = true;
        }
    }

    private void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t failure)
    {
        try
        {
            var id = failure.m_info.m_identityRemote.GetSteamID64();
            if (!Authorized(id) || !_peers.TryGetValue(id, out var peer)) return;
            var now = Time.unscaledTimeAsDouble;
            if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: native session failed; state={failure.m_info.m_eState}; endReason={failure.m_info.m_eEndReason}.");
            if (!peer.RecordSessionFailure(now)) return;
            if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: failed native session cleared; restarting with 30s cooldown.");
            _received.Remove(id);
            CloseChannel(id);
        }
        catch (Exception error)
        {
            if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, "session-failure exception: " + error.GetType().Name + ".");
            _faulted = true;
        }
    }

    private void Receive()
    {
        try
        {
            var now = Time.unscaledTimeAsDouble;
            var count = _receiveBudget.Receive(now);
            // Each snapshot replaces the one before, so a backlog decodes only its newest per peer: the highest sequence,
            // so a replayed old snapshot can't displace a newer one in the same batch.
            _newestSnapshot.Clear();
            for (var i = 0; i < count; i++)
            {
                if (_messages[i] == IntPtr.Zero) continue;
                var message = SteamNetworkingMessage_t.FromIntPtr(_messages[i]);
                if (WireKind(message) != DrawingPacketKind.Snapshot || message.m_cbSize < DrawingProtocol.SequenceOffset + 4) continue;
                var sender = message.m_identityPeer.GetSteamID64();
                var sequence = (uint)Marshal.ReadInt32(message.m_pData, DrawingProtocol.SequenceOffset);
                if (!_newestSnapshot.TryGetValue(sender, out var newest) || sequence >= newest.Sequence)
                    _newestSnapshot[sender] = (i, sequence);
            }
            for (var i = 0; i < count; i++)
            {
                if (_messages[i] == IntPtr.Zero) continue;
                var message = SteamNetworkingMessage_t.FromIntPtr(_messages[i]);
                var id = message.m_identityPeer.GetSteamID64();
                _receiveBudget.TryConsume(now); // a batch never exceeds the window's remaining budget
                if (!_peers.TryGetValue(id, out var peer)) continue;
                if (!peer.CountReceive(now, out var flooded, message.m_cbSize))
                {
                    if (flooded)
                    {
                        if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: receive message or byte budget exceeded; resetting its drawing channel.");
                        _received.Remove(id);
                        CloseChannel(id);
                        peer.Restart(now + 5);
                    }
                    continue;
                }
                if (!Authorized(id))
                {
                    if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, "rx ignored: sender not eligible.");
                    continue;
                }
                if (message.m_nChannel != Channel || message.m_cbSize <= 0 ||
                    message.m_cbSize > DrawingProtocol.MaxPacketBytes || message.m_pData == IntPtr.Zero)
                {
                    RejectPeer(id, "invalid native message envelope");
                    continue;
                }
                if (!DrawingProtocol.TryReadHeader(message.m_pData, message.m_cbSize, out var version, out _))
                {
                    RejectPeer(id, "invalid packet header");
                    continue;
                }
                if (version != DrawingProtocol.Version)
                {
                    if (!peer.OtherVersion && _networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: runs protocol {version}, this build {DrawingProtocol.Version}; ignoring it until it matches.");
                    peer.OtherVersion = true;
                    continue;
                }
                if (WireKind(message) == DrawingPacketKind.Snapshot && _newestSnapshot.TryGetValue(id, out var newestSnapshot) &&
                    newestSnapshot.Index != i)
                {
                    if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: rx Snapshot superseded in the same batch; skipped.");
                    continue;
                }
                var bytes = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, bytes, 0, bytes.Length);
                if (!DrawingProtocol.TryDecode(bytes, out var packet) || packet == null)
                {
                    if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: decode rejected; bytes={bytes.Length}; wireVersion={(bytes.Length > 4 ? bytes[4] : -1)}; expectedVersion={DrawingProtocol.Version}.");
                    RejectPeer(id, "invalid packet or incompatible protocol");
                    continue;
                }
                if (packet.Kind == DrawingPacketKind.DeleteRequest)
                {
                    if (peer.Protocol.TryReceiveDelete(packet) &&
                        ChannelRouting.CanCollaborate(packet.TargetChannel, _host.Channels.Names,
                            peer.Protocol.RemoteBroadcastChannels))
                    {
                        var removed = RemoveOwnedShape(packet.TargetShapeId, packet.TargetChannel, peer);
                        if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: erase request accepted; seq={packet.Sequence}; removed={removed}.");
                        peer.LastReceived = Time.unscaledTimeAsDouble;
                    }
                    else if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: erase request rejected by session, replay, ownership or collaboration checks; seq={packet.Sequence}.");
                    continue;
                }
                if (packet.Kind == DrawingPacketKind.Hello && !AllowHelloSession(peer, packet.SenderSession))
                {
                    if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: hello rejected at two-session/30s limit.");
                    continue;
                }
                var revision = peer.Protocol.Revision;
                var wasReady = peer.Protocol.IsReady;
                if (!peer.Protocol.Receive(packet, out var response, out var snapshot))
                {
                    if (_networkLog.Enabled)
                    {
                        var reason = !packet.Epoch.Equals(_epoch) ? "mission or faction epoch mismatch"
                            : packet.RecipientSession != Guid.Empty && packet.RecipientSession != peer.Protocol.LocalSession ? "recipient session mismatch"
                            : packet.Kind == DrawingPacketKind.Snapshot && !peer.Protocol.IsReady ? "handshake incomplete"
                            : "sender session, replay or handshake validation";
                        _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: rx {packet.Kind} rejected: {reason}; seq={packet.Sequence}.");
                    }
                    continue;
                }
                peer.LastReceived = Time.unscaledTimeAsDouble;
                peer.OtherVersion = false;
                _discovery.Accepted(id, packet.Kind, peer.Protocol.IsReady);
                if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: rx {packet.Kind} accepted; seq={packet.Sequence}; bytes={bytes.Length}.");
                if (revision != peer.Protocol.Revision)
                {
                    if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: handshake revision={peer.Protocol.Revision}; ready={peer.Protocol.IsReady}; clearing received drawings.");
                    _received.Remove(id);
                    peer.ObserveHandshake();
                }
                if (!wasReady && peer.Protocol.IsReady && _networkLog.Enabled)
                {
                    LogPeerState(peer, Time.unscaledTimeAsDouble);
                }
                if (packet.Kind == DrawingPacketKind.Acknowledgement && wasReady && peer.Protocol.IsReady &&
                    peer.Protocol.AcknowledgedSnapshot < peer.Advertised)
                {
                    // The heartbeat named a snapshot the teammate never applied: send the drawings again.
                    if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: holds snapshot {peer.Protocol.AcknowledgedSnapshot} of {peer.Advertised}; resending.");
                    peer.SentVersion = -1;
                    peer.Advertised = 0;
                }
                if (response != null) Send(id, peer, response);
                if (snapshot == null) continue;
                if (!SharedChannels(snapshot.Shapes, peer))
                {
                    RejectPeer(id, "snapshot channel authorization or sharing policy check failed");
                    continue;
                }
                peer.ReceivedSnapshots.Pending = snapshot;
            }
            foreach (var entry in _peers)
            {
                var id = entry.Key;
                var peer = entry.Value;
                if (peer.ReceivedSnapshots.Pending == null || !Authorized(id) || !peer.Protocol.IsReady) continue;
                var snapshot = _snapshotBudget.TryTake(peer.ReceivedSnapshots, id, _received, now);
                if (snapshot == null) continue;
                if (!_received.TryReplace(id, snapshot.Shapes, out var capacityExceeded))
                {
                    if (capacityExceeded)
                    {
                        if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot rejected at received-drawing capacity; shapes={snapshot.Shapes.Length}; priorTotal={_received.Shapes.Count}; retracting owner drawings.");
                        _received.Remove(id);
                    }
                    else RejectPeer(id, "invalid received drawings");
                }
                else if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot applied; seq={snapshot.Sequence}; shapes={snapshot.Shapes.Length}; receivedTotal={_received.Shapes.Count}.");
            }
        }
        finally
        {
            for (var i = 0; i < _messages.Length; i++)
            {
                if (_messages[i] == IntPtr.Zero) continue;
                SteamNetworkingMessage_t.Release(_messages[i]); _messages[i] = IntPtr.Zero;
            }
        }
    }

    /// <summary>Only current-protocol snapshots can supersede each other before decoding.</summary>
    private static DrawingPacketKind WireKind(SteamNetworkingMessage_t message) =>
        message.m_nChannel == Channel && message.m_cbSize <= DrawingProtocol.MaxPacketBytes &&
        DrawingProtocol.TryReadHeader(message.m_pData, message.m_cbSize, out var version, out var kind) &&
        version == DrawingProtocol.Version ? kind : 0;

    private static bool AllowHelloSession(DrawingPeerState peer, Guid session)
    {
        if (session == peer.LastHelloSession) return true;
        var now = Time.unscaledTimeAsDouble;
        if (peer.HelloSessionChanges == 0 || now - peer.HelloWindow >= 30)
        {
            peer.HelloWindow = now;
            peer.HelloSessionChanges = 0;
        }
        if (peer.HelloSessionChanges >= 2) return false;
        peer.HelloSessionChanges++;
        peer.LastHelloSession = session;
        return true;
    }

    private void RejectPeer(ulong id, string reason)
    {
        if (_networkLog.Enabled && _peers.TryGetValue(id, out var peer)) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: quarantined; {reason}; closing drawing channel.");
        if (_quarantine.Reject(id)) { _faulted = true; _received.Clear(); }
        _received.Remove(id);
        CloseChannel(id);
    }

    private bool SharedChannels(MapShape[] shapes, DrawingPeerState peer)
    {
        foreach (var shape in shapes)
            if (!ChannelRouting.CanSend(shape.Channel, peer.Protocol.RemoteBroadcastChannels,
                    _host.Channels.Names, _host.ReceiveChannels.Names) ||
                !SharedShapeVisibility.CanShare(shape)) return false;
        return true;
    }

    private bool RemoveOwnedShape(int id, string channel, DrawingPeerState peer)
    {
        foreach (var shape in _host.Context.Shapes.Shapes)
        {
            if (shape.Id != id || shape.Channel != channel || !SharedShapeVisibility.CanShare(shape) ||
                !ChannelRouting.CanCollaborate(channel, _host.Channels.Names,
                    peer.Protocol.RemoteBroadcastChannels)) continue;
            return _host.Context.Shapes.Remove(shape);
        }
        return false;
    }

    /// <summary>Requests removal from the original owner; the owner publishes the resulting snapshot.</summary>
    public bool CanRequestErase(MapShape shape) => _active && _wanted && !_faulted && _host.SharingEligible &&
        SharedShapeVisibility.CanShare(shape) && _received.TryFindOwner(shape, out var owner, out _) &&
        _quarantine.Allows(owner) && _peers.TryGetValue(owner, out var peer) &&
        peer.CanRequestDelete(Time.unscaledTimeAsDouble) && ChannelRouting.CanCollaborate(shape.Channel,
            _host.Channels.Names, peer.Protocol.RemoteBroadcastChannels);

    public bool RequestErase(MapShape shape)
    {
        try
        {
            if (!CanRequestErase(shape) ||
                !_received.TryFindOwner(shape, out var owner, out var sourceId) ||
                !_peers.TryGetValue(owner, out var peer) || !Authorized(owner))
                return false;

            peer.NextDelete = Time.unscaledTimeAsDouble + 1;
            var request = new DrawingPacket(DrawingPacketKind.DeleteRequest, _epoch, peer.Protocol.LocalSession,
                peer.Protocol.RemoteSession, ++peer.DeleteSequence, Array.Empty<string>(), Array.Empty<MapShape>(),
                targetShapeId: sourceId, targetChannel: shape.Channel);
            if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: erase requested; seq={request.Sequence}.");
            return DrawingProtocol.TryEncode(request, out var bytes) && SendBytes(owner, peer, bytes, request.Kind);
        }
        catch (Exception error)
        {
            StopSharing(error);
            return false;
        }
    }

    private void SendSnapshot(ulong id, DrawingPeerState peer)
    {
        var shapes = new List<MapShape>();
        var localOnly = 0;
        var unmatched = 0;
        var geometryPoints = 0;
        foreach (var shape in _host.Context.Shapes.Shapes)
        {
            if (!SharedShapeVisibility.CanShare(shape)) { localOnly++; continue; }
            if (ChannelRouting.CanSend(shape.Channel, _host.Channels.Names,
                peer.Protocol.RemoteBroadcastChannels, peer.Protocol.RemoteChannels))
            {
                shapes.Add(shape);
                if (_networkLog.Enabled) geometryPoints += DrawingProtocol.GeometryPointCount(shape);
            }
            else unmatched++;
        }
        if (peer.Sequence == uint.MaxValue)
        {
            if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot sequence exhausted; restarting with 30s cooldown.");
            peer.Restart(Time.unscaledTimeAsDouble + 30);
            _received.Remove(id); CloseChannel(id);
            return;
        }
        var all = new DrawingPacket(DrawingPacketKind.Snapshot, _epoch, peer.Protocol.LocalSession,
            peer.Protocol.RemoteSession, ++peer.Sequence, Array.Empty<string>(), shapes.ToArray());
        peer.Unshared = DrawingProtocol.EncodeFitting(all, out var packet, out var bytes);
        if (peer.Unshared > 0 && _networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot over the wire limits; attemptedShapes={shapes.Count}; geometryPoints={geometryPoints}; leftOut={peer.Unshared}.");
        if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot prepared; seq={peer.Sequence}; shapes={packet.Shapes.Length}; bytes={bytes.Length}; omittedLocalOnly={localOnly}; omittedChannel={unmatched}.");
        if (SendBytes(id, peer, bytes, packet.Kind))
        {
            peer.Protocol.RecordSentSnapshot(packet.Shapes);
            peer.SentVersion = _host.Context.Shapes.Version;
        }
    }

    private void Send(ulong id, DrawingPeerState peer, DrawingPacket packet)
    {
        if (DrawingProtocol.TryEncode(packet, out var bytes)) SendBytes(id, peer, bytes, packet.Kind);
        else if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: {packet.Kind} encode failed.");
    }

    private bool SendBytes(ulong id, DrawingPeerState peer, byte[] bytes, DrawingPacketKind kind)
    {
        var now = Time.unscaledTimeAsDouble;
        if (!peer.CanTransmit(now) || !Authorized(id)) return false;
        var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(id);
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            // A probe that found the session broken, by an earlier unanswered probe from either side, would fail at once
            // without the restart flag, and a teammate who enabled sharing late would never pair. DrawingDiscovery's
            // three probes, not the flag, bound the attempts made towards a teammate without the mod.
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = SteamNetworkingMessages.SendMessageToUser(ref identity, pin.AddrOfPinnedObject(), (uint)bytes.Length,
                Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession, Channel);
            LogSlowCall("SendMessageToUser", start);
            var success = result == EResult.k_EResultOK;
            if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: tx {kind}; bytes={bytes.Length}; result={result}.");
            if (!success && _networkLog.Enabled)
            {
                try
                {
                    var state = SteamNetworkingMessages.GetSessionConnectionInfo(ref identity, out var info, out _);
                    _networkLog.Write(now, $"peer {peer.DiagnosticId}: native connection state={state}; endReason={info.m_eEndReason}.");
                }
                catch (Exception error) { _networkLog.Write(now, "connection diagnostic query failed: " + error.GetType().Name + "."); }
            }
            if (peer.RecordSend(success, now))
            {
                if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: three consecutive send failures; restarting with 30s cooldown.");
                _received.Remove(id); CloseChannel(id);
            }
            return success;
        }
        finally { pin.Free(); }
    }

    private static string[] CreateReadyStatuses()
    {
        var statuses = new string[MaxPeers + 1];
        statuses[0] = "Waiting for teammates with sharing enabled.";
        for (var i = 1; i <= MaxPeers; i++) statuses[i] = $"Sharing with {i} teammate(s).";
        return statuses;
    }

    private static void CloseChannel(ulong id)
    {
        if (!SteamManager.ClientInitialized) return;
        var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(id);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        SteamNetworkingMessages.CloseChannelWithUser(ref identity, Channel);
        LogSlowCall("CloseChannelWithUser", start);
    }

    private void Reset(string reason = "stopped")
    {
        if (_networkLog.Enabled && (_active || _peers.Count > 0)) _networkLog.Write(Time.unscaledTimeAsDouble, $"reset: {reason}; closing {_peers.Count} drawing channels; clearing {_received.Shapes.Count} received shapes.");
        _active = false;
        _request?.Dispose(); _request = null;
        _failure?.Dispose(); _failure = null;
        foreach (var id in _peers.Keys)
        {
            try { CloseChannel(id); } catch { /* Cleanup must not affect the game's connection. */ }
        }
        _peers.Clear(); _roster.Clear(); _received.Clear(); _quarantine.Clear();
        _mission = null; _connection = null; _hq = null;
        _receiveBudget.Clear();
        _snapshotBudget.Clear();
    }

    public void Dispose() { _wanted = false; Reset(); }
}
