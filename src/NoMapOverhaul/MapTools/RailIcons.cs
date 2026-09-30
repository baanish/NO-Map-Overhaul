using NoMapOverhaul.Drawing;
using UnityEngine;

namespace NoMapOverhaul.MapTools;

/// <summary>The line icons on the map tools rail.</summary>
internal enum RailIcon
{
    Tools,
    Waypoint,
    Pen,
    Text,
    BearingRange,
    Circle,
    Eraser,
    Undo,
    Redo,
    Clear,
}

/// <summary>
/// Draws the rail's icons as <see cref="StrokeGraphic"/> lines: a 24 px box with 2 px strokes, sharp joins, and square
/// ends, the outlines taken from the design mock's SVG paths (Y down, origin at the box's top-left). Filled dots are
/// rings as thick as their radius. An icon is rebuilt only when its colour changes, so the few allocations here don't
/// recur per frame.
/// </summary>
internal static class RailIcons
{
    private const float BoxCenter = 12f;
    private const float StrokeWidth = 2f;
    private const int ArcSteps = 12;
    private const int RingSteps = 32;
    private const int CurveSteps = 10;

    /// <param name="center">The box's centre in the graphic's parent space.</param>
    /// <param name="scale">Parent units per icon pixel.</param>
    /// <param name="feather">Edge fade in parent units, about one screen pixel.</param>
    public static void Draw(StrokeGraphic graphic, RailIcon icon, Vector2 center, float scale, float feather, Color32 color)
    {
        var pen = new Pen(graphic, center, scale, feather, color);
        graphic.Clear();
        switch (icon)
        {
            case RailIcon.Tools:
                pen.Closed(5, 20, 5, 5, 20, 20);
                pen.Closed(8.5f, 16.5f, 8.5f, 13.3f, 11.7f, 16.5f);
                break;
            case RailIcon.Waypoint:
                pen.Closed(6, 13.5f, 9.5f, 17, 6, 20.5f, 2.5f, 17);
                pen.Closed(18, 3.5f, 21.5f, 7, 18, 10.5f, 14.5f, 7);
                pen.Open(8.5f, 14.5f, 15.5f, 9.5f);
                break;
            case RailIcon.Pen:
                pen.Curve(new Vector2(3, 18), new Vector2(6, 9), new Vector2(9, 7), new Vector2(11, 12), first: true);
                pen.Curve(new Vector2(11, 12), new Vector2(13, 17), new Vector2(15, 18), new Vector2(21, 5), first: false);
                pen.End(closed: false);
                break;
            case RailIcon.Text:
                pen.Open(5, 5, 19, 5);
                pen.Open(12, 5, 12, 20);
                pen.Open(9, 20, 15, 20);
                break;
            case RailIcon.BearingRange:
                pen.Dot(5, 19, 1.6f);
                pen.Open(5, 19, 18.5f, 5.5f);
                pen.Open(11.5f, 5.5f, 18.5f, 5.5f, 18.5f, 12.5f);
                break;
            case RailIcon.Circle:
                pen.Ring(12, 12, 8.5f);
                pen.Dot(12, 12, 1.4f);
                break;
            case RailIcon.Eraser:
                pen.Closed(4, 15, 12, 7, 19, 14, 11, 22);
                pen.Open(8, 11, 15, 18);
                pen.Open(13, 21, 21, 21);
                break;
            case RailIcon.Undo:
                pen.Point(8, 9);
                pen.Arc(15, 14, 5, -90f, 90f);
                pen.Point(9, 19);
                pen.End(closed: false);
                pen.Open(11.5f, 5.5f, 8, 9, 11.5f, 12.5f);
                break;
            case RailIcon.Redo:
                pen.Point(16, 9);
                pen.Arc(9, 14, 5, -90f, -270f);
                pen.Point(15, 19);
                pen.End(closed: false);
                pen.Open(12.5f, 5.5f, 16, 9, 12.5f, 12.5f);
                break;
            case RailIcon.Clear:
                pen.Closed(4, 4, 20, 4, 20, 20, 4, 20);
                pen.Open(8.5f, 8.5f, 15.5f, 15.5f);
                pen.Open(15.5f, 8.5f, 8.5f, 15.5f);
                break;
        }

        graphic.Apply();
    }

