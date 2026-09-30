using System;
using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools;

/// <summary>An axis-aligned box in icon units, Y up: a label's plate, or something on the map a label keeps clear of.</summary>
public readonly struct LabelBox
{
    public LabelBox(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public Vector2 Min { get; }

    public Vector2 Max { get; }

    public static LabelBox Around(Vector2 center, Vector2 halfSize) => new(center - halfSize, center + halfSize);

    public LabelBox Grown(float by) => new(Min - new Vector2(by), Max + new Vector2(by));

    /// <summary>Touching edges don't count, so labels may sit side by side.</summary>
    public bool Overlaps(LabelBox other) => Min.X < other.Max.X && other.Min.X < Max.X && Min.Y < other.Max.Y && other.Min.Y < Max.Y;

    public Vector2 Nearest(Vector2 point) => Vector2.Clamp(point, Min, Max);

    /// <summary>Whether the segment passes through the box, clipping it against each pair of sides in turn.</summary>
    public bool Crosses(Vector2 from, Vector2 to)
    {
        var enter = 0f;
        var leave = 1f;
        return Clip(from.X, to.X - from.X, Min.X, Max.X, ref enter, ref leave) && Clip(from.Y, to.Y - from.Y, Min.Y, Max.Y, ref enter, ref leave);
    }

    private static bool Clip(float start, float delta, float min, float max, ref float enter, ref float leave)
    {
        if (MathF.Abs(delta) < 1e-6f)
        {
            return start > min && start < max;
        }

        var near = (min - start) / delta;
        var far = (max - start) / delta;
        if (near > far)
        {
            (near, far) = (far, near);
        }

        enter = MathF.Max(enter, near);
        leave = MathF.Min(leave, far);
        return enter < leave;
    }
}

/// <summary>One label for <see cref="LabelLayout.Place"/>: the caller fills in what it is, the layout where it goes. Icon units, Y up.</summary>
public sealed class PlacedLabel
{
    public LabelAnchor Anchor { get; set; }

    /// <summary>Half the plate's width and height.</summary>
    public Vector2 HalfSize { get; set; }

    /// <summary>Compared with other labels' text to find the same label drawn twice.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Drawn by a tool's overlay, such as a measurement still being picked, rather than a stored shape.</summary>
    public bool Overlay { get; set; }

    /// <summary>Higher is newer: the shape's id.</summary>
    public int Order { get; set; }

    /// <summary>
    /// The candidate slot it took, kept from pass to pass so a label stays put while its slot stays clear. -1 for a new
    /// label. <see cref="LabelLayout.Place"/> sets it; reset it to start over.
    /// </summary>
    public int Slot { get; set; } = -1;

    /// <summary>The point the label hangs from: the note's point, the marker, the arrow's head, or the point on the ring.</summary>
    public Vector2 Origin { get; private set; }

    public Vector2 Center { get; private set; }

    /// <summary>The label sits away from the arrow's head, joined to it by a leader from <see cref="LeaderFrom"/> to <see cref="LeaderTo"/>.</summary>
    public bool Leader { get; private set; }

    public Vector2 LeaderFrom { get; private set; }

    public Vector2 LeaderTo { get; private set; }

    public LabelBox Box => LabelBox.Around(Center, HalfSize);

    internal PlacedLabel? MirrorOf { get; set; }

    internal void Take(int slot)
    {
        Slot = slot;
        LabelLayout.Candidate(Anchor, HalfSize, slot, out var origin, out var center);
        Origin = origin;
        Center = center;
        Leader = LabelLayout.IsLeaderSlot(Anchor.Kind, slot);
        if (Leader)
        {
            LeaderTo = Box.Nearest(Anchor.Point);
            var toward = LeaderTo - Anchor.Point;
            var length = toward.Length();
            LeaderFrom = length > LabelLayout.LeaderStart ? Anchor.Point + toward * (LabelLayout.LeaderStart / length) : Anchor.Point;
        }
    }

    internal void CopyFrom(PlacedLabel other)
    {
        Slot = other.Slot;
        Origin = other.Origin;
        Center = other.Center;
        Leader = other.Leader;
        LeaderFrom = other.LeaderFrom;
        LeaderTo = other.LeaderTo;
    }
}

/// <summary>
/// Places map labels clear of each other and of what the game draws: unit icons, airbase names, runway numbers, the grid
/// labels, and the map tools rail. Labels are placed in priority order and a later label never pushes an earlier one:
/// notes and waypoint numbers first (the pilot put them there), then labels being drawn right now, then bearings newest
/// first, then circle radii newest first. Each kind tries its candidate slots in order and takes the first clear one,
/// keeping the slot it had while that stays clear, so nothing jumps while the map pans; with no clear slot it takes its
/// first. A bearing tries past its arrowhead, beside the head, then slid back along its shaft, and last the nearest
/// free slot within <see cref="LeaderReach"/>, joined to the head by a leader. A radius tries its ring at 12, 2, 10, 6,
/// 4, and 8 o'clock; a waypoint number tries upper right, upper left, lower right, lower left. Notes never move.
/// Unity-free and allocation-free once its lists have grown. Sizes are icon units: about a screen pixel at 1080p, three
/// quarters of one at 1440p, where the design's sizes are measured.
/// </summary>
public sealed class LabelLayout
{
    /// <summary>Between a label and what it names: 8 px at 1440p.</summary>
    public const float Gap = MapCanvasMetrics.LabelGap;

    /// <summary>How far from the arrow's head a leader may reach: 48 px at 1440p.</summary>
    public const float LeaderReach = 36f;

    /// <summary>A leader starts this far from the head, clear of the arrowhead's point.</summary>
    public const float LeaderStart = 3f;

    /// <summary>How far back along the shaft a bearing may slide, as a fraction of its length.</summary>
    public const float SlideLimit = 0.4f;

    /// <summary>How far a unit icon's box grows before labels keep clear of it: 6 px at 1440p.</summary>
    public const float IconMargin = 4.5f;

    private const int SlideSteps = 4;
    private const int LeaderRings = 3;
    private const int LeaderDirections = 16;
    private const int FirstSlide = 3;
    private const int FirstLeader = FirstSlide + 2 * SlideSteps;

    /// <summary>Clock positions of a radius on its ring, in degrees clockwise from north: 12, 2, 10, 6, 4, 8.</summary>
    private static readonly float[] RingDegrees = { 0f, 60f, -60f, 180f, 120f, -120f };

    private readonly List<PlacedLabel> _order = new();
    private readonly List<LabelBox> _placed = new();

    /// <summary>This pass's obstacles, copied out of the caller's list once rather than read through it for every slot.</summary>
    private LabelBox[] _obstacles = Array.Empty<LabelBox>();
    private int _obstacleCount;

    /// <summary>The obstacles and placed labels that reach the label being placed, which only they can block.</summary>
    private readonly List<LabelBox> _near = new();

    public static int SlotCount(LabelKind kind) => kind switch
    {
        LabelKind.Waypoint => 4,
        LabelKind.Bearing => FirstLeader + LeaderRings * LeaderDirections,
        LabelKind.Radius => RingDegrees.Length,
        _ => 1,
    };

    public static bool IsLeaderSlot(LabelKind kind, int slot) => kind == LabelKind.Bearing && slot >= FirstLeader;

    /// <summary>
    /// A text plate's half size for text of <paramref name="textSize"/>, measured or estimated, at <paramref name="fontSize"/>:
    /// half an em of padding either side and a little above and below, as the design's 22 px plate holds 14 px text.
    /// A note has no plate, so it gets its rim's width.
    /// </summary>
    public static Vector2 HalfSize(LabelKind kind, Vector2 textSize, float fontSize) =>
        kind == LabelKind.Note ? textSize * 0.5f + Vector2.One : textSize * 0.5f + new Vector2(0.5f, 0.15f) * fontSize;

    /// <summary>
    /// Where a label of <paramref name="halfSize"/> sits in a candidate slot: <paramref name="origin"/> is the point it
    /// hangs from and <paramref name="center"/> the middle of its plate.
    /// </summary>
    public static void Candidate(LabelAnchor anchor, Vector2 halfSize, int slot, out Vector2 origin, out Vector2 center)
    {
        origin = anchor.Point;
        switch (anchor.Kind)
        {
            case LabelKind.Waypoint:
                // Past the corner of the game's round, 20-unit marker in any of the four diagonal directions.
                var corner = MapCanvasMetrics.MarkerRadius * 0.75f;
                var side = new Vector2(slot % 2 == 0 ? 1f : -1f, slot < 2 ? 1f : -1f);
                center = origin + side * (new Vector2(corner) + halfSize);
                break;
            case LabelKind.Radius:
                var angle = RingDegrees[slot] * MathF.PI / 180f;
                origin = anchor.Point + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * anchor.Radius;
                center = origin;
                break;
            case LabelKind.Bearing:
                center = BearingCandidate(anchor, halfSize, slot);
                break;
            default:
                center = origin;
                break;
        }
    }

    /// <summary>
    /// Places every label, as described on the class. With <paramref name="obstacles"/> null nothing is avoided: each
    /// label keeps the slot it had, or takes its first, which is what the full map does while it moves. The minimap,
    /// where the game's labels aren't known, passes none, so its labels avoid only each other.
    /// A label drawn twice, such as the eraser's highlight redrawing a drawing, takes the slot of the first.
    /// </summary>
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
            var count = SlotCount(label.Anchor.Kind);
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
            if (!label.Overlay && label.Anchor.Equals(copy.Anchor) && string.Equals(label.Text, copy.Text, StringComparison.Ordinal))
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>A bearing's slots: past the head, beside it, slid back along the shaft, then leader slots nearest first.</summary>
    private static Vector2 BearingCandidate(LabelAnchor anchor, Vector2 halfSize, int slot)
    {
        var head = anchor.Point;
        var shaft = head - anchor.From;
        var length = shaft.Length();
        var along = length > 1e-4f ? shaft / length : Vector2.UnitX;
        if (slot == 0)
        {
            return Beside(head, along, halfSize, Gap);
        }

        if (slot < FirstSlide)
        {
            var away = AwayFromShaft(along, anchor.From - head);
            return Beside(head, slot == 1 ? away : -away, halfSize, Gap);
        }

        if (slot < FirstLeader)
        {
            var step = slot - FirstSlide;
            var above = Above(along);
            var back = head - along * (length * SlideLimit * (step / 2 + 1) / SlideSteps);
            return Beside(back, step % 2 == 0 ? above : -above, halfSize, Gap);
        }

        var leader = slot - FirstLeader;
        var reach = LeaderReach * (leader / LeaderDirections + 1) / LeaderRings;
        var turn = leader % LeaderDirections;
        var steps = (turn + 1) / 2 * (turn % 2 == 1 ? 1 : -1); // 0, +1, -1, +2, -2 ... around from past the head
        return Beside(head, Rotate(along, steps * 2f * MathF.PI / LeaderDirections), halfSize, reach);
    }

    /// <summary>A box whose nearest side is <paramref name="gap"/> from the point in <paramref name="direction"/>.</summary>
    private static Vector2 Beside(Vector2 point, Vector2 direction, Vector2 halfSize, float gap) =>
        point + direction * (gap + MathF.Abs(direction.X) * halfSize.X + MathF.Abs(direction.Y) * halfSize.Y);

    /// <summary>
    /// Of the head's two sides, the one the shaft leans away from: a shaft rising to the right from the head puts the
    /// label on the head's left. Judged across whichever screen axis the normal mostly points along.
    /// </summary>
    private static Vector2 AwayFromShaft(Vector2 along, Vector2 towardTail)
    {
        var normal = new Vector2(-along.Y, along.X);
        var lean = MathF.Abs(normal.X) >= MathF.Abs(normal.Y) ? normal.X * towardTail.X : normal.Y * towardTail.Y;
        return lean > 0f ? -normal : normal;
    }

    /// <summary>The shaft's normal that points up the screen, or right for an upright shaft.</summary>
    private static Vector2 Above(Vector2 along)
    {
        var normal = new Vector2(-along.Y, along.X);
        return normal.Y < 0f || (normal.Y == 0f && normal.X < 0f) ? -normal : normal;
    }

    private static Vector2 Rotate(Vector2 direction, float radians)
    {
        var (sin, cos) = (MathF.Sin(radians), MathF.Cos(radians));
        return new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
    }

    /// <summary>
    /// The kept slot if it's still clear, else the first clear one, else the first. A bearing whose kept slot is blocked
    /// may try all its slots, dozens of them, so it first gathers the boxes that overlap any of them
    /// (<see cref="GatherNear"/>): no other box can block one, so each slot checks a few boxes rather than every icon on
    /// the map. The choice is the same either way.
    /// </summary>
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
        return !IsLeaderSlot(anchor.Kind, slot) || Vector2.Distance(box.Nearest(anchor.Point), anchor.Point) <= LeaderReach;
    }

    private static LabelBox SlotBox(PlacedLabel label, int slot)
    {
        Candidate(label.Anchor, label.HalfSize, slot, out _, out var center);
        return LabelBox.Around(center, label.HalfSize);
    }

    /// <summary>
    /// Fills <see cref="_near"/> with the obstacles and placed labels that overlap the ground a bearing's slots cover.
    /// A leader slot only fits with its box's nearest point within <see cref="LeaderReach"/> of the head, so the leader
    /// slots together cover no more than that reach plus the box's size around the head, and a unit more for rounding.
    /// </summary>
    private void GatherNear(PlacedLabel label)
    {
        var reach = LabelBox.Around(label.Anchor.Point, new Vector2(LeaderReach + 1f) + 2f * label.HalfSize);
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

    /// <summary>Notes and waypoint numbers in drawing order, then labels being drawn now, then bearings and radii newest first.</summary>
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
            LabelKind.Note or LabelKind.Waypoint => 0,
            _ when label.Overlay => 1,
            LabelKind.Bearing => 2,
            _ => 3,
        };
    }
}
