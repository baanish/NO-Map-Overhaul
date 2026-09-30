using System.Collections.Generic;
using System.Numerics;

namespace NoMapOverhaul.MapTools.Pen;

/// <summary>One freehand line, already thinned and simplified by <see cref="PenTool"/>. Each point counts against MaxPenPoints.</summary>
public sealed class PenStroke : MapShape
{
    private readonly Vector2[] _points;

    public PenStroke(Vector2[] points, ShapeColor color)
        : base(color) =>
        _points = points;

    public IReadOnlyList<Vector2> Points => _points;

    public override int PointCount => _points.Length;

    public override void Draw(IMapCanvas canvas) => canvas.Polyline(_points, Color);
}
