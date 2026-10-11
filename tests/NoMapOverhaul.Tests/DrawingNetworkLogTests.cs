using System.Globalization;
using NoMapOverhaul.Diagnostics;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingNetworkLogTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("drawing network logging emits nothing while disabled", Disabled),
        ("drawing network logging timestamps use invariant formatting", InvariantTimestamp),
        ("drawing network logging caps events and flushes suppression without traffic", EventLimit),
        ("drawing network logging skips missed windows without a catch-up burst", MissedWindows),
        ("drawing network logging disables once and re-enables with fresh capacity", Toggle),
    };

    private static void Disabled()
    {
        var messages = new List<string>();
        var log = new DrawingNetworkLog(messages.Add);
        Expect(!log.Enabled, "the sink stays disabled until its setting is applied");
        log.Write(0, "snapshot");
        log.Update(false, 5);
        log.Write(100, "rejection");
        log.Update(false, 100);
        Expect(messages.Count == 0, "disabled logging must not invoke the output sink");
    }

    private static void InvariantTimestamp()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var messages = new List<string>();
            var log = new DrawingNetworkLog(messages.Add);
            log.Update(true, 12.25);
            log.Write(12.3456, "snapshot accepted; shapes=3 bytes=120.");
            Expect(log.Enabled && messages.Count == 2, "enabling emits one notice and permits metadata");
            ExpectText(messages[0], "Network 12.250s: logging on; peer labels are local to this client.");
            ExpectText(messages[1], "Network 12.346s: snapshot accepted; shapes=3 bytes=120.");
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static void EventLimit()
    {
        Expect(DrawingNetworkLog.MaxEventsPerWindow == 128 && DrawingNetworkLog.WindowSeconds == 5,
            "the diagnostic output contract is one hundred twenty-eight events per five seconds");
        var messages = new List<string>();
        var log = new DrawingNetworkLog(messages.Add);
        log.Update(true, 0);
        for (var i = 0; i < 127; i++) log.Write(0.2, "event");
        Expect(messages.Count == 128, "the enabling notice consumes one event slot");
        log.Write(0.3, "dropped first");
        log.Write(0.4, "dropped second");
        log.Write(4.999, "dropped third");
        log.Update(true, 4.999);
        Expect(messages.Count == 128, "excess events must stay suppressed before the boundary");
        log.Update(true, 5);
        Expect(messages.Count == 129, "an idle update flushes exactly one suppression summary");
        ExpectText(messages[^1], "Network 5.000s: suppressed 3 events at the logging limit.");
        log.Update(true, 5);
        Expect(messages.Count == 129, "a suppression summary cannot repeat");
        log.Write(5, "next window");
        Expect(messages.Count == 130, "the next window accepts fresh events");
        ExpectText(messages[^1], "Network 5.000s: next window");
    }

    private static void MissedWindows()
    {
        var messages = new List<string>();
        var log = new DrawingNetworkLog(messages.Add);
        log.Update(true, 0);
        for (var i = 0; i < 127; i++) log.Write(0, "event");
        log.Write(1, "dropped");
        log.Update(true, 100);
        Expect(messages.Count == 129, "a twenty-window jump emits one suppression summary");
        ExpectText(messages[^1], "Network 100.000s: suppressed 1 events at the logging limit.");
        log.Update(true, 100);
        for (var i = 0; i < 128; i++) log.Write(100, "fresh event");
        log.Write(100, "dropped again");
        log.Update(true, 104.999);
        Expect(messages.Count == 257, "the fresh window has exactly one hundred twenty-eight slots");
        log.Update(true, 105);
        Expect(messages.Count == 258, "the next boundary follows the jump time, without catch-up windows");
        ExpectText(messages[^1], "Network 105.000s: suppressed 1 events at the logging limit.");
    }

    private static void Toggle()
    {
        var messages = new List<string>();
        var log = new DrawingNetworkLog(messages.Add);
        log.Update(true, 0);
        for (var i = 0; i < 127; i++) log.Write(0, "event");
        log.Write(0, "dropped first");
        log.Write(0, "dropped second");
        log.Update(false, 1);
        Expect(!log.Enabled && messages.Count == 130, "disabling flushes dropped events and emits one off notice");
        ExpectText(messages[^2], "Network 1.000s: suppressed 2 events at the logging limit.");
        ExpectText(messages[^1], "Network 1.000s: logging off.");
        log.Update(false, 1);
        log.Write(1, "disabled event");
        Expect(messages.Count == 130, "repeated disabled updates and writes emit nothing");
        log.Update(true, 2);
        Expect(log.Enabled, "logging can be re-enabled before the previous window expired");
        ExpectText(messages[^1], "Network 2.000s: logging on; peer labels are local to this client.");
        for (var i = 0; i < 127; i++) log.Write(2, "fresh event");
        Expect(messages.Count == 258, "re-enabling restores one hundred twenty-eight slots including its notice");
        log.Update(true, 7);
        Expect(messages.Count == 258, "suppression state from the previous session must not survive re-enabling");
    }
}
