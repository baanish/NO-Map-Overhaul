using System;
using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Finds the shape under a click by drawing each shape into itself and measuring how close every stroke passes, so any
/// shape a tool can draw can be picked without the picker knowing its type. Distances are meters.
/// </summary>
public sealed class ShapeHitTest : IMapCanvas
{
    private readonly IMapView _view;
    private Vector2 _point;
    private float _nearest;

    public ShapeHitTest(IMapView view) => _view = view;

    public DistanceUnit Units => _view.Units;
    public float MetersPerIconUnit => _view.MetersPerIconUnit;
    public float TextSize => _view.TextSize;
    public MapPoint? OwnAircraft => _view.OwnAircraft;

    /// <summary>The shape drawn nearest the point, within reach; on a tie the topmost. Null if none is in reach.</summary>
    public MapShape? Find(IReadOnlyList<MapShape> shapes, Vector2 point, float reach)
    {
        _point = point;
        MapShape? best = null;
        var bestDistance = float.MaxValue;
        for (var i = shapes.Count - 1; i >= 0; i--)
        {
            _nearest = float.MaxValue;
            shapes[i].Draw(this);
            if (_nearest <= reach && _nearest < bestDistance)
            {
                best = shapes[i];
                bestDistance = _nearest;
            }
        }

        return best;
    }

    public bool TryResolve(MapPoint point, out Vector2 position) => _view.TryResolve(point, out position);

    public void Line(Vector2 from, Vector2 to, ShapeColor color) => Measure(SegmentDistance(from, to));

    public void Arrow(Vector2 from, Vector2 to, ShapeColor color) => Measure(SegmentDistance(from, to));

    public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color)
    {
        if (points.Count == 1)
        {
            Measure(Vector2.Distance(_point, points[0]));
        }

        for (var i = 1; i < points.Count; i++)
        {
            Measure(SegmentDistance(points[i - 1], points[i]));
        }
    }

    public void Circle(Vector2 center, float radius, ShapeColor color) =>
        Measure(MathF.Abs(Vector2.Distance(_point, center) - radius));

    public void Marker(Vector2 position, ShapeColor color) =>
        Measure(MathF.Max(0f, Vector2.Distance(_point, position) - MapCanvasMetrics.MarkerRadius * MetersPerIconUnit));

    /// <summary>
    /// Boxes the text by its longest line and its line count, in the label's first slot: a click anywhere on the words
    /// counts. The map may have moved a label clear of the game's; a click on the line still takes the drawing.
    /// </summary>
    public void Label(LabelAnchor anchor, string text, ShapeColor color)
    {
        var (columns, lines) = TextExtent(text);
        var unit = MetersPerIconUnit;
        var half = LabelLayout.HalfSize(anchor.Kind, new Vector2(columns * TextSize * MapCanvasMetrics.CharWidth, lines * TextSize), TextSize);
        LabelLayout.Candidate(anchor.Scaled(1f / unit), half, 0, out _, out var center);
        Measure(Vector2.Max(Vector2.Abs(_point - center * unit) - half * unit, Vector2.Zero).Length());
    }

    private static (int Columns, int Lines) TextExtent(string text)
    {
        int columns = 0, lines = 1, run = 0;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                lines++;
                run = 0;
            }
            else
            {
                columns = Math.Max(columns, ++run);
            }
        }

        return (columns, lines);
    }

    private void Measure(float distance) => _nearest = MathF.Min(_nearest, distance);

    private float SegmentDistance(Vector2 from, Vector2 to)
    {
        var along = to - from;
        var lengthSquared = along.LengthSquared();
        var t = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(_point - from, along) / lengthSquared, 0f, 1f) : 0f;
        return Vector2.Distance(_point, from + along * t);
    }
}
