using System;
using System.Collections.Generic;
using NoMapOverhaul.Drawing;
using UnityEngine;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Every map note, fixed or on a unit, with its dark rim, drawn from a few shared meshes rather than a TextMeshPro and
/// a <see cref="TextRim"/> each: Unity culls every graphic under the map's mask on every frame, and sorts and batches
/// each. Notes are typeset by <see cref="PlateText"/>, and each is drawn as its own text would be: the rim, the HUD
/// font's glyphs copied around the face one unit each way, then the face, then the glyphs of each fallback font, last
/// used first, as TextMeshPro orders its sub-meshes. <see cref="NoteRuns"/> sorts those pieces into one
/// <see cref="GlyphBatch"/> per material wherever no overlap tells, so notes still cover each other as they were
/// made. It sits after the plated text, so notes draw over every plate. The meshes are built again only when a note,
/// the set of notes, the zoom, or the minimap's upright step changed, and each note keeps its pieces as last built,
/// so a note moving with its unit doesn't build every other note again.
/// </summary>
internal sealed class NoteText
{
    /// <summary><see cref="GlyphBatch"/>'s cap, past which a run's quads would be dropped.</summary>
    private const int RunVertices = 64000;

    /// <summary>A comparer, not a <see cref="Comparison{T}"/>: Mono's List.Sort wraps a Comparison in a new object each call.</summary>
    private static readonly IComparer<Note> ByOrder = new OrderComparer();

    private readonly RectTransform _root;

    /// <summary>Drawn in index order, a run's material set as it's used; those past the last draw's runs are off.</summary>
    private readonly List<GlyphBatch> _runs = new();

    private readonly List<Material> _materialKeys = new();
    private readonly NoteRuns _planner = new(RunVertices);
    private List<Note> _notes = new();
    private List<Note> _applied = new();
    private int _nextOrder;

    /// <param name="labels">The label canvas; the runs go at its end, so make this after <see cref="PlateText"/>.</param>
    public NoteText(RectTransform labels) => _root = NewRect("NoteText", labels);

    /// <summary>A new note, placed among the others: later notes draw over earlier ones.</summary>
    public Note NewNote() => new(_nextOrder++);

    public void Clear() => _notes.Clear();

    /// <summary>A note to draw, its glyphs' middle at <paramref name="center"/> in the upright frame's icon units.</summary>
    public void Add(Note note, PlateText.Glyphs glyphs, Color32 face, Color32 rim, Vector2 center)
    {
        note.Wanted = new NoteLook(glyphs, glyphs.Version, face, rim, center);
        _notes.Add(note);
    }

    /// <summary>
    /// Builds the meshes for the notes added since <see cref="Clear"/>, unless they're the notes the meshes already show.
    /// The meshes are built in the upright frame and the root turns them into the map's by <paramref name="toMap"/>,
    /// so each step of the heading-up minimap turns one transform instead of rebuilding every note.
    /// </summary>
    public void Apply(Quaternion toMap, float inverseScale)
    {
        if (_root.localRotation != toMap)
        {
            _root.localRotation = toMap;
        }

        toMap = Quaternion.identity;
        _notes.Sort(ByOrder);
        if (Unchanged(toMap, inverseScale))
        {
            return;
        }

        _planner.Clear();
        foreach (var run in _runs)
        {
            run.Clear();
        }

        foreach (var note in _notes)
        {
            if (!note.Current(toMap, inverseScale))
            {
                Build(note, toMap, inverseScale);
            }

            foreach (var piece in note.Pieces)
            {
                Run(piece).AddRange(note.Vertices, piece.First, piece.Count, piece.Min, piece.Max);
            }
        }

        for (var i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            run.Apply();
            if (i >= _planner.Count && run.enabled)
            {
                run.enabled = false; // an empty graphic left on is still culled every frame
            }
        }

        (_notes, _applied) = (_applied, _notes);
    }

