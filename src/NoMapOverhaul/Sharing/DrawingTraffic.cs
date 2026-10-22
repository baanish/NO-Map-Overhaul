namespace NoMapOverhaul.Sharing;

/// <summary>Who takes sharing's traffic counts, each counting on its own.</summary>
public enum TrafficReader
{
    PerformanceLog,
    PerfTest,
}

/// <summary>
/// What drawing sharing sent, received and spent since it was last taken, for the performance log and the perf test.
/// Steam time is the main thread's time inside Steam's calls, where a slow call stalls the game.
/// </summary>
public struct DrawingTraffic
{
    public int SentMessages;
    public long SentBytes;
    public int ReceivedMessages;
    public long ReceivedBytes;
    public int SnapshotsApplied;
    public double SteamMs;
    public double SteamWorstMs;

    public void AddSteamCall(double ms)
    {
        SteamMs += ms;
        if (ms > SteamWorstMs) SteamWorstMs = ms;
    }

    public void Add(DrawingTraffic other)
    {
        SentMessages += other.SentMessages;
        SentBytes += other.SentBytes;
        ReceivedMessages += other.ReceivedMessages;
        ReceivedBytes += other.ReceivedBytes;
        SnapshotsApplied += other.SnapshotsApplied;
        SteamMs += other.SteamMs;
        if (other.SteamWorstMs > SteamWorstMs) SteamWorstMs = other.SteamWorstMs;
    }
}

/// <summary>
/// Steam's own view of the drawing session with one teammate, from <c>GetSessionConnectionInfo</c>'s quick status:
/// reliable bytes piling up or a long queue time mean Steam can't send as fast as sharing queues.
/// </summary>
public readonly struct DrawingSessionStatus
{
    public DrawingSessionStatus(string state, int pingMs, float quality, int pendingReliableBytes, int unackedReliableBytes, double queueMs)
    {
        State = state;
        PingMs = pingMs;
        Quality = quality;
        PendingReliableBytes = pendingReliableBytes;
        UnackedReliableBytes = unackedReliableBytes;
        QueueMs = queueMs;
    }

    /// <summary>The connection state's name, such as Connected or ProblemDetectedLocally.</summary>
    public string State { get; }

    public int PingMs { get; }

    /// <summary>The share of packets that arrived, 0 to 1, as Steam measures it locally; negative while unknown.</summary>
    public float Quality { get; }

    /// <summary>Reliable bytes queued and not yet sent.</summary>
    public int PendingReliableBytes { get; }

    /// <summary>Reliable bytes sent but not yet acknowledged.</summary>
    public int UnackedReliableBytes { get; }

    /// <summary>How long a message queued now would wait before going out.</summary>
    public double QueueMs { get; }
}
