using System.Numerics;
using BaanishUiImprovements.Tracking;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>Where a unit is drawn from what its side knows: live tracks move it, anything else leaves it where it was last known.</summary>
internal static class KnownPositionsTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a live track moves the unit", LiveTrackMoves),
        ("a stale or lost track freezes the unit until it is spotted again", StaleTrackFreezesUntilSpotted),
        ("a track first seen stale takes the game's last known position", FirstSeenStaleTakesLastKnown),
        ("a unit never known has no position", NeverKnownHasNoPosition),
    };

    private static readonly Vector3 First = new(1000, 3000, 2000);
    private static readonly Vector3 Moved = new(4000, 5000, 6000);

    private static void LiveTrackMoves()
    {
        var known = new KnownPositions();
        Expect(known.TryResolve(7, TrackState.Live, First, out var position) && position == First, "expected the reported position");
        Expect(known.TryResolve(7, TrackState.Live, Moved, out position) && position == Moved, "expected a live track to move");
    }

    private static void StaleTrackFreezesUntilSpotted()
    {
        var known = new KnownPositions();
        known.TryResolve(7, TrackState.Live, First, out _);
        Expect(known.TryResolve(7, TrackState.Stale, Moved, out var position) && position == First, "expected a stale track to stay at the last live position");
        Expect(known.TryResolve(7, TrackState.Unknown, Moved, out position) && position == First, "expected a lost track to stay there too");
        Expect(known.TryResolve(7, TrackState.Live, Moved, out position) && position == Moved, "expected a track spotted again to move");
    }

    private static void FirstSeenStaleTakesLastKnown()
    {
        var known = new KnownPositions();
        Expect(known.TryResolve(7, TrackState.Stale, First, out var position) && position == First, "expected the game's last known position");
        Expect(known.TryResolve(7, TrackState.Stale, Moved, out position) && position == First, "expected it to stay there while stale");
    }

    private static void NeverKnownHasNoPosition()
    {
        var known = new KnownPositions();
        Expect(!known.TryResolve(7, TrackState.Unknown, Moved, out _), "expected an unknown unit to have no position");
        known.TryResolve(7, TrackState.Live, First, out _);
        known.Forget();
        Expect(!known.TryResolve(7, TrackState.Unknown, Moved, out _), "expected a new mission to forget old positions");
    }
}