    /// <summary>
    /// The strip's warning sign: a solid triangle with an exclamation mark cut out in <paramref name="cutout"/>, 16 px
    /// wide and 15 tall, its top point at <paramref name="top"/> in parent space. The triangle is a ring inset by half
    /// its inradius and as thick as that, so its outer edge is anti-aliased like every other line.
    /// </summary>
    public static void DrawWarning(StrokeGraphic graphic, Vector2 top, float scale, float feather, Color32 fill, Color32 cutout)
    {
        const float inradius = 4.8f; // area 120 over half the perimeter, 25
        const float inset = inradius * 0.5f;
        graphic.Clear();
        var incenter = new Vector2(0f, -15f + inradius);
        foreach (var corner in new[] { Vector2.zero, new Vector2(8f, -15f), new Vector2(-8f, -15f) })
        {
            graphic.AddPoint(top + (corner + (incenter - corner) * (inset / inradius)) * scale);
        }

        graphic.EndStroke(true, inset * scale, fill, 0f, fill, feather);
        var half = StrokeWidth * 0.5f * scale;
        graphic.AddPoint(top + new Vector2(0f, -5f) * scale);
        graphic.AddPoint(top + new Vector2(0f, -10f) * scale);
        graphic.EndStroke(false, half, cutout, 0f, cutout, feather);
        graphic.AddPoint(top + new Vector2(0f, -11.5f) * scale);
        graphic.AddPoint(top + new Vector2(0f, -13.5f) * scale);
        graphic.EndStroke(false, half, cutout, 0f, cutout, feather);
        graphic.Apply();
    }

    /// <summary>Adds icon-box points to a stroke graphic and ends the strokes they make.</summary>
    private readonly struct Pen
    {
        private readonly StrokeGraphic _graphic;
        private readonly Vector2 _center;
        private readonly float _scale;
        private readonly float _feather;
        private readonly Color32 _color;

        public Pen(StrokeGraphic graphic, Vector2 center, float scale, float feather, Color32 color)
        {
            _graphic = graphic;
            _center = center;
            _scale = scale;
            _feather = feather;
            _color = color;
        }

        public void Point(float x, float y) => _graphic.AddPoint(_center + new Vector2(x - BoxCenter, BoxCenter - y) * _scale);

        public void End(bool closed) => End(closed, StrokeWidth * 0.5f);

        public void Open(params float[] xy) => Poly(false, xy);

        public void Closed(params float[] xy) => Poly(true, xy);

        /// <summary>From <paramref name="from"/> to <paramref name="to"/> degrees, Y down, so positive turns clockwise on screen.</summary>
        public void Arc(float cx, float cy, float radius, float from, float to, int steps = ArcSteps)
        {
            for (var i = 0; i <= steps; i++)
            {
                var angle = Mathf.Lerp(from, to, (float)i / steps) * Mathf.Deg2Rad;
                Point(cx + radius * Mathf.Cos(angle), cy + radius * Mathf.Sin(angle));
            }
        }

        public void Ring(float cx, float cy, float radius)
        {
            Arc(cx, cy, radius, 0f, 360f, RingSteps);
            End(closed: true);
        }

        /// <summary>A filled disk: a ring at half the radius, a radius thick.</summary>
        public void Dot(float cx, float cy, float radius)
        {
            Arc(cx, cy, radius * 0.5f, 0f, 360f);
            End(true, radius * 0.5f);
        }

        /// <summary>A cubic Bezier; the first of a chain adds its start point too.</summary>
        public void Curve(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1, bool first)
        {
            for (var i = first ? 0 : 1; i <= CurveSteps; i++)
            {
                var t = (float)i / CurveSteps;
                var u = 1f - t;
                var point = u * u * u * p0 + 3f * u * u * t * c0 + 3f * u * t * t * c1 + t * t * t * p1;
                Point(point.x, point.y);
            }
        }

        private void Poly(bool closed, float[] xy)
        {
            for (var i = 0; i + 1 < xy.Length; i += 2)
            {
                Point(xy[i], xy[i + 1]);
            }

            End(closed);
        }

        private void End(bool closed, float halfWidth) =>
            _graphic.EndStroke(closed, halfWidth * _scale, _color, 0f, _color, _feather);
    }
}
