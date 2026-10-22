using System.Collections.Generic;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Sorts the pieces of map notes, in draw order, into runs that each draw in one material as one mesh, without
/// changing what any overlap looks like. A piece is one note's glyphs in one font material, its rim included. It joins
/// the last run in its material unless that run is full or a piece in a later run overlaps it: a joined piece then
/// moves ahead only of pieces it doesn't touch. With only the HUD font every piece joins the one run, and no box is
/// tested.
/// </summary>
internal sealed class NoteRuns
{
    private readonly int _maxVertices;
    private readonly List<Run> _runs = new();
    private int _count;

    /// <param name="maxVertices">The most vertices one run's mesh may hold.</param>
    public NoteRuns(int maxVertices) => _maxVertices = maxVertices;

    /// <summary>How many runs the pieces since <see cref="Clear"/> use.</summary>
    public int Count => _count;

    public void Clear()
    {
        for (var i = 0; i < _count; i++)
        {
            _runs[i].Boxes.Clear();
        }

        _count = 0;
    }

    /// <summary>
    /// The run the next piece goes in, given its material, its bounds, and its vertex count. A new run always takes
    /// the next index.
    /// </summary>
    public int Pick(int materialKey, float minX, float minY, float maxX, float maxY, int vertices)
    {
        var box = new Box(minX, minY, maxX, maxY);
        for (var i = _count - 1; i >= 0; i--)
        {
            var run = _runs[i];
            if (run.MaterialKey != materialKey)
            {
                continue;
            }

            if (run.Vertices + vertices <= _maxVertices && !OverlapsAfter(i, box))
            {
                run.Vertices += vertices;
                run.Boxes.Add(box);
                return i;
            }

            break;
        }

        if (_count == _runs.Count)
        {
            _runs.Add(new Run());
        }

        var opened = _runs[_count];
        opened.MaterialKey = materialKey;
        opened.Vertices = vertices;
        opened.Boxes.Add(box);
        return _count++;
    }

    private bool OverlapsAfter(int run, Box box)
    {
        for (var i = run + 1; i < _count; i++)
        {
            foreach (var other in _runs[i].Boxes)
            {
                // Touching counts: two glyphs sharing an edge still blend where they meet.
                if (box.MinX <= other.MaxX && other.MinX <= box.MaxX && box.MinY <= other.MaxY && other.MinY <= box.MaxY)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private readonly struct Box
    {
        public readonly float MinX;
        public readonly float MinY;
        public readonly float MaxX;
        public readonly float MaxY;

        public Box(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }
    }

    private sealed class Run
    {
        public int MaterialKey;
        public int Vertices;
        public readonly List<Box> Boxes = new();
    }
}
