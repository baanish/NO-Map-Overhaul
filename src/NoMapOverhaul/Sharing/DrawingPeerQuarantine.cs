using System.Collections.Generic;

namespace NoMapOverhaul.Sharing;

/// <summary>Rejected identities survive roster churn. Exhausting this bounded record stops sharing until reset.</summary>
internal sealed class DrawingPeerQuarantine
{
    public const int MaxRejectedPeers = 64;
    private readonly HashSet<ulong> _rejected = new();

    public bool Allows(ulong id) => _rejected.Count < MaxRejectedPeers && !_rejected.Contains(id);

    public bool Reject(ulong id)
    {
        if (_rejected.Count < MaxRejectedPeers) _rejected.Add(id);
        return _rejected.Count == MaxRejectedPeers;
    }

    public void Clear() => _rejected.Clear();
}
