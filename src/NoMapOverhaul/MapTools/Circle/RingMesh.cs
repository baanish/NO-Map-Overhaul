namespace NoMapOverhaul.MapTools.Circle;

/// <summary>
/// The mesh every ring shares when the GPU draws them (the WorldRing shader in <c>unity/</c>): a chord per
/// <see cref="RingProjection.Segments"/> point, each a strip across the line in <see cref="Lanes"/> lanes, the
/// <c>EdgeProfile</c> of a flat line without a rim. A vertex holds no position the shader reads, only codes: which point
/// it sits on, which way its chord runs, and its lane. The shader places it. Chords share no vertices, so each can be
/// cut where it leaves the view on its own.
/// </summary>
public static class RingMesh
{
    /// <summary>Lanes across the line, outer edge to outer edge: the fade, the fill's edge, the fill's other edge, the fade.</summary>
    public static readonly float[] Lanes = { -2f, -1f, 1f, 2f };

    public const int VerticesPerChord = 8;
    public const int VertexCount = RingProjection.Segments * VerticesPerChord;
    public const int IndexCount = RingProjection.Segments * (4 - 1) * 6;

    /// <summary>
    /// Each vertex's codes, as the shader reads them from its first UV channel. X is the point, counted from east
    /// towards north, for a vertex whose chord runs to the next point, and -1 less the point for one whose chord runs
    /// back to the one before. Y is the lane. The last chord ends on point <see cref="RingProjection.Segments"/>, the
    /// first point again by another name, so both of its ends name the same pair of points as the shader clips it.
    /// </summary>
    public static (float Point, float Lane)[] Codes()
    {
        var codes = new (float, float)[VertexCount];
        for (var chord = 0; chord < RingProjection.Segments; chord++)
        {
            for (var lane = 0; lane < Lanes.Length; lane++)
            {
                codes[chord * VerticesPerChord + lane] = (chord, Lanes[lane]);
                codes[chord * VerticesPerChord + Lanes.Length + lane] = (-1f - (chord + 1), Lanes[lane]);
            }
        }

        return codes;
    }

    /// <summary>Two triangles between each pair of neighbouring lanes along each chord, wound as <c>StrokeGraphic</c> winds them.</summary>
    public static int[] Indices()
    {
        var indices = new int[IndexCount];
        var next = 0;
        for (var chord = 0; chord < RingProjection.Segments; chord++)
        {
            var from = chord * VerticesPerChord;
            var to = from + Lanes.Length;
            for (var lane = 0; lane < Lanes.Length - 1; lane++)
            {
                indices[next++] = from + lane;
                indices[next++] = to + lane;
                indices[next++] = to + lane + 1;
                indices[next++] = to + lane + 1;
                indices[next++] = from + lane + 1;
                indices[next++] = from + lane;
            }
        }

        return indices;
    }

    /// <summary>Whether a lane is on the fill, drawn in the ring's colour, rather than at the end of the fade, transparent.</summary>
    public static bool IsFill(float lane) => lane is > -1.5f and < 1.5f;
}
