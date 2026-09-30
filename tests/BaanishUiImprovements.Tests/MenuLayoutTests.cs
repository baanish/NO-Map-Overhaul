using BaanishUiImprovements.MapTools;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// Where the map tools menu goes on the full map. The game's map is 900 canvas units square in the screen's centre, and
/// its canvases scale by the screen's height over 1080, so each screen here builds the map and HUD the game would.
/// Outside the map the placements are the same in design pixels at every 16:9 or wider screen, and at 5:4.
/// </summary>
internal static class MenuLayoutTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("one rail column is design C's rail", OneColumnIsDesignC),
        ("rail columns read left to right, a group to a row", ColumnsReadLeftToRight),
        ("at 2560x1440 the rail stands left of the map in three columns and the strip above it", OutsideAt2560x1440),
        ("at 1920x1080 the menu sits outside as at 1440p", OutsideAt1920x1080),
        ("at 3440x1440 the menu sits outside as at 1440p", OutsideAt3440x1440),
        ("at 1280x1024 the menu sits outside as at 1440p", OutsideAt1280x1024),
        ("a square screen keeps the rail inside and the strip outside", SquareScreenKeepsRailInside),
        ("a map near the screen's top keeps the strip inside, and one rail column fits", TallMapKeepsStripInside),
        ("the HUD in the way keeps the rail inside", HudInTheWayKeepsRailInside),
        ("the strip takes the first wide enough stretch above the map, else goes inside", StripTakesFirstWideStretch),
    };

    private const int Tools = 6;
    private const int Swatches = 6;

    private static void OneColumnIsDesignC()
    {
        var grid = new RailGrid(1, Tools, Swatches);
        ExpectNear(grid.Width, 48f, "width");
        ExpectNear(grid.Height, 527f, "height");
        ExpectSlot(grid.Head, 0f, 0f, "head");
        ExpectSlot(grid.Tools[5], 0f, 49f + 5 * 44f, "last tool");
        ExpectSlot(grid.Actions[2], 0f, 321f + 2 * 40f, "clear");
        ExpectNear(grid.SwatchCenters[0].X, 12f, "first swatch x");
        ExpectNear(grid.SwatchCenters[5].X, 36f, "last swatch x");
        ExpectNear(grid.SwatchCenters[5].Y, 449f + 2.5f * 24f, "last swatch y");
    }

    private static void ColumnsReadLeftToRight()
    {
        var grid = new RailGrid(3, Tools, Swatches);
        ExpectNear(grid.Width, 144f, "width");
        ExpectNear(grid.Height, 223f, "height");
        ExpectSlot(grid.Head, 96f, 0f, "head, at the right end");
        ExpectSlot(grid.Tools[0], 0f, 49f, "first tool");
        ExpectSlot(grid.Tools[2], 96f, 49f, "third tool");
        ExpectSlot(grid.Tools[3], 0f, 93f, "fourth tool, starting the second row");
        ExpectSlot(grid.Actions[0], 0f, 145f, "undo, starting its own row");
        ExpectSlot(grid.Actions[2], 96f, 145f, "clear");
        ExpectNear(grid.SwatchCenters[0].X, 12f, "first swatch x");
        ExpectNear(grid.SwatchCenters[5].X, 132f, "last swatch x, in one row");
        ExpectNear(grid.SwatchCenters[5].Y, 205f, "swatch row y");

        var two = new RailGrid(2, Tools, Swatches);
        ExpectNear(two.Height, 331f, "two columns' height");
        ExpectNear(two.SwatchCenters[2].X, 80f, "two columns spread three swatches to a row");
    }

    private static void OutsideAt2560x1440() => ExpectOutside(2560, 1440, tagRoom: 520f);

    private static void OutsideAt1920x1080() => ExpectOutside(1920, 1080, tagRoom: 520f);

    private static void OutsideAt3440x1440() => ExpectOutside(3440, 1440, tagRoom: 960f);

    /// <summary>The screen's left edge is nearer, so a long hover tag goes right over the map instead.</summary>
    private static void OutsideAt1280x1024() => ExpectOutside(1280, 1024, tagRoom: 140f);

    /// <summary>The map's sides touch the screen's, leaving no room left of it, while the band above it keeps its width.</summary>
    private static void SquareScreenKeepsRailInside()
    {
        var (rail, strip) = Place(1080, 1080);
        ExpectRailInside(rail);
        Expect(strip.Outside, "expected the strip outside");
        ExpectNear(strip.X, 88f, "strip x");
    }

    /// <summary>A map 1080 units tall fills the screen's height: nothing fits above it, but a single column fits beside it.</summary>
    private static void TallMapKeepsStripInside()
    {
        var (rail, strip) = Place(1920, 1080, mapUnits: 1080f);
        Expect(rail.Outside && rail.Columns == 1, $"expected one rail column outside, got {Describe(rail)}");
        ExpectNear(rail.X, -8f - 48f, "rail x");
        Expect(!strip.Outside, "expected the strip inside");
        ExpectNear(strip.X, MenuLayout.RailX, "strip x, in the corner the rail left");
        ExpectNear(strip.Y, MenuLayout.RailY, "strip y");
    }

    private static void HudInTheWayKeepsRailInside()
    {
        var hud = new List<PixelBox>(MenuLayout.GameHud(2560, 1440, 4f / 3f)) { new(0f, 120f, 680f, 400f) };
        var (rail, strip) = MenuLayout.Place(2560, 1440, Map(2560, 1440, 900f), 1f, hud, Tools, Swatches);
        ExpectRailInside(rail);
        Expect(strip.Outside, "expected the strip outside");
    }

    /// <summary>Something in the stretch left of the attitude ball moves the strip right of it; something there too moves it inside.</summary>
    private static void StripTakesFirstWideStretch()
    {
        var hud = new List<PixelBox>(MenuLayout.GameHud(2560, 1440, 4f / 3f)) { new(1100f, 60f, 40f, 40f) };
        var (rail, strip) = MenuLayout.Place(2560, 1440, Map(2560, 1440, 900f), 1f, hud, Tools, Swatches);
        Expect(rail.Outside, "expected the rail outside");
        Expect(strip.Outside, "expected the strip outside");
        ExpectNear(strip.X, 1321.33f - 680f, "strip x, right of the attitude ball");

        hud.Add(new PixelBox(1500f, 60f, 40f, 40f));
        (rail, strip) = MenuLayout.Place(2560, 1440, Map(2560, 1440, 900f), 1f, hud, Tools, Swatches);
        Expect(rail.Outside, "expected the rail outside");
        Expect(!strip.Outside, "expected the strip inside");
        ExpectNear(strip.X, MenuLayout.RailX, "strip x, in the corner the rail left");

        hud.Add(new PixelBox(0f, 120f, 680f, 400f));
        (_, strip) = MenuLayout.Place(2560, 1440, Map(2560, 1440, 900f), 1f, hud, Tools, Swatches);
        ExpectNear(strip.X, MenuLayout.RailX + RailGrid.RailWidth, "strip x, beside the rail's head");
        ExpectNear(strip.MaxWidth, MenuLayout.StripMaxWidth, "strip width inside");
    }

    /// <summary>
    /// The rail: three columns, 8 px left of the map, its top level with the map's, clear of the MFD buttons below. The
    /// strip: 8 px above the map, from 8 px right of the speed readout to 8 px short of the attitude ball, free to grow up
    /// to 8 px below the screen's top.
    /// </summary>
    private static void ExpectOutside(int width, int height, float tagRoom)
    {
        var (rail, strip) = Place(width, height);
        Expect(rail.Outside && rail.Columns == 3, $"expected three rail columns outside, got {Describe(rail)}");
        ExpectNear(rail.X, -8f - 144f, "rail x");
        ExpectNear(rail.Y, 0f, "rail y");
        ExpectNear(rail.TagRoom, tagRoom, "room left of the rail");
        Expect(strip.Outside, "expected the strip outside");
        ExpectNear(strip.X, 88f, "strip x");
        ExpectNear(strip.Y, -8f, "strip bottom");
        ExpectNear(strip.MaxWidth, 470.67f, "strip width");
        ExpectNear(strip.MaxHeight, 104f, "strip height");
    }

    private static (RailPlacement Rail, StripPlacement Strip) Place(int width, int height, float mapUnits = 900f)
    {
        var scale = height / 1080f;
        return MenuLayout.Place(width, height, Map(width, height, mapUnits), scale * 0.75f, MenuLayout.GameHud(width, height, scale), Tools,
            Swatches);
    }

    private static PixelBox Map(int width, int height, float units)
    {
        var side = units * height / 1080f;
        return PixelBox.Around(width * 0.5f, height * 0.5f, side, side);
    }

    private static void ExpectRailInside(RailPlacement rail)
    {
        Expect(!rail.Outside && rail.Columns == 1, $"expected the rail inside, got {Describe(rail)}");
        ExpectNear(rail.X, MenuLayout.RailX, "rail x");
        ExpectNear(rail.Y, MenuLayout.RailY, "rail y");
    }

    private static void ExpectSlot(RailSlot slot, float x, float y, string what)
    {
        ExpectNear(slot.X, x, what + " x");
        ExpectNear(slot.Y, y, what + " y");
    }

    private static void ExpectNear(float actual, float expected, string what) =>
        Expect(MathF.Abs(actual - expected) < 0.01f, $"expected {what} {expected}, got {actual}");

    private static string Describe(RailPlacement rail) => $"{(rail.Outside ? "outside" : "inside")} at ({rail.X}, {rail.Y}) in {rail.Columns}";
}
