using BaanishUiImprovements.MapTools;
using BepInEx.Configuration;
using UnityEngine;

namespace BaanishUiImprovements;

/// <summary>Every player-facing knob, editable in BepInEx/config or live through ConfigurationManager (F1).</summary>
internal sealed class ModSettings
{
    public ModSettings(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Off hides everything this mod draws, as if it were not installed. Useful for checking whether a problem comes from the mod or the game.");
        LogPerformance = config.Bind("General", "LogPerformance", false, "Every 5 seconds, log the game's frame rate and the mod's own time per frame and per map refresh to BepInEx/LogOutput.log. Flip Enabled while it's on to compare with and without the mod.");
        PerfTest = config.Bind("General", "PerfTest", false, new ConfigDescription("In a mission, in an aircraft: measures the frame rate with the mod off, on, and on with a heavy set of drawings, on the minimap and the full map, then puts everything back. About 75 seconds; press again to cancel. Results go to the screen and BepInEx/LogOutput.log.", null, PerfTestButton));

        MapRunways = config.Bind("Map", "ShowRunways", true, "Draw friendly runways and their numbers on the minimap and full map.");
        RunwayColor = config.Bind("Map", "RunwayColor", new Color(0f, 0.85f, 0f, 0.85f), "Fill colour of runway strips.");
        RunwayLabelColor = config.Bind("Map", "RunwayLabelColor", new Color(0.6f, 1f, 0.6f, 1f), "Colour of the runway numbers on the map.");
        RunwayBothEnds = config.Bind("Map", "NumberBothEnds", true, "Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from.");
        RunwayLabelSize = config.Bind("Map", "RunwayLabelSize", 8f, new ConfigDescription("Runway number size on the map.", new AcceptableValueRange<float>(4f, 64f)));
        OutlineColor = config.Bind("Map", "OutlineColor", new Color(0.02f, 0.08f, 0.02f, 0.85f), "Dark rim around runways, approach dashes, and map text so they stay readable over bright map linework.");
        OutlineWidth = config.Bind("Map", "OutlineWidth", 0.75f, new ConfigDescription("Rim width around runways and approach dashes, in map icon units. 0 turns it off.", new AcceptableValueRange<float>(0f, 6f)));
        RunwayMinWidth = config.Bind("Map", "RunwayMinWidth", 6f, new ConfigDescription("Minimum drawn runway width when zoomed out, in map icon units.", new AcceptableValueRange<float>(1f, 30f)));

        ShowBoundaries = config.Bind("Airbase Boundary", "ShowBoundary", false, "Shade each friendly airbase's landing zone: stop inside it after landing and the sortie ends as returned instead of crashed. The same circle is the capture zone. Uses the game's friendly map colour.");
        BoundaryFillOpacity = config.Bind("Airbase Boundary", "FillOpacity", 0.01f, new ConfigDescription("Opacity of the shaded zone. The game blends in linear colour space, so small values already read strongly over the dark map.", new AcceptableValueRange<float>(0f, 1f)));
        BoundaryEdgeOpacity = config.Bind("Airbase Boundary", "EdgeOpacity", 0.1f, new ConfigDescription("Opacity of the zone's edge line. 0 hides it.", new AcceptableValueRange<float>(0f, 1f)));

        ShowAirbaseNames = config.Bind("Airbase Names", "ShowNames", false, "Name your faction's airbases just under them on the full map.");
        AirbaseNamesEnemyAndNeutral = config.Bind("Airbase Names", "ShowEnemyAndNeutral", false, "Also name enemy and neutral airbases. Off by default, since the names show where bases are that the game doesn't mark.");
        AirbaseNameColor = config.Bind("Airbase Names", "Color", new Color(0.6f, 1f, 0.6f, 0.75f), "Colour of the airbase names, a fainter version of the runway numbers' green by default. Much lower alpha loses the green text over the map's green linework.");
        AirbaseNameSize = config.Bind("Airbase Names", "Size", 8f, new ConfigDescription("Airbase name size on the map.", new AcceptableValueRange<float>(4f, 64f)));

        ApproachRangeKm = config.Bind("Approach", "TriggerRangeKm", 5f, new ConfigDescription("Show the approach line and HUD callout for the nearest runway within this distance.", new AcceptableValueRange<float>(0.5f, 30f)));
        ApproachLineLengthKm = config.Bind("Approach", "LineLengthKm", 5f, new ConfigDescription("Length of the dashed centerline drawn off the nearest runway end. Each dash plus gap is 500 m.", new AcceptableValueRange<float>(0.5f, 30f)));
        ApproachLineColor = config.Bind("Approach", "LineColor", new Color(0f, 0.85f, 0f, 0.4f), "Colour of the dashed approach line on the map. Keep it fainter than the runway so the two read apart.");
        ApproachLineWidth = config.Bind("Approach", "LineWidth", 1f, new ConfigDescription("Approach line width, in map icon units.", new AcceptableValueRange<float>(0.5f, 10f)));

        HudCallout = config.Bind("HUD", "ShowRunwayCallout", true, "Label the nearest runway end in the 3D view, for ATC calls.");
        HudGearDownOnly = config.Bind("HUD", "OnlyWithGearDown", false, "Show the callout only while the landing gear is down.");
        HudAirbaseName = config.Bind("HUD", "IncludeAirbaseName", false, "Prefix the callout with the abbreviated airbase name, e.g. NBSCLI RWY 27.");
        HudColor = config.Bind("HUD", "CalloutColor", new Color(0.2f, 1f, 0.2f, 1f), "Colour of the HUD runway callout.");

        ShowMissileArrows = config.Bind("Missile Arrows", "ShowArrows", true, "Point an arrow from the screen edge at each incoming missile outside the view. Only missiles the game's missile warning already knows about get one.");
        MissileArrowColor = config.Bind("Missile Arrows", "Color", new Color(1f, 0.25f, 0.2f, 1f), "Colour of the missile arrows.");

        ShowMapTools = config.Bind("Map Tools", "ShowTools", true, "Show the Tools button on the full map and everything drawn with it. Off hides both; drawings come back when it's on again, until you leave the mission.");
        MapToolShowOnMinimap = config.Bind("Map Tools", "ShowOnMinimap", true, "Show drawings on the minimap as well as the full map. Off keeps the minimap clear; the full map and the 3D labels still show them.");
        MapToolCirclesIn3D = config.Bind("Map Tools", "ShowCirclesIn3D", true, "Draw each circle in the 3D view as well, as a thin ring level with its centre: a unit's altitude, or the ground under a fixed point.");
        MapToolColor = config.Bind("Map Tools", "Color", (Color)new Color32(51, 255, 51, 255), "Colour of new lines, arrows, and text. The menu's swatches set it; any colour works here.");
        MapToolLineWidth = config.Bind("Map Tools", "LineWidth", 1.5f, new ConfigDescription("Width of drawn lines, in map icon units.", new AcceptableValueRange<float>(0.5f, 8f)));
        MapToolTextSize = config.Bind("Map Tools", "TextSize", 10f, new ConfigDescription("Size of text the tools draw on the map.", new AcceptableValueRange<float>(4f, 64f)));
        MapToolUnits = config.Bind("Map Tools", "DistanceUnits", UnitsSetting.Game, "Units for distances the tools show. Game follows the game's own setting: kilometres for metric, nautical miles for imperial.");
        MapToolMaxShapes = config.Bind("Map Tools", "MaxShapes", 200, new ConfigDescription("Most drawings kept at once. Tools can't add more until something is erased or undone.", new AcceptableValueRange<int>(10, 1000)));
        MapToolMaxPenPoints = config.Bind("Map Tools", "MaxPenPoints", 5000, new ConfigDescription("Most freehand points kept across all pen strokes, so the map stays fast.", new AcceptableValueRange<int>(500, 7500)));
        MapToolUndoKey = config.Bind("Map Tools", "UndoKey", new KeyboardShortcut(KeyCode.Z), "Undo the last drawing change while the full map is open. Z isn't bound in the game's default controls.");
        MapToolRedoKey = config.Bind("Map Tools", "RedoKey", new KeyboardShortcut(KeyCode.Y), "Redo while the full map is open. Y isn't bound in the game's default controls.");

        MapToolWaypointReachKm = config.Bind("Map Tools", "WaypointReachKm", 2.5f, new ConfigDescription("A waypoint counts as reached once you fly within this distance of it, and the route moves on to the next. NOAutopilot's default.", new AcceptableValueRange<float>(0.1f, 20f)));
        MapToolWaypointPassedKm = config.Bind("Map Tools", "WaypointPassedKm", 10f, new ConfigDescription("A waypoint behind you also counts as reached while it's within this distance, so a wide miss still moves the route on. NOAutopilot's default. 0 turns it off.", new AcceptableValueRange<float>(0f, 50f)));
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> LogPerformance { get; }

    /// <summary>Drawn as a button in F1, which starts or cancels the test; set true any other way, it does the same and resets.</summary>
    public ConfigEntry<bool> PerfTest { get; }

    /// <summary>The F1 button's look. <see cref="Diagnostics.PerfTest"/> sets its drawer.</summary>
    public Diagnostics.ConfigurationManagerAttributes PerfTestButton { get; } = new() { HideDefaultButton = true };

    public ConfigEntry<bool> MapRunways { get; }
    public ConfigEntry<Color> RunwayColor { get; }
    public ConfigEntry<Color> RunwayLabelColor { get; }
    public ConfigEntry<bool> RunwayBothEnds { get; }
    public ConfigEntry<float> RunwayLabelSize { get; }
    public ConfigEntry<float> RunwayMinWidth { get; }
    public ConfigEntry<Color> OutlineColor { get; }
    public ConfigEntry<float> OutlineWidth { get; }

    public ConfigEntry<bool> ShowBoundaries { get; }
    public ConfigEntry<float> BoundaryFillOpacity { get; }
    public ConfigEntry<float> BoundaryEdgeOpacity { get; }

    public ConfigEntry<bool> ShowAirbaseNames { get; }
    public ConfigEntry<bool> AirbaseNamesEnemyAndNeutral { get; }
    public ConfigEntry<Color> AirbaseNameColor { get; }
    public ConfigEntry<float> AirbaseNameSize { get; }

    public ConfigEntry<float> ApproachRangeKm { get; }
    public ConfigEntry<float> ApproachLineLengthKm { get; }
    public ConfigEntry<Color> ApproachLineColor { get; }
    public ConfigEntry<float> ApproachLineWidth { get; }

    public ConfigEntry<bool> HudCallout { get; }
    public ConfigEntry<bool> HudGearDownOnly { get; }
    public ConfigEntry<bool> HudAirbaseName { get; }
    public ConfigEntry<Color> HudColor { get; }

    public ConfigEntry<bool> ShowMissileArrows { get; }
    public ConfigEntry<Color> MissileArrowColor { get; }

    public ConfigEntry<bool> ShowMapTools { get; }
    public ConfigEntry<bool> MapToolShowOnMinimap { get; }
    public ConfigEntry<bool> MapToolCirclesIn3D { get; }
    public ConfigEntry<Color> MapToolColor { get; }
    public ConfigEntry<float> MapToolLineWidth { get; }
    public ConfigEntry<float> MapToolTextSize { get; }
    public ConfigEntry<UnitsSetting> MapToolUnits { get; }
    public ConfigEntry<int> MapToolMaxShapes { get; }
    public ConfigEntry<int> MapToolMaxPenPoints { get; }
    public ConfigEntry<KeyboardShortcut> MapToolUndoKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolRedoKey { get; }

    public ConfigEntry<float> MapToolWaypointReachKm { get; }
    public ConfigEntry<float> MapToolWaypointPassedKm { get; }
}
