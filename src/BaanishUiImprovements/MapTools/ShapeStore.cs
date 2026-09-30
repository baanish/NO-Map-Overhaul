using System;
using System.Collections.Generic;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Every drawing on the map, from every tool, with undo and redo. Each change stores a new snapshot of the shape list,
/// so undo and redo only move between snapshots, and every Add, Remove, Replace, and Clear is exactly one undo step.
/// Shapes are immutable, so snapshots share them.
/// List order is draw order: the last shape draws on top.
/// </summary>
public sealed class ShapeStore
{
    /// <summary>Undo steps kept. Older ones drop off, which also bounds the memory held by erased shapes.</summary>
    public const int HistoryDepth = 100;

    private readonly List<MapShape[]> _history = new() { Array.Empty<MapShape>() };
    private int _current;
    private int _nextId = 1;

    /// <summary>The most shapes <see cref="Add"/> accepts. Undo can still bring back more.</summary>
    public int MaxShapes { get; set; } = int.MaxValue;

    /// <summary>The most <see cref="MapShape.PointCount"/> summed over all shapes that Add and Replace accept.</summary>
    public int MaxPoints { get; set; } = int.MaxValue;

    /// <summary>The current shapes. Index it with a for loop in per-frame code: foreach over the interface allocates.</summary>
    public IReadOnlyList<MapShape> Shapes => _history[_current];

    /// <summary>Changes on every edit, undo, redo, and reset, so a renderer can tell when to redraw.</summary>
    public int Version { get; private set; }

    public bool CanUndo => _current > 0;

    public bool CanRedo => _current < _history.Count - 1;

    /// <summary>At the shape cap: <see cref="Add"/> refuses until something is erased or undone.</summary>
    public bool IsFull => _history[_current].Length >= MaxShapes;

    public int PointCount
    {
        get
        {
            var total = 0;
            foreach (var shape in _history[_current])
            {
                total += shape.PointCount;
            }

            return total;
        }
    }

    /// <summary>False when an undo, the eraser, or Clear has taken the shape out, so a tool can let go of it.</summary>
    public bool Contains(MapShape shape) => Array.IndexOf(_history[_current], shape) >= 0;

    /// <summary>False, and nothing changes, when the shape is already here or would pass a cap.</summary>
    public bool Add(MapShape shape)
    {
        var shapes = _history[_current];
        if (Array.IndexOf(shapes, shape) >= 0 || shapes.Length >= MaxShapes || PointCount + shape.PointCount > MaxPoints)
        {
            return false;
        }

        shape.Id = _nextId++;
        var next = new MapShape[shapes.Length + 1];
        shapes.CopyTo(next, 0);
        next[shapes.Length] = shape;
        Commit(next);
        return true;
    }

    public bool Remove(MapShape shape)
    {
        var shapes = _history[_current];
        var index = Array.IndexOf(shapes, shape);
        if (index < 0)
        {
            return false;
        }

        var next = new MapShape[shapes.Length - 1];
        Array.Copy(shapes, 0, next, 0, index);
        Array.Copy(shapes, index + 1, next, index, shapes.Length - index - 1);
        Commit(next);
        return true;
    }

    /// <summary>Swaps in an edited copy at the same place in the draw order, keeping the id. Growing past the point cap fails.</summary>
    public bool Replace(MapShape shape, MapShape replacement)
    {
        var shapes = _history[_current];
        var index = Array.IndexOf(shapes, shape);
        if (index < 0 || Array.IndexOf(shapes, replacement) >= 0 ||
            (replacement.PointCount > shape.PointCount && PointCount - shape.PointCount + replacement.PointCount > MaxPoints))
        {
            return false;
        }

        replacement.Id = shape.Id;
        var next = (MapShape[])shapes.Clone();
        next[index] = replacement;
        Commit(next);
        return true;
    }

    /// <summary>Removes every shape as one undo step. Nothing to clear adds no step.</summary>
    public void Clear()
    {
        if (_history[_current].Length > 0)
        {
            Commit(Array.Empty<MapShape>());
        }
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        _current--;
        Version++;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        _current++;
        Version++;
        return true;
    }

    /// <summary>Leaving the mission: no shapes and no history, so nothing can be undone back into the next one.</summary>
    public void Reset()
    {
        _history.Clear();
        _history.Add(Array.Empty<MapShape>());
        _current = 0;
        Version++;
    }

    private void Commit(MapShape[] next)
    {
        _history.RemoveRange(_current + 1, _history.Count - _current - 1);
        _history.Add(next);
        if (_history.Count > HistoryDepth + 1)
        {
            _history.RemoveAt(0);
        }

        _current = _history.Count - 1;
        Version++;
    }
}
