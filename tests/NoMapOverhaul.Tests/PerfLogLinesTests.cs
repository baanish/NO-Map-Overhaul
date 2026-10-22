using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>The performance log's detail and teammates lines, and the counts they're built from.</summary>
internal static class PerfLogLinesTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("the detail line names render costs, rebuilds, rings and the session", DetailLine),
        ("the detail line reads n/a for what wasn't measured", DetailLineUnmeasured),
        ("the teammates line gives each teammate's sharing and Steam status, and counts silent ones", TeammatesLine),
        ("mission time reads m:ss, and h:mm:ss from an hour", MissionTime),
        ("mod activity keeps the most rings and starts over when taken", ActivityTake),
    };

    private static void DetailLine()
    {
        var render = new RenderSummary(0.42f, float.NaN, float.NaN, 412.4f, 160f, 520f, 1203400f, 0, 0);
        var line = PerfLogLines.Detail(render, 3.1f, new ActivityCounts(3, 41, 2, 2, RingPath.Gpu),
            new PerfContext("Multiplayer", 5, 213, fullMap: false, 754f));
        ExpectText(line, "Perf detail: Unity canvas update 0.42 ms avg, 3.10 ms worst frame; per frame 412 batches, 160 SetPass calls, " +
            "520 draw calls, 1203400 vertices. Rebuilt label layouts 3x, meshes 41x, received drawings 2x. 3D rings 2 at most, on the GPU. " +
            "Multiplayer, 5 on the team, 213 map icons, minimap, mission time 12:34.");
    }

    private static void DetailLineUnmeasured()
    {
        var context = new PerfContext("MainMenu", -1, -1, fullMap: false, float.NaN);
        ExpectText(PerfLogLines.Detail(null, float.NaN, default, context),
            "Perf detail: Unity canvas and render counters are the perf test's while it runs. Rebuilt label layouts 0x, meshes 0x, " +
            "received drawings 0x. 3D rings not drawn. MainMenu, no team, no map, no mission.");

        var unrecorded = new RenderSummary(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, 0, 0);
        var line = PerfLogLines.Detail(unrecorded, float.NaN, new ActivityCounts(0, 0, 0, 0, RingPath.Cpu),
            new PerfContext("SinglePlayer", 1, 40, fullMap: true, 5f));
        ExpectText(line, "Perf detail: Unity canvas update n/a avg, n/a worst frame; per frame n/a batches, n/a SetPass calls, " +
            "n/a draw calls, n/a vertices. Rebuilt label layouts 0x, meshes 0x, received drawings 0x. 3D rings 0 at most, on the CPU. " +
            "SinglePlayer, 1 on the team, 40 map icons, full map, mission time 0:05.");
    }

    private static void TeammatesLine()
    {
        Expect(PerfLogLines.Teammates(Array.Empty<TeammateWindow>()) == null, "no teammates, no line");
        var paired = new DrawingTraffic { SentMessages = 20, SentBytes = 13312, ReceivedMessages = 22, ReceivedBytes = 14438, SnapshotsApplied = 10 };
        var probing = new DrawingTraffic { SentMessages = 3, SentBytes = 100 };
        var line = PerfLogLines.Teammates(new[]
        {
            new TeammateWindow(1, ready: true, 12, 10, paired, new DrawingSessionStatus("Connected", 48, 1f, 0, 2048, 0.5)),
            new TeammateWindow(2, ready: false, 0, 0, probing, null),
        });
        ExpectText(line!, "Perf teammates: peer 1 ready: drawings 12 held, 10 shown; 10 snapshots applied; in 22 (14.1 KB), out 20 (13.0 KB); " +
            "Steam Connected, ping 48 ms, quality 100%, reliable 0 B pending, 2.0 KB unacked, queue 0.5 ms. " +
            "peer 2 not ready: drawings 0 held, 0 shown; 0 snapshots applied; in 0 (0.0 KB), out 3 (0.1 KB); Steam n/a.");

        var unknownQuality = PerfLogLines.Teammates(new[]
        {
            new TeammateWindow(3, ready: true, 0, 0, default, new DrawingSessionStatus("FindingRoute", -1, -1f, 0, 0, 0)),
        });
        Expect(unknownQuality!.Contains("Steam FindingRoute, ping -1 ms, quality n/a,"), $"expected an unknown quality, got '{unknownQuality}'");

        var silent = new TeammateWindow(5, ready: false, 0, 0, default, null);
        var folded = PerfLogLines.Teammates(new[]
        {
            new TeammateWindow(4, ready: true, 0, 0, default, null),
            silent,
            silent,
            new TeammateWindow(6, ready: false, 0, 0, new DrawingTraffic { ReceivedMessages = 1, ReceivedBytes = 40 }, null),
        });
        ExpectText(folded!, "Perf teammates: peer 4 ready: drawings 0 held, 0 shown; 0 snapshots applied; in 0 (0.0 KB), out 0 (0.0 KB); Steam n/a. " +
            "peer 6 not ready: drawings 0 held, 0 shown; 0 snapshots applied; in 1 (0.0 KB), out 0 (0.0 KB); Steam n/a. 2 not ready with no traffic.");
        ExpectText(PerfLogLines.Teammates(new[] { silent })!, "Perf teammates: 1 not ready with no traffic.");
    }

    private static void MissionTime()
    {
        ExpectText(PerfLogLines.MissionTime(0f), "0:00");
        ExpectText(PerfLogLines.MissionTime(754.9f), "12:34");
        ExpectText(PerfLogLines.MissionTime(3723f), "1:02:03");
        ExpectText(PerfLogLines.MissionTime(-2f), "0:00");
    }

    private static void ActivityTake()
    {
        ModActivity.Take();
        ModActivity.LabelLayouts++;
        ModActivity.MeshRebuilds += 5;
        ModActivity.CountRings(3, gpu: true);
        ModActivity.CountRings(1, gpu: false);
        var taken = ModActivity.Take();
        Expect(taken.LabelLayouts == 1 && taken.MeshRebuilds == 5 && taken.ReceivedSyncs == 0, "expected the counts since the last take");
        Expect(taken.MostRings == 3 && taken.RingPath == RingPath.Cpu, $"expected 3 rings at most, last on the CPU, got {taken.MostRings} {taken.RingPath}");
        var again = ModActivity.Take();
        Expect(again.LabelLayouts == 0 && again.MeshRebuilds == 0 && again.MostRings == 0 && again.RingPath == RingPath.None,
            "expected a take to start every count over");
    }
}
