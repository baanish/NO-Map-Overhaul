using NoMapOverhaul.MapTools.Circle;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>
/// The mesh the WorldRing shader places. The shader decodes each vertex's point and chord as these tests do, so a
/// layout that drifts from it shows here rather than as a broken ring in game.
/// </summary>
internal static class RingMeshTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("both ends of each ring chord decode to the same pair of points", ChordEndsDecodeToTheirChord),
        ("each ring triangle joins neighbouring lanes across both ends of one chord", TrianglesStayInTheirChord),
    };

    /// <summary>The shader's decoding: the vertex's point, and the lower-numbered point of its chord.</summary>
    private static (int Point, int First) Decode(float code)
    {
        var toNext = code >= 0 ? 1 : 0;
        var point = code >= 0 ? (int)code : (int)(-code - 1);
        return (point, point - 1 + toNext);
    }

    private static void ChordEndsDecodeToTheirChord()
    {
        var codes = RingMesh.Codes();
        Expect(codes.Length == RingMesh.VertexCount, $"expected {RingMesh.VertexCount} vertices, got {codes.Length}");
        for (var chord = 0; chord < RingProjection.Segments; chord++)
        {
            for (var vertex = 0; vertex < RingMesh.VerticesPerChord; vertex++)
            {
                var (point, lane) = codes[chord * RingMesh.VerticesPerChord + vertex];
                var (decoded, first) = Decode(point);
                var end = vertex < RingMesh.Lanes.Length ? chord : chord + 1;
                Expect(decoded == end && first == chord,
                    $"chord {chord} vertex {vertex}: expected point {end} of chord {chord}, decoded point {decoded} of chord {first}");
                Expect(lane == RingMesh.Lanes[vertex % RingMesh.Lanes.Length], $"chord {chord} vertex {vertex}: lane {lane} out of order");
            }
        }

        Expect(RingMesh.IsFill(-1f) && RingMesh.IsFill(1f) && !RingMesh.IsFill(-2f) && !RingMesh.IsFill(2f),
            "expected the inner lanes filled and the outer ones clear");
    }

    private static void TrianglesStayInTheirChord()
    {
        var indices = RingMesh.Indices();
        Expect(indices.Length == RingMesh.IndexCount, $"expected {RingMesh.IndexCount} indices, got {indices.Length}");
        var lanes = RingMesh.Lanes.Length;
        for (var triangle = 0; triangle < indices.Length; triangle += 3)
        {
            var corners = new[] { indices[triangle], indices[triangle + 1], indices[triangle + 2] };
            var chord = corners[0] / RingMesh.VerticesPerChord;
            Expect(corners.All(index => index / RingMesh.VerticesPerChord == chord), $"triangle {triangle / 3} spans two chords: {string.Join(", ", corners)}");
            Expect(corners.Distinct().Count() == 3, $"triangle {triangle / 3} is degenerate: {string.Join(", ", corners)}");
            var ends = corners.Select(index => index % RingMesh.VerticesPerChord / lanes).Distinct().Count();
            var laneSpan = corners.Max(index => index % lanes) - corners.Min(index => index % lanes);
            Expect(ends == 2 && laneSpan == 1, $"triangle {triangle / 3} should join neighbouring lanes across both ends: {string.Join(", ", corners)}");
        }
    }
}
