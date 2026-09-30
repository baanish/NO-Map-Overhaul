using System;
using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools;

/// <summary>Where the map placed the labels of the shapes in the store, which may be a slot or a leader away from their first.</summary>
public interface ILabelPlacements
{
    /// <summary>
    /// The box of <paramref name="shape"/>'s label at <paramref name="index"/>, counted in the order its draw adds
    /// labels, in icon units. False if the map hasn't placed it.
    /// </summary>
    bool TryGetBox(MapShape shape, int index, out LabelBox box);
}

/// <summary>
/// Finds the shape under a click by drawing each shape into itself and measuring how close every stroke passes, so any
/// shape a tool can draw can be picked without the picker knowing its type. Distances are meters.
/// </summary>
public sealed class ShapeHitTest : IMapCanvas
{
    private readonly IMapView _view;
    private readonly ILabelPlacements? _placements;
    private Vector2 _point;
    private float _nearest;
    private MapShape? _shape;
    private int _labelIndex;

    public ShapeHitTest(IMapView view, ILabelPlacements? placements = null)
    {
        _view = view;
        _placements = placements;
    }

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
            _shape = shapes[i];
            _labelIndex = 0;
            _shape.Draw(this);
            if (_nearest <= reach && _nearest < bestDistance)
            {
                best = shapes[i];
                bestDistance = _nearest;
            }
        }

        _shape = null;
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
    /// The label's box where the map placed it, so a click anywhere on the plate counts. A label the map hasn't placed
    /// is boxed by its longest line and its line count, in its first slot.
    /// </summary>
    public void Label(LabelAnchor anchor, string text, ShapeColor color)
    {
        var unit = MetersPerIconUnit;
        if (_placements == null || _shape == null || !_placements.TryGetBox(_shape, _labelIndex++, out var box))
        {
            var (columns, lines) = TextExtent(text);
            var half = LabelLayout.HalfSize(anchor.Kind, new Vector2(columns * TextSize * MapCanvasMetrics.CharWidth, lines * TextSize), TextSize);
            LabelLayout.Candidate(anchor.Scaled(1f / unit), half, 0, out _, out var center);
            box = LabelBox.Around(center, half);
        }

        var point = _point / unit;
        Measure(Vector2.Distance(box.Nearest(point), point) * unit);
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
