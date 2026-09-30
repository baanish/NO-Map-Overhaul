using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>Moving saved values from the 0.4.0 sections, checked against a real 0.4.0 settings file.</summary>
internal static class SettingsMigrationTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a 0.4.0 settings file moves into the new sections", OldFileMovesIntoNewSections),
        ("a value already in the new section wins", NewValueWins),
        ("a removed setting's value is dropped from its old and new sections", RemovedSettingIsDropped),
        ("each move has its own source and target", MovesAreDistinct),
        ("tool keys saved at their old defaults move to the new ones", OldToolKeysMove),
        ("a changed tool key keeps every tool key as saved", ChangedToolKeyKeepsAll),
        ("the new tool key defaults swap the old ones among themselves", ToolKeyDefaultsSwap),
    };

    private static readonly string[] ToolKeys = { "BearingRangeKey", "CircleKey", "TextKey", "PenKey", "WaypointKey", "EraserKey" };

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

    /// <summary>The six tool keys as the first build with tool keys saved them: 1 Waypoint, 2 Pen, 3 Text, 4 Bearing/range, 5 Circle, 6 Eraser.</summary>
    private static void OldToolKeysMove()
    {
        var saved = ToolKeysSaved("Alpha4", "Alpha5", "Alpha3", "Alpha2", "Alpha1", "Alpha6");
        saved[(SettingSections.MapToolsKeysAndLimits, "UndoKey")] = "Z";
        Expect(SettingsMigration.MoveToolKeyDefaults(saved, (section, key) => (section, key)), "expected the tool keys moved");
        ExpectToolKeys(saved, "Alpha1", "Alpha2", "Alpha3", "Alpha4", "Alpha5", "Alpha6");
        ExpectText(saved[(SettingSections.MapToolsKeysAndLimits, "UndoKey")], "Z");

        Expect(!SettingsMigration.MoveToolKeyDefaults(saved, (section, key) => (section, key)), "expected keys at the new defaults to stay");
        ExpectToolKeys(saved, "Alpha1", "Alpha2", "Alpha3", "Alpha4", "Alpha5", "Alpha6");
        Expect(!SettingsMigration.MoveToolKeyDefaults(new Dictionary<(string, string), string>(), (section, key) => (section, key)),
            "expected nothing to move in a file without tool keys");
    }

    private static void ChangedToolKeyKeepsAll()
    {
        var saved = ToolKeysSaved("Alpha4", "Alpha5", "Alpha3", "Alpha2", "F", "Alpha6");
        Expect(!SettingsMigration.MoveToolKeyDefaults(saved, (section, key) => (section, key)), "expected nothing moved");
        ExpectToolKeys(saved, "Alpha4", "Alpha5", "Alpha3", "Alpha2", "F", "Alpha6");
    }

    private static void ToolKeyDefaultsSwap()
    {
        var defaults = SettingsMigration.ToolKeyDefaults;
        Expect(defaults.Select(d => d.OldDefault).OrderBy(k => k).SequenceEqual(defaults.Select(d => d.NewDefault).OrderBy(k => k)),
            "expected the new defaults to reuse the old ones");
    }

    /// <summary>Values for <see cref="ToolKeys"/>, in that order.</summary>
    private static Dictionary<(string, string), string> ToolKeysSaved(params string[] values)
    {
        var saved = new Dictionary<(string, string), string>();
        for (var i = 0; i < ToolKeys.Length; i++)
        {
            saved[(SettingSections.MapToolsKeysAndLimits, ToolKeys[i])] = values[i];
        }

        return saved;
    }

    private static void ExpectToolKeys(Dictionary<(string, string), string> saved, params string[] values)
    {
        for (var i = 0; i < ToolKeys.Length; i++)
        {
            ExpectText(saved[(SettingSections.MapToolsKeysAndLimits, ToolKeys[i])], values[i]);
        }
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
