namespace NoMapOverhaul;

/// <summary>
/// The 0.6.0 look: black runways with white numbers and a black outline, airbase names and landing zones shown, the HUD
/// callout naming the airbase, and runway numbers as ATC calls them. A player's settings file holds every setting, so a
/// new default reaches only new installs. The release's first start therefore moves each setting still at its old
/// default to the new one, once, and leaves alone any a player changed. Values are compared as BepInEx writes them.
/// </summary>
internal static class LookUpgrade
{
    /// <summary>The release whose first start makes the switch; a settings file marked with it has had it.</summary>
    public const string Release = "0.6.0";

    /// <summary>Each changed setting: the old defaults it replaces, from every build that shipped one, and the new.</summary>
    internal static readonly (string Section, string Key, string[] OldDefaults, string NewDefault)[] Changes =
    {
        (SettingSections.MapRunways, "RunwayColor", new[] { "00D900D9" }, "000000FF"),
        (SettingSections.MapRunways, "RunwayLabelColor", new[] { "99FF99FF" }, "FFFFFFFF"),
        (SettingSections.MapRunways, "NumbersOnRunwayFrom", new[] { "1" }, "0.75"),
        (SettingSections.MapRunways, "PaintedNumbers", new[] { "true" }, "false"),
        (SettingSections.MapRunways, "RunwayLabelSize", new[] { "8" }, "10"),
        (SettingSections.MapAirbaseNames, "ShowNames", new[] { "false" }, "true"),
        (SettingSections.MapAirbaseNames, "Color", new[] { "99FF99BF" }, "BFFFBFFF"),
        (SettingSections.MapAirbaseNames, "Size", new[] { "8", "11" }, "14"),
        (SettingSections.MapAirbaseBoundary, "ShowBoundary", new[] { "false" }, "true"),
        (SettingSections.MapOutline, "OutlineColor", new[] { "051405D9" }, "000000FF"),
        (SettingSections.MapOutline, "TextOutlineWidth", new[] { "1" }, "2"),
        (SettingSections.MapOutline, "OutlineWidth", new[] { "0.75" }, "1"),
        (SettingSections.HudRunwayCallout, "IncludeAirbaseName", new[] { "false" }, "true"),
    };

    /// <summary>The new default for a setting saved at one of its old defaults, or null to keep what's saved.</summary>
    public static string? Upgrade(string section, string key, string saved)
    {
        foreach (var change in Changes)
        {
            if (change.Section != section || change.Key != key)
            {
                continue;
            }

            foreach (var old in change.OldDefaults)
            {
                if (string.Equals(old, saved, System.StringComparison.OrdinalIgnoreCase))
                {
                    return change.NewDefault;
                }
            }

            return null;
        }

        return null;
    }
}
