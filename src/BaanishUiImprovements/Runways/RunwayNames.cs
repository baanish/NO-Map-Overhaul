using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BaanishUiImprovements.Runways;

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
