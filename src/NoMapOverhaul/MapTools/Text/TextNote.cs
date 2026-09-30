using NoMapOverhaul.MapTools.BearingRange;

namespace NoMapOverhaul.MapTools.Text;

/// <summary>
/// A typed note, centred on a fixed point or beside a unit's icon. A note on a unit follows the unit as its icon does,
/// and a lost unit leaves it where the unit was last known, saying so. A fixed note keeps the ground height under it,
/// so the 3D view can pin it to the terrain.
/// </summary>
public sealed class TextNote : MapShape
{
    /// <param name="elevation">Ground height under a fixed note, for its 3D label. A note on a unit uses the unit's altitude.</param>
    public TextNote(MapPoint at, float elevation, string text, ShapeColor color)
        : base(color)
    {
        At = at;
        Elevation = elevation;
        Text = text;
        LostText = text + MeasureLabel.LostLine;
    }

    public MapPoint At { get; }

    public float Elevation { get; }

    public string Text { get; }

    /// <summary>The text with the lost line under it, built once, since a note on a unit draws ten times a second.</summary>
    public string LostText { get; }

    /// <summary>The note's map label, shared with the tool's preview while typing: centred on a fixed point, or beside a unit's icon.</summary>
    public static void DrawLabel(IMapCanvas canvas, MapPoint at, string text, string lostText, ShapeColor color)
    {
        if (!at.IsAnchored)
        {
            canvas.Label(LabelAnchor.Note(at.Position), text, color);
            return;
        }

        var found = canvas.TryResolve(at, out var unit);
        canvas.Label(LabelAnchor.UnitNote(unit), found ? text : lostText, color);
    }

    public override void Draw(IMapCanvas canvas) => DrawLabel(canvas, At, Text, LostText, Color);

    /// <summary>The note again in the 3D view: on its unit at the altitude its side knows, or on the ground under a fixed point.</summary>
    public void AddWorldLabel(IMapToolContext context, IWorldLabels labels)
    {
        var found = BearingRangeShape.TryResolveEnd(context, At, Elevation, out var position);
        labels.Add(position, found ? Text : LostText, Color);
    }
}
