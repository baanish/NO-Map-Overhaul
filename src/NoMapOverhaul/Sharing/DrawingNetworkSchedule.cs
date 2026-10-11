namespace NoMapOverhaul.Sharing;

/// <summary>Five polling ticks per second at most; missed ticks are not replayed.</summary>
internal sealed class DrawingNetworkSchedule
{
    public const double TickInterval = 0.2;
    private double _nextTick;

    public bool TryTick(double now)
    {
        if (now < _nextTick) return false;
        _nextTick = now + TickInterval;
        return true;
    }
}
