using System;
using System.Collections.Generic;

namespace NoMapOverhaul;

/// <summary>
/// Carries saved values over from the 0.4.0 settings layout, where every setting sat in a flat section, and drops the
/// values of settings that are gone. BepInEx keeps a saved value only under its exact section and key, and writes back
/// every saved value no setting claims, so without this a moved setting would reset to its default, and its old entry,
/// like a removed setting's, would linger in the file. It also moves the map tool keys saved at their old defaults to
/// the new ones, and on the first start after the rename from Baanish UI Improvements copies that plugin's settings
/// file in first. Delete this once players have had a release or two to pick up the move.
/// </summary>
internal static class SettingsMigration
{
    /// <summary>Every setting that moved. Keys didn't change, only sections.</summary>
    internal static readonly (string OldSection, string Key, string NewSection)[] Moves =
    {
        ("General", "LogPerformance", SettingSections.Diagnostics),
        ("General", "PerfTest", SettingSections.Diagnostics),
        ("Map", "ShowRunways", SettingSections.MapRunways),
        ("Map", "RunwayColor", SettingSections.MapRunways),
        ("Map", "RunwayLabelColor", SettingSections.MapRunways),
        ("Map", "NumberBothEnds", SettingSections.MapRunways),
        ("Map", "RunwayLabelSize", SettingSections.MapRunways),
        ("Map", "RunwayMinWidth", SettingSections.MapRunways),
        ("Map", "OutlineColor", SettingSections.MapOutline),
        ("Map", "OutlineWidth", SettingSections.MapOutline),
        ("Approach", "TriggerRangeKm", SettingSections.MapApproachLine),
        ("Approach", "LineLengthKm", SettingSections.MapApproachLine),
        ("Approach", "LineColor", SettingSections.MapApproachLine),
        ("Approach", "LineWidth", SettingSections.MapApproachLine),
        ("Airbase Names", "ShowNames", SettingSections.MapAirbaseNames),
        ("Airbase Names", "Color", SettingSections.MapAirbaseNames),
        ("Airbase Names", "Size", SettingSections.MapAirbaseNames),
        ("Airbase Boundary", "ShowBoundary", SettingSections.MapAirbaseBoundary),
        ("Airbase Boundary", "FillOpacity", SettingSections.MapAirbaseBoundary),
        ("Airbase Boundary", "EdgeOpacity", SettingSections.MapAirbaseBoundary),
        ("HUD", "ShowRunwayCallout", SettingSections.HudRunwayCallout),
        ("HUD", "OnlyWithGearDown", SettingSections.HudRunwayCallout),
        ("HUD", "IncludeAirbaseName", SettingSections.HudRunwayCallout),
        ("HUD", "CalloutColor", SettingSections.HudRunwayCallout),
        ("Map Tools", "ShowTools", SettingSections.MapToolsGeneral),
        ("Map Tools", "ShowOnMinimap", SettingSections.MapToolsGeneral),
        ("Map Tools", "ShowCirclesIn3D", SettingSections.MapToolsGeneral),
        ("Map Tools", "DistanceUnits", SettingSections.MapToolsGeneral),
        ("Map Tools", "Color", SettingSections.MapToolsDrawing),
        ("Map Tools", "LineWidth", SettingSections.MapToolsDrawing),
        ("Map Tools", "TextSize", SettingSections.MapToolsDrawing),
        ("Map Tools", "WaypointReachKm", SettingSections.MapToolsWaypoints),
        ("Map Tools", "WaypointPassedKm", SettingSections.MapToolsWaypoints),
        ("Map Tools", "UndoKey", SettingSections.MapToolsKeysAndLimits),
        ("Map Tools", "RedoKey", SettingSections.MapToolsKeysAndLimits),
        ("Map Tools", "MaxShapes", SettingSections.MapToolsKeysAndLimits),
        ("Map Tools", "MaxPenPoints", SettingSections.MapToolsKeysAndLimits),
    };

