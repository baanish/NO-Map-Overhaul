using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingReceiveBudgetTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("global drawing budget defers dequeue and release of a one-shot erase", QueuedErase),
        ("drawing receive callback pulls only the remaining window budget", PartialWindow),
    };

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
