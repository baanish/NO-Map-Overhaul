using System;
using System.Collections.Generic;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.BearingRange;
using NoMapOverhaul.MapTools.Circle;
using NoMapOverhaul.MapTools.Pen;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.MapTools.Waypoint;

namespace NoMapOverhaul.Sharing;

public sealed class ReceivedDrawings
{
    // Remote drawings have a separate fixed budget so teammates cannot consume the local drawing allowance.
    public const int MaxPeers = 32;
    /// <summary>
    /// Drawn across all teammates, split evenly between them: each shows its oldest drawings up to an equal share, so
    /// nobody's plan is pushed off the map by whoever filled the pool first. Up to four teammates show everything.
    /// </summary>
    public const int MaxShapes = 400;
    public const int MaxGeometryPoints = 10000;

    private readonly Dictionary<ulong, Dictionary<int, MapShape>> _owners = new();

    /// <summary>Teammates in the order they first sent drawings, so whose drawings draw on top stays put when one leaves or re-pairs.</summary>
    private readonly List<ulong> _order = new();
    private MapShape[] _shapes = Array.Empty<MapShape>();

    /// <summary>
    /// Kept between snapshots rather than allocated for each, twice a second per teammate: the next snapshot fills the
    /// dictionary the last one replaced, or the one it left unused.
    /// </summary>
    private Dictionary<int, MapShape> _spare = new();
    private readonly HashSet<int> _usedIds = new();
    private readonly List<MapShape> _shown = new();

    public IReadOnlyList<MapShape> Shapes => _shapes;
    public int Version { get; private set; }

    public bool TryFindOwner(MapShape shape, out ulong owner, out int sourceId)
    {
        foreach (var sender in _owners)
        {
            foreach (var entry in sender.Value)
            {
                if (!ReferenceEquals(entry.Value, shape)) continue;
                owner = sender.Key;
                sourceId = entry.Key;
                return true;
            }
        }

        owner = 0;
        sourceId = 0;
        return false;
    }

    /// <summary>
    /// The drawings a teammate's last snapshot left here, and how many of them are among <paramref name="shown"/>: the
    /// map shows fewer when the pool's equal share cuts them, or a unit they're on was never known to this side.
    /// </summary>
    public (int Held, int Shown) CountOwned(ulong sender, ICollection<MapShape> shown)
    {
        if (!_owners.TryGetValue(sender, out var owned)) return (0, 0);
        var count = 0;
        foreach (var shape in owned.Values)
            if (shown.Contains(shape)) count++;
        return (owned.Count, count);
    }

    public bool TryReplace(ulong sender, MapShape[] wireShapes) => TryReplace(sender, wireShapes, out _);

    public int ChangeCost(ulong sender, MapShape[] wireShapes)
    {
        _owners.TryGetValue(sender, out var previous);
        var cost = 0;
        foreach (var shape in wireShapes)
            if (previous == null || !previous.TryGetValue(shape.Id, out var old) || !SameContent(old, shape)) cost++;
        return cost;
    }

    public bool TryReplace(ulong sender, MapShape[] wireShapes, out bool capacityExceeded)
    {
        capacityExceeded = false;
        if (sender == 0 || wireShapes == null || wireShapes.Length > DrawingProtocol.MaxShapes) return false;
        if (wireShapes.Length == 0)
        {
            Remove(sender);
            return true;
        }

        _owners.TryGetValue(sender, out var previous);
        if (previous == null && _owners.Count >= MaxPeers)
        {
            capacityExceeded = true;
            return false;
        }
        var replacement = _spare;
        replacement.Clear();
        var points = 0;
        foreach (var shape in wireShapes)
        {
            if (shape == null || shape.Id <= 0 || replacement.ContainsKey(shape.Id) ||
                !DrawingChannels.IsCanonicalName(shape.Channel)) return false;
            int count;
            try { count = DrawingProtocol.GeometryPointCount(shape); }
            catch (ArgumentException) { return false; }
            if (count < 1 || count > DrawingProtocol.MaxGeometryPoints - points) return false;
            points += count;
            replacement.Add(shape.Id, previous != null && previous.TryGetValue(shape.Id, out var old) && SameContent(old, shape)
                ? old : shape);
        }

        // Heartbeats retain instances and label placements until content or draw order changes.
        if (previous != null && SameOrder(previous, replacement)) return true;

        // Every retained drawing keeps its ID, shown or not, so one revealed by a departure never collides.
        var usedIdsFilled = false;
        var nextId = -1;
        foreach (var entry in replacement)
        {
            if (previous != null && previous.TryGetValue(entry.Key, out var old))
            {
                entry.Value.Id = old.Id;
            }
            else
            {
                if (!usedIdsFilled)
                {
                    _usedIds.Clear();
                    foreach (var owner in _owners.Values)
                        foreach (var shape in owner.Values) _usedIds.Add(shape.Id);
                    usedIdsFilled = true;
                }
                while (_usedIds.Contains(nextId)) nextId--;
                entry.Value.Id = nextId;
                _usedIds.Add(nextId);
            }
        }

        if (previous == null) _order.Add(sender);
        _owners[sender] = replacement;
        _spare = previous ?? new Dictionary<int, MapShape>();
        Rebuild();
        return true;
    }

