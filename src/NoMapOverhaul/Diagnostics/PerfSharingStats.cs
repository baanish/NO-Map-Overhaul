using NoMapOverhaul.Sharing;

namespace NoMapOverhaul.Diagnostics;

/// <summary>One perf test condition's sharing traffic and garbage collections over its slices. Unity-free.</summary>
public struct PerfSharingStats
{
    private DrawingTraffic _traffic;
    private int _collections;
    private int _mostShown;
    private double _seconds;

    /// <param name="shown">Teammates' drawings showing as the slice ended.</param>
    public void Add(DrawingTraffic traffic, int collections, int shown, double seconds)
    {
        _traffic.Add(traffic);
        _collections += collections;
        _mostShown = System.Math.Max(_mostShown, shown);
        _seconds += seconds;
    }

    public PerfSharingSummary Summarize(int frames)
    {
        var perSecond = _seconds > 0 ? 1 / _seconds : 0;
        return new PerfSharingSummary(
            (float)(_traffic.SentBytes / 1024.0 * perSecond), (float)(_traffic.SentMessages * perSecond),
            (float)(_traffic.ReceivedBytes / 1024.0 * perSecond), (float)(_traffic.ReceivedMessages * perSecond),
            (float)(_traffic.SnapshotsApplied * perSecond), frames > 0 ? (float)(_traffic.SteamMs / frames) : 0f,
            (float)_traffic.SteamWorstMs, (float)(_collections * perSecond), _mostShown);
    }
}

/// <summary>Per second unless named otherwise. Steam ms is the main thread's time in Steam's calls, per frame and the longest one.</summary>
public readonly struct PerfSharingSummary
{
    public PerfSharingSummary(float sentKb, float sentMessages, float receivedKb, float receivedMessages, float snapshotsApplied,
        float steamMsPerFrame, float steamWorstMs, float collections, int receivedShown)
    {
        SentKb = sentKb;
        SentMessages = sentMessages;
        ReceivedKb = receivedKb;
        ReceivedMessages = receivedMessages;
        SnapshotsApplied = snapshotsApplied;
        SteamMsPerFrame = steamMsPerFrame;
        SteamWorstMs = steamWorstMs;
        Collections = collections;
        ReceivedShown = receivedShown;
    }

    public float SentKb { get; }
    public float SentMessages { get; }
    public float ReceivedKb { get; }
    public float ReceivedMessages { get; }
    public float SnapshotsApplied { get; }
    public float SteamMsPerFrame { get; }
    public float SteamWorstMs { get; }

    /// <summary>The game's garbage collections, any code's.</summary>
    public float Collections { get; }

    /// <summary>The most teammates' drawings showing at any slice's end.</summary>
    public int ReceivedShown { get; }
}
