using System;
using System.Collections.Generic;

namespace NoMapOverhaul.Sharing;

internal sealed class DrawingPeerState
{
    private readonly SharedMissionEpoch _epoch;
    private readonly string[] _receive;
    private readonly string[] _broadcast;
    private int _attempts;
    private int _failures;
    private double _nextHello;
    private double _retryAt;
    private bool _compatible;
    private bool _sessionFailureHandled;

    public DrawingPeerState(SharedMissionEpoch epoch, IReadOnlyList<string> receive, IReadOnlyList<string> broadcast, int diagnosticId = 0)
    {
        _epoch = epoch;
        DiagnosticId = diagnosticId;
        _receive = new List<string>(receive).ToArray();
        _broadcast = new List<string>(broadcast).ToArray();
        Protocol = new DrawingPeer(epoch, _receive, _broadcast);
    }

    public DrawingPeer Protocol;
    public readonly int DiagnosticId;
    public int HelloAttempts => _attempts;
    public double LastReceived;
    public double NextSnapshot;
    /// <summary>The snapshot sequence the last heartbeat named; an acknowledgement below it means a resend.</summary>
    public uint Advertised;
    public double ReceiveWindow;
    public int ReceiveCount;
    public int ReceiveBytes;
    public readonly DrawingSnapshotBudget.Peer ReceivedSnapshots = new();
    public int SentVersion = -1;
    public int Revision = -1;
    public uint Sequence;
    public uint DeleteSequence;
    public double NextDelete;
    /// <summary>Newest drawings left out of the last snapshot to fit the wire limits.</summary>
    public int Unshared;
    /// <summary>The last frame came from another protocol version; cleared by the next frame that decodes.</summary>
    public bool OtherVersion { get; set; }
    public double NextAccept = 0;
    /// <summary>This teammate's traffic since the performance log last took it; kept across restarts, since the teammate stays.</summary>
    public DrawingTraffic LogTraffic = default;
    public Guid LastHelloSession;
    public double HelloWindow;
    public int HelloSessionChanges;

    /// <summary>
    /// Messages one peer may send in a second before its channel is reset. Far above normal traffic (two snapshots, a
    /// heartbeat and an erase), so only a long stall's backlog or a misbehaving peer reaches it. A reset discards the
    /// backlog and re-pairs; it is not quarantine, since a stall or quick channel changes are not an attack.
    /// </summary>
    public const int FloodMessages = 40;
    public const int FloodBytes = 256 * 1024;

    public bool CanTransmit(double now) => now >= _retryAt;

    internal static bool IsUniqueTeammate<T>(IEnumerable<T> players, ulong id,
        Func<T, ulong> steamId, Func<T, bool> isTeammate)
    {
        if (id == 0) return false;
        var found = false;
        var eligible = false;
        foreach (var player in players)
        {
            if (steamId(player) != id) continue;
            if (found) return false;
            found = true;
            eligible = isTeammate(player);
        }
        return found && eligible;
    }

    /// <summary>Counts every dequeued frame, including unauthorized and superseded ones, before any payload decode.</summary>
    public bool CountReceive(double now, out bool flooded, int bytes = 0)
    {
        if (now - ReceiveWindow >= 1) { ReceiveWindow = now; ReceiveCount = ReceiveBytes = 0; }
        var wasFlooded = ReceiveCount > FloodMessages || ReceiveBytes > FloodBytes;
        ReceiveCount++;
        ReceiveBytes = (int)Math.Min((long)FloodBytes + 1, (long)ReceiveBytes + Math.Max(0, bytes));
        var allowed = ReceiveCount <= FloodMessages && ReceiveBytes <= FloodBytes;
        flooded = !allowed && !wasFlooded;
        return allowed;
    }

    public bool CanRequestDelete(double now) => Protocol.IsReady && CanTransmit(now) &&
        DeleteSequence != uint.MaxValue && now >= NextDelete;

    public bool TryHello(double now)
    {
        ObserveHandshake();
        // A teammate that has shown it runs the mod keeps trying: a half-open handshake whose replies all failed would
        // otherwise never pair. Probing teammates who may not have the mod is DrawingDiscovery's single probe.
        if (!CanTransmit(now) || now < _nextHello || (!Protocol.IsReady && !_compatible && _attempts >= 3)) return false;
        _nextHello = now + 5;
        if (!Protocol.IsReady) _attempts++;
        return true;
    }

    public void ObserveHandshake()
    {
        if (Revision == Protocol.Revision) return;
        ReceivedSnapshots.Pending = null;
        Revision = Protocol.Revision;
        if (Revision > 0)
        {
            _compatible = true;
            _attempts = 0;
        }
        _nextHello = 0;
        Sequence = DeleteSequence = Advertised = 0;
        NextDelete = 0;
        SentVersion = -1;
    }

    public bool RecordSend(bool success, double now)
    {
        _sessionFailureHandled = false;
        if (success) { _failures = 0; return false; }
        if (++_failures < 3) return false;
        Restart(now + 30);
        return true;
    }

    public bool RecordSessionFailure(double now)
    {
        // Repeated callbacks for the same failed transport cannot extend its cooldown.
        if (_sessionFailureHandled || !CanTransmit(now)) return false;
        Restart(now + 30);
        return true;
    }

    public void Restart(double retryAt = 0)
    {
        ReceivedSnapshots.Pending = null;
        _compatible |= Protocol.Revision > 0;
        Protocol = new DrawingPeer(_epoch, _receive, _broadcast);
        // A transport failure is not evidence that an unknown teammate has the mod.
        if (_compatible) _attempts = 0;
        _failures = 0;
        _sessionFailureHandled = true;
        _nextHello = _retryAt = retryAt;
        LastReceived = NextSnapshot = 0;
        SentVersion = Revision = -1;
        Sequence = DeleteSequence = Advertised = 0;
        NextDelete = 0;
        Unshared = 0;
    }
}
