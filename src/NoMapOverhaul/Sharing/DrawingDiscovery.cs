using System.Collections.Generic;

namespace NoMapOverhaul.Sharing;

/// <summary>
/// Which teammates have shown they run the mod this mission, and when an unconfirmed one may be probed. Probes to a
/// teammate who may not have the mod are sparse, three per mission, since their native networking is shared with the
/// game; each is one connection attempt. The schedule outlives channel changes, which recreate every peer, and starts over only with
/// a new mission or faction. A teammate who enables sharing later probes us, so stopping after the last offset loses
/// nothing.
/// </summary>
internal sealed class DrawingDiscovery
{
    /// <summary>Seconds after a teammate is first seen. NOXMFD's schedule for the same Steam Messages transport.</summary>
    public static readonly double[] ProbeOffsets = { 0, 60, 300 };

    private readonly Dictionary<ulong, (double FirstSeen, int Sent)> _unconfirmed = new();
    private readonly HashSet<ulong> _confirmed = new();
    private SharedMissionEpoch _epoch;

    public void Begin(SharedMissionEpoch epoch)
    {
        if (_epoch.Equals(epoch)) return;
        _epoch = epoch;
        _unconfirmed.Clear();
        _confirmed.Clear();
    }

    public bool IsConfirmed(ulong id) => _confirmed.Contains(id);

    /// <summary>
    /// An accepted packet. Only the mod sends a valid Hello, so its sender is confirmed as soon as one arrives, before
    /// pairing: if our confirming acknowledgement is then lost, its retries must not wait on spent probes.
    /// </summary>
    public void Accepted(ulong id, DrawingPacketKind kind, bool paired)
    {
        if (paired || kind == DrawingPacketKind.Hello) Confirm(id);
    }

    /// <summary>This teammate runs the mod: it gets the responsive heartbeat and recovery from here on.</summary>
    public void Confirm(ulong id)
    {
        _confirmed.Add(id);
        _unconfirmed.Remove(id);
    }

    public bool TryProbe(ulong id, double now)
    {
        if (_confirmed.Contains(id)) return false;
        if (!_unconfirmed.TryGetValue(id, out var record)) record = (now, 0);
        if (record.Sent >= ProbeOffsets.Length || now < record.FirstSeen + ProbeOffsets[record.Sent])
        {
            _unconfirmed[id] = record;
            return false;
        }

        _unconfirmed[id] = (record.FirstSeen, record.Sent + 1);
        return true;
    }
}
