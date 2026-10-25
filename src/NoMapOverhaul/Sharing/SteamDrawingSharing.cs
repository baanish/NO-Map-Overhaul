using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Mirage;
using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.MapTools;
using NuclearOption.Networking;
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
    private readonly Dictionary<(ulong Sender, SharedMissionEpoch Epoch, Guid SenderSession, Guid RecipientSession),
        (int Index, uint Sequence)> _newestSnapshot = new(MaxPeers);
    private static readonly string[] ReadyStatuses = CreateReadyStatuses();
    private readonly IDrawingTransport _transport;
    private readonly DrawingMessage[] _messages = new DrawingMessage[DrawingReceiveBudget.MaxMessagesPerTick];
    private readonly DrawingReceiveBudget _receiveBudget;
    private readonly DrawingNetworkSchedule _schedule = new();
    private readonly DrawingSnapshotBudget _snapshotBudget = new();
    private readonly DrawingNetworkLog _networkLog;
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
    private int _admitFirst;
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

    /// <summary>Each message is copied here to be decoded, rather than into a new array per message. Bounded by the envelope check before the copy.</summary>
    private static readonly byte[] ReceiveScratch = new byte[DrawingProtocol.MaxPacketBytes];

    private static DrawingTraffic _traffic;

    /// <summary>What each <see cref="TrafficReader"/> hasn't taken yet.</summary>
    private static readonly DrawingTraffic[] Untaken = new DrawingTraffic[2];

    /// <summary>What sharing sent, received and spent in Steam's calls since this reader last took it.</summary>
    public static DrawingTraffic TakeTraffic(TrafficReader reader)
    {
        Collect();
        var taken = Untaken[(int)reader];
        Untaken[(int)reader] = default;
        return taken;
    }

    private readonly List<TeammateWindow> _teammates = new(MaxPeers);
    private readonly HashSet<MapShape> _shown = new();

    /// <summary>
    /// Each teammate on the roster since the performance log last asked: their drawings held and shown, their traffic,
    /// and the transport's status of the session, one Steam call each. Once per logged window; empty while sharing is off.
    /// </summary>
    public IReadOnlyList<TeammateWindow> TakeTeammates()
    {
        _teammates.Clear();
        if (!_active) return _teammates;
        _shown.Clear();
        foreach (var shape in _host.VisibleReceived) _shown.Add(shape);
        foreach (var entry in _peers)
        {
            var peer = entry.Value;
            var (held, shown) = _received.CountOwned(entry.Key, _shown);
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            DrawingSessionStatus? session = null;
            // The log runs outside the plugin's guard, so a failed diagnostic reads n/a rather than throwing into it.
            try { if (_transport.TryGetSessionStatus(entry.Key, out var status)) session = status; }
            catch (Exception) { }
            WarnSlowCall("GetSessionConnectionInfo", start);
            _teammates.Add(new TeammateWindow(peer.DiagnosticId, peer.Protocol.IsReady, held, shown, peer.LogTraffic, session));
            peer.LogTraffic = default;
        }

        return _teammates;
    }

    /// <summary>Starts each teammate's logged traffic over, for a new performance log window.</summary>
    public void DropTeammateTraffic()
    {
        foreach (var peer in _peers.Values) peer.LogTraffic = default;
    }

    private static void Collect()
    {
        for (var i = 0; i < Untaken.Length; i++) Untaken[i].Add(_traffic);
        _traffic = default;
    }

    private static void LogSlowCall(string call, long start) => _traffic.AddSteamCall(WarnSlowCall(call, start) * 1000.0);

    /// <summary>The call's seconds, with a warning when it blocked the game. Alone for the performance log's own calls, which stay out of the Steam time it reports.</summary>
    private static double WarnSlowCall(string call, long start)
    {
        var seconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (seconds > SlowCallSeconds) _slowCallLog?.LogWarning($"Steam {call} blocked the game for {seconds:F2}s.");
        return seconds;
    }

    public SteamDrawingSharing(MapToolHost host, ReceivedDrawings received, ManualLogSource log, IDrawingTransport transport)
    {
        _host = host; _received = received; _log = log; _transport = transport;
        _slowCallLog = log;
        _networkLog = new DrawingNetworkLog(message => _log.LogInfo(message));
        _receiveBudget = new DrawingReceiveBudget(count =>
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var received = _transport.Receive(Channel, _messages, count);
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
        if (wanted) PrepareRelayNetwork();
        if (!wanted) _faulted = false;
        _wanted = wanted;
        try
        {
            var reason = !enabled ? "mod or map tools disabled"
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
            var broadcast = _host.SharingBroadcast;
            var receive = _host.SharingReceive;
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
                _transport.Listen(OnSessionRequest, OnSessionFailed);
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
                    : !peer.Protocol.IsReady && _discovery.TryProbe(entry.Key))
                {
                    Send(entry.Key, peer, peer.Protocol.CreateHello(peer.Sequence));
                    peer.Advertised = peer.Sequence;
                }
                if (!peer.Protocol.IsReady) continue;
                ready++;
                if (now >= peer.NextSnapshot && peer.SentVersion != _host.OutgoingVersion)
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
                _networkLog.Write(now, $"summary: eligible={_roster.Count}; ready={ready}; ownedShapes={_host.OutgoingShapes.Count}; receivedShapes={_received.Shapes.Count}.");
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
            if (ChannelRouting.CanSend(channel, peer.Protocol.RemoteBroadcastChannels, _host.SharingBroadcast, _host.SharingReceive)) incoming++;
        var outgoing = 0;
        foreach (var channel in _host.SharingBroadcast)
            if (ChannelRouting.CanSend(channel, _host.SharingBroadcast, peer.Protocol.RemoteBroadcastChannels, peer.Protocol.RemoteChannels)) outgoing++;
        _networkLog.Write(now, $"peer {peer.DiagnosticId}: state; ready={peer.Protocol.IsReady}; helloAttempts={peer.HelloAttempts}; retryAllowed={peer.CanTransmit(now)}; receivedAny={peer.LastReceived > 0}; remoteBroadcast={peer.Protocol.RemoteBroadcastChannels.Count}; remoteReceiveOnly={peer.Protocol.RemoteChannels.Count}; incomingChannelMatches={incoming}; outgoingChannelMatches={outgoing}.");
    }

    private bool TryContext(out NetworkManagerNuclearOption manager, out Player local, out SharedMissionEpoch epoch, out ulong server, out string reason)
    {
        manager = NetworkManagerNuclearOption.i; local = null!; epoch = default; server = 0;
        reason = "multiplayer mission not ready";
        if (GameManager.gameState != GameState.Multiplayer || !MissionManager.IsRunning ||
            NetworkManagerNuclearOption.IsLoadingScene || manager == null || !manager.Client.Active) return false;
        if (_transport.Unavailable(manager) is { } unavailable)
        {
            reason = unavailable;
            return false;
        }
        reason = "local authenticated player or faction not ready";
        if (manager.Client.Player == null || !manager.Client.Player.IsAuthenticated || !manager.Client.Player.HasCharacter ||
            !GameManager.GetLocalPlayer<Player>(out local) || local == null || local.HQ == null || local.HQ.Identity == null ||
            local.HQ.Identity.NetId == 0) return false;
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
        }
        if (!_transport.TryIdentify(manager, local, out server, out reason)) return false;
        epoch = new SharedMissionEpoch(mission.Identity.NetId, mission.multiplayerStartTime, local.HQ.Identity.NetId);
        return true;
    }

    private static bool _relayPrepared;

    /// <summary>The transport's set-up (<see cref="IDrawingTransport.Prepare"/>), once, as soon as sharing could run.</summary>
    private void PrepareRelayNetwork()
    {
        if (_relayPrepared) return;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _relayPrepared = _transport.Prepare();
        LogSlowCall("InitRelayNetworkAccess", start);
        if (!_relayPrepared) return;
        if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, "relay network access requested.");
    }

    /// <summary>Senders outside the team roster whose session was closed this session, so each is closed once. Bounded.</summary>
    private readonly HashSet<ulong> _strangers = new();
    private const int MaxStrangers = 64;

    /// <summary>
    /// Closes the session with a sender who isn't a teammate in this mission, once: its messages would otherwise keep
    /// arriving and take the receive budget the team's need. A stranger who opens a new session is turned away by
    /// <see cref="OnSessionRequest"/>, which accepts teammates only.
    /// </summary>
    private void CloseStranger(ulong id)
    {
        if (_strangers.Count >= MaxStrangers || !_strangers.Add(id)) return;
        CloseChannel(id);
        if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, "rx from a sender outside the team roster; closed its drawing session.");
    }

    private void RefreshRoster(Player local)
    {
        _roster.Clear();
        foreach (var player in UnitRegistry.playerLookup.Values)
        {
            if (player != null && player != local && player.HQ == local.HQ &&
                _transport.IdOf(player) is var id && UniqueTeammate(id, local))
            {
                var index = _roster.BinarySearch(id);
                if (index >= 0 || ~index >= MaxPeers) continue;
                if (_roster.Count == MaxPeers) _roster.RemoveAt(MaxPeers - 1);
                _roster.Insert(~index, id);
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
                var peer = new DrawingPeerState(_epoch, _host.SharingReceive, _host.SharingBroadcast, ++_nextDiagnosticPeer);
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
        return UniqueTeammate(id, local);
    }

    private bool UniqueTeammate(ulong id, Player local) => DrawingPeerState.IsUniqueTeammate(
        UnitRegistry.playerLookup.Values, id, player => player != null ? _transport.IdOf(player) : 0,
        player => player != null && player != local && player.HQ == local.HQ);

    /// <summary>
    /// The name the game shows for the one player on the roster with this ID, or null when none or several have it, or
    /// Steam hasn't named them yet. Read from the game's name cache rather than <c>Player.GetPlayerName</c>, which logs
    /// the Steam ID and name whenever it resolves one. The game escapes <c>&lt;</c> for its rich-text labels; the
    /// label beside the cursor shows text as written, so that's undone.
    /// </summary>
    public string? OwnerName(ulong id)
    {
        Player? owner = null;
        foreach (var player in UnitRegistry.playerLookup.Values)
        {
            if (player == null || _transport.IdOf(player) != id) continue;
            if (owner != null) return null;
            owner = player;
        }
        return owner != null && UnitRegistry.cachedPlayerNames.TryGetValue(owner.CSteamID, out var name)
            ? name.GetDisplayName(PlayerNameContext.Other).Replace("<<i></i>", "<") : null;
    }

    private void OnSessionRequest(ulong id)
    {
        try
        {
            if (Authorized(id) && _peers.TryGetValue(id, out var peer) && Time.unscaledTimeAsDouble >= peer.NextAccept)
            {
                peer.NextAccept = Time.unscaledTimeAsDouble + 1;
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var accepted = _transport.Accept(id);
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

    private void OnSessionFailed(ulong id, string state)
    {
        try
        {
            if (!Authorized(id) || !_peers.TryGetValue(id, out var peer)) return;
            var now = Time.unscaledTimeAsDouble;
            if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: native session failed; {state}.");
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
            // Only the newest snapshot in one session needs decoding. A foreign epoch or session's sequence cannot
            // supersede the current session, even when a handshake and its first snapshot arrive in the same batch.
            _newestSnapshot.Clear();
            for (var i = 0; i < count; i++)
            {
                if (!_messages[i].Present) continue;
                var message = _messages[i];
                if (WireKind(message) != DrawingPacketKind.Snapshot || message.Size < DrawingProtocol.HeaderBytes) continue;
                Marshal.Copy(message.Data, ReceiveScratch, 0, DrawingProtocol.HeaderBytes);
                if (!DrawingProtocol.TryReadPacketHeader(ReceiveScratch, DrawingProtocol.HeaderBytes, out var header)) continue;
                var key = (message.Sender, header!.Epoch, header.SenderSession, header.RecipientSession);
                if (!_newestSnapshot.TryGetValue(key, out var newest) || header.Sequence >= newest.Sequence)
                    _newestSnapshot[key] = (i, header.Sequence);
            }
            for (var i = 0; i < count; i++)
            {
                if (!_messages[i].Present) continue;
                var message = _messages[i];
                var id = message.Sender;
                _traffic.ReceivedMessages++;
                _traffic.ReceivedBytes += message.Size;
                if (!_peers.TryGetValue(id, out var peer))
                {
                    CloseStranger(id);
                    continue;
                }
                peer.LogTraffic.ReceivedMessages++;
                peer.LogTraffic.ReceivedBytes += message.Size;
                if (!peer.CountReceive(now, out var flooded, message.Size))
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
                _receiveBudget.TryConsume(now); // a batch never exceeds the window's remaining budget
                if (message.Channel != Channel || message.Size <= 0 ||
                    message.Size > DrawingProtocol.MaxPacketBytes || message.Data == IntPtr.Zero)
                {
                    RejectPeer(id, "invalid native message envelope");
                    continue;
                }
                if (!DrawingProtocol.TryReadHeader(message.Data, message.Size, out var version, out _))
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
                var size = message.Size;
                Marshal.Copy(message.Data, ReceiveScratch, 0, size);
                if (DrawingProtocol.TryReadPacketHeader(ReceiveScratch, size, out var snapshotHeader) &&
                    snapshotHeader!.Kind == DrawingPacketKind.Snapshot &&
                    _newestSnapshot.TryGetValue((id, snapshotHeader.Epoch, snapshotHeader.SenderSession, snapshotHeader.RecipientSession), out var newestSnapshot) &&
                    newestSnapshot.Index != i)
                {
                    if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: rx Snapshot superseded in the same batch; skipped.");
                    continue;
                }
                if (!peer.Protocol.TryDecode(ReceiveScratch, size, out var packet, out var malformed) || packet == null)
                {
                    if (!malformed)
                    {
                        if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: snapshot header rejected by session or replay checks.");
                        continue;
                    }
                    if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: decode rejected; bytes={size}; wireVersion={(size > 4 ? ReceiveScratch[4] : -1)}; expectedVersion={DrawingProtocol.Version}.");
                    RejectPeer(id, "invalid packet or incompatible protocol");
                    continue;
                }
                if (packet.Kind == DrawingPacketKind.DeleteRequest)
                {
                    if (peer.Protocol.TryReceiveDelete(packet) &&
                        ChannelRouting.CanCollaborate(packet.TargetChannel, _host.SharingBroadcast,
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
                if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: rx {packet.Kind} accepted; seq={packet.Sequence}; bytes={size}.");
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
            // Teammates take turns at the front of the queue, so a busy one can't keep taking the shared budget while
            // another's snapshot waits.
            var teammates = _roster.Count;
            _admitFirst = teammates == 0 ? 0 : (_admitFirst + 1) % teammates;
            for (var k = 0; k < teammates; k++)
            {
                var id = _roster[(_admitFirst + k) % teammates];
                if (!_peers.TryGetValue(id, out var peer)) continue;
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
                else
                {
                    _traffic.SnapshotsApplied++;
                    peer.LogTraffic.SnapshotsApplied++;
                    if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot applied; seq={snapshot.Sequence}; shapes={snapshot.Shapes.Length}; receivedTotal={_received.Shapes.Count}.");
                }
            }
        }
        finally
        {
            Array.Clear(_messages, 0, _messages.Length);
            _transport.ReleaseReceived();
        }
    }

    /// <summary>Only current-protocol snapshots can supersede each other before decoding.</summary>
    private static DrawingPacketKind WireKind(DrawingMessage message) =>
        message.Channel == Channel && message.Size <= DrawingProtocol.MaxPacketBytes &&
        DrawingProtocol.TryReadHeader(message.Data, message.Size, out var version, out var kind) &&
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
                    _host.SharingBroadcast, _host.SharingReceive) ||
                !SharedShapeVisibility.CanShare(shape)) return false;
        return true;
    }

    private bool RemoveOwnedShape(int id, string channel, DrawingPeerState peer)
    {
        if (_host.OutgoingHeld) return false; // the perf test has the player's drawings set aside
        foreach (var shape in _host.Context.Shapes.Shapes)
        {
            if (shape.Id != id || shape.Channel != channel || !SharedShapeVisibility.CanShare(shape) ||
                !ChannelRouting.CanCollaborate(channel, _host.SharingBroadcast,
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
            _host.SharingBroadcast, peer.Protocol.RemoteBroadcastChannels);

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

    /// <summary>
    /// Snapshots already encoded for <see cref="_encodedVersion"/> of the outgoing drawings, one per set of drawings a
    /// teammate's channels let through. Teammates on the same channels get the same encoding, readdressed.
    /// </summary>
    private readonly List<EncodedSnapshot> _encoded = new();
    private int _encodedVersion = -1;
    private const int MaxEncoded = 8;
    private readonly List<MapShape> _snapshotShapes = new();

    private sealed class EncodedSnapshot
    {
        public EncodedSnapshot(MapShape[] shapes, MapShape[] sent, byte[] bytes, int unshared)
        {
            Shapes = shapes; Sent = sent; Bytes = bytes; Unshared = unshared;
        }

        public MapShape[] Shapes { get; }
        public MapShape[] Sent { get; }
        public byte[] Bytes { get; }
        public int Unshared { get; }

        public bool Holds(List<MapShape> shapes)
        {
            if (shapes.Count != Shapes.Length) return false;
            for (var i = 0; i < Shapes.Length; i++)
                if (!ReferenceEquals(shapes[i], Shapes[i])) return false;
            return true;
        }
    }

    private void SendSnapshot(ulong id, DrawingPeerState peer)
    {
        var shapes = _snapshotShapes;
        shapes.Clear();
        var localOnly = 0;
        var unmatched = 0;
        var geometryPoints = 0;
        var outgoing = _host.OutgoingShapes;
        var version = _host.OutgoingVersion;
        foreach (var shape in outgoing)
        {
            if (!SharedShapeVisibility.CanShare(shape)) { localOnly++; continue; }
            if (ChannelRouting.CanSend(shape.Channel, _host.SharingBroadcast,
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
        if (version != _encodedVersion)
        {
            _encoded.Clear();
            _encodedVersion = version;
        }
        EncodedSnapshot? encoded = null;
        foreach (var entry in _encoded)
            if (entry.Holds(shapes)) { encoded = entry; break; }
        if (encoded == null)
        {
            var all = new DrawingPacket(DrawingPacketKind.Snapshot, _epoch, peer.Protocol.LocalSession,
                peer.Protocol.RemoteSession, peer.Sequence + 1, Array.Empty<string>(), shapes.ToArray());
            var unshared = DrawingProtocol.EncodeFitting(all, out var fitted, out var encodedBytes);
            encoded = new EncodedSnapshot(all.Shapes, fitted.Shapes, encodedBytes, unshared);
            if (_encoded.Count == MaxEncoded) _encoded.Clear();
            _encoded.Add(encoded);
        }
        DrawingProtocol.Readdress(encoded.Bytes, peer.Protocol.LocalSession, peer.Protocol.RemoteSession, ++peer.Sequence);
        peer.Unshared = encoded.Unshared;
        if (peer.Unshared > 0 && _networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot over the wire limits; attemptedShapes={shapes.Count}; geometryPoints={geometryPoints}; leftOut={peer.Unshared}.");
        if (_networkLog.Enabled) _networkLog.Write(Time.unscaledTimeAsDouble, $"peer {peer.DiagnosticId}: snapshot prepared; seq={peer.Sequence}; shapes={encoded.Sent.Length}; bytes={encoded.Bytes.Length}; omittedLocalOnly={localOnly}; omittedChannel={unmatched}.");
        if (SendBytes(id, peer, encoded.Bytes, DrawingPacketKind.Snapshot))
        {
            peer.Protocol.RecordSentSnapshot(encoded.Sent);
            peer.SentVersion = version;
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
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var success = _transport.Send(id, Channel, bytes, out var result);
        LogSlowCall("SendMessageToUser", start);
        _traffic.SentMessages++;
        _traffic.SentBytes += bytes.Length;
        peer.LogTraffic.SentMessages++;
        peer.LogTraffic.SentBytes += bytes.Length;
        if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: tx {kind}; bytes={bytes.Length}; result={result}.");
        if (!success && _networkLog.Enabled)
        {
            try { _networkLog.Write(now, $"peer {peer.DiagnosticId}: native connection {_transport.ConnectionState(id)}."); }
            catch (Exception error) { _networkLog.Write(now, "connection diagnostic query failed: " + error.GetType().Name + "."); }
        }
        if (peer.RecordSend(success, now))
        {
            if (_networkLog.Enabled) _networkLog.Write(now, $"peer {peer.DiagnosticId}: three consecutive send failures; restarting with 30s cooldown.");
            _received.Remove(id); CloseChannel(id);
        }
        return success;
    }

    private static string[] CreateReadyStatuses()
    {
        var statuses = new string[MaxPeers + 1];
        statuses[0] = "Waiting for teammates with sharing enabled.";
        for (var i = 1; i <= MaxPeers; i++) statuses[i] = $"Sharing with {i} teammate(s).";
        return statuses;
    }

    private void CloseChannel(ulong id)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _transport.Close(id, Channel);
        LogSlowCall("CloseChannelWithUser", start);
    }

    private void Reset(string reason = "stopped")
    {
        // Update resets every frame while sharing can't run (single player, menus, sharing off): once is enough.
        if (!_active && _peers.Count == 0 && !_transport.Listening && _received.Shapes.Count == 0) return;
        if (_networkLog.Enabled && (_active || _peers.Count > 0)) _networkLog.Write(Time.unscaledTimeAsDouble, $"reset: {reason}; closing {_peers.Count} drawing channels; clearing {_received.Shapes.Count} received shapes.");
        _active = false;
        _transport.StopListening();
        foreach (var id in _peers.Keys)
        {
            try { CloseChannel(id); } catch { /* Cleanup must not affect the game's connection. */ }
        }
        _peers.Clear(); _roster.Clear(); _received.Clear(); _quarantine.Clear(); _strangers.Clear();
        _mission = null; _connection = null; _hq = null;
        _receiveBudget.Clear();
        _snapshotBudget.Clear();
        _encoded.Clear(); _encodedVersion = -1; // encodings carry the session's epoch
        DrawingProtocol.ForgetSnapshots();
    }

    public void Dispose() { _wanted = false; Reset(); }
}
