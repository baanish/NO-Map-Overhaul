using System.Collections.Generic;

namespace NoMapOverhaul.Sharing;

/// <summary>
/// Which teammates have shown they run the mod this mission, and which unconfirmed ones have had their probe. Each
/// teammate who may not have the mod gets one probe per mission: a probe is a Steam connection set up at both ends,
/// and each new connection can make Steam hold the networking lock the game waits on. On a server with 26 teammates,
/// three probes each meant about 70 failed connections per player. A teammate who joins or enables sharing later
/// probes us, so one is enough. The record outlives channel changes, which recreate every peer, and starts over only
/// with a new mission or faction.
/// </summary>
internal sealed class DrawingDiscovery
{
    private readonly HashSet<ulong> _probed = new();
    private readonly HashSet<ulong> _confirmed = new();
    private SharedMissionEpoch _epoch;

    public void Begin(SharedMissionEpoch epoch)
    {
        if (_epoch.Equals(epoch)) return;
        _epoch = epoch;
        _probed.Clear();
        _confirmed.Clear();
    }

    public bool IsConfirmed(ulong id) => _confirmed.Contains(id);

    /// <summary>
    /// An accepted packet. Only the mod sends a valid Hello, so its sender is confirmed as soon as one arrives, before
    /// pairing: if our confirming acknowledgement is then lost, its retries must not wait on a spent probe.
    /// </summary>
    public void Accepted(ulong id, DrawingPacketKind kind, bool paired)
    {
        if (paired || kind == DrawingPacketKind.Hello) Confirm(id);
    }

    /// <summary>This teammate runs the mod: it gets the responsive heartbeat and recovery from here on.</summary>
    public void Confirm(ulong id) => _confirmed.Add(id);

    /// <summary>True the first time an unconfirmed teammate is asked about this mission.</summary>
    public bool TryProbe(ulong id) => !_confirmed.Contains(id) && _probed.Add(id);
}
