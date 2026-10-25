using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingReceiveBudgetTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("global drawing budget defers dequeue and release of a one-shot erase", QueuedErase),
        ("drawing receive callback pulls only the remaining window budget", PartialWindow),
        ("flooding and unknown drawing senders preserve an honest peer's global allowance", FloodIsolation),
    };

    private static void FloodIsolation()
    {
        var epoch = new SharedMissionEpoch(42, 100.25, 7);
        var flooder = new DrawingPeerState(epoch, Array.Empty<string>(), Array.Empty<string>());
        var honest = new DrawingPeerState(epoch, Array.Empty<string>(), Array.Empty<string>());
        var queue = new Queue<DrawingPeerState?>(Enumerable.Repeat<DrawingPeerState?>(flooder, 64)
            .Concat(Enumerable.Repeat<DrawingPeerState?>(null, 32)).Append(honest));
        var returned = new List<DrawingPeerState?>();
        var budget = new DrawingReceiveBudget(limit =>
        {
            Expect(limit <= 32, "rejected traffic still has a per-tick dequeue cap");
            returned.Clear();
            while (returned.Count < limit && queue.Count > 0) returned.Add(queue.Dequeue());
            return returned.Count;
        });
        var honestReceived = 0;
        for (var tick = 0; tick < 4; tick++)
        {
            var now = tick * 0.2;
            var count = budget.Receive(now);
            for (var i = 0; i < count; i++)
            {
                var peer = returned[i];
                if (peer == null || !peer.CountReceive(now, out _)) continue;
                Expect(budget.TryConsume(now), "each roster peer below its flood limit has capacity");
                if (ReferenceEquals(peer, honest)) honestReceived++;
            }
        }
        Expect(queue.Count == 0 && honestReceived == 1, "the second peer is received in the same window");
        var remaining = 0;
        while (budget.TryConsume(0.8)) remaining++;
        Expect(remaining == 87, "only forty flooder frames and one honest frame consumed the 128-message window");
    }

    private static void QueuedErase()
    {
        Expect(DrawingReceiveBudget.MaxMessagesPerTick == 32 && DrawingReceiveBudget.MaxMessagesPerSecond == 128,
            "bounded native batches cover four messages per second from thirty-two peers");
        var queue = new Queue<string>(Enumerable.Repeat("snapshot", 128).Append("delete"));
        var returned = new List<string>();
        var released = new List<string>();
        var calls = 0;
        var acceptedDeletes = 0;
        var budget = new DrawingReceiveBudget(limit =>
        {
            calls++;
            Expect(limit <= 32, "native dequeue remains tick bounded");
            returned.Clear();
            while (returned.Count < limit && queue.Count > 0) returned.Add(queue.Dequeue());
            return returned.Count;
        });
        void Drain(double now)
        {
            var count = budget.Receive(now);
            for (var i = 0; i < count; i++)
            {
                try
                {
                    Expect(budget.TryConsume(now), "returned authorized messages have global processing capacity");
                    if (returned[i] == "delete") acceptedDeletes++;
                }
                finally { released.Add(returned[i]); }
            }
        }
        Drain(0);
        Drain(0.2);
        Drain(0.4);
        Drain(0.65);
        Drain(0.999);
        Expect(calls == 4 && queue.Count == 1 && queue.Peek() == "delete", "exhausted window must not dequeue the erase");
        Expect(released.Count == 128 && acceptedDeletes == 0, "queued erase has not been processed or released");
        Drain(1);
        Expect(calls == 5 && queue.Count == 0 && acceptedDeletes == 1 && released.Count == 129,
            "next window delivers and releases the one-shot erase exactly once");
    }

    private static void PartialWindow()
    {
        var limits = new List<int>();
        var budget = new DrawingReceiveBudget(limit => { limits.Add(limit); return 0; });
        budget.Receive(0);
        Expect(limits.Single() == 32, "full global capacity still pulls only one bounded native batch");
        for (var i = 0; i < 126; i++) Expect(budget.TryConsume(0), "setup consumes one hundred twenty-six message slots");
        budget.Receive(0.5);
        Expect(limits.Count == 2 && limits[1] == 2, "only the two remaining slots can be pulled from Steam");
        Expect(budget.TryConsume(0.5) && budget.TryConsume(0.5) && !budget.TryConsume(0.5), "global budget stays exactly one hundred twenty-eight");
        budget.Receive(0.75);
        Expect(limits.Count == 2, "no native callback while the window is exhausted");
        budget.Clear();
        budget.Receive(0.75);
        Expect(limits.Count == 3 && limits[2] == 32, "local reset restores the initial window budget");
    }
}
