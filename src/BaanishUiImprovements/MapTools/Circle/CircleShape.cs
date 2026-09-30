using System.Numerics;
using BaanishUiImprovements.MapTools.BearingRange;

namespace BaanishUiImprovements.MapTools.Circle;

/// <summary>
/// A ring of a set radius with the radius written above it. A centre anchored to a unit follows it, and a lost unit
/// leaves the ring where it was last seen, labelled as lost. A fixed centre gets a small cross; a unit's icon marks its own.
/// </summary>
public sealed class CircleShape : MapShape
{
    /// <summary>Half the centre cross, in icon units.</summary>
    public const float CrossArm = 4f;

    private readonly MeasureLabel _label = new();

    public CircleShape(MapPoint center, float radius, ShapeColor color)
        : base(color)
    {
        Center = center;
        Radius = radius;
    }

    public MapPoint Center { get; }

    /// <summary>Meters.</summary>
    public float Radius { get; }

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

        canvas.Label(center + new Vector2(0f, radius), label.Radius(radius, canvas.Units, lost), color, LabelPlacement.Above);
    }

    public override void Draw(IMapCanvas canvas)
    {
        var found = canvas.TryResolve(Center, out var center);
        DrawRing(canvas, _label, center, Radius, Center.IsAnchored, !found, Color);
    }
}
