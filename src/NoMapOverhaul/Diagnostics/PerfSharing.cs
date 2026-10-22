namespace NoMapOverhaul.Diagnostics;

/// <summary>Which way the perf test's drawings travel between teammates, the Diagnostics → Perf test sharing setting.</summary>
public enum PerfTestSharing
{
    /// <summary>Its drawings stay on this map, as the test always measured.</summary>
    LocalOnly,

    /// <summary>Its drawings also go to teammates who receive the test's channel.</summary>
    Send,

    /// <summary>It draws nothing itself and shows what teammates running the test send on its channel.</summary>
    Receive,

    /// <summary>Both: it sends its drawings and shows teammates' on the test's channel.</summary>
    Bidirectional,
}

/// <summary>
/// The perf test's channel while a sharing mode runs, joined to the channel lists in memory by
/// the map tools' PerfSharing. A teammate sees the test's drawings only while running a test that
/// receives, so a teammate who isn't testing sees none.
/// </summary>
public sealed class PerfSharing
{
    public const string Channel = "perf-test";

    public PerfSharing(PerfTestSharing mode)
    {
        Sends = mode is PerfTestSharing.Send or PerfTestSharing.Bidirectional;
        Receives = mode is PerfTestSharing.Receive or PerfTestSharing.Bidirectional;
    }

    public bool Sends { get; }

    public bool Receives { get; }
}
