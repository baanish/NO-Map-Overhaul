using NoMapOverhaul.MapTools;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>Map notes merged into as few meshes as they can be without any overlapping glyphs changing order.</summary>
internal static class NoteRunsTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("notes in one font share one run", OneMaterialOneRun),
        ("a fallback glyph between two overlapping notes splits the runs", OverlapSplits),
        ("a fallback glyph clear of the later notes merges into its run", ClearFallbackMerges),
        ("a full run opens a new one", FullRunOpensNew),
    };

    private const int Primary = 0;
    private const int Fallback = 1;

    private static void OneMaterialOneRun()
    {
        var runs = new NoteRuns(64000);
        for (var i = 0; i < 80; i++)
        {
            Expect(runs.Pick(Primary, 0f, 0f, 10f, 10f, 540) == 0, $"expected note {i} in the first run");
        }

        Expect(runs.Count == 1, $"expected one run, got {runs.Count}");
    }

    private static void OverlapSplits()
    {
        var runs = new NoteRuns(64000);
        Expect(runs.Pick(Primary, 0f, 0f, 10f, 10f, 36) == 0, "expected the first note in run 0");
        Expect(runs.Pick(Fallback, 0f, 0f, 10f, 10f, 4) == 1, "expected its fallback glyph after it");
        // The second note covers the first one's fallback glyph, so it can't join run 0 and draw under it.
        Expect(runs.Pick(Primary, 5f, 5f, 15f, 15f, 36) == 2, "expected the second note in a run of its own");
        // A fallback glyph that only touches the second note still blends into it, so it can't go back into run 1.
        Expect(runs.Pick(Fallback, 15f, 15f, 17f, 17f, 4) == 3, "expected a touching fallback glyph to open a run too");
        Expect(runs.Count == 4, $"expected four runs, got {runs.Count}");
    }

    private static void ClearFallbackMerges()
    {
        var runs = new NoteRuns(64000);
        Expect(runs.Pick(Primary, 0f, 0f, 10f, 10f, 36) == 0, "expected the first note in run 0");
        Expect(runs.Pick(Fallback, 0f, 0f, 10f, 10f, 4) == 1, "expected its fallback glyph after it");
        Expect(runs.Pick(Primary, 50f, 50f, 60f, 60f, 36) == 0, "expected a note clear of the fallback glyph to join run 0");
        Expect(runs.Pick(Fallback, 50f, 50f, 60f, 60f, 4) == 1, "expected its fallback glyph to join run 1");
        Expect(runs.Count == 2, $"expected two runs, got {runs.Count}");
        runs.Clear();
        Expect(runs.Pick(Fallback, 0f, 0f, 1f, 1f, 4) == 0 && runs.Count == 1, "expected a cleared planner to start again at run 0");
    }

    private static void FullRunOpensNew()
    {
        var runs = new NoteRuns(100);
        Expect(runs.Pick(Primary, 0f, 0f, 1f, 1f, 60) == 0, "expected the first note in run 0");
        Expect(runs.Pick(Primary, 0f, 0f, 1f, 1f, 40) == 0, "expected a note that just fits to join it");
        Expect(runs.Pick(Primary, 0f, 0f, 1f, 1f, 4) == 1, "expected a note past the cap to open run 1");
        Expect(runs.Pick(Primary, 0f, 0f, 1f, 1f, 4) == 1, "expected the next note to join the newer run");
        Expect(runs.Count == 2, $"expected two runs, got {runs.Count}");
    }
}
