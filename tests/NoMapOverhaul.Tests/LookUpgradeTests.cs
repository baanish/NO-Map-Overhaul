using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>Moving settings saved at an old default to the 0.6.0 look, and nothing a player changed.</summary>
internal static class LookUpgradeTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a setting saved at an old default moves to the new look", OldDefaultsMove),
        ("a setting a player changed keeps its value", ChangedSettingsStay),
        ("the look's new defaults are pinned", NewDefaultsPinned),
    };

    private static void OldDefaultsMove()
    {
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapRunways, "RunwayColor", "00D900D9") ?? "kept", "000000FF");
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapRunways, "RunwayColor", "00d900d9") ?? "kept", "000000FF");
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapAirbaseNames, "Size", "8") ?? "kept", "14");
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapAirbaseNames, "Size", "11") ?? "kept", "14");
        ExpectText(LookUpgrade.Upgrade(SettingSections.HudRunwayCallout, "IncludeAirbaseName", "false") ?? "kept", "true");
    }

    private static void ChangedSettingsStay()
    {
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapRunways, "RunwayColor", "FF0000FF") ?? "kept", "kept");
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapAirbaseNames, "Size", "20") ?? "kept", "kept");
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapRunways, "RunwayMinWidth", "6") ?? "kept", "kept");
        ExpectText(LookUpgrade.Upgrade(SettingSections.MapApproachLine, "RunwayColor", "00D900D9") ?? "kept", "kept");
    }

    private static void NewDefaultsPinned()
    {
        var pinned = string.Join(", ", LookUpgrade.Changes.Select(change => $"{change.Key}={change.NewDefault}"));
        ExpectText(pinned, "RunwayColor=000000FF, RunwayLabelColor=FFFFFFFF, NumbersOnRunwayFrom=0.75, PaintedNumbers=false, " +
            "RunwayLabelSize=10, ShowNames=true, Color=BFFFBFFF, Size=14, ShowBoundary=true, OutlineColor=000000FF, " +
            "TextOutlineWidth=2, OutlineWidth=1, IncludeAirbaseName=true");
    }
}
