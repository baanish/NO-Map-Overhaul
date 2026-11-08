using System.Collections.Generic;
using System.Numerics;

namespace NoMapOverhaul.MapTools;

/// <summary>Passes draw calls through with one colour, to draw a stored shape again: the eraser's highlight, the recolour tool's preview.</summary>
internal sealed class RecolorCanvas : IMapCanvas
{
    private IMapCanvas _inner = null!;
    private ShapeColor _color;

    public DistanceUnit Units => _inner.Units;
    public float MetersPerIconUnit => _inner.MetersPerIconUnit;
    public float TextSize => _inner.TextSize;
    public MapPoint? OwnAircraft => _inner.OwnAircraft;

    public RecolorCanvas Over(IMapCanvas inner, ShapeColor color)
    {
        _inner = inner;
        _color = color;
        return this;
    }

    public bool TryResolve(MapPoint point, out Vector2 position) => _inner.TryResolve(point, out position);
    public bool TryResolveWorld(MapPoint point, out Vector3 position) => _inner.TryResolveWorld(point, out position);
    public void Line(Vector2 from, Vector2 to, ShapeColor color) => _inner.Line(from, to, _color);
    public void Arrow(Vector2 from, Vector2 to, ShapeColor color) => _inner.Arrow(from, to, _color);
    public void Polyline(IReadOnlyList<Vector2> points, ShapeColor color) => _inner.Polyline(points, _color);
    public void Circle(Vector2 center, float radius, ShapeColor color) => _inner.Circle(center, radius, _color);
    public void Marker(Vector2 position, ShapeColor color) => _inner.Marker(position, _color);

    public void Label(LabelAnchor anchor, string text, ShapeColor color) => _inner.Label(anchor, text, _color);
}
