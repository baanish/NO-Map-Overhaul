using System;

namespace NoMapOverhaul.Sharing;

/// <summary>Defer expensive replacements without losing the newest authorized state. Removals and unchanged shapes are free.</summary>
internal sealed class DrawingSnapshotBudget
{
    public const int PeerCapacity = 200;
    public const int PeerRefillPerSecond = 100;
    public const int GlobalCapacity = 400;
    public const int GlobalRefillPerSecond = 200;

    internal sealed class Peer
    {
        internal readonly Bucket Changes = new(PeerCapacity, PeerRefillPerSecond);
        internal double NextSnapshot;
        public DrawingPacket? Pending;
    }

    internal sealed class Bucket
    {
        private readonly int _capacity;
        private readonly int _refill;
        private double _spent;
        private double _refillStart;

        public Bucket(int capacity, int refill) { _capacity = capacity; _refill = refill; }

        public bool Fits(int cost, double now)
        {
            // Whole-shape debits avoid accumulating fractional refill errors on every deferred tick.
            var refilled = Math.Max(0, now - _refillStart) * _refill;
            if (refilled >= _spent) { _refillStart = now; _spent = 0; return cost <= _capacity; }
            return cost <= _capacity + refilled - _spent;
        }

        public void Consume(int cost) => _spent += cost;
    }

    private Bucket _changes = new(GlobalCapacity, GlobalRefillPerSecond);

    public DrawingPacket? TryTake(Peer peer, ulong sender, ReceivedDrawings received, double now)
    {
        if (peer.Pending == null || now < peer.NextSnapshot) return null;
        var cost = received.ChangeCost(sender, peer.Pending.Shapes);
        if (!peer.Changes.Fits(cost, now) || !_changes.Fits(cost, now)) return null;
        peer.Changes.Consume(cost);
        _changes.Consume(cost);
        peer.NextSnapshot = now + DrawingNetworkSchedule.SnapshotInterval;
        var snapshot = peer.Pending;
        peer.Pending = null;
        return snapshot;
    }

    public void Clear() => _changes = new Bucket(GlobalCapacity, GlobalRefillPerSecond);
}
