using System.Numerics;
using NoMapOverhaul.MapTools.BearingRange;

namespace NoMapOverhaul.MapTools.Circle;

/// <summary>
/// A ring of a set radius with the radius written on it, drawn again in the 3D view. A centre anchored to a unit follows it, and a lost unit
/// leaves the ring where it was last seen, labelled as lost. A fixed centre gets a small cross; a unit's icon marks its own.
/// </summary>
public sealed class CircleShape : MapShape
{
    /// <summary>Half the centre cross, in icon units.</summary>
    public const float CrossArm = 4f;

    private readonly MeasureLabel _label = new();

    /// <param name="centerElevation">Ground height under a fixed centre, for its 3D ring. An anchored centre uses the unit's altitude.</param>
    public CircleShape(MapPoint center, float radius, float centerElevation, ShapeColor color)
        : base(color)
    {
        Center = center;
        Radius = radius;
        CenterElevation = centerElevation;
    }

    public MapPoint Center { get; }

    /// <summary>Meters.</summary>
    public float Radius { get; }

    public float CenterElevation { get; }

    /// <summary>The ring and its label, shared with the tool's preview while the radius is still being picked.</summary>
    public static void DrawRing(IMapCanvas canvas, MeasureLabel label, Vector2 center, float radius, bool onUnit, bool lost, ShapeColor color)
    {
        canvas.Circle(center, radius, color);
        if (!onUnit)
        {
            var arm = CrossArm * canvas.MetersPerIconUnit;
            canvas.Line(center - new Vector2(arm, 0f), center + new Vector2(arm, 0f), color);
            canvas.Line(center - new Vector2(0f, arm), center + new Vector2(0f, arm), color);
        }

        canvas.Label(LabelAnchor.Ring(center, radius), label.Radius(radius, canvas.Units, lost), color);
    }

    public override void Draw(IMapCanvas canvas)
    {
        var found = canvas.TryResolve(Center, out var center);
        DrawRing(canvas, _label, center, Radius, Center.IsAnchored, !found, Color);
    }

    /// <summary>The ring again in the 3D view, level with its centre: a unit's altitude, or the ground under a fixed point.</summary>
    public void AddWorldRing(IMapToolContext context, IWorldLabels labels)
    {
        context.TryResolveWorld(Center, out var world);
        if (!Center.IsAnchored)
        {
            world.Y = CenterElevation;
        }

        labels.Ring(world, Radius, Color);
    }
}
