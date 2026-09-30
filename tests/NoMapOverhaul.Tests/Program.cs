using System.Numerics;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.Missiles;
using NoMapOverhaul.Runways;

namespace NoMapOverhaul.Tests;

/// <summary>
/// Runway 09/27: Start at the west end, End at the east end, 2 km long. Landing at Start is "09" (not reversed).
/// Positions are meters, X east and Y north.
/// </summary>
internal static class Program
{
    private const float Range = 5000f;
    private static readonly RunwayLine EastWest = new(new Vector2(0, 0), new Vector2(2000, 0));
    private static readonly RunwayLine NorthSouth = new(new Vector2(1000, -1000), new Vector2(1000, 1000));

    private static int Main()
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("out of range selects nothing", OutOfRangeSelectsNothing),
            ("the closer end shows", CloserEndShows),
            ("side-on on a base leg shows the approach end", SideOnBaseLegShowsApproachEnd),
            ("the nearest runway wins", NearestRunwayWins),
            ("over the runway shows the closer end", OverTheRunwayShowsCloserEnd),
            ("callout without airbase", CalloutWithoutAirbase),
            ("painted number overrides the heading number", PaintedNumberOverridesHeading),
            ("callout uses the squadron table", CalloutUsesTable),
            ("unknown airbase is abbreviated", UnknownAirbaseAbbreviated),
            ("a missile on screen gets no arrow", MissileOnScreenGetsNoArrow),
            ("a missile off the right gets the right edge", MissileOffRightGetsRightEdge),
            ("a missile behind on the left points left", MissileBehindLeftPointsLeft),
            ("a missile behind and above points up", MissileBehindAbovePointsUp),
            ("a missile dead astern points down", MissileDeadAsternPointsDown),
        };
        tests.AddRange(MapToolTests.All);
        tests.AddRange(WaypointTests.All);
        tests.AddRange(PenTextTests.All);
        tests.AddRange(MeasureToolTests.All);
        tests.AddRange(LabelLayoutTests.All);
        tests.AddRange(MenuLayoutTests.All);
        tests.AddRange(FrameStatsTests.All);
        tests.AddRange(PerfTestTests.All);
        tests.AddRange(RingProjectionTests.All);
        tests.AddRange(KnownPositionsTests.All);
        tests.AddRange(SettingsMigrationTests.All);

        var failed = 0;
        foreach (var (name, test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine(failed == 0 ? $"All {tests.Count} tests passed." : $"{failed} of {tests.Count} tests failed.");
        return failed == 0 ? 0 : 1;
    }

    private static ApproachChoice? Select(Vector2 position, params RunwayLine[] runways) =>
        ApproachSelector.Select(runways.Length == 0 ? new[] { EastWest } : runways, position, Range);

    private static void OutOfRangeSelectsNothing() =>
        Expect(Select(new Vector2(-6000, 0)) is null, "expected nothing beyond 5 km of the runway");

    private static void CloserEndShows()
    {
        ExpectEnd(Select(new Vector2(-4000, 0)), reverse: false);
        ExpectEnd(Select(new Vector2(6000, 0)), reverse: true);
    }

    /// <summary>The line's main use: judging the turn onto final while flying 90 degrees to the runway.</summary>
    private static void SideOnBaseLegShowsApproachEnd() =>
        ExpectEnd(Select(new Vector2(-1500, 1200)), reverse: false);

    private static void NearestRunwayWins()
    {
        var north = new RunwayLine(new Vector2(0, 400), new Vector2(2000, 400));
        Expect(Select(new Vector2(-3000, 50), EastWest, north) is { RunwayIndex: 0 }, "expected the south runway");
        Expect(Select(new Vector2(-2500, 380), EastWest, north) is { RunwayIndex: 1, Reverse: false }, "expected the north runway");
        Expect(Select(new Vector2(1050, -3000), EastWest, NorthSouth) is { RunwayIndex: 1, Reverse: false }, "expected the crossing runway");
    }

    private static void OverTheRunwayShowsCloserEnd()
    {
        ExpectEnd(Select(new Vector2(500, 0)), reverse: false);
        ExpectEnd(Select(new Vector2(1500, 0)), reverse: true);
    }

    private static void CalloutWithoutAirbase() =>
        ExpectText(RunwayNames.Callout("27", null), "RWY 27");

    private static void PaintedNumberOverridesHeading()
    {
        ExpectText(RunwayNames.Painted("Feldspar International Airport", "33"), "34");
        ExpectText(RunwayNames.Painted("Feldspar International Airport", "15"), "16");
        ExpectText(RunwayNames.Painted("Feldspar International Airport", "06"), "06");
        ExpectText(RunwayNames.Painted("Maris Airport", "33"), "33");
        ExpectText(RunwayNames.Painted(null, "33"), "33");
    }

    private static void CalloutUsesTable()
    {
        ExpectText(RunwayNames.Callout("27", "North Boscali Airbase"), "NBSCLI RWY 27");
        ExpectText(RunwayNames.AbbreviateAirbase("Ashwood Auxiliary Airstrip"), "ASHWD AUX");
    }

    private static void UnknownAirbaseAbbreviated()
    {
        ExpectText(RunwayNames.AbbreviateAirbase("Dustbowl Highway Strip"), "DSTBWL HWY STRP");
        ExpectText(RunwayNames.AbbreviateAirbase("Opal Airport"), "OPL");
        ExpectText(RunwayNames.AbbreviateAirbase("Maris Heliport"), "MRS HELI");
    }

    private static void MissileOnScreenGetsNoArrow() =>
        Expect(Pin(new Vector3(100, 0, 1000)) is null, "expected no arrow for a missile inside the view");

    private static void MissileOffRightGetsRightEdge() =>
        ExpectPin(Pin(new Vector3(2000, 0, 1000)), new Vector2(960, 0));

    /// <summary>Camera.WorldToScreenPoint mirrors a point behind the camera, which would put this arrow on the right.</summary>
    private static void MissileBehindLeftPointsLeft() =>
        ExpectPin(Pin(new Vector3(-500, 0, -1000)), new Vector2(-960, 0));

    private static void MissileBehindAbovePointsUp() =>
        ExpectPin(Pin(new Vector3(0, 300, -1000)), new Vector2(0, 540));

    private static void MissileDeadAsternPointsDown() =>
        ExpectPin(Pin(new Vector3(0, 0, -1000)), new Vector2(0, -540));

    /// <summary>A 1920x1080 screen with a 60 degree vertical field of view.</summary>
    private static Vector2? Pin(Vector3 cameraSpace) =>
        ScreenEdge.Pin(cameraSpace, new Vector2(960, 540), 540f / MathF.Tan(MathF.PI / 6f));

    private static void ExpectPin(Vector2? actual, Vector2 expected) =>
        Expect(actual is { } pin && Vector2.Distance(pin, expected) < 0.01f, $"expected an arrow at {expected}, got {actual?.ToString() ?? "none"}");

    private static void ExpectEnd(ApproachChoice? choice, bool reverse) =>
        Expect(choice is { } c && c.Reverse == reverse, $"expected {(reverse ? "27" : "09")}, got {Describe(choice)}");

    internal static void ExpectText(string actual, string expected) =>
        Expect(actual == expected, $"expected '{expected}', got '{actual}'");

    private static string Describe(ApproachChoice? choice) =>
        choice is { } c ? $"runway {c.RunwayIndex} reverse={c.Reverse}" : "none";

    /// <summary>Whether the tool asked for its overlay to be redrawn since the last call, cleared as the map layer does.</summary>
    internal static bool TakeOverlayInvalid(MapTool tool)
    {
        var invalid = tool.OverlayInvalid;
        tool.OverlayInvalid = false;
        return invalid;
    }

    internal static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
