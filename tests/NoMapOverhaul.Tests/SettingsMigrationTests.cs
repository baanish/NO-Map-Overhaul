using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>
/// Moving saved values from the 0.4.0 sections, and carrying them over from the plugin's old name, checked against a
/// real 0.4.0 settings file.
/// </summary>
internal static class SettingsMigrationTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a 0.4.0 settings file moves into the new sections", OldFileMovesIntoNewSections),
        ("a value already in the new section wins", NewValueWins),
        ("a removed setting's value is dropped from its old and new sections", RemovedSettingIsDropped),
        ("each move has its own source and target", MovesAreDistinct),
        ("the old plugin's 0.4.0 settings carry over on the first start", OldPluginFileCarriesOver),
        ("the old plugin's grouped settings carry over without the missile arrows", GroupedFileCarriesOverWithoutMissileArrows),
        ("settings already saved under the new name aren't replaced", ExistingSettingsAreNotReplaced),
    };

    private static void OldFileMovesIntoNewSections()
    {
        var before = ReadFixture();
        var after = new Dictionary<(string, string), string>(before);
        var moved = SettingsMigration.MoveSavedValues(after, (section, key) => (section, key));

        Expect(moved == SettingsMigration.Moves.Length, $"expected all {SettingsMigration.Moves.Length} settings moved, got {moved}");
        foreach (var (oldSection, key, newSection) in SettingsMigration.Moves)
        {
            Expect(!after.ContainsKey((oldSection, key)), $"expected {oldSection}/{key} removed");
            Expect(after.TryGetValue((newSection, key), out var value) && value == before[(oldSection, key)], $"expected {newSection}/{key} to keep {before[(oldSection, key)]}");
        }

        var removed = SettingsMigration.Removed.Count(before.ContainsKey);
        Expect(removed == 3, $"expected the fixture's enemy and neutral names setting and two missile arrow settings, got {removed}");
        Expect(!SettingsMigration.Removed.Any(after.ContainsKey), "expected the removed settings dropped");
        Expect(after.Count == before.Count - removed, $"expected {before.Count - removed} settings after the move, got {after.Count}");
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

    private static void RemovedSettingIsDropped()
    {
        var saved = new Dictionary<(string, string), string>
        {
            [("Airbase Names", "ShowEnemyAndNeutral")] = "true",
            [(SettingSections.MapAirbaseNames, "ShowEnemyAndNeutral")] = "true",
            [(SettingSections.MapAirbaseNames, "ShowNames")] = "true",
        };
        SettingsMigration.MoveSavedValues(saved, (section, key) => (section, key));
        Expect(saved.Count == 1 && saved.ContainsKey((SettingSections.MapAirbaseNames, "ShowNames")), "expected only ShowNames left");
    }

    private static void MovesAreDistinct()
    {
        var moves = SettingsMigration.Moves;
        Expect(moves.Select(m => (m.OldSection, m.Key)).Distinct().Count() == moves.Length, "expected no old setting listed twice");
        Expect(moves.Select(m => (m.NewSection, m.Key)).Distinct().Count() == moves.Length, "expected no two settings moved to one place");
    }

    /// <summary>The first start after the rename, with nothing saved yet under the new GUID and a 0.4.0 file under the old one.</summary>
    private static void OldPluginFileCarriesOver()
    {
        var saved = new Dictionary<(string, string), string>();
        var copied = SettingsMigration.CarryOver(saved, File.ReadLines(FixturePath), (section, key) => (section, key));
        var before = ReadFixture();
        Expect(copied == before.Count, $"expected all {before.Count} saved values copied, got {copied}");

        SettingsMigration.MoveSavedValues(saved, (section, key) => (section, key));
        Expect(!SettingsMigration.Removed.Any(saved.ContainsKey), "expected the missile arrows and other removed settings left behind");
        foreach (var (oldSection, key, newSection) in SettingsMigration.Moves)
        {
            Expect(saved.TryGetValue((newSection, key), out var value) && value == before[(oldSection, key)], $"expected {newSection}/{key} to keep {before[(oldSection, key)]}");
        }

        ExpectText(saved[("General", "Enabled")], "true");
    }

    /// <summary>A 0.5.0 build before the rename saved the new sections, missile arrows included.</summary>
    private static void GroupedFileCarriesOverWithoutMissileArrows()
    {
        var lines = new[]
        {
            "## Settings file was created by plugin Baanish UI Improvements v0.5.0",
            "[HUD / Missile Arrows]",
            "ShowArrows = false",
            "",
            "[Map / Runways]",
            "# Default value: 00D900D9",
            "RunwayColor = 11223344",
            "not a setting",
        };
        var saved = new Dictionary<(string, string), string>();
        SettingsMigration.CarryOver(saved, lines, (section, key) => (section, key));
        SettingsMigration.MoveSavedValues(saved, (section, key) => (section, key));
        Expect(saved.Count == 1, $"expected only the runway colour, got {string.Join(", ", saved.Keys)}");
        ExpectText(saved[(SettingSections.MapRunways, "RunwayColor")], "11223344");
    }

    private static void ExistingSettingsAreNotReplaced()
    {
        var saved = new Dictionary<(string, string), string> { [(SettingSections.MapRunways, "ShowRunways")] = "false" };
        var copied = SettingsMigration.CarryOver(saved, File.ReadLines(FixturePath), (section, key) => (section, key));
        Expect(copied == 0, $"expected nothing copied over saved settings, got {copied}");
        Expect(saved.Count == 1 && saved[(SettingSections.MapRunways, "ShowRunways")] == "false", "expected the saved settings unchanged");
    }

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-0.4.0.cfg");

    private static Dictionary<(string, string), string> ReadFixture() =>
        SettingsMigration.ReadSavedValues(File.ReadLines(FixturePath)).ToDictionary(v => (v.Section, v.Key), v => v.Value);
}
