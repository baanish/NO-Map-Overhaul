using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>Moving saved values from the 0.4.0 sections, checked against a real 0.4.0 settings file.</summary>
internal static class SettingsMigrationTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a 0.4.0 settings file moves into the new sections", OldFileMovesIntoNewSections),
        ("a value already in the new section wins", NewValueWins),
        ("each move has its own source and target", MovesAreDistinct),
    };

    private static void OldFileMovesIntoNewSections()
    {
        var before = ReadCfg(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-0.4.0.cfg"));
        var after = new Dictionary<(string, string), string>(before);
        var moved = SettingsMigration.MoveSavedValues(after, (section, key) => (section, key));

        Expect(moved == SettingsMigration.Moves.Length, $"expected all {SettingsMigration.Moves.Length} settings moved, got {moved}");
        foreach (var (oldSection, key, newSection) in SettingsMigration.Moves)
        {
            Expect(!after.ContainsKey((oldSection, key)), $"expected {oldSection}/{key} removed");
            Expect(after.TryGetValue((newSection, key), out var value) && value == before[(oldSection, key)], $"expected {newSection}/{key} to keep {before[(oldSection, key)]}");
        }

        Expect(after.Count == before.Count, $"expected {before.Count} settings after the move, got {after.Count}");
        var left = after.Keys.Where(k => !SettingsMigration.Moves.Any(m => m.NewSection == k.Item1 && m.Key == k.Item2)).ToList();
        Expect(left.SequenceEqual(new[] { ("General", "Enabled") }), $"expected only General/Enabled left in place, got {string.Join(", ", left)}");
        ExpectText(after[(SettingSections.MapAirbaseBoundary, "FillOpacity")], "0.009577462");
        ExpectText(after[(SettingSections.HudRunwayCallout, "IncludeAirbaseName")], "true");
    }

    private static void NewValueWins()
    {
        var saved = new Dictionary<(string, string), string>
        {
            [("Map", "ShowRunways")] = "false",
            [(SettingSections.MapRunways, "ShowRunways")] = "true",
        };
        var moved = SettingsMigration.MoveSavedValues(saved, (section, key) => (section, key));
        Expect(moved == 0, $"expected nothing moved, got {moved}");
        Expect(saved.Count == 1 && saved[(SettingSections.MapRunways, "ShowRunways")] == "true", "expected only the new value, unchanged");
    }

    private static void MovesAreDistinct()
    {
        var moves = SettingsMigration.Moves;
        Expect(moves.Select(m => (m.OldSection, m.Key)).Distinct().Count() == moves.Length, "expected no old setting listed twice");
        Expect(moves.Select(m => (m.NewSection, m.Key)).Distinct().Count() == moves.Length, "expected no two settings moved to one place");
    }

    /// <summary>Sections and "key = value" lines, as BepInEx reads them; comments and blank lines skipped.</summary>
    private static Dictionary<(string, string), string> ReadCfg(string path)
    {
        var values = new Dictionary<(string, string), string>();
        var section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                values[(section, parts[0].Trim())] = parts[1].Trim();
            }
        }

        return values;
    }
}