    private static bool SameOrder(Dictionary<int, MapShape> previous, Dictionary<int, MapShape> replacement)
    {
        if (previous.Count != replacement.Count) return false;
        using var before = previous.Values.GetEnumerator();
        foreach (var shape in replacement.Values)
        {
            if (!before.MoveNext() || !ReferenceEquals(before.Current, shape)) return false;
        }
        return true;
    }

    private static bool SamePoint(MapPoint left, MapPoint right) =>
        left.Position == right.Position && left.UnitId == right.UnitId;

    private static bool SameContent(MapShape left, MapShape right)
    {
        if (left.Color != right.Color || left.Channel != right.Channel) return false;
        switch (left)
        {
            case BearingRangeShape a when right is BearingRangeShape b:
                return SamePoint(a.From, b.From) && SamePoint(a.To, b.To) &&
                    a.FromElevation == b.FromElevation && a.ToElevation == b.ToElevation;
            case CircleShape a when right is CircleShape b:
                return SamePoint(a.Center, b.Center) && a.Radius == b.Radius && a.CenterElevation == b.CenterElevation;
            case TextNote a when right is TextNote b:
                return SamePoint(a.At, b.At) && a.Elevation == b.Elevation && a.Text == b.Text;
            case PenStroke a when right is PenStroke b:
                if (a.Points.Count != b.Points.Count) return false;
                for (var i = 0; i < a.Points.Count; i++)
                    if (a.Points[i] != b.Points[i]) return false;
                return true;
            case WaypointRoute a when right is WaypointRoute b:
                if (a.Count != b.Count) return false;
                for (var i = 0; i < a.Count; i++)
                    if (!SamePoint(a[i].Point, b[i].Point) || a[i].Elevation != b[i].Elevation) return false;
                return true;
            default:
                return false;
        }
    }

    public void Remove(ulong sender)
    {
        if (!_owners.Remove(sender)) return;
        _order.Remove(sender);
        Rebuild();
    }

    public void Clear()
    {
        if (_owners.Count == 0) return;
        _owners.Clear();
        _order.Clear();
        Rebuild();
    }

    private void Rebuild()
    {
        _shown.Clear();
        var shareShapes = _owners.Count == 0 ? 0 : MaxShapes / _owners.Count;
        var sharePoints = _owners.Count == 0 ? 0 : MaxGeometryPoints / _owners.Count;
        foreach (var sender in _order)
        {
            var owner = _owners[sender];
            var count = 0;
            var points = 0;
            foreach (var shape in owner.Values)
            {
                points += DrawingProtocol.GeometryPointCount(shape);
                if (++count > shareShapes || points > sharePoints) break;
                _shown.Add(shape);
            }
        }
        _shapes = _shown.ToArray();
        Version++;
    }
}

public static class SharedShapeVisibility
{
    /// <summary>
    /// Every drawing kind shares, unit-linked ones included: the codec sends a unit anchor as its mission ID alone, with
    /// zero position and elevation, and each recipient places it from its own tracks.
    /// </summary>
    public static bool CanShare(MapShape shape) =>
        shape is BearingRangeShape or CircleShape or PenStroke or TextNote or WaypointRoute;

    /// <summary>Every anchor must pass the recipient's visibility check; sender fallback data cannot establish visibility.</summary>
    public static bool HasKnownAnchors(MapShape shape, Func<uint, bool> known)
    {
        bool Visible(MapPoint point) => !point.IsAnchored || known(point.UnitId);
        switch (shape)
        {
            case BearingRangeShape bearing:
                return Visible(bearing.From) && Visible(bearing.To);
            case CircleShape circle:
                return Visible(circle.Center);
            case PenStroke:
                return true;
            case TextNote note:
                return Visible(note.At);
            case WaypointRoute route:
                for (var i = 0; i < route.Count; i++)
                    if (!Visible(route[i].Point)) return false;
                return true;
            default:
                return false;
        }
    }
}
