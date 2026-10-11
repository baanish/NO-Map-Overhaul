using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingNetworkScheduleTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("sharing polling enforces the exact two hundred millisecond deadline", Deadline),
        ("sharing polling runs five ticks across one hundred twenty frames", FrameRate),
        ("sharing polling skips missed ticks after a frame stall", Stall),
    };

    private static void Deadline()
    {
        var schedule = new DrawingNetworkSchedule();
        Expect(DrawingNetworkSchedule.TickInterval == 0.2, "fixed five hertz interval");
        Expect(schedule.TryTick(0), "first eligible update polls immediately");
        Expect(!schedule.TryTick(0) && !schedule.TryTick(0.199999), "no polling before the deadline");
        Expect(schedule.TryTick(0.2) && !schedule.TryTick(0.2), "exact deadline allows one tick");
        Expect(!schedule.TryTick(0.399999) && schedule.TryTick(0.4), "next tick retains its full interval");
    }

    private static void FrameRate()
    {
        var schedule = new DrawingNetworkSchedule();
        var ticks = 0;
        for (var frame = 0; frame < 120; frame++)
            if (schedule.TryTick(frame / 120.0)) ticks++;
        Expect(ticks == 5, "one second of render frames produces exactly five polling batches");
    }

    private static void Stall()
    {
        var schedule = new DrawingNetworkSchedule();
        Expect(schedule.TryTick(0) && schedule.TryTick(10), "poll once after the stall");
        Expect(!schedule.TryTick(10) && !schedule.TryTick(10.199999), "missed ticks cannot burst on resumption");
        Expect(schedule.TryTick(10.2), "resume the normal interval from the actual polling time");
    }
}
