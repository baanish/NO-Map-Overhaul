using System;
using System.Globalization;

namespace NoMapOverhaul.Diagnostics;

/// <summary>Opt-in metadata logging stays bounded even when a peer floods rejected packets.</summary>
internal sealed class DrawingNetworkLog
{
    public const int MaxEventsPerWindow = 128;
    public const double WindowSeconds = 5;
    private readonly Action<string> _write;
    private double _windowStart;
    private int _events;
    private int _suppressed;

    public DrawingNetworkLog(Action<string> write) => _write = write;

    public bool Enabled { get; private set; }

    public void Update(bool enabled, double now)
    {
        if (enabled == Enabled)
        {
            if (enabled) Refresh(now);
            return;
        }

        if (!enabled)
        {
            FlushSuppressed(now);
            Emit(now, "logging off.");
        }
        Enabled = enabled;
        _windowStart = now;
        _events = _suppressed = 0;
        if (enabled) Write(now, "logging on; peer labels are local to this client.");
    }

    public void Write(double now, string message)
    {
        if (!Enabled) return;
        Refresh(now);
        if (_events == MaxEventsPerWindow)
        {
            if (_suppressed < int.MaxValue) _suppressed++;
            return;
        }
        _events++;
        Emit(now, message);
    }

    private void Refresh(double now)
    {
        if (now - _windowStart < WindowSeconds) return;
        FlushSuppressed(now);
        _windowStart = now;
        _events = 0;
    }

    private void FlushSuppressed(double now)
    {
        if (_suppressed > 0) Emit(now, $"suppressed {_suppressed} events at the logging limit.");
        _suppressed = 0;
    }

    private void Emit(double now, string message) =>
        _write("Network " + now.ToString("F3", CultureInfo.InvariantCulture) + "s: " + message);
}
