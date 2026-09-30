namespace NoMapOverhaul;

/// <summary>
/// The settings file's sections, which are also the F1 window's headings. F1 has no nested sections, so the part
/// before " / " names the group. Shared by <see cref="ModSettings"/> and <see cref="SettingsMigration"/>, and free of
/// Unity so the migration's tests can use them.
/// </summary>
internal static class SettingSections
{
    public const string General = "General";
    public const string MapRunways = "Map / Runways";
    public const string MapApproachLine = "Map / Approach Line";
    public const string MapAirbaseNames = "Map / Airbase Names";
    public const string MapAirbaseBoundary = "Map / Airbase Boundary";
    public const string MapOutline = "Map / Outline";
    public const string HudRunwayCallout = "HUD / Runway Callout";
    public const string HudMissileArrows = "HUD / Missile Arrows";
    public const string MapToolsGeneral = "Map Tools / General";
    public const string MapToolsDrawing = "Map Tools / Drawing";
    public const string MapToolsWaypoints = "Map Tools / Waypoints";
    public const string MapToolsKeysAndLimits = "Map Tools / Keys and Limits";
    public const string Diagnostics = "Diagnostics";
}
