using System.Numerics;

namespace BaanishUiImprovements.MapTools.Text;

/// <summary>A typed note centred on a fixed point, with the ground height there so the 3D view can pin it to the terrain.</summary>
public sealed class TextNote : MapShape
{
    public TextNote(Vector2 position, float elevation, string text, ShapeColor color)
        : base(color)
    {
        Position = position;
        Text = text;
        WorldPosition = new Vector3(position.X, elevation, position.Y);
    }

    public Vector2 Position { get; }

    public string Text { get; }

    /// <summary>Global meters: X east, Y up from sea level, Z north.</summary>
    public Vector3 WorldPosition { get; }

    public override void Draw(IMapCanvas canvas) => canvas.Label(LabelAnchor.Note(Position), Text, Color);
}
