using System.Numerics;
using BaanishUiImprovements.MapTools;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// Where map labels go: each kind's candidate slots, the priority order, and keeping a slot while it stays clear.
/// Positions are icon units, X right and Y up. Most labels are 40 by 16, and the gap to what they name is 6.
/// </summary>
internal static class LabelLayoutTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a bearing label sits past its arrowhead when that's clear", BearingPastHeadWhenClear),
        ("a blocked bearing label sits beside the head, away from the shaft", BearingBesideHeadAwayFromShaft),
        ("a bearing label slides back along its shaft next", BearingSlidesBackAlongShaft),
        ("last, a bearing label takes a free slot on a leader", BearingFallsBackToLeader),
        ("with no clear slot a label keeps its first", NoClearSlotKeepsFirst),
        ("a radius tries 12, 2, then 10 o'clock on its ring", RadiusTriesClockPositions),
        ("a waypoint number tries upper right, then upper left", WaypointTriesCorners),
        ("notes come first, then newer bearings, then radii", PriorityOrder),
        ("a label keeps its slot while it stays clear", LabelKeepsClearSlot),
        ("without obstacles labels keep their slots", NoObstaclesKeepsSlots),
        ("a label drawn twice takes the first one's slot", MirrorTakesOriginalSlot),
    };

    private static readonly Vector2 Half = new(20, 8);

    private static void BearingPastHeadWhenClear()
    {
        var label = Place(Bearing(new Vector2(0, 0), new Vector2(100, 0)));
        Expect(label.Slot == 0 && label.Center == new Vector2(126, 0) && !label.Leader, $"expected the label 6 past the head, got {label.Center}");
    }

    /// <summary>The shaft rises to the right from the head, as in the design's third arrow, so the label goes left.</summary>
    private static void BearingBesideHeadAwayFromShaft()
    {
        var blockPast = Box(-10, -40, 10, -10);
        var label = Place(Bearing(new Vector2(16, 106), Vector2.Zero), blockPast);
        Expect(label.Slot == 1 && label.Center.X < -20 && MathF.Abs(label.Center.Y) < 6, $"expected the label left of the head, got slot {label.Slot} at {label.Center}");
    }

    private static void BearingSlidesBackAlongShaft()
    {
        var aroundHead = Box(170, -30, 260, 30);
        var label = Place(Bearing(Vector2.Zero, new Vector2(200, 0)), aroundHead);
        Expect(label.Center == new Vector2(140, 14) && !label.Leader, $"expected the label above the shaft 30% back, got {label.Center}");
    }

    private static void BearingFallsBackToLeader()
    {
        var band = Box(-100, -30, 200, 30);
        var head = new Vector2(30, 0);
        var label = Place(Bearing(Vector2.Zero, head), band);
        var nearest = Vector2.Distance(head, label.Box.Nearest(head));
        Expect(label.Leader && !label.Box.Overlaps(band), $"expected a leader to a clear slot, got slot {label.Slot} at {label.Center}");
        Expect(nearest <= LabelLayout.LeaderReach + 0.01f, $"expected the slot within the leader's reach, got {nearest}");
        Expect(label.LeaderTo == label.Box.Nearest(head) && Vector2.Distance(label.LeaderFrom, head) <= LabelLayout.LeaderStart + 0.01f,
            "expected the leader from beside the head to the plate");
    }

    private static void NoClearSlotKeepsFirst()
    {
        var label = Place(Bearing(Vector2.Zero, new Vector2(100, 0)), Box(-500, -500, 500, 500));
        Expect(label.Slot == 0 && label.Center == new Vector2(126, 0), $"expected the first slot, got {label.Slot}");
    }

    private static void RadiusTriesClockPositions()
    {
        var ring = LabelAnchor.Ring(Vector2.Zero, 100);
        var noon = Box(-5, 95, 5, 105);
        var two = Place(Label(ring), noon);
        Expect(two.Slot == 1 && Near(two.Center, new Vector2(86.6f, 50)) && two.Origin == two.Center, $"expected 2 o'clock, got {two.Center}");
        var ten = Place(Label(ring), noon, Box(80, 45, 90, 55));
        Expect(ten.Slot == 2 && Near(ten.Center, new Vector2(-86.6f, 50)), $"expected 10 o'clock, got {ten.Center}");
    }

    private static void WaypointTriesCorners()
    {
        var marker = LabelAnchor.Waypoint(Vector2.Zero);
        var free = Place(Label(marker));
        Expect(free.Center == new Vector2(27.5f, 15.5f), $"expected upper right, got {free.Center}");
        var blocked = Place(Label(marker), Box(10, 10, 50, 30));
        Expect(blocked.Center == new Vector2(-27.5f, 15.5f), $"expected upper left, got {blocked.Center}");
    }

    private static void PriorityOrder()
    {
        var arrow = LabelAnchor.Bearing(Vector2.Zero, new Vector2(100, 0));
        var older = Label(arrow, order: 1, text: "older");
        var newer = Label(arrow, order: 2, text: "newer");
        var radius = Label(LabelAnchor.Ring(new Vector2(126, -8), 40), order: 3);
        var note = Label(LabelAnchor.Note(new Vector2(126, 30)), order: 4);
        new LabelLayout().Place(new List<PlacedLabel> { older, newer, radius, note }, new List<LabelBox>());
        Expect(newer.Slot == 0, $"expected the newer bearing past the head, got slot {newer.Slot}");
        Expect(older.Slot != 0 && !older.Box.Overlaps(newer.Box), $"expected the older bearing to step aside, got slot {older.Slot}");
        Expect(!radius.Box.Overlaps(newer.Box) && !radius.Box.Overlaps(older.Box), "expected the radius to give way to both bearings");
        Expect(note.Center == new Vector2(126, 30) && !older.Box.Overlaps(note.Box), "expected the note to stay put and the others to avoid it");
    }

    private static void LabelKeepsClearSlot()
    {
        var label = Label(Bearing(Vector2.Zero, new Vector2(100, 0)).Anchor);
        label.Slot = 1;
        var layout = new LabelLayout();
        layout.Place(new List<PlacedLabel> { label }, new List<LabelBox>());
        Expect(label.Slot == 1, $"expected the label to stay beside the head, got slot {label.Slot}");
        var blocked = label.Box;
        layout.Place(new List<PlacedLabel> { label }, new List<LabelBox> { blocked });
        Expect(label.Slot != 1 && !label.Box.Overlaps(blocked), $"expected the label to move once blocked, got slot {label.Slot}");
    }

    private static void NoObstaclesKeepsSlots()
    {
        var arrow = LabelAnchor.Bearing(Vector2.Zero, new Vector2(100, 0));
        var first = Label(arrow, order: 1, text: "a");
        var second = Label(arrow, order: 2, text: "b");
        second.Slot = 2;
        new LabelLayout().Place(new List<PlacedLabel> { first, second }, null);
        Expect(first.Slot == 0 && second.Slot == 2, $"expected slots 0 and 2 kept, got {first.Slot} and {second.Slot}");
    }

    /// <summary>The eraser redraws a hovered drawing as its highlight: an overlay label with the same anchor and text.</summary>
    private static void MirrorTakesOriginalSlot()
    {
        var arrow = LabelAnchor.Bearing(Vector2.Zero, new Vector2(100, 0));
        var stored = Label(arrow, order: 5);
        var highlight = Label(arrow, overlay: true);
        new LabelLayout().Place(new List<PlacedLabel> { stored, highlight }, new List<LabelBox>());
        Expect(stored.Slot == 0 && highlight.Center == stored.Center, $"expected both past the head, got {stored.Slot} and {highlight.Center}");
    }

    private static PlacedLabel Bearing(Vector2 from, Vector2 head) => Label(LabelAnchor.Bearing(from, head));

    private static PlacedLabel Label(LabelAnchor anchor, int order = 0, bool overlay = false, string text = "x") =>
        new() { Anchor = anchor, HalfSize = Half, Order = order, Overlay = overlay, Text = text };

    private static PlacedLabel Place(PlacedLabel label, params LabelBox[] obstacles)
    {
        new LabelLayout().Place(new List<PlacedLabel> { label }, obstacles);
        return label;
    }

    private static LabelBox Box(float x0, float y0, float x1, float y1) => new(new Vector2(x0, y0), new Vector2(x1, y1));

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.1f;
}