    /// <summary>
    /// Settings that are gone, in every section they were saved under. Airbase names are your own side's only since
    /// 0.5.0: enemy and neutral ones showed where bases are that the game doesn't mark. The missile arrows moved to
    /// their own mod, NO Missile Indicators.
    /// </summary>
    internal static readonly (string Section, string Key)[] Removed =
    {
        ("Airbase Names", "ShowEnemyAndNeutral"),
        (SettingSections.MapAirbaseNames, "ShowEnemyAndNeutral"),
        ("Missile Arrows", "ShowArrows"),
        ("Missile Arrows", "Color"),
        ("HUD / Missile Arrows", "ShowArrows"),
        ("HUD / Missile Arrows", "Color"),
    };

    /// <summary>
    /// Tool keys whose default changed when the rail was reordered, with the old and new default as the file writes them.
    /// The four swap numbers among themselves, so moving all of them can't put two tools on one key.
    /// </summary>
    internal static readonly (string Key, string OldDefault, string NewDefault)[] ToolKeyDefaults =
    {
        ("BearingRangeKey", "Alpha4", "Alpha1"),
        ("CircleKey", "Alpha5", "Alpha2"),
        ("PenKey", "Alpha2", "Alpha4"),
        ("WaypointKey", "Alpha1", "Alpha5"),
    };

    /// <summary>
    /// Copies every value saved in the old plugin's settings file (<paramref name="oldFileLines"/>) into
    /// <paramref name="saved"/>, but only while <paramref name="saved"/> is empty, as it is on the first start after the
    /// rename. <see cref="MoveSavedValues"/> then moves and drops them as it would in the old file, so the missile
    /// arrows' values stay behind. Returns how many values were copied.
    /// </summary>
    internal static int CarryOver<TKey>(IDictionary<TKey, string> saved, IEnumerable<string> oldFileLines, Func<string, string, TKey> keyOf)
    {
        if (saved.Count > 0)
        {
            return 0;
        }

        foreach (var (section, key, value) in ReadSavedValues(oldFileLines))
        {
            saved[keyOf(section, key)] = value;
        }

        return saved.Count;
    }

    /// <summary>
    /// A settings file's saved values as BepInEx reads them: each <c>key = value</c> line under the last
    /// <c>[Section]</c>, split at the first '=' and trimmed. Comments and other lines are skipped.
    /// </summary>
    internal static IEnumerable<(string Section, string Key, string Value)> ReadSavedValues(IEnumerable<string> lines)
    {
        var section = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }

            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length == 2)
            {
                yield return (section, parts[0].Trim(), parts[1].Trim());
            }
        }
    }

    /// <summary>
    /// Moves the saved tool keys in <paramref name="saved"/> to their new defaults, but only when all four still hold
    /// their old defaults: BepInEx keeps a saved value over a new default, so the old numbering would otherwise stay. A
    /// player who changed any of them keeps every one as saved. Returns whether they moved.
    /// </summary>
    internal static bool MoveToolKeyDefaults<TKey>(IDictionary<TKey, string> saved, Func<string, string, TKey> keyOf)
    {
        foreach (var (key, oldDefault, _) in ToolKeyDefaults)
        {
            if (!saved.TryGetValue(keyOf(SettingSections.MapToolsKeysAndLimits, key), out var value) || value != oldDefault)
            {
                return false;
            }
        }

        foreach (var (key, _, newDefault) in ToolKeyDefaults)
        {
            saved[keyOf(SettingSections.MapToolsKeysAndLimits, key)] = newDefault;
        }

        return true;
    }

    /// <summary>
    /// Moves each saved value in <paramref name="saved"/> (section and key to the value as written in the file) from its
    /// old place to its new one. A value already saved in the new place wins. Either way the old entry goes, so the file
    /// holds each setting once. A removed setting's value goes too. Returns how many values moved.
    /// </summary>
    internal static int MoveSavedValues<TKey>(IDictionary<TKey, string> saved, Func<string, string, TKey> keyOf)
    {
        var moved = 0;
        foreach (var (oldSection, key, newSection) in Moves)
        {
            var from = keyOf(oldSection, key);
            if (!saved.TryGetValue(from, out var value))
            {
                continue;
            }

            saved.Remove(from);
            var to = keyOf(newSection, key);
            if (!saved.ContainsKey(to))
            {
                saved[to] = value;
                moved++;
            }
        }

        foreach (var (section, key) in Removed)
        {
            saved.Remove(keyOf(section, key));
        }

        return moved;
    }
}