    private bool Unchanged(Quaternion toMap, float inverseScale)
    {
        if (_notes.Count != _applied.Count)
        {
            return false;
        }

        for (var i = 0; i < _notes.Count; i++)
        {
            if (!ReferenceEquals(_notes[i], _applied[i]) || !_notes[i].Current(toMap, inverseScale))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The note's pieces in the map's frame, in draw order: the HUD font's glyphs rim first, then each fallback font's.</summary>
    private static void Build(Note note, Quaternion toMap, float inverseScale)
    {
        var look = note.Wanted;
        var glyphs = look.Glyphs;
        var at = (Vector3)look.Center;
        note.Vertices.Clear();
        note.Pieces.Clear();
        var highest = 0;
        foreach (var index in glyphs.MaterialIndices)
        {
            highest = Mathf.Max(highest, index);
        }

        var piece = new PieceBuilder(note);
        foreach (var offset in TextRim.Offsets)
        {
            piece.AddQuads(glyphs, 0, at + (Vector3)offset, look.Rim, toMap, inverseScale);
        }

        piece.AddQuads(glyphs, 0, at, look.Face, toMap, inverseScale);
        piece.End();
        for (var index = highest; index > 0; index--)
        {
            piece = new PieceBuilder(note);
            piece.AddQuads(glyphs, index, at, look.Face, toMap, inverseScale);
            piece.End();
        }

        note.Built = look;
        note.BuiltToMap = toMap;
        note.BuiltInverseScale = inverseScale;
    }

    /// <summary>The run for a piece, made and switched on as needed.</summary>
    private GlyphBatch Run(Piece piece)
    {
        var index = _planner.Pick(MaterialKey(piece.Material), piece.Min.x, piece.Min.y, piece.Max.x, piece.Max.y, piece.Count);
        while (_runs.Count <= index)
        {
            var created = NewRect("Glyphs", _root).gameObject.AddComponent<GlyphBatch>();
            created.raycastTarget = false;
            _runs.Add(created);
        }

        var run = _runs[index];
        if (!ReferenceEquals(run.material, piece.Material))
        {
            run.material = piece.Material;
        }

        if (!run.enabled)
        {
            run.enabled = true;
        }

        return run;
    }

    private int MaterialKey(Material material)
    {
        for (var i = 0; i < _materialKeys.Count; i++)
        {
            if (ReferenceEquals(_materialKeys[i], material))
            {
                return i;
            }
        }

        _materialKeys.Add(material);
        return _materialKeys.Count - 1;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>One label's note: its place among the notes, what it last asked to show, and its pieces as last built.</summary>
    public sealed class Note
    {
        internal readonly int Order;
        internal readonly List<UIVertex> Vertices = new();
        internal readonly List<Piece> Pieces = new();
        internal NoteLook Wanted;
        internal NoteLook Built;
        internal Quaternion BuiltToMap;
        internal float BuiltInverseScale;

        internal Note(int order) => Order = order;

        internal bool Current(Quaternion toMap, float inverseScale) =>
            Built.Same(Wanted) && BuiltToMap.Equals(toMap) && BuiltInverseScale == inverseScale;
    }

    /// <summary>One note's glyphs in one material, its rim included, in the map's frame.</summary>
    internal readonly struct Piece
    {
        public readonly Material Material;
        public readonly int First;
        public readonly int Count;
        public readonly Vector2 Min;
        public readonly Vector2 Max;

        public Piece(Material material, int first, int count, Vector2 min, Vector2 max)
        {
            Material = material;
            First = first;
            Count = count;
            Min = min;
            Max = max;
        }
    }

    /// <summary>A note's glyphs as typeset, its colours, and where it stands in the upright frame.</summary>
    internal readonly struct NoteLook
    {
        public readonly PlateText.Glyphs Glyphs;
        public readonly int Version;
        public readonly Color32 Face;
        public readonly Color32 Rim;
        public readonly Vector2 Center;

        public NoteLook(PlateText.Glyphs glyphs, int version, Color32 face, Color32 rim, Vector2 center)
        {
            Glyphs = glyphs;
            Version = version;
            Face = face;
            Rim = rim;
            Center = center;
        }

        public bool Same(in NoteLook other) =>
            Glyphs != null && ReferenceEquals(Glyphs, other.Glyphs) && Version == other.Version && SameColor(Face, other.Face) &&
            SameColor(Rim, other.Rim) && Center.Equals(other.Center);

        private static bool SameColor(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    /// <summary>Adds one piece's quads to a note's vertices, and its material and map-frame bounds to its pieces.</summary>
    private struct PieceBuilder
    {
        private readonly Note _note;
        private readonly int _first;
        private Material? _material;
        private Vector2 _min;
        private Vector2 _max;

        public PieceBuilder(Note note)
        {
            _note = note;
            _first = note.Vertices.Count;
            _material = null;
            _min = new Vector2(float.MaxValue, float.MaxValue);
            _max = new Vector2(float.MinValue, float.MinValue);
        }

        public void AddQuads(PlateText.Glyphs glyphs, int materialIndex, Vector3 at, Color32 color, Quaternion toMap, float inverseScale)
        {
            // TextMeshPro draws a sprite white at the text's opacity, unless told to tint every sprite.
            var sprite = new Color32(255, 255, 255, color.a);
            for (var quad = 0; quad < glyphs.MaterialIndices.Count; quad++)
            {
                if (glyphs.MaterialIndices[quad] != materialIndex)
                {
                    continue;
                }

                _material ??= glyphs.Materials[quad];
                var quadColor = glyphs.Sprites[quad] ? sprite : color;
                for (var corner = 0; corner < 4; corner++)
                {
                    var vertex = glyphs.Vertices[quad * 4 + corner];
                    vertex.position = toMap * ((at + vertex.position) * inverseScale);
                    vertex.color = quadColor;
                    Vector2 position = vertex.position;
                    _min = Vector2.Min(_min, position);
                    _max = Vector2.Max(_max, position);
                    _note.Vertices.Add(vertex);
                }
            }
        }

        public void End()
        {
            var count = _note.Vertices.Count - _first;
            if (count > 0)
            {
                _note.Pieces.Add(new Piece(_material!, _first, count, _min, _max));
            }
        }
    }

    private sealed class OrderComparer : IComparer<Note>
    {
        public int Compare(Note a, Note b) => a.Order.CompareTo(b.Order);
    }
}
