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
        ("a strip wide enough keeps the hint and buttons on one row", WideStripKeepsOneRow),
        ("a narrow strip moves the buttons to a second row rather than wrap a short hint", NarrowStripMovesButtonsDown),
        ("only a hint too long for its row wraps", LongHintWraps),
        ("a hover tag sits beside its cell, centred on it, on every screen", HoverTagSitsBesideItsCell),
    };

    /// <summary>A hover tag's height, and the widths of a short tag ("TOOLS H") and the longest ("BEARING/RANGE" and its key).</summary>
    private const float TagHeight = 28f;

    private static readonly float[] TagWidths = { 100f, 190f };

    /// <summary>The waypoint tool's start: its name, then a 231-pixel hint, then SKIP and RESTART with their gaps.</summary>
    private const float WaypointLead = 101f;

    private const float StartHint = 231f;
    private const float RouteButtons = 142f;

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

    /// <summary>
    /// Something in the stretch left of the mission clock shrinks the strip, or where it leaves too little room, moves it
    /// right of the clock; something there too moves it inside.
    /// </summary>
    private static void StripTakesFirstWideStretch()
    {
        var hud = new List<PixelBox>(MenuLayout.GameHud(2560, 1440, 4f / 3f)) { new(1150f, 60f, 40f, 40f) };
        var (rail, strip) = MenuLayout.Place(2560, 1440, Map(2560, 1440, 900f), 1f, hud, Tools, Swatches);
        Expect(strip.Outside, "expected the strip outside");
        ExpectNear(strip.X, 88f, "strip x, still right of the speed readout");
        ExpectNear(strip.MaxWidth, 1142f - 768f, "strip width, up to 8 px short of the box");

        hud[hud.Count - 1] = new PixelBox(1100f, 60f, 40f, 40f);
        (rail, strip) = MenuLayout.Place(2560, 1440, Map(2560, 1440, 900f), 1f, hud, Tools, Swatches);
        Expect(rail.Outside, "expected the rail outside");
        Expect(strip.Outside, "expected the strip outside");
        ExpectNear(strip.X, 1361.33f - 680f, "strip x, right of the mission clock");
        ExpectNear(strip.MaxWidth, 430.67f, "strip width, up to the altitude readout");

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

    private static void WideStripKeepsOneRow() =>
        ExpectFit(MenuLayout.FitStrip(WaypointLead, StartHint, RouteButtons, MenuLayout.StripMaxWidth), secondRow: false, StartHint, wraps: false);

    private static void NarrowStripMovesButtonsDown() =>
        ExpectFit(MenuLayout.FitStrip(WaypointLead, StartHint, RouteButtons, 430f), secondRow: true, StartHint, wraps: false);

    /// <summary>With buttons, the hint wraps above them on its own row; without, it wraps on the one row.</summary>
    private static void LongHintWraps()
    {
        ExpectFit(MenuLayout.FitStrip(WaypointLead, 400f, RouteButtons, 430f), secondRow: true, 430f - WaypointLead - 14f, wraps: true);
        ExpectFit(MenuLayout.FitStrip(WaypointLead, 400f, 0f, 430f), secondRow: false, 430f - WaypointLead - 14f, wraps: true);
        ExpectFit(MenuLayout.FitStrip(WaypointLead, 400f, RouteButtons, 200f), secondRow: true, MenuLayout.MinHintWidth, wraps: true);
    }

    /// <summary>
    /// The Tools head (all a closed rail shows), every tool, and Undo, Redo, and Clear, in each column, outside the map at
    /// the four screens above and inside it on a square one. A tag goes left, on screen, when there's room left of its
    /// cell, and there it covers no cell of the head's or first column's; else right.
    /// </summary>
    private static void HoverTagSitsBesideItsCell()
    {
        foreach (var (width, height) in new[] { (2560, 1440), (1920, 1080), (3440, 1440), (1280, 1024), (1080, 1080) })
        {
            var (rail, _) = Place(width, height);
            var grid = new RailGrid(rail.Columns, Tools, Swatches);
            var cells = new List<RailSlot> { grid.Head };
            cells.AddRange(grid.Tools);
            cells.AddRange(grid.Actions);
            foreach (var tagWidth in TagWidths)
            {
                foreach (var cell in cells)
                {
                    var (x, y) = MenuLayout.HoverTag(rail, cell, tagWidth, TagHeight);
                    var what = $"at {width}x{height}, a {tagWidth}-wide tag for the cell at ({cell.X}, {cell.Y})";
                    ExpectNear(y + TagHeight * 0.5f, cell.Y + cell.Height * 0.5f, what + ": middle");
                    var left = rail.Outside && tagWidth + MenuLayout.TagGap <= rail.TagRoom + cell.X;
                    ExpectNear(left ? cell.X - (x + tagWidth) : x - (cell.X + cell.Width), MenuLayout.TagGap, what + ": gap");
                    Expect(!left || x >= -rail.TagRoom, $"{what}: expected it on screen, got x {x}");
                    if (left && (cell.X == 0f || cell.Equals(grid.Head)))
                    {
                        var tag = new PixelBox(x, y, tagWidth, TagHeight);
                        Expect(!cells.Any(other => tag.Overlaps(new PixelBox(other.X, other.Y, other.Width, other.Height))), $"{what}: expected it clear of the rail");
                    }
                }
            }
        }

        // The closed Tools button at the right end of three columns, with its tag just left of it rather than of the first column.
        var (outside, _) = Place(2560, 1440);
        var (headX, _) = MenuLayout.HoverTag(outside, new RailGrid(outside.Columns, Tools, Swatches).Head, 100f, TagHeight);
        ExpectNear(headX + 100f, 96f - MenuLayout.TagGap, "the Tools tag's right edge");
    }

    private static void ExpectFit(StripFit fit, bool secondRow, float hintWidth, bool wraps)
    {
        Expect(fit.SecondRow == secondRow, $"expected the buttons {(secondRow ? "on a second row" : "beside the hint")}");
        Expect(fit.Wraps == wraps, $"expected the hint {(wraps ? "wrapped" : "on one line")}");
        ExpectNear(fit.HintWidth, hintWidth, "hint width");
    }

    /// <summary>
    /// The rail: three columns, 8 px left of the map, its top level with the map's, clear of the MFD buttons below. The
    /// strip: 8 px above the map, from 8 px right of the speed readout to 8 px short of the mission clock, free to grow up
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
        ExpectNear(strip.MaxWidth, 430.67f, "strip width");
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
