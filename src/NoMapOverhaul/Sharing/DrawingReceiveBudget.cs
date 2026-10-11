using System;

namespace NoMapOverhaul.Sharing;

/// <summary>Global decode limits defer native dequeues so one-shot requests remain in Steam's queue.</summary>
internal sealed class DrawingReceiveBudget
{
    public const int MaxMessagesPerTick = 32;
    public const int MaxMessagesPerSecond = ReceivedDrawings.MaxPeers * 4;
    private readonly Func<int, int> _dequeue;
    private double _window;
    private int _count;

    public DrawingReceiveBudget(Func<int, int> dequeue) => _dequeue = dequeue;

    public int Receive(double now)
    {
        Refresh(now);
        return _count == MaxMessagesPerSecond ? 0 : _dequeue(Math.Min(MaxMessagesPerTick, MaxMessagesPerSecond - _count));
    }

    public bool TryConsume(double now)
    {
        Refresh(now);
        if (_count == MaxMessagesPerSecond) return false;
        _count++;
        return true;
    }

    private void Refresh(double now)
    {
        if (now - _window >= 1) { _window = now; _count = 0; }
    }

    public void Clear() { _window = 0; _count = 0; }
}
