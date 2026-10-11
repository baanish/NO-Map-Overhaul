using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoMapOverhaul.Runways;

/// <summary>
/// Runway and airbase names as a pilot reads them. Airbase names are shortened for radio calls
/// ("NBSCLI RWY 27"): known fields use the abbreviations the squadron
/// already flies with; anything else is shortened the same way: generic "Airbase/Airport/Airstrip" words
/// are dropped, common words get their aviation abbreviation, and other words keep their first letter
/// plus consonants (Ashwood becomes ASHWD).
/// </summary>
public static class RunwayNames
{
    private static readonly Dictionary<string, string> KnownAirbases = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["North Boscali Airbase"] = "NBSCLI",
        ["Harmony Sands Airstrip"] = "HMNY SNDS",
        ["Vigil Cay Naval Airbase"] = "VGL CAY",
        ["Broken Atoll Airbase"] = "BRKN ATL",
        ["South Boscali General Aviation"] = "SBGA",
        ["Hogshead Airbase"] = "HGSHD",
        ["Agrapol Airbase"] = "AGRPL",
        ["Feldspar International Airport"] = "FLDSPR INT",
        ["Maris Airport"] = "MARIS",
        ["Cliffline Airbase"] = "CLFFLNE",
        ["Sandrift Airbase"] = "SNDRFT",
        ["Ashwood Airbase"] = "ASHWD AB",
        ["Ashwood Auxiliary Airstrip"] = "ASHWD AUX",
    };

    /// <summary>
    /// Runway ends whose painted number differs from the heading number, keyed by airbase and heading number.
    /// The paint lives in the level textures, not in data, so each entry comes from a check in game.
    /// </summary>
    private static readonly Dictionary<(string Airbase, string HeadingNumber), string> PaintedNumbers = new()
    {
        // Feldspar's crossing runway on Ignus points at 330.0 degrees.
        [("Feldspar International Airport", "33")] = "34",
        [("Feldspar International Airport", "15")] = "16",
    };

    private static readonly HashSet<string> DroppedWords = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "Airbase", "Airport", "Airstrip", "Airfield", "Naval",
    };

    private static readonly Dictionary<string, string> WordAbbreviations = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["International"] = "INT",
        ["Auxiliary"] = "AUX",
        ["Highway"] = "HWY",
        ["Heliport"] = "HELI",
        ["North"] = "N",
        ["South"] = "S",
        ["East"] = "E",
        ["West"] = "W",
    };

    /// <summary>Runways within 5 degrees of each other's heading are parallel.</summary>
    private static readonly float ParallelSine = (float)System.Math.Sin(5.0 * System.Math.PI / 180.0);

    /// <summary>
    /// Parallel runways less than this apart side to side are one line: the stock game lays a takeoff-only runway over a
    /// landing one (Harmony Sands' "Runway14L Short"). Real parallel runways are hundreds of meters apart.
    /// </summary>
    private const float SameLineMeters = 100f;

    /// <summary>
    /// The letter a pilot adds to the number of a parallel runway, landing at the Start of <paramref name="runways"/>[<paramref name="index"/>]:
    /// "L", "C" or "R" for where its line lies among the parallel lines, seen flying from Start to End. Empty for a
    /// runway with nothing alongside it, and for more than three lines, which have no standard letters. Landing at the
    /// End sees the lines the other way round: <see cref="Mirrored"/>.
    /// </summary>
    public static string ParallelSide(IReadOnlyList<RunwayLine> runways, int index)
    {
        var runway = runways[index];
        var axis = System.Numerics.Vector2.Normalize(runway.End - runway.Start);
        var left = new System.Numerics.Vector2(-axis.Y, axis.X);
        var offsets = new List<float>(runways.Count) { 0f }; // each parallel runway's offset to the left of this one's centerline
        for (var i = 0; i < runways.Count; i++)
        {
            var other = runways[i];
            var otherAxis = System.Numerics.Vector2.Normalize(other.End - other.Start);
            if (i != index && System.Math.Abs(axis.X * otherAxis.Y - axis.Y * otherAxis.X) <= ParallelSine)
            {
                offsets.Add(System.Numerics.Vector2.Dot((other.Start + other.End) * 0.5f - runway.Start, left));
            }
        }

        // Sorted, so the lines don't depend on the order a mission lists its runways in: a new line starts at each gap of
        // SameLineMeters or more, and a gap above this runway's 0 puts a line to its left.
        offsets.Sort();
        var lines = 1;
        var linesLeft = 0;
        for (var i = 1; i < offsets.Count; i++)
        {
            if (offsets[i] - offsets[i - 1] >= SameLineMeters)
            {
                lines++;
                linesLeft += offsets[i - 1] >= 0f ? 1 : 0;
            }
        }

        return lines switch
        {
            2 => linesLeft == 0 ? "L" : "R",
            3 => linesLeft == 0 ? "L" : linesLeft == 1 ? "C" : "R",
            _ => string.Empty,
        };
    }

    /// <summary>The same runway's letter from its other end: left and right swap, center stays.</summary>
    public static string Mirrored(string side) => side == "L" ? "R" : side == "R" ? "L" : side;

    /// <summary>The number to show for one runway end: a known paint override, otherwise the heading number itself.</summary>
    public static string Painted(string? airbaseDisplayName, string headingNumber) =>
        airbaseDisplayName != null && PaintedNumbers.TryGetValue((airbaseDisplayName, headingNumber), out var painted)
            ? painted
            : headingNumber;

    /// <summary>"RWY 27", or "NBSCLI RWY 27" when an airbase name is given.</summary>
    public static string Callout(string endNumber, string? airbaseDisplayName) =>
        string.IsNullOrWhiteSpace(airbaseDisplayName)
            ? "RWY " + endNumber
            : AbbreviateAirbase(airbaseDisplayName!) + " RWY " + endNumber;

    public static string AbbreviateAirbase(string displayName)
    {
        var name = displayName.Trim();
        if (KnownAirbases.TryGetValue(name, out var known))
        {
            return known;
        }

        var words = name.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        var kept = words.Where(word => !DroppedWords.Contains(word)).ToArray();
        return string.Join(" ", (kept.Length > 0 ? kept : words).Select(AbbreviateWord));
    }

    private static string AbbreviateWord(string word)
    {
        if (WordAbbreviations.TryGetValue(word, out var abbreviation))
        {
            return abbreviation;
        }

        var result = new StringBuilder(word.Length);
        for (var i = 0; i < word.Length; i++)
        {
            var c = char.ToUpperInvariant(word[i]);
            if (i == 0 || "AEIOU".IndexOf(c) < 0)
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
