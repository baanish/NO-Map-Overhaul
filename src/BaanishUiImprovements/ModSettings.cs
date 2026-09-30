using BaanishUiImprovements.Diagnostics;
using BaanishUiImprovements.MapTools;
using BepInEx.Configuration;
using UnityEngine;

namespace BaanishUiImprovements;

/// <summary>
/// Every player-facing knob, editable in BepInEx/config or live through ConfigurationManager (F1). F1 lists sections in
/// the order they are first bound and settings in bind order within a section, so this constructor reads top to bottom
/// like the window: each feature's on/off switch first, tuning knobs marked advanced so F1 hides them by default.
/// </summary>
internal sealed class ModSettings
{
    private readonly ConfigFile _config;

    /// <summary>F1 lists higher Order first, so counting down keeps bind order. A setting bound without <see cref="Bind{T}"/> has Order 0 and goes last in its section.</summary>
    private int _order = 1000;

    public ModSettings(ConfigFile config)
    {
        _config = config;

        Enabled = Bind(SettingSections.General, "Enabled", true, "Enabled", "Off hides everything this mod draws, as if it were not installed. Useful for checking whether a problem comes from the mod or the game.");

        MapRunways = Bind(SettingSections.MapRunways, "ShowRunways", true, "Show runways", "Draw friendly runways and their numbers on the minimap and full map.");
        RunwayColor = Bind(SettingSections.MapRunways, "RunwayColor", new Color(0f, 0.85f, 0f, 0.85f), "Runway colour", "Fill colour of runway strips.");
        RunwayLabelColor = Bind(SettingSections.MapRunways, "RunwayLabelColor", new Color(0.6f, 1f, 0.6f, 1f), "Number colour", "Colour of the runway numbers on the map.");
        RunwayBothEnds = Bind(SettingSections.MapRunways, "NumberBothEnds", true, "Number both ends", "Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from.");
        RunwayLabelSize = Bind(SettingSections.MapRunways, "RunwayLabelSize", 8f, "Number size", "Runway number size on the map.", new AcceptableValueRange<float>(4f, 64f), advanced: true);
        RunwayMinWidth = Bind(SettingSections.MapRunways, "RunwayMinWidth", 6f, "Minimum width", "Narrowest a runway strip gets when zoomed out, in map icon units.", new AcceptableValueRange<float>(1f, 30f), advanced: true);

        ApproachRangeKm = Bind(SettingSections.MapApproachLine, "TriggerRangeKm", 5f, "Show within (km)", "Show the approach line, and the HUD runway callout, for the nearest runway within this distance.", new AcceptableValueRange<float>(0.5f, 30f));
        ApproachLineLengthKm = Bind(SettingSections.MapApproachLine, "LineLengthKm", 5f, "Line length (km)", "Length of the dashed centerline drawn off the nearest runway end. Each dash plus gap is 500 m.", new AcceptableValueRange<float>(0.5f, 30f));
        ApproachLineColor = Bind(SettingSections.MapApproachLine, "LineColor", new Color(0f, 0.85f, 0f, 0.4f), "Line colour", "Colour of the dashed approach line on the map. Keep it fainter than the runway so the two read apart.");
        ApproachLineWidth = Bind(SettingSections.MapApproachLine, "LineWidth", 1f, "Line width", "Approach line width, in map icon units.", new AcceptableValueRange<float>(0.5f, 10f), advanced: true);

        ShowAirbaseNames = Bind(SettingSections.MapAirbaseNames, "ShowNames", false, "Show airbase names", "Name your faction's airbases just under them on the full map.");
        AirbaseNameColor = Bind(SettingSections.MapAirbaseNames, "Color", new Color(0.6f, 1f, 0.6f, 0.75f), "Name colour", "Colour of the airbase names, a fainter version of the runway numbers' green by default. Much lower alpha loses the green text over the map's green linework.");
        AirbaseNameSize = Bind(SettingSections.MapAirbaseNames, "Size", 8f, "Name size", "Airbase name size on the map.", new AcceptableValueRange<float>(4f, 64f), advanced: true);

        ShowBoundaries = Bind(SettingSections.MapAirbaseBoundary, "ShowBoundary", false, "Show landing zone", "Shade each friendly airbase's landing zone: stop inside it after landing and the sortie ends as returned instead of crashed. The same circle is the capture zone. Uses the game's friendly map colour.");
        BoundaryFillOpacity = Bind(SettingSections.MapAirbaseBoundary, "FillOpacity", 0.01f, "Fill opacity", "Opacity of the shaded zone, 0 to 1. The game blends in linear colour space, so small values already read strongly over the dark map.", new AcceptableValueRange<float>(0f, 1f), advanced: true, display: new() { ShowRangeAsPercent = false });
        BoundaryEdgeOpacity = Bind(SettingSections.MapAirbaseBoundary, "EdgeOpacity", 0.1f, "Edge opacity", "Opacity of the zone's edge line, 0 to 1. 0 hides it.", new AcceptableValueRange<float>(0f, 1f), advanced: true, display: new() { ShowRangeAsPercent = false });

        OutlineColor = Bind(SettingSections.MapOutline, "OutlineColor", new Color(0.02f, 0.08f, 0.02f, 0.85f), "Outline colour", "Dark rim around runways, approach dashes, and map text so they stay readable over bright map linework.", advanced: true);
        OutlineWidth = Bind(SettingSections.MapOutline, "OutlineWidth", 0.75f, "Outline width", "Rim width around runways and approach dashes, in map icon units. 0 turns it off. Text always gets a 1-unit rim.", new AcceptableValueRange<float>(0f, 6f), advanced: true);

        HudCallout = Bind(SettingSections.HudRunwayCallout, "ShowRunwayCallout", true, "Show runway callout", "Label the nearest runway end in the 3D view, for ATC calls.");
        HudGearDownOnly = Bind(SettingSections.HudRunwayCallout, "OnlyWithGearDown", false, "Only with gear down", "Show the callout only while the landing gear is down.");
        HudAirbaseName = Bind(SettingSections.HudRunwayCallout, "IncludeAirbaseName", false, "Include airbase name", "Prefix the callout with the abbreviated airbase name, e.g. NBSCLI RWY 27.");
        HudColor = Bind(SettingSections.HudRunwayCallout, "CalloutColor", new Color(0.2f, 1f, 0.2f, 1f), "Callout colour", "Colour of the HUD runway callout.");

        ShowMissileArrows = Bind(SettingSections.HudMissileArrows, "ShowArrows", true, "Show missile arrows", "Point an arrow from the screen edge at each incoming missile outside the view. Only missiles the game's missile warning already knows about get one.");
        MissileArrowColor = Bind(SettingSections.HudMissileArrows, "Color", new Color(1f, 0.25f, 0.2f, 1f), "Arrow colour", "Colour of the missile arrows.");

        ShowMapTools = Bind(SettingSections.MapToolsGeneral, "ShowTools", true, "Show map tools", "Show the Tools button on the full map and everything drawn with it. Off hides both; drawings come back when it's on again, until you leave the mission.");
        MapToolShowOnMinimap = Bind(SettingSections.MapToolsGeneral, "ShowOnMinimap", true, "Show on minimap", "Show drawings on the minimap as well as the full map. Off keeps the minimap clear; the full map and the 3D labels still show them.");
        MapToolCirclesIn3D = Bind(SettingSections.MapToolsGeneral, "ShowCirclesIn3D", true, "Circles in 3D view", "Draw each circle in the 3D view as well, as a thin ring level with its centre: a unit's altitude, or the ground under a fixed point.");
        MapToolUnits = Bind(SettingSections.MapToolsGeneral, "DistanceUnits", UnitsSetting.Game, "Distance units", "Units for distances the tools show. Game setting follows the game: kilometres for metric, nautical miles for imperial.");

        MapToolColor = Bind(SettingSections.MapToolsDrawing, "Color", (Color)new Color32(51, 255, 51, 255), "Drawing colour", "Colour of new lines, arrows, and text. The menu's swatches set it; any colour works here.", advanced: true);
        MapToolLineWidth = Bind(SettingSections.MapToolsDrawing, "LineWidth", 1.5f, "Line width", "Width of drawn lines, in map icon units. Arrowheads grow with it.", new AcceptableValueRange<float>(0.5f, 8f), advanced: true);
        MapToolTextSize = Bind(SettingSections.MapToolsDrawing, "TextSize", 10f, "Text size", "Size of text the tools draw on the map.", new AcceptableValueRange<float>(4f, 64f), advanced: true);

        MapToolWaypointReachKm = Bind(SettingSections.MapToolsWaypoints, "WaypointReachKm", 2.5f, "Reached within (km)", "A waypoint counts as reached once you fly within this distance of it, and the route moves on to the next. NOAutopilot's default.", new AcceptableValueRange<float>(0.1f, 20f), advanced: true);
        MapToolWaypointPassedKm = Bind(SettingSections.MapToolsWaypoints, "WaypointPassedKm", 10f, "Passed within (km)", "A waypoint behind you also counts as reached while it's within this distance, so a wide miss still moves the route on. NOAutopilot's default. 0 turns it off.", new AcceptableValueRange<float>(0f, 50f), advanced: true);

        MapToolsKey = Bind(SettingSections.MapToolsKeysAndLimits, "ToolsKey", new KeyboardShortcut(KeyCode.H), "Tools key", "Open or close the tools rail while the full map is open, like clicking Tools. H isn't bound in the game's default controls.");
        MapToolUndoKey = Bind(SettingSections.MapToolsKeysAndLimits, "UndoKey", new KeyboardShortcut(KeyCode.Z), "Undo key", "Undo the last drawing change while the full map is open. Z isn't bound in the game's default controls.");
        MapToolRedoKey = Bind(SettingSections.MapToolsKeysAndLimits, "RedoKey", new KeyboardShortcut(KeyCode.Y), "Redo key", "Redo while the full map is open. Y isn't bound in the game's default controls.");
        MapToolBearingRangeKey = BindToolKey("BearingRangeKey", "Bearing/range", KeyCode.Alpha1);
        MapToolCircleKey = BindToolKey("CircleKey", "Circle", KeyCode.Alpha2);
        MapToolTextKey = BindToolKey("TextKey", "Text", KeyCode.Alpha3);
        MapToolPenKey = BindToolKey("PenKey", "Pen", KeyCode.Alpha4);
        MapToolWaypointKey = BindToolKey("WaypointKey", "Waypoint", KeyCode.Alpha5);
        MapToolEraserKey = BindToolKey("EraserKey", "Eraser", KeyCode.Alpha6);
        MapToolMaxShapes = Bind(SettingSections.MapToolsKeysAndLimits, "MaxShapes", 200, "Most drawings", "Most drawings kept at once. Tools can't add more until something is erased or undone.", new AcceptableValueRange<int>(10, 1000), advanced: true);
        MapToolMaxPenPoints = Bind(SettingSections.MapToolsKeysAndLimits, "MaxPenPoints", 5000, "Most pen points", "Most freehand points kept across all pen strokes, so the map stays fast.", new AcceptableValueRange<int>(500, 7500), advanced: true);

        PerfTest = Bind(SettingSections.Diagnostics, "PerfTest", false, "Perf test", "In a mission, in an aircraft: measures the frame rate with the mod off, on, and on with a typical and a heavy set of drawings, on the minimap and the full map, then puts everything back. About 85 seconds; press again to cancel. Results go to the screen and BepInEx/LogOutput.log.", display: PerfTestButton);
        LogPerformance = Bind(SettingSections.Diagnostics, "LogPerformance", false, "Log performance", "Every 5 seconds, log the game's frame rate and the mod's own time per frame and per map refresh to BepInEx/LogOutput.log. Flip Enabled while it's on to compare with and without the mod.", advanced: true);
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> LogPerformance { get; }

    /// <summary>Drawn as a button in F1, which starts or cancels the test; set true any other way, it does the same and resets.</summary>
    public ConfigEntry<bool> PerfTest { get; }

    /// <summary>The F1 button's look. <see cref="Diagnostics.PerfTest"/> sets its drawer.</summary>
    public ConfigurationManagerAttributes PerfTestButton { get; } = new() { HideDefaultButton = true };

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

    /// <summary>
    /// True while the perf test runs: the map tools and their drawings show on both maps as if ShowTools and
    /// ShowOnMinimap were on, without writing either, so a crash mid-test can't save them on.
    /// </summary>
    public bool PerfTestShowsDrawings { get; set; }
    public ConfigEntry<bool> MapToolCirclesIn3D { get; }
    public ConfigEntry<Color> MapToolColor { get; }
    public ConfigEntry<float> MapToolLineWidth { get; }
    public ConfigEntry<float> MapToolTextSize { get; }
    public ConfigEntry<UnitsSetting> MapToolUnits { get; }
    public ConfigEntry<int> MapToolMaxShapes { get; }
    public ConfigEntry<int> MapToolMaxPenPoints { get; }
    public ConfigEntry<KeyboardShortcut> MapToolsKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolUndoKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolRedoKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolWaypointKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolPenKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolTextKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolBearingRangeKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolCircleKey { get; }
    public ConfigEntry<KeyboardShortcut> MapToolEraserKey { get; }

    public ConfigEntry<float> MapToolWaypointReachKm { get; }
    public ConfigEntry<float> MapToolWaypointPassedKm { get; }

    /// <summary>
    /// Binds one setting with its F1 name, position, and advanced flag. <paramref name="display"/> carries any other F1
    /// attributes; this fills in the rest.
    /// </summary>
    private ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string name, string description, AcceptableValueBase? values = null, bool advanced = false, ConfigurationManagerAttributes? display = null)
    {
        display ??= new ConfigurationManagerAttributes();
        display.DispName = name;
        display.Order = _order--;
        display.IsAdvanced = advanced;
        return _config.Bind(section, key, defaultValue, new ConfigDescription(description, values, display));
    }

    /// <summary>A key that picks one map tool. The number row, where they start, isn't bound in the game's default controls.</summary>
    private ConfigEntry<KeyboardShortcut> BindToolKey(string key, string tool, KeyCode defaultKey) =>
        Bind(SettingSections.MapToolsKeysAndLimits, key, new KeyboardShortcut(defaultKey), tool + " key",
            $"Pick the {tool} tool while the full map is open, opening the tools rail if it's closed.", advanced: true);
}
