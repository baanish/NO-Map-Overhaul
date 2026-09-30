using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoMapOverhaul.MapTools;

/// <summary>A box on screen in pixels, Y down from the screen's top-left corner.</summary>
internal readonly struct PixelBox
{
    public PixelBox(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public float Right => X + Width;

    public float Bottom => Y + Height;

    public static PixelBox Around(float centerX, float centerY, float width, float height) =>
        new(centerX - width * 0.5f, centerY - height * 0.5f, width, height);

    public PixelBox Grown(float by) => new(X - by, Y - by, Width + 2f * by, Height + 2f * by);

    /// <summary>Touching edges don't count, so boxes a gap apart may meet at the gap's edge.</summary>
    public bool Overlaps(PixelBox other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public override string ToString() => $"({X:0.#}, {Y:0.#}) {Width:0.#} x {Height:0.#}";
}

/// <summary>A rail cell's box in design pixels from the rail's top-left corner.</summary>
internal readonly struct RailSlot
{
    public RailSlot(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }
}

/// <summary>
/// The rail's cells in design pixels from its top-left corner, in one or more columns. One column is design C's rail. More
/// keep the same cells and gaps and read left to right in rows: the Tools head alone in the first row, at its right end so
/// the closed button sits nearest the map, then the tools, then Undo, Redo, and Clear, then the swatches spread evenly
/// across the width, each group starting a new row.
/// </summary>
internal sealed class RailGrid
{
    public const float RailWidth = 48f;
    public const float HeadHeight = 48f;
    public const float ToolHeight = 44f;
    public const float ActionHeight = 40f;
    public const float GroupGap = 8f;
    public const float SwatchCell = 24f;

    /// <summary>Undo, Redo, and Clear.</summary>
    public const int ActionCount = 3;

    public RailGrid(int columns, int tools, int swatches)
    {
        Columns = columns;
        Width = columns * RailWidth;
        Head = new RailSlot((columns - 1) * RailWidth, 0f, RailWidth, HeadHeight);

        const float toolTop = HeadHeight + 1f;
        var toolSlots = new RailSlot[tools];
        for (var i = 0; i < tools; i++)
        {
            toolSlots[i] = new RailSlot(i % columns * RailWidth, toolTop + i / columns * ToolHeight, RailWidth, ToolHeight);
        }

        Tools = toolSlots;
        ActionTop = toolTop + Rows(tools, columns) * ToolHeight + GroupGap;
        var actionSlots = new RailSlot[ActionCount];
        for (var i = 0; i < ActionCount; i++)
        {
            actionSlots[i] = new RailSlot(i % columns * RailWidth, ActionTop + i / columns * ActionHeight, RailWidth, ActionHeight);
        }

        Actions = actionSlots;
        SwatchTop = ActionTop + Rows(ActionCount, columns) * ActionHeight + GroupGap;
        var swatchRows = Rows(swatches, 2 * columns);
        var perRow = Rows(swatches, swatchRows);
        var slot = Width / perRow;
        var centers = new Vector2[swatches];
        for (var i = 0; i < swatches; i++)
        {
            centers[i] = new Vector2(slot * (0.5f + i % perRow), SwatchTop + SwatchCell * (0.5f + i / perRow));
        }

        SwatchCenters = centers;
        Height = SwatchTop + swatchRows * SwatchCell + 6f;
    }

    public int Columns { get; }

    public float Width { get; }

    public float Height { get; }

    public RailSlot Head { get; }

    public IReadOnlyList<RailSlot> Tools { get; }

    /// <summary>Undo, Redo, and Clear, in that order.</summary>
    public IReadOnlyList<RailSlot> Actions { get; }

    public IReadOnlyList<Vector2> SwatchCenters { get; }

    public float ActionTop { get; }

    public float SwatchTop { get; }

    /// <summary>How many rows <paramref name="count"/> items take, <paramref name="perRow"/> to a row.</summary>
    private static int Rows(int count, int perRow) => (count + perRow - 1) / perRow;
}

/// <summary>
/// Where the rail goes: outside the map, left of it with its top level with the map's top edge, or inside the map's
/// top-left corner as design C draws it. <see cref="X"/> and <see cref="Y"/> are its top-left corner in design pixels from
/// the map's top-left corner, so negative outside.
/// </summary>
internal readonly struct RailPlacement
{
    public RailPlacement(bool outside, float x, float y, int columns, float tagRoom)
    {
        Outside = outside;
        X = x;
        Y = y;
        Columns = columns;
        TagRoom = tagRoom;
    }

    public bool Outside { get; }

    public float X { get; }

    public float Y { get; }

    public int Columns { get; }

    /// <summary>Design pixels left of the rail before the screen's edge, for a hover tag. Zero inside the map, where tags go right.</summary>
    public float TagRoom { get; }
}

/// <summary>
/// Where the strip goes: outside the map, above its top edge, or inside it beside the rail's head (or, with the rail
/// outside, in the corner the head leaves). Outside, <see cref="Y"/> is the strip's bottom edge and it grows up for a
/// wrapped hint; inside, <see cref="Y"/> is its top edge and it grows down. Design pixels from the map's top-left corner.
/// </summary>
internal readonly struct StripPlacement
{
    public StripPlacement(bool outside, float x, float y, float maxWidth, float maxHeight)
    {
        Outside = outside;
        X = x;
        Y = y;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
    }

    public bool Outside { get; }

    public float X { get; }

    public float Y { get; }

    public float MaxWidth { get; }

    public float MaxHeight { get; }
}

/// <summary>
/// How the strip's contents share its width: whether the option buttons, their unit, and the count take a second row
/// under the hint, and how wide the hint is, wrapped or on one line.
/// </summary>
internal readonly struct StripFit
{
    public StripFit(bool secondRow, float hintWidth, bool wraps)
    {
        SecondRow = secondRow;
        HintWidth = hintWidth;
        Wraps = wraps;
    }

    public bool SecondRow { get; }

    public float HintWidth { get; }

    public bool Wraps { get; }
}

/// <summary>
/// Places the map tools menu on the full map. The rail and the strip each go outside the map when there's room there, so
/// the map stays clear, and back inside its top-left corner when there isn't: on a screen too narrow for the rail beside
/// the map, with the map too near the screen's top for the strip, or with the game's HUD in the way. Works in screen
/// pixels, Y down, and hands back design pixels from the map's top-left corner, the units <see cref="MapToolMenu"/> draws in.
/// </summary>
internal static class MenuLayout
{
    /// <summary>The rail's corner inside the map, from the map's top-left corner: just clear of the grid's row letters and column numbers.</summary>
    public const float RailX = 32f;

    public const float RailY = 38f;

    /// <summary>
    /// Outside the map, the room kept between the menu and the map's edge (the grid's letters and numbers sit just inside
    /// it), the game's HUD, and the screen's edge.
    /// </summary>
    public const float OutsideGap = 8f;

    public const float StripMaxWidth = 640f;

    /// <summary>The strip's padding at either end, and the gap between its name, divider, hint, and buttons.</summary>
    public const float StripPad = 14f;

    public const float StripGap = 12f;

    /// <summary>The narrowest a wrapped hint gets, even if that makes the strip wider than its placement.</summary>
    public const float MinHintWidth = 160f;

    /// <summary>
    /// The narrowest strip outside the map: the widest tool's name beside a hint wrapped to its narrowest, with the
    /// buttons on a second row. It fits the 430 pixels between the speed readout and the mission clock.
    /// </summary>
    public const float MinOutsideStripWidth = 360f;

    /// <summary>Inside the map a hint wraps onto two lines at most, since the strip may be as wide as <see cref="StripMaxWidth"/>.</summary>
    public const float InsideStripMaxHeight = 2f * RailGrid.HeadHeight;

    /// <summary>Between a hover tag and the cell it names.</summary>
    public const float TagGap = 6f;

    /// <summary>
    /// The game's HUD around the full map: the speed, altitude, and attitude readouts along the top, the mission clock the
    /// game shows there instead while the player has no aircraft, and the columns of MFD buttons down both sides, from the
    /// game's VirtualMFD in its level1 scene (tools/dump_map_ui.py prints them). Centres and sizes in the canvas units of a
    /// 1920x1080 screen, from the screen's centre, Y up. Its canvas scales with the screen's height, as the map's does, so
    /// these keep their place beside the map at any resolution.
    /// </summary>
    private static readonly (float X, float Y, float Width, float Height)[] HudBoxes =
    {
        (-450f, 490f, 120f, 35f),
        (450f, 490f, 120f, 35f),
        (0f, 490f, 50f, 50f),
        (0f, 490f, 110f, 32f), // clockPanel fits its text, "00:00:12" 91 wide plus 5 either side, with room for wider digits
        (-480f, 0f, 50f, 550f),
        (480f, 0f, 50f, 550f),
    };

    /// <summary>The game's HUD boxes on a screen of this size, with its canvases at <paramref name="canvasScale"/> pixels per unit.</summary>
    public static PixelBox[] GameHud(float screenWidth, float screenHeight, float canvasScale)
    {
        var boxes = new PixelBox[HudBoxes.Length];
        for (var i = 0; i < HudBoxes.Length; i++)
        {
            var (x, y, width, height) = HudBoxes[i];
            boxes[i] = PixelBox.Around(screenWidth * 0.5f + x * canvasScale, screenHeight * 0.5f - y * canvasScale, width * canvasScale,
                height * canvasScale);
        }

        return boxes;
    }

    /// <summary>
    /// Places the rail, then the strip, which keeps clear of the rail too. <paramref name="map"/> is the full map's rect
    /// on screen, <paramref name="pixelsPerDesign"/> the screen pixels per design pixel, and <paramref name="hud"/> what
    /// else is on screen beside the map.
    /// </summary>
    public static (RailPlacement Rail, StripPlacement Strip) Place(float screenWidth, float screenHeight, PixelBox map, float pixelsPerDesign,
        IReadOnlyList<PixelBox> hud, int tools, int swatches)
    {
        var gap = OutsideGap * pixelsPerDesign;
        var blockers = new List<PixelBox>(hud.Count + 1);
        foreach (var box in hud)
        {
            blockers.Add(box.Grown(gap));
        }

        var rail = PlaceRail(screenWidth, screenHeight, map, pixelsPerDesign, blockers, tools, swatches);
        if (rail.Outside)
        {
            var grid = new RailGrid(rail.Columns, tools, swatches);
            blockers.Add(new PixelBox(map.X + rail.X * pixelsPerDesign, map.Y + rail.Y * pixelsPerDesign, grid.Width * pixelsPerDesign,
                grid.Height * pixelsPerDesign).Grown(gap));
        }

        var strip = PlaceStrip(screenWidth, map, pixelsPerDesign, blockers) ??
            new StripPlacement(false, RailX + (rail.Outside ? 0f : RailGrid.RailWidth), RailY, StripMaxWidth, InsideStripMaxHeight);
        return (rail, strip);
    }

    /// <summary>
    /// Fits the strip's contents to <paramref name="maxWidth"/>, in design pixels. <paramref name="lead"/> is the width left
    /// of the hint, <paramref name="hint"/> the hint's on one line, and <paramref name="tail"/> what the buttons, their unit,
    /// and the count take right of it, gaps included. All on one row if it fits; else the buttons go to a second row, so a
    /// short hint keeps its line, and only a hint too long for the row by itself wraps.
    /// </summary>
    public static StripFit FitStrip(float lead, float hint, float tail, float maxWidth)
    {
        var room = maxWidth - lead - StripPad;
        if (hint + tail <= room)
        {
            return new StripFit(false, hint, false);
        }

        var secondRow = tail > 0f;
        return hint <= room ? new StripFit(secondRow, hint, false) : new StripFit(secondRow, Math.Max(room, MinHintWidth), true);
    }

    /// <summary>
    /// The top-left corner of the hover tag naming <paramref name="cell"/>, in design pixels from the rail's top-left
    /// corner: <see cref="TagGap"/> beside the cell and centred on it. It points left, off the map, while the rail stands
    /// outside it and the screen has room for the tag left of the cell, else right, over the map.
    /// </summary>
    public static (float X, float Y) HoverTag(RailPlacement rail, RailSlot cell, float width, float height)
    {
        var left = rail.Outside && width + TagGap <= rail.TagRoom + cell.X;
        return (left ? cell.X - TagGap - width : cell.X + cell.Width + TagGap, cell.Y + (cell.Height - height) * 0.5f);
    }

    /// <summary>The fewest columns that fit left of the map, from its top edge down, clear of the HUD and on screen.</summary>
    private static RailPlacement PlaceRail(float screenWidth, float screenHeight, PixelBox map, float pixelsPerDesign, List<PixelBox> blockers,
        int tools, int swatches)
    {
        var gap = OutsideGap * pixelsPerDesign;
        for (var columns = 1; columns <= Math.Max(tools, 1); columns++)
        {
            var grid = new RailGrid(columns, tools, swatches);
            var width = grid.Width * pixelsPerDesign;
            var box = new PixelBox(map.X - gap - width, map.Y, width, grid.Height * pixelsPerDesign);
            if (box.X < gap || box.Right > screenWidth || box.Y < 0f || box.Bottom > screenHeight - gap || OverlapsAny(box, blockers))
            {
                continue;
            }

            return new RailPlacement(true, -OutsideGap - grid.Width, 0f, columns, (box.X - gap) / pixelsPerDesign);
        }

        return new RailPlacement(false, RailX, RailY, 1, 0f);
    }

    /// <summary>
    /// The leftmost stretch above the map, clear of everything in <paramref name="blockers"/> at the strip's height, that
    /// is wide enough. The strip may then grow up to the lowest blocker over that stretch, or the screen's top edge.
    /// </summary>
    private static StripPlacement? PlaceStrip(float screenWidth, PixelBox map, float pixelsPerDesign, List<PixelBox> blockers)
    {
        var gap = OutsideGap * pixelsPerDesign;
        var bottom = map.Y - gap;
        var top = bottom - RailGrid.HeadHeight * pixelsPerDesign;
        if (top < gap)
        {
            return null;
        }

        var blocked = new List<(float Left, float Right)>();
        foreach (var box in blockers)
        {
            if (box.Y < bottom && top < box.Bottom)
            {
                blocked.Add((box.X, box.Right));
            }
        }

        blocked.Sort((a, b) => a.Left.CompareTo(b.Left));
        var minWidth = MinOutsideStripWidth * pixelsPerDesign;
        var end = Math.Min(map.Right, screenWidth - gap);
        var left = Math.Max(map.X, gap);
        var right = end;
        foreach (var (blockLeft, blockRight) in blocked)
        {
            if (Math.Min(blockLeft, end) - left >= minWidth)
            {
                right = Math.Min(blockLeft, end);
                break;
            }

            left = Math.Max(left, blockRight);
        }

        if (right - left < minWidth)
        {
            return null;
        }

        var ceiling = gap;
        foreach (var box in blockers)
        {
            if (box.X < right && left < box.Right && box.Bottom <= top)
            {
                ceiling = Math.Max(ceiling, box.Bottom);
            }
        }

        return new StripPlacement(true, (left - map.X) / pixelsPerDesign, -OutsideGap, Math.Min((right - left) / pixelsPerDesign, StripMaxWidth),
            (bottom - ceiling) / pixelsPerDesign);
    }

    private static bool OverlapsAny(PixelBox box, List<PixelBox> blockers)
    {
        foreach (var blocker in blockers)
        {
            if (box.Overlaps(blocker))
            {
                return true;
            }
        }

        return false;
    }
}
