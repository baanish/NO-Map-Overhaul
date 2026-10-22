using System.Numerics;
using NoMapOverhaul.MapTools;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

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
        ("a note on a unit sits beside its icon, then round it, then further out", UnitNoteSitsBesideIcon),
        ("notes come first, then newer bearings, then radii", PriorityOrder),
        ("a label keeps its slot while it stays clear", LabelKeepsClearSlot),
        ("without obstacles labels keep their slots", NoObstaclesKeepsSlots),
        ("a label drawn twice takes the first one's slot", MirrorTakesOriginalSlot),
        ("a highlight of one of two identical drawings takes its own drawing's slot", MirrorTakesItsOwnDrawingsSlot),
        ("crowded bearings each take the first slot clear of everything", CrowdedBearingsTakeFirstClearSlot),
        ("indexed placement matches brute force across randomized maps and repeated passes", RandomizedPlacementMatchesBruteForce),
        ("indexed placement preserves touching edges and extreme coordinates", CellEdgesMatchBruteForce),
        ("label placement reuses its buffers while panning", PlacementReusesBuffers),
        ("minimap labels turn upright again only after a degree of turn", UprightStepsByADegree),
        ("minimap turns count across north", TurnsCountAcrossNorth),
    };

    private static void UprightStepsByADegree()
    {
        var step = MinimapHeading.UprightStepDegrees;
        Expect(!MinimapHeading.Turned(10f, 10.9f, step), "expected no turn under a degree");
        Expect(!MinimapHeading.Turned(10f, 9.1f, step), "expected no turn under a degree the other way");
        Expect(MinimapHeading.Turned(10f, 11f, step), "expected a turn at a degree");
        Expect(MinimapHeading.Turned(10f, 8.5f, step), "expected a turn past a degree the other way");
        Expect(!MinimapHeading.Turned(10f, 14.9f, MinimapHeading.RespaceStepDegrees), "expected no relayout under five degrees");
        Expect(MinimapHeading.Turned(10f, 15f, MinimapHeading.RespaceStepDegrees), "expected a relayout at five degrees");
        Expect(!MinimapHeading.Turned(10f, 10f, 0f), "expected a step of 0 to ignore no change");
        Expect(MinimapHeading.Turned(10f, 10.001f, 0f), "expected a step of 0 to count any change");
    }

    private static void TurnsCountAcrossNorth()
    {
        Expect(!MinimapHeading.Turned(359.6f, 0.3f, 1f), "expected 0.7 degrees across north to be no step");
        Expect(MinimapHeading.Turned(359.5f, 0.6f, 1f), "expected 1.1 degrees across north to be a step");
        Expect(MinimapHeading.Turned(0.4f, 359f, 1f), "expected 1.4 degrees back across north to be a step");
        Expect(MathF.Abs(MinimapHeading.Delta(350f, 10f) - 20f) < 1e-3f, "expected the short way round");
        Expect(MathF.Abs(MinimapHeading.Delta(10f, 350f) + 20f) < 1e-3f, "expected the short way round, turning left");
    }

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

    /// <summary>The icons are the obstacles the full map collects: 15 and 30 units across, grown by 4.5.</summary>
    private static void UnitNoteSitsBesideIcon()
    {
        var unit = LabelAnchor.UnitNote(Vector2.Zero);
        var icon = Box(-12, -12, 12, 12);
        var right = Place(Label(unit), icon);
        Expect(right.Slot == 0 && right.Center == new Vector2(36, 0) && !right.Box.Overlaps(icon), $"expected right of the icon, got {right.Center}");
        var left = Place(Label(unit), icon, Box(12, -40, 100, 40));
        Expect(left.Slot == 1 && left.Center == new Vector2(-36, 0), $"expected left of the icon, got {left.Center}");
        var above = Place(Label(unit), icon, Box(-100, -40, 100, 0));
        Expect(above.Slot == 2 && above.Center == new Vector2(0, 24), $"expected above the icon, got {above.Center}");
        var big = Place(Label(unit), Box(-19.5f, -19.5f, 19.5f, 19.5f));
        Expect(big.Slot == 4 && big.Center == new Vector2(46, 0), $"expected right of a big icon, further out, got {big.Center}");
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

    /// <summary>Two measurements between the same points: the newer takes the first slot, and the eraser hovers the newer.</summary>
    private static void MirrorTakesItsOwnDrawingsSlot()
    {
        var arrow = LabelAnchor.Bearing(Vector2.Zero, new Vector2(100, 0));
        var older = Label(arrow, order: 1);
        var newer = Label(arrow, order: 2);
        var highlight = Label(arrow, order: 2, overlay: true);
        new LabelLayout().Place(new List<PlacedLabel> { older, newer, highlight }, new List<LabelBox>());
        Expect(newer.Slot == 0 && older.Slot != 0, $"expected the newer past the head, got slots {older.Slot} and {newer.Slot}");
        Expect(highlight.Center == newer.Center, $"expected the highlight on the newer's label, got {highlight.Center}");
    }

    /// <summary>
    /// A blocked bearing checks its slots against only the boxes near it. Among hundreds of obstacles, each bearing, newest
    /// first, must still take the first slot a check against every obstacle and every newer bearing finds clear.
    /// </summary>
    private static void CrowdedBearingsTakeFirstClearSlot()
    {
        var random = new Random(7);
        Vector2 Around(float spread) => new Vector2(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f) * 2f * spread;
        var obstacles = new List<LabelBox>();
        for (var i = 0; i < 300; i++)
        {
            obstacles.Add(LabelBox.Around(Around(300f), new Vector2(10)).Grown(LabelLayout.IconMargin));
        }

        var labels = new List<PlacedLabel>();
        for (var i = 0; i < 16; i++)
        {
            var head = Around(80f);
            labels.Add(Label(LabelAnchor.Bearing(head + Around(200f), head), order: i, text: i.ToString()));
        }

        new LabelLayout().Place(labels, obstacles);
        var leaders = 0;
        var newerFirst = labels.OrderByDescending(label => label.Order).ToList();
        for (var i = 0; i < newerFirst.Count; i++)
        {
            var label = newerFirst[i];
            var blockers = obstacles.Concat(newerFirst.Take(i).Select(newer => newer.Box)).ToList();
            var expected = Enumerable.Range(0, LabelLayout.SlotCount(LabelKind.Bearing)).FirstOrDefault(slot => IsClear(label, slot, blockers));
            Expect(label.Slot == expected, $"expected bearing {label.Text} in slot {expected}, got {label.Slot}");
            leaders += label.Leader ? 1 : 0;
        }

        Expect(leaders > 0, "expected the crowd to push some bearings onto leaders");
    }

    /// <summary>The layout's rules for a slot, checked against every box.</summary>
    private static bool IsClear(PlacedLabel label, int slot, List<LabelBox> blockers)
    {
        LabelLayout.Candidate(label.Anchor, label.HalfSize, slot, out _, out var center);
        var box = LabelBox.Around(center, label.HalfSize);
        var head = label.Anchor.Point;
        return !box.Crosses(label.Anchor.From, head) &&
               (!LabelLayout.IsLeaderSlot(LabelKind.Bearing, slot) || Vector2.Distance(box.Nearest(head), head) <= LabelLayout.LeaderReach) &&
               !blockers.Any(box.Overlaps);
    }

    private static PlacedLabel Bearing(Vector2 from, Vector2 head) => Label(LabelAnchor.Bearing(from, head));

    private static void RandomizedPlacementMatchesBruteForce()
    {
        var random = new Random(26022);
        var layout = new LabelLayout();
        var reference = new BruteForceLayout();
        Vector2 Around(float spread) => new(random.NextSingle() * spread - spread / 2, random.NextSingle() * spread - spread / 2);
        float Coordinate() => random.Next(6) switch
        {
            0 => random.Next(-16, 17) * 64f,
            1 => MathF.BitIncrement(random.Next(-16, 17) * 64f),
            2 => MathF.BitDecrement(random.Next(-16, 17) * 64f),
            3 => BitConverter.Int32BitsToSingle(random.Next(0x7f800000)) * (random.Next(2) == 0 ? -1 : 1),
            _ => random.NextSingle() * 2000 - 1000,
        };

        for (var scene = 0; scene < 300; scene++)
        {
            var labels = new List<PlacedLabel>();
            var originals = new List<PlacedLabel>();
            var obstacles = new List<LabelBox>();
            var obstacleCount = random.Next(90, 180);
            for (var i = 0; i < obstacleCount; i++)
            {
                var at = i % 3 == 0 ? new Vector2(Coordinate(), Coordinate()) : Around(800);
                var half = i % 11 == 0 ? new Vector2(4000, 1200) : new Vector2(random.Next(0, 45), random.Next(0, 25));
                if (i % 11 == 0 && scene % 9 != 0)
                {
                    at += new Vector2(5000);
                }

                obstacles.Add(LabelBox.Around(at, half));
            }

            var count = random.Next(80, 200);
            for (var i = 0; i < count; i++)
            {
                var at = i % 7 == 0 ? new Vector2(Coordinate(), Coordinate()) : Around(scene % 2 == 0 ? 250 : 2500);
                if (i % 17 == 0 && scene % 11 != 0)
                {
                    at += new Vector2(10000, -10000);
                }

                var anchor = (i % 5) switch
                {
                    0 => LabelAnchor.Note(at),
                    1 => LabelAnchor.UnitNote(at),
                    2 => LabelAnchor.Waypoint(at),
                    3 => LabelAnchor.Bearing(at + Around(500), at),
                    _ => LabelAnchor.Ring(at, random.NextSingle() * 400),
                };
                var label = Label(anchor, order: i / 2, text: (i / 2).ToString());
                label.HalfSize = i % 17 == 0 ? new Vector2(1500, 800) : new Vector2(random.Next(0, 55), random.Next(0, 20));
                label.Slot = random.Next(-3, LabelLayout.SlotCount(anchor.Kind) + 3);
                if (i > 0 && i % 13 == 0)
                {
                    var original = labels[i - 1];
                    label.Anchor = original.Anchor;
                    label.Order = i % 2 == 0 ? original.Order : 0;
                    label.Text = original.Text;
                    label.Overlay = true;
                }

                labels.Add(label);
                originals.Add(new PlacedLabel
                {
                    Anchor = label.Anchor, HalfSize = label.HalfSize, Order = label.Order,
                    Text = label.Text, Overlay = label.Overlay, Slot = label.Slot,
                });
            }

            for (var pass = 0; pass < 6; pass++)
            {
                if (pass == 2 || pass == 5)
                {
                    var shift = Around(3000);
                    for (var i = 0; i < labels.Count; i++)
                    {
                        var anchor = labels[i].Anchor;
                        var moved = anchor.Kind switch
                        {
                            LabelKind.Note => LabelAnchor.Note(anchor.Point + shift),
                            LabelKind.UnitNote => LabelAnchor.UnitNote(anchor.Point + shift),
                            LabelKind.Waypoint => LabelAnchor.Waypoint(anchor.Point + shift),
                            LabelKind.Bearing => LabelAnchor.Bearing(anchor.From + shift, anchor.Point + shift),
                            _ => LabelAnchor.Ring(anchor.Point + shift, anchor.Radius),
                        };
                        labels[i].Anchor = originals[i].Anchor = moved;
                    }

                    obstacles.Reverse();
                    obstacles.RemoveRange(0, obstacles.Count / 3);
                }

                IReadOnlyList<LabelBox>? blockers = pass switch { 3 => Array.Empty<LabelBox>(), 4 => null, _ => obstacles };
                layout.Place(labels, blockers);
                reference.Place(originals, blockers);
                for (var i = 0; i < labels.Count; i++)
                {
                    Expect(labels[i].Slot == originals[i].Slot,
                        $"scene {scene}, pass {pass}, label {i}: expected brute-force slot {originals[i].Slot}, got {labels[i].Slot}");
                }
            }
        }
    }

    private static void CellEdgesMatchBruteForce()
    {
        var layout = new LabelLayout();
        var reference = new BruteForceLayout();
        foreach (var coordinate in new[] { -float.MaxValue, -1e30f, -137438953472f, -128f, -64f, -float.Epsilon, 0f, 64f, 128f, 137438953472f, 1e30f, float.MaxValue })
        {
            foreach (var kind in Enum.GetValues<LabelKind>())
            {
                var at = new Vector2(coordinate, coordinate);
                var anchor = kind switch
                {
                    LabelKind.Note => LabelAnchor.Note(at),
                    LabelKind.UnitNote => LabelAnchor.UnitNote(at),
                    LabelKind.Waypoint => LabelAnchor.Waypoint(at),
                    LabelKind.Bearing => LabelAnchor.Bearing(at - new Vector2(200, 0), at),
                    _ => LabelAnchor.Ring(at, 100),
                };
                var label = Label(anchor);
                var original = Label(anchor);
                LabelLayout.Candidate(anchor, Half, 0, out _, out var center);
                var box = LabelBox.Around(center, Half);
                var blockers = new[]
                {
                    new LabelBox(new Vector2(box.Max.X, box.Min.Y), box.Max + new Vector2(64, 0)),
                    new LabelBox(new Vector2(MathF.BitDecrement(box.Max.X), box.Min.Y), box.Max + new Vector2(64, 0)),
                    new LabelBox(new Vector2(box.Min.X - 64, box.Min.Y), new Vector2(box.Min.X, box.Max.Y)),
                    new LabelBox(center, center),
                    new LabelBox(new Vector2(-float.MaxValue), new Vector2(float.MaxValue)),
                };
                foreach (var blocker in blockers)
                {
                    label.Slot = original.Slot = -1;
                    layout.Place(new List<PlacedLabel> { label }, new[] { blocker });
                    reference.Place(new List<PlacedLabel> { original }, new[] { blocker });
                    Expect(label.Slot == original.Slot,
                        $"{kind} at {coordinate}, blocker {blocker.Min}..{blocker.Max}: expected slot {original.Slot}, got {label.Slot}");
                }
            }
        }
    }

    private static void PlacementReusesBuffers()
    {
        var labels = new List<PlacedLabel>();
        var obstacles = new LabelBox[40];
        for (var i = 0; i < 120; i++)
        {
            var at = new Vector2(i % 12 * 28, i / 12 * 28);
            labels.Add(Label(i % 2 == 0 ? LabelAnchor.Bearing(at - new Vector2(100, 40), at) : LabelAnchor.Waypoint(at), order: i));
        }

        for (var i = 0; i < obstacles.Length; i++)
        {
            obstacles[i] = LabelBox.Around(new Vector2(i % 8 * 40, i / 8 * 40), new Vector2(12));
        }

        var layout = new LabelLayout();
        void PanAndPlace(int pass)
        {
            var shift = new Vector2(pass % 2 == 0 ? 64 : -64, 0);
            foreach (var label in labels)
            {
                var anchor = label.Anchor;
                label.Anchor = anchor.Kind == LabelKind.Bearing
                    ? LabelAnchor.Bearing(anchor.From + shift, anchor.Point + shift)
                    : LabelAnchor.Waypoint(anchor.Point + shift);
                label.Slot = -1;
            }

            for (var i = 0; i < obstacles.Length; i++)
            {
                obstacles[i] = new LabelBox(obstacles[i].Min + shift, obstacles[i].Max + shift);
            }

            layout.Place(labels, obstacles);
        }

        for (var pass = 0; pass < 100; pass++)
        {
            PanAndPlace(pass);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var pass = 0; pass < 100; pass++)
        {
            PanAndPlace(pass);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Expect(allocated == 0, $"expected no allocations after warming the grid, got {allocated} bytes");
    }

    private static PlacedLabel Label(LabelAnchor anchor, int order = 0, bool overlay = false, string text = "x") =>
        new() { Anchor = anchor, HalfSize = Half, Order = order, Overlay = overlay, Text = text };

    private static PlacedLabel Place(PlacedLabel label, params LabelBox[] obstacles)
    {
        new LabelLayout().Place(new List<PlacedLabel> { label }, obstacles);
        return label;
    }

    private static LabelBox Box(float x0, float y0, float x1, float y1) => new(new Vector2(x0, y0), new Vector2(x1, y1));

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.1f;

    /// <summary>Linear reference: scans every obstacle and already placed box, with the same bearing near filter.</summary>
    private sealed class BruteForceLayout
    {
        private const int FirstLeader = 11;
        private readonly List<PlacedLabel> _order = new();
        private readonly List<LabelBox> _placed = new();

        /// <summary>This pass's obstacles, copied out of the caller's list once rather than read through it for every slot.</summary>
        private LabelBox[] _obstacles = Array.Empty<LabelBox>();
        private int _obstacleCount;

        /// <summary>The obstacles and placed labels that reach the label being placed, which only they can block.</summary>
        private readonly List<LabelBox> _near = new();

        public void Place(List<PlacedLabel> labels, IReadOnlyList<LabelBox>? obstacles)
        {
            _order.Clear();
            foreach (var label in labels)
            {
                label.MirrorOf = label.Overlay ? FindOriginal(labels, label) : null;
                if (label.MirrorOf == null)
                {
                    _order.Add(label);
                }
            }

            _order.Sort(PriorityOrder.Instance);
            _placed.Clear();
            CopyObstacles(obstacles);
            foreach (var label in _order)
            {
                var count = LabelLayout.SlotCount(label.Anchor.Kind);
                var kept = label.Slot >= 0 && label.Slot < count ? label.Slot : 0;
                label.Take(obstacles == null ? kept : Choose(label, kept, count));
                _placed.Add(label.Box);
            }

            foreach (var label in labels)
            {
                if (label.MirrorOf is { } original)
                {
                    label.CopyFrom(original);
                }
            }
        }

        private static PlacedLabel? FindOriginal(List<PlacedLabel> labels, PlacedLabel copy)
        {
            foreach (var label in labels)
            {
                if (!label.Overlay && (copy.Order == 0 || label.Order == copy.Order) && label.Anchor.Equals(copy.Anchor) &&
                    string.Equals(label.Text, copy.Text, StringComparison.Ordinal))
                {
                    return label;
                }
            }

            return null;
        }

        private int Choose(PlacedLabel label, int kept, int count)
        {
            if (label.Slot >= 0 && IsClear(label, kept, near: false))
            {
                return kept;
            }

            var near = label.Anchor.Kind == LabelKind.Bearing;
            if (near)
            {
                GatherNear(label);
            }

            for (var slot = 0; slot < count; slot++)
            {
                if (IsClear(label, slot, near))
                {
                    return slot;
                }
            }

            return 0;
        }

        /// <summary>
        /// Clear of every obstacle, every label placed so far, and, for a bearing, its own shaft. With <paramref name="near"/>
        /// it checks only the boxes <see cref="GatherNear"/> gathered for this label.
        /// </summary>
        private bool IsClear(PlacedLabel label, int slot, bool near) =>
            Fits(label, slot, out var box) &&
            (near ? !OverlapsAny(box, _near) : !OverlapsAny(box, _obstacles, _obstacleCount) && !OverlapsAny(box, _placed));

        /// <summary>The label's box in the slot, and false where a bearing's box would cross its own shaft or a leader would reach too far.</summary>
        private static bool Fits(PlacedLabel label, int slot, out LabelBox box)
        {
            var anchor = label.Anchor;
            box = SlotBox(label, slot);
            if (anchor.Kind == LabelKind.Bearing && box.Crosses(anchor.From, anchor.Point))
            {
                return false;
            }

            // A leader slot sets its box's side, not its corner, at the ring's distance; a corner past the reach is too far.
            return !LabelLayout.IsLeaderSlot(anchor.Kind, slot) || Vector2.Distance(box.Nearest(anchor.Point), anchor.Point) <= LabelLayout.LeaderReach;
        }

        private static LabelBox SlotBox(PlacedLabel label, int slot)
        {
            LabelLayout.Candidate(label.Anchor, label.HalfSize, slot, out _, out var center);
            return LabelBox.Around(center, label.HalfSize);
        }

        /// <summary>
        /// Fills <see cref="_near"/> with the obstacles and placed labels that overlap the ground a bearing's slots cover.
        /// A leader slot only fits with its box's nearest point within <see cref="LeaderReach"/> of the head, so the leader
        /// slots together cover no more than that reach plus the box's size around the head, and a unit more for rounding.
        /// </summary>
        private void GatherNear(PlacedLabel label)
        {
            var reach = LabelBox.Around(label.Anchor.Point, new Vector2(LabelLayout.LeaderReach + 1f) + 2f * label.HalfSize);
            for (var slot = 0; slot < FirstLeader; slot++)
            {
                var box = SlotBox(label, slot);
                reach = new LabelBox(Vector2.Min(reach.Min, box.Min), Vector2.Max(reach.Max, box.Max));
            }

            _near.Clear();
            for (var i = 0; i < _obstacleCount; i++)
            {
                if (reach.Overlaps(_obstacles[i]))
                {
                    _near.Add(_obstacles[i]);
                }
            }

            foreach (var placed in _placed)
            {
                if (reach.Overlaps(placed))
                {
                    _near.Add(placed);
                }
            }
        }

        private void CopyObstacles(IReadOnlyList<LabelBox>? obstacles)
        {
            _obstacleCount = obstacles?.Count ?? 0;
            if (_obstacles.Length < _obstacleCount)
            {
                _obstacles = new LabelBox[Math.Max(_obstacleCount, 2 * _obstacles.Length)];
            }

            for (var i = 0; i < _obstacleCount; i++)
            {
                _obstacles[i] = obstacles![i];
            }
        }

        private static bool OverlapsAny(LabelBox box, LabelBox[] boxes, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (box.Overlaps(boxes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsAny(LabelBox box, List<LabelBox> boxes)
        {
            for (var i = 0; i < boxes.Count; i++)
            {
                if (box.Overlaps(boxes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Notes, fixed or on units, and waypoint numbers in drawing order, then labels being drawn now, then bearings and radii newest first.</summary>
        private sealed class PriorityOrder : IComparer<PlacedLabel>
        {
            public static readonly PriorityOrder Instance = new();

            public int Compare(PlacedLabel? x, PlacedLabel? y)
            {
                var tier = Tier(x!).CompareTo(Tier(y!));
                if (tier != 0)
                {
                    return tier;
                }

                return Tier(x!) == 0 ? x!.Order.CompareTo(y!.Order) : y!.Order.CompareTo(x!.Order);
            }

            private static int Tier(PlacedLabel label) => label.Anchor.Kind switch
            {
                LabelKind.Note or LabelKind.UnitNote or LabelKind.Waypoint => 0,
                _ when label.Overlay => 1,
                LabelKind.Bearing => 2,
                _ => 3,
            };
        }
    }
}
