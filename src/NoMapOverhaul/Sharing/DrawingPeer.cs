using System;
using System.Collections.Generic;
using NoMapOverhaul.MapTools;

namespace NoMapOverhaul.Sharing;

/// <summary>Nonce handshake and replay protection for one authenticated peer in one mission.</summary>
public sealed class DrawingPeer
{
    private readonly SharedMissionEpoch _epoch;
    private readonly string[] _localReceiveOnlyChannels;
    private readonly string[] _localBroadcastChannels;
    private Guid _candidate;
    private uint _lastSequence;
    private uint _lastDeleteSequence;
    private readonly HashSet<int> _sentShapeIds = new();

    public DrawingPeer(SharedMissionEpoch epoch, IReadOnlyList<string> localChannels,
        IReadOnlyList<string>? localBroadcastChannels = null)
    {
        localBroadcastChannels ??= Array.Empty<string>();
        if (!ValidChannels(localChannels) || !ValidChannels(localBroadcastChannels))
        {
            throw new ArgumentException("Expected unique canonical drawing channels.", nameof(localChannels));
        }

        _epoch = epoch;
        _localReceiveOnlyChannels = CopyChannels(localChannels);
        _localBroadcastChannels = CopyChannels(localBroadcastChannels);
    }

    public Guid LocalSession { get; private set; } = Guid.NewGuid();
    public Guid RemoteSession { get; private set; }
    public bool IsReady { get; private set; }
    /// <summary>Additional read-only subscriptions; broadcast channels also receive drawings.</summary>
    public IReadOnlyList<string> RemoteChannels { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> RemoteBroadcastChannels { get; private set; } = Array.Empty<string>();

    /// <summary>Changes when an incoming handshake begins or completes. Callers discard received drawings on a restart.</summary>
    public int Revision { get; private set; }

    /// <summary>The newest snapshot sequence the remote reported holding, in its latest acknowledgement of a heartbeat.</summary>
    public uint AcknowledgedSnapshot { get; private set; }

    /// <summary>A Hello, which a paired peer also sends as its heartbeat, naming the last snapshot it sent.</summary>
    public DrawingPacket CreateHello(uint lastSnapshot = 0) => Handshake(DrawingPacketKind.Hello, Guid.Empty, lastSnapshot);

    /// <summary>
    /// Receives an already decoded packet after the adapter authorizes its sender. Only an accepted snapshot is
    /// returned in snapshot; rejected packets leave state unchanged. Responses must go to the authenticated sender.
    /// </summary>
    public bool Receive(DrawingPacket packet, out DrawingPacket? response, out DrawingPacket? snapshot)
    {
        response = snapshot = null;
        if (!packet.Epoch.Equals(_epoch) || packet.SenderSession == Guid.Empty)
        {
            return false;
        }

        if (packet.Kind == DrawingPacketKind.Snapshot)
        {
            if (!IsReady || packet.SenderSession != RemoteSession || packet.RecipientSession != LocalSession ||
                packet.Sequence == 0 || packet.Sequence <= _lastSequence || packet.Channels.Length != 0 ||
                packet.BroadcastChannels.Length != 0 || packet.TargetShapeId != 0 ||
                !string.IsNullOrEmpty(packet.TargetChannel))
            {
                return false;
            }

            _lastSequence = packet.Sequence;
            snapshot = packet;
            return true;
        }

        if (packet.Shapes.Length != 0 || packet.TargetShapeId != 0 ||
            !string.IsNullOrEmpty(packet.TargetChannel) ||
            !ValidChannels(packet.Channels) || !ValidChannels(packet.BroadcastChannels))
        {
            return false;
        }

        if (packet.Kind == DrawingPacketKind.Hello)
        {
            if (packet.RecipientSession != Guid.Empty ||
                (IsReady && packet.SenderSession == RemoteSession &&
                    (!SameChannels(packet.Channels, RemoteChannels) ||
                     !SameChannels(packet.BroadcastChannels, RemoteBroadcastChannels))))
            {
                return false;
            }

            // Before pairing, a newer Hello only replaces the candidate. Rotating LocalSession here too would void the
            // Ack already in flight, and a peer whose stale heartbeat crossed a restart would never pair.
            if (IsReady && packet.SenderSession != RemoteSession)
            {
                LocalSession = Guid.NewGuid();
                RemoteSession = Guid.Empty;
                RemoteChannels = Array.Empty<string>();
                RemoteBroadcastChannels = Array.Empty<string>();
                IsReady = false;
                _lastSequence = 0;
                _lastDeleteSequence = 0;
                _sentShapeIds.Clear();
            }

            if (_candidate != packet.SenderSession) Revision++;
            _candidate = packet.SenderSession;
            response = Handshake(DrawingPacketKind.Acknowledgement, packet.SenderSession, IsReady ? _lastSequence : 0);
            return true;
        }

        if (packet.Kind != DrawingPacketKind.Acknowledgement || packet.RecipientSession != LocalSession ||
            (_candidate != Guid.Empty && packet.SenderSession != _candidate))
        {
            return false;
        }

        if (IsReady)
        {
            if (packet.SenderSession != RemoteSession || !SameChannels(packet.Channels, RemoteChannels) ||
                !SameChannels(packet.BroadcastChannels, RemoteBroadcastChannels)) return false;
            AcknowledgedSnapshot = packet.Sequence;
            return true;
        }

        RemoteSession = _candidate = packet.SenderSession;
        RemoteChannels = Array.AsReadOnly(CopyChannels(packet.Channels));
        RemoteBroadcastChannels = Array.AsReadOnly(CopyChannels(packet.BroadcastChannels));
        IsReady = true;
        _lastSequence = 0;
        _lastDeleteSequence = 0;
        AcknowledgedSnapshot = 0;
        _sentShapeIds.Clear();
        Revision++;
        response = Handshake(DrawingPacketKind.Acknowledgement, RemoteSession);
        return true;
    }

    /// <summary>Only drawing IDs in the last successful outbound snapshot may be edited by this peer.</summary>
    public void RecordSentSnapshot(IReadOnlyList<MapShape> shapes)
    {
        _sentShapeIds.Clear();
        if (!IsReady) return;
        foreach (var shape in shapes) if (shape.Id > 0) _sentShapeIds.Add(shape.Id);
    }

    /// <summary>An edit request can remove one current owner drawing; its sequence is separate from snapshots.</summary>
    public bool TryReceiveDelete(DrawingPacket packet)
    {
        if (!IsReady || packet.Kind != DrawingPacketKind.DeleteRequest || !packet.Epoch.Equals(_epoch) ||
            packet.SenderSession != RemoteSession || packet.RecipientSession != LocalSession ||
            packet.Sequence == 0 || packet.Sequence <= _lastDeleteSequence ||
            packet.TargetShapeId <= 0 || packet.Channels.Length != 0 ||
            packet.BroadcastChannels.Length != 0 || packet.Shapes.Length != 0 ||
            !_sentShapeIds.Contains(packet.TargetShapeId) ||
            !ChannelRouting.CanCollaborate(packet.TargetChannel, _localBroadcastChannels,
                RemoteBroadcastChannels)) return false;

        _lastDeleteSequence = packet.Sequence;
        return true;
    }

    private DrawingPacket Handshake(DrawingPacketKind kind, Guid recipient, uint sequence = 0) =>
        new(kind, _epoch, LocalSession, recipient, sequence, (string[])_localReceiveOnlyChannels.Clone(), Array.Empty<MapShape>(),
            (string[])_localBroadcastChannels.Clone());

    private static string[] CopyChannels(IReadOnlyList<string> channels)
    {
        var copy = new string[channels.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = channels[i];
        }

        return copy;
    }

    private static bool SameChannels(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var i = 0; i < first.Count; i++)
        {
            if (first[i] != second[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidChannels(IReadOnlyList<string> channels)
    {
        if (channels.Count > DrawingChannels.MaxChannels)
        {
            return false;
        }

        var parsed = new DrawingChannels();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < channels.Count; i++)
        {
            var name = channels[i];
            if (name == null || !parsed.TryConfigure(name, out _) || parsed.Names.Count != 1 ||
                parsed.Names[0] != name || !names.Add(name))
            {
                return false;
            }
        }

        return true;
    }
}
