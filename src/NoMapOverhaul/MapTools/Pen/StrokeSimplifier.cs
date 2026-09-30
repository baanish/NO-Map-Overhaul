using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoMapOverhaul.MapTools.Pen;

/// <summary>Douglas-Peucker simplification of a freehand stroke, so a long stroke keeps its shape with far fewer points.</summary>
public static class StrokeSimplifier
{
    /// <summary>
    /// Drops, in place, every point the line through its kept neighbours passes within <paramref name="tolerance"/> of.
    /// The ends always stay. Distances go to the segment rather than the infinite line, so a stroke that doubles back
    /// keeps its turn. Iterative, so a stroke of thousands of points can't overflow the stack.
    /// </summary>
    public static void Simplify(List<Vector2> points, float tolerance)
    {
        if (points.Count < 3)
        {
            return;
        }

        var keep = new bool[points.Count];
        keep[0] = keep[points.Count - 1] = true;
        var spans = new Stack<(int First, int Last)>();
        spans.Push((0, points.Count - 1));
        var toleranceSquared = tolerance * tolerance;
        while (spans.Count > 0)
        {
            var (first, last) = spans.Pop();
            var farthest = -1;
            var farthestDistance = toleranceSquared;
            for (var i = first + 1; i < last; i++)
            {
                var distance = SegmentDistanceSquared(points[i], points[first], points[last]);
                if (distance > farthestDistance)
                {
                    farthest = i;
                    farthestDistance = distance;
                }
            }

            if (farthest >= 0)
            {
                keep[farthest] = true;
                spans.Push((first, farthest));
                spans.Push((farthest, last));
            }
        }

        var kept = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                points[kept++] = points[i];
            }
        }

        points.RemoveRange(kept, points.Count - kept);
    }

    private static float SegmentDistanceSquared(Vector2 point, Vector2 from, Vector2 to)
    {
        var along = to - from;
        var lengthSquared = along.LengthSquared();
        var t = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(point - from, along) / lengthSquared, 0f, 1f) : 0f;
        return Vector2.DistanceSquared(point, from + along * t);
    }
}
