using System;
using System.Collections.Generic;

namespace BaanishUiImprovements;

/// <summary>
/// Carries saved values over from the 0.4.0 settings layout, where every setting sat in a flat section. BepInEx keeps a
/// saved value only under its exact section and key, so without this a moved setting would reset to its default and
/// its old entry would linger in the file. Delete this once players have had a release or two to pick up the move.
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
        ("Airbase Names", "ShowEnemyAndNeutral", SettingSections.MapAirbaseNames),
        ("Airbase Names", "Color", SettingSections.MapAirbaseNames),
        ("Airbase Names", "Size", SettingSections.MapAirbaseNames),
        ("Airbase Boundary", "ShowBoundary", SettingSections.MapAirbaseBoundary),
        ("Airbase Boundary", "FillOpacity", SettingSections.MapAirbaseBoundary),
        ("Airbase Boundary", "EdgeOpacity", SettingSections.MapAirbaseBoundary),
        ("HUD", "ShowRunwayCallout", SettingSections.HudRunwayCallout),
        ("HUD", "OnlyWithGearDown", SettingSections.HudRunwayCallout),
        ("HUD", "IncludeAirbaseName", SettingSections.HudRunwayCallout),
        ("HUD", "CalloutColor", SettingSections.HudRunwayCallout),
        ("Missile Arrows", "ShowArrows", SettingSections.HudMissileArrows),
        ("Missile Arrows", "Color", SettingSections.HudMissileArrows),
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
    /// Moves each saved value in <paramref name="saved"/> (section and key to the value as written in the file) from its
    /// old place to its new one. A value already saved in the new place wins. Either way the old entry goes, so the file
    /// holds each setting once. Returns how many values moved.
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

        return moved;
    }
}
