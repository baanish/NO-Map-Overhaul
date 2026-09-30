using System.Numerics;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// A point on the map in meters: X east, Y north, the game's global x and z. A point with a <see cref="UnitId"/> is
/// anchored to that unit and follows it. <see cref="Position"/> is then where the unit was when anchored, the fallback
/// until the unit is found.
/// </summary>
public readonly struct MapPoint
{
    public MapPoint(Vector2 position, uint unitId = 0)
    {
        Position = position;
        UnitId = unitId;
    }

    public Vector2 Position { get; }

    /// <summary>The unit's <c>PersistentID.Id</c>, which every client shares; 0, the game's "none", for a fixed point.</summary>
    public uint UnitId { get; }

    public bool IsAnchored => UnitId != 0;
}

/// <summary>
/// One drawing on the map. It holds plain data only (meters, unit ids, colour bytes), so drawings can be saved or
/// shared with other players later without a rewrite. It is immutable once in the <see cref="ShapeStore"/>: to change
/// a shape, <see cref="ShapeStore.Replace"/> it with an edited copy, so undo can bring the old one back.
/// </summary>
public abstract class MapShape
{
    protected MapShape(ShapeColor color) => Color = color;

    /// <summary>Unique within the mission, set by the store. A replacement keeps the id of the shape it replaces.</summary>
    public int Id { get; internal set; }

    public ShapeColor Color { get; }

    /// <summary>Freehand points this shape holds, counted against the MaxPenPoints setting. Shapes built from a few anchors leave it 0.</summary>
    public virtual int PointCount => 0;

    /// <summary>
    /// Draws the shape. The same call renders it on the map and hit-tests it for the eraser, so draw everything that
    /// should be clickable. Read anchored points through <see cref="IMapView.TryResolve"/> and ownship through
    /// <see cref="IMapView.OwnAircraft"/>: either marks the shape live, and the map then redraws it on each of the game's
    /// 10 Hz map refreshes. Other shapes redraw only when added, removed, or zoomed. Don't allocate here: a live shape
    /// draws ten times a second, so keep label strings cached until their text changes.
    /// </summary>
    public abstract void Draw(IMapCanvas canvas);
}
