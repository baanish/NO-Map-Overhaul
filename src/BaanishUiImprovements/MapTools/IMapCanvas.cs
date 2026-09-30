using System;
using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools;

/// <summary>What a shape or a tool can ask about the map. Positions and distances are meters; on-screen sizes are map icon units.</summary>
public interface IMapView
{
    /// <summary>Units for distances shown to the player: the DistanceUnits setting, or the game's own unit system.</summary>
    DistanceUnit Units { get; }

    /// <summary>
    /// Meters covered by one map icon unit at the current zoom. Icon units size the game's own map icons, about one
    /// screen pixel on the full map at 1080p, so they suit click reach and on-screen gaps.
    /// </summary>
    float MetersPerIconUnit { get; }

    /// <summary>Size of text the tools draw on the map, in icon units: the TextSize setting.</summary>
    float TextSize { get; }

    /// <summary>The local player's aircraft as a point anchored to it, or null while not flying.</summary>
    MapPoint? OwnAircraft { get; }

    /// <summary>
    /// Where a point is now. An anchored point follows its unit the way the unit's map icon does, through the
    /// faction's tracking for an enemy, so it reveals nothing the map doesn't. False once the unit is destroyed or no
    /// longer tracked: <paramref name="position"/> is then where it was last seen, and it follows again if the unit
    /// is tracked again. A fixed point is always true.
    /// </summary>
    bool TryResolve(MapPoint point, out Vector2 position);
}

/// <summary>What a label names, which sets the places it may take and which labels give way to it (see <see cref="LabelLayout"/>).</summary>
public enum LabelKind
{
    /// <summary>Typed text, centred on its point. It never moves and has no plate.</summary>
    Note,

    /// <summary>A waypoint's number beside its marker.</summary>
    Waypoint,

    /// <summary>A bearing and range at an arrow's head.</summary>
    Bearing,

    /// <summary>A circle's radius, on its ring.</summary>
    Radius,
}

/// <summary>What a label is attached to. Positions are meters for a shape; the layout works in icon units.</summary>
public readonly struct LabelAnchor : IEquatable<LabelAnchor>
{
    private LabelAnchor(LabelKind kind, Vector2 point, Vector2 from, float radius)
    {
        Kind = kind;
        Point = point;
        From = from;
        Radius = radius;
    }

    public LabelKind Kind { get; }

    /// <summary>The note's point, the marker, the arrow's head, or the circle's centre.</summary>
    public Vector2 Point { get; }

    /// <summary>The arrow's tail, for a bearing.</summary>
    public Vector2 From { get; }

    /// <summary>The circle's radius, for a radius.</summary>
    public float Radius { get; }

    public static LabelAnchor Note(Vector2 point) => new(LabelKind.Note, point, point, 0f);

    public static LabelAnchor Waypoint(Vector2 marker) => new(LabelKind.Waypoint, marker, marker, 0f);

    public static LabelAnchor Bearing(Vector2 from, Vector2 head) => new(LabelKind.Bearing, head, from, 0f);

    public static LabelAnchor Ring(Vector2 center, float radius) => new(LabelKind.Radius, center, center, radius);

    /// <summary>The same anchor with every position and length times <paramref name="factor"/>, such as meters to icon units.</summary>
    public LabelAnchor Scaled(float factor) => new(Kind, Point * factor, From * factor, Radius * factor);

    public bool Equals(LabelAnchor other) => Kind == other.Kind && Point == other.Point && From == other.From && Radius == other.Radius;

    public override bool Equals(object? obj) => obj is LabelAnchor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Point, From, Radius);
}

/// <summary>
/// Draw calls for one shape, in meters. Line width, the dark rim, and text size come from the settings and stay the
/// same on screen at any zoom. Everything drawn here can be clicked by the eraser.
/// </summary>
public interface IMapCanvas : IMapView
{
    void Line(Vector2 from, Vector2 to, ShapeColor color);

    /// <summary>A line with an arrowhead at <paramref name="to"/>.</summary>
    void Arrow(Vector2 from, Vector2 to, ShapeColor color);

    /// <summary>An open line through the points. The canvas copies them, so a shape can pass its own list.</summary>
    void Polyline(IReadOnlyList<Vector2> points, ShapeColor color);

    /// <summary>A ring, not a filled disk.</summary>
    void Circle(Vector2 center, float radius, ShapeColor color);

    /// <summary>The game's own map waypoint marker (its steerpoint sprite, 20 icon units across), tinted.</summary>
    void Marker(Vector2 position, ShapeColor color);

    /// <summary>
    /// Upright text in the HUD font: a note with a dark rim, anything else on a dark plate that the full map moves clear
    /// of the game's labels and icons (see <see cref="LabelLayout"/>). May hold line breaks. A live shape draws ten times
    /// a second, so pass a cached string.
    /// </summary>
    void Label(LabelAnchor anchor, string text, ShapeColor color);
}

/// <summary>Sizes the map drawing and the eraser's hit test share, in icon units.</summary>
public static class MapCanvasMetrics
{
    public const float LabelGap = 6f;

    /// <summary>Half the game's 20-unit map waypoint marker.</summary>
    public const float MarkerRadius = 10f;

    public const float ArrowHeadLength = 10f;

    /// <summary>A character's rough width as a fraction of the text size. The hit test has no font, so it boxes labels with this.</summary>
    public const float CharWidth = 0.6f;
}
