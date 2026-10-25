using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.MapTools;
using BepInEx.Configuration;
using UnityEngine;

namespace NoMapOverhaul;

/// <summary>
/// Persistent settings, edited through the map UI or ConfigurationManager (F1). F1 lists sections in
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
        RunwayColor = Bind(SettingSections.MapRunways, "RunwayColor", Color.black, "Runway colour", "Fill colour of runway strips.");
        RunwayLabelColor = Bind(SettingSections.MapRunways, "RunwayLabelColor", Color.white, "Number colour", "Colour of the runway numbers on the map.");
        RunwayBothEnds = Bind(SettingSections.MapRunways, "NumberBothEnds", true, "Number both ends", "Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from.");
        RunwayNumbersOnRunway = Bind(SettingSections.MapRunways, "NumbersOnRunway", true, "Numbers on runway", "Once you zoom in far enough for a number to fit across its runway, draw it on the runway at that end, the way it's painted, upright like the rest of the map's text.");
        RunwayNumbersOnRunwayFrom = Bind(SettingSections.MapRunways, "NumbersOnRunwayFrom", 0.75f, "On runway from", "How large a number must be able to show across its runway, as a multiple of its size beside the runway, before it moves onto it. Higher waits until you're zoomed in further.", new AcceptableValueRange<float>(0.5f, 5f));
        RunwayNumbersTurn = Bind(SettingSections.MapRunways, "NumbersOnRunwayTurn", false, "Turn with runway", "Turn a number on the runway to read for a pilot landing there, like the paint, instead of keeping it upright.");
        RunwayPaintedNumbers = Bind(SettingSections.MapRunways, "PaintedNumbers", false, "Number as painted", "Where the number painted on a runway differs from the game's own, show the paint: 34/16 rather than 33/15 on Feldspar's crossing runway, and 14/32 rather than 13/31 on Harmony Sands' southwest runway. Off shows the game's number, the one ATC calls. Applies to the map and the HUD callout.");
        RunwayLabelSize = Bind(SettingSections.MapRunways, "RunwayLabelSize", 14f, "Number size", "Runway number size on the full map.", new AcceptableValueRange<float>(4f, 64f), advanced: true);
        MinimapRunwayLabelSize = Bind(SettingSections.MapRunways, "MinimapRunwayLabelSize", 8f, "Minimap number size", "Runway number size on the minimap, which shows a smaller area.", new AcceptableValueRange<float>(4f, 64f), advanced: true);
        RunwayMinWidth = Bind(SettingSections.MapRunways, "RunwayMinWidth", 6f, "Minimum width", "Narrowest a runway strip gets when zoomed out, in map icon units.", new AcceptableValueRange<float>(1f, 30f), advanced: true);
        RunwayNumbersMinGridSquare = Bind(SettingSections.MapRunways, "NumbersMinGridSquare", 100f, "Numbers from zoom", "On the full map, hide every runway number while one grid square is narrower than this on screen, in map icon units, so zoomed far out they don't pile onto the airbases. The minimap always shows them. 0 always shows them.", new AcceptableValueRange<float>(0f, 1000f), advanced: true);

        CentreLine = Bind(SettingSections.MapCentreLine, "ShowCentreLine", false, "Show centre line", "Draw a dashed line down the middle of each runway, between its ends.");
        CentreLineColor = Bind(SettingSections.MapCentreLine, "Color", Color.white, "Line colour", "Colour of the runway centre line.");
        CentreLineWidth = Bind(SettingSections.MapCentreLine, "Width", 1f, "Line width", "Centre line width, in map icon units.", new AcceptableValueRange<float>(0.25f, 6f));
        CentreLineDash = Bind(SettingSections.MapCentreLine, "DashLength", 30f, "Dash length (m)", "Length of each dash on the ground, in metres, so the dashes grow as you zoom in like the paint. Zoomed out, once a dash would be shorter than 2 map icon units, the line isn't drawn.", new AcceptableValueRange<float>(5f, 500f));
        CentreLineGap = Bind(SettingSections.MapCentreLine, "GapLength", 60f, "Gap length (m)", "Length of the gap between dashes on the ground, in metres.", new AcceptableValueRange<float>(5f, 500f));

        ApproachRangeKm = Bind(SettingSections.MapApproachLine, "TriggerRangeKm", 5f, "Show within (km)", "Show the approach line, and the HUD runway callout, for the nearest runway within this distance.", new AcceptableValueRange<float>(0.5f, 30f));
        ApproachLineLengthKm = Bind(SettingSections.MapApproachLine, "LineLengthKm", 5f, "Line length (km)", "Length of the dashed centerline drawn off the nearest runway end. Each dash plus gap is 500 m.", new AcceptableValueRange<float>(0.5f, 30f));
        ApproachLineColor = Bind(SettingSections.MapApproachLine, "LineColor", new Color(0f, 0.85f, 0f, 0.4f), "Line colour", "Colour of the dashed approach line on the map. Keep it fainter than the runway so the two read apart.");
        ApproachLineWidth = Bind(SettingSections.MapApproachLine, "LineWidth", 1f, "Line width", "Approach line width, in map icon units.", new AcceptableValueRange<float>(0.5f, 10f), advanced: true);

        ShowAirbaseNames = Bind(SettingSections.MapAirbaseNames, "ShowNames", true, "Show airbase names", "Name your faction's airbases just under them on the full map.");
        AirbaseNameColor = Bind(SettingSections.MapAirbaseNames, "Color", new Color(0.75f, 1f, 0.75f, 1f), "Name colour", "Colour of the airbase names, a fainter version of the runway numbers' green by default. Much lower alpha loses the green text over the map's green linework.");
        AirbaseNameSize = Bind(SettingSections.MapAirbaseNames, "Size", 14f, "Name size", "Airbase name size on the map.", new AcceptableValueRange<float>(4f, 64f), advanced: true);

        ShowBoundaries = Bind(SettingSections.MapAirbaseBoundary, "ShowBoundary", true, "Show landing zone", "Shade each friendly airbase's landing zone: stop inside it after landing and the sortie ends as returned instead of crashed. The same circle is the capture zone. Uses the game's friendly map colour.");
        BoundaryFillOpacity = Bind(SettingSections.MapAirbaseBoundary, "FillOpacity", 0.01f, "Fill opacity", "Opacity of the shaded zone, 0 to 1. The game blends in linear colour space, so small values already read strongly over the dark map.", new AcceptableValueRange<float>(0f, 1f), advanced: true, display: new() { ShowRangeAsPercent = false });
        BoundaryEdgeOpacity = Bind(SettingSections.MapAirbaseBoundary, "EdgeOpacity", 0.1f, "Edge opacity", "Opacity of the zone's edge line, 0 to 1. 0 hides it.", new AcceptableValueRange<float>(0f, 1f), advanced: true, display: new() { ShowRangeAsPercent = false });

        OutlineColor = Bind(SettingSections.MapOutline, "OutlineColor", Color.black, "Outline colour", "Dark rim around runways, approach dashes, and map text so they stay readable over bright map linework.", advanced: true);
        TextOutlineWidth = Bind(SettingSections.MapOutline, "TextOutlineWidth", 2f, "Text outline width", "Rim width around runway numbers and airbase names, in map icon units, in the outline colour. 0 turns it off.", new AcceptableValueRange<float>(0f, 3f), advanced: true);
        OutlineWidth = Bind(SettingSections.MapOutline, "OutlineWidth", 1f, "Outline width", "Rim width around runways and approach dashes, in map icon units. 0 turns it off. Text outline width sets the rim around map text.", new AcceptableValueRange<float>(0f, 6f), advanced: true);

        HudCallout = Bind(SettingSections.HudRunwayCallout, "ShowRunwayCallout", true, "Show runway callout", "Label the nearest runway end in the 3D view, for ATC calls.");
        HudGearDownOnly = Bind(SettingSections.HudRunwayCallout, "OnlyWithGearDown", false, "Only with gear down", "Show the callout only while the landing gear is down.");
        HudAirbaseName = Bind(SettingSections.HudRunwayCallout, "IncludeAirbaseName", true, "Include airbase name", "Prefix the callout with the abbreviated airbase name, e.g. NBSCLI RWY 27.");
        HudColor = Bind(SettingSections.HudRunwayCallout, "CalloutColor", new Color(0.2f, 1f, 0.2f, 1f), "Callout colour", "Colour of the HUD runway callout.");

        ShowMapTools = Bind(SettingSections.MapToolsGeneral, "ShowTools", true, "Show map tools", "Show the Tools button on the full map and everything drawn with it. Off hides both; drawings come back when it's on again, until you leave the mission.");
        MapToolShowOnMinimap = Bind(SettingSections.MapToolsGeneral, "ShowOnMinimap", true, "Show on minimap", "Show drawings on the minimap as well as the full map. Off keeps the minimap clear; the full map and the 3D labels still show them.");
        MapToolCirclesIn3D = Bind(SettingSections.MapToolsGeneral, "ShowCirclesIn3D", true, "Circles in 3D view", "Draw each circle in the 3D view as well, as a thin ring level with its centre: a unit's altitude, or the ground under a fixed point.");
        MapToolUnits = Bind(SettingSections.MapToolsGeneral, "DistanceUnits", UnitsSetting.Game, "Distance units", "Units for distances the tools show. Game setting follows the game: kilometres for metric, nautical miles for imperial.");

        MapSharingEnabled = Bind(SettingSections.MapToolsSharing, "Enabled", false, "Share drawings", "Managed by Channels in the full map's Tools menu. Off stops sending and receiving drawings.", display: new ConfigurationManagerAttributes { Browsable = false });
        MapBroadcastChannels = Bind(SettingSections.MapToolsSharing, "BroadcastChannels", string.Empty, "Broadcast channels", "Managed by Channels in the full map's Tools menu. Comma-separated channels where you send, receive and collaborate, up to 10. Empty keeps your drawings local. Channel keys choose the channel for new drawings, defaulting to Shift+1 to Shift+9 and Shift+0. Rebind them in F1.", display: new ConfigurationManagerAttributes { Browsable = false });
        MapReceiveChannels = Bind(SettingSections.MapToolsSharing, "ReceiveChannels", string.Empty, "Receive-only channels", "Managed by Channels in the full map's Tools menu. Extra channels whose drawings you only view, up to 10. Broadcast channels are already received and allow collaboration. Names use ASCII letters, digits, spaces, dots, underscores or hyphens, up to 32 characters.", display: new ConfigurationManagerAttributes { Browsable = false });

        MapToolColor = Bind(SettingSections.MapToolsDrawing, "Color", (Color)new Color32(51, 255, 51, 255), "Drawing colour", "Colour of new lines, arrows, and text. The menu's swatches set it; any colour works here.", advanced: true);
        MapToolCustomColors = Bind(SettingSections.MapToolsDrawing, "CustomColors", string.Empty, "Custom colours", "Managed by the + swatch in the full map's Tools menu. Up to six comma-separated #RRGGBB colours for the menu's second row of swatches. Entries that aren't #RRGGBB are ignored.", display: new ConfigurationManagerAttributes { Browsable = false });
        OtherChannelsOpacity = Bind(SettingSections.MapToolsDrawing, "OtherChannelsOpacity", 0.5f, "Other channels opacity", "Opacity of drawings on a channel other than the one you're drawing to, yours and teammates', as a share of their own. 1 shows them like the rest.", new AcceptableValueRange<float>(0f, 1f));
        OtherChannelsBrightness = Bind(SettingSections.MapToolsDrawing, "OtherChannelsBrightness", 0.6f, "Other channels brightness", "Brightness of drawings on a channel other than the one you're drawing to, as a share of their own colour. 1 keeps their colour.", new AcceptableValueRange<float>(0f, 1f));
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
        MapChannelKeys = new ConfigEntry<KeyboardShortcut>[DrawingChannels.MaxChannels];
        for (var index = 0; index < MapChannelKeys.Length; index++)
        {
            var slot = index + 1;
            var key = index == 9 ? KeyCode.Alpha0 : (KeyCode)((int)KeyCode.Alpha1 + index);
            MapChannelKeys[index] = Bind(SettingSections.MapToolsKeysAndLimits, "ChannelKey" + slot,
                new KeyboardShortcut(key, KeyCode.LeftShift), "Channel " + slot + " key",
                $"Pick Broadcast slot {slot} for new drawings while the full map is open. Either Shift key works for a Shift modifier. Clear the binding to disable this shortcut.", advanced: true);
        }

        MapToolMaxShapes = Bind(SettingSections.MapToolsKeysAndLimits, "MaxShapes", 100, "Most drawings", "Most drawings kept at once. Tools can't add more until something is erased or undone.", new AcceptableValueRange<int>(10, 100), advanced: true);
        MapToolMaxPenPoints = Bind(SettingSections.MapToolsKeysAndLimits, "MaxPenPoints", 2500, "Most pen points", "Most freehand points kept across all pen strokes, so the map stays fast.", new AcceptableValueRange<int>(500, 2500), advanced: true);

        PerfTest = Bind(SettingSections.Diagnostics, "PerfTest", false, "Perf test", "In a mission, in an aircraft: measures the frame rate with the mod off, on, and on with a typical and a heavy set of drawings, on the minimap and the full map, then puts everything back. About 85 seconds; press again to cancel. Results go to the screen and BepInEx/LogOutput.log.", display: PerfTestButton);
        PerfTestSharing = Bind(SettingSections.Diagnostics, "PerfTestSharing", Diagnostics.PerfTestSharing.LocalOnly, "Perf test sharing", "Where the perf test's drawings go. LocalOnly keeps them on this map. Send also sends them over sharing, Receive draws none and shows a teammate's, Bidirectional does both: start the test on both PCs within the same half minute, in a Steam multiplayer mission on the same team, and their slices line up. The test uses its own perf-test channel and needs no channel set up; your drawings and your teammates' are kept and sent as before.", advanced: true);
        PerfTestCycles = Bind(SettingSections.Diagnostics, "PerfTestCycles", 4, "Perf test cycles", "How many times the perf test measures each condition. 4 takes about 85 seconds; more measures sustained performance, and the log gets a line per cycle to show any drift.", new AcceptableValueList<int>(4, 8, 16, 32, 64, 128), advanced: true);
        LogPerformance = Bind(SettingSections.Diagnostics, "LogPerformance", true, "Log performance", "Every 5 seconds, log the game's frame rate and the mod's own time per frame and per map refresh to BepInEx/LogOutput.log. Flip Enabled while it's on to compare with and without the mod.", advanced: true);
        LogNetwork = Bind(SettingSections.Diagnostics, "LogNetwork", true, "Log network", "Log drawing-sharing connections, handshakes, channel changes, packet counts and rejection reasons to BepInEx/LogOutput.log. No Steam IDs, IPs, note text or coordinates. Enable when troubleshooting sharing.");
        RingsOnCpu = Bind(SettingSections.Diagnostics, "RingsOnCpu", false, "Draw 3D rings on the CPU", "Build the circles' rings in the 3D view on the CPU, as the mod does when its shader file is missing, instead of drawing them with the shader. For comparing the two, or if the rings look wrong on your graphics card.", advanced: true);

        // The beta's testers ran earlier builds with both logs saved off, which a new default doesn't change: they're
        // switched on once, and a tester who turns them off afterwards keeps them off.
        var logsOnFor = Bind(SettingSections.Diagnostics, "LogsTurnedOnFor", string.Empty, "Logs turned on for", "The beta that last switched Log performance and Log network on once.", display: new ConfigurationManagerAttributes { Browsable = false });
        if (logsOnFor.Value != LogsOnBeta)
        {
            LogPerformance.Value = true;
            LogNetwork.Value = true;
            logsOnFor.Value = LogsOnBeta;
        }

        var lookFor = Bind(SettingSections.Diagnostics, "LookUpdatedFor", string.Empty, "Look updated for", "The release that last moved the map's look to its new defaults once (LookUpgrade).", display: new ConfigurationManagerAttributes { Browsable = false });
        if (lookFor.Value != LookUpgrade.Release)
        {
            foreach (var (section, key, _, _) in LookUpgrade.Changes)
            {
                var definition = new ConfigDefinition(section, key);
                if (_config.ContainsKey(definition) &&
                    LookUpgrade.Upgrade(section, key, _config[definition].GetSerializedValue()) is { } upgraded)
                {
                    _config[definition].SetSerializedValue(upgraded);
                }
            }

            lookFor.Value = LookUpgrade.Release;
        }
    }

    /// <summary>The beta whose first start switches both logs on once, whatever an earlier build saved.</summary>
    private const string LogsOnBeta = "0.6.0-beta-4";

    public ConfigEntry<float> OtherChannelsOpacity { get; }
    public ConfigEntry<float> OtherChannelsBrightness { get; }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> LogPerformance { get; }
    public ConfigEntry<bool> LogNetwork { get; }
    public ConfigEntry<bool> RingsOnCpu { get; }

    /// <summary>Drawn as a button in F1, which starts or cancels the test; set true any other way, it does the same and resets.</summary>
    public ConfigEntry<bool> PerfTest { get; }
    public ConfigEntry<Diagnostics.PerfTestSharing> PerfTestSharing { get; }
    public ConfigEntry<int> PerfTestCycles { get; }

    /// <summary>The F1 button's look. <see cref="Diagnostics.PerfTest"/> sets its drawer.</summary>
    public ConfigurationManagerAttributes PerfTestButton { get; } = new() { HideDefaultButton = true };

    public ConfigEntry<bool> MapRunways { get; }
    public ConfigEntry<Color> RunwayColor { get; }
    public ConfigEntry<Color> RunwayLabelColor { get; }
    public ConfigEntry<bool> RunwayBothEnds { get; }
    public ConfigEntry<bool> RunwayPaintedNumbers { get; }
    public ConfigEntry<bool> RunwayNumbersOnRunway { get; }
    public ConfigEntry<float> RunwayNumbersOnRunwayFrom { get; }
    public ConfigEntry<bool> RunwayNumbersTurn { get; }
    public ConfigEntry<float> RunwayLabelSize { get; }
    public ConfigEntry<float> MinimapRunwayLabelSize { get; }
    public ConfigEntry<float> RunwayMinWidth { get; }
    public ConfigEntry<float> RunwayNumbersMinGridSquare { get; }
    public ConfigEntry<Color> OutlineColor { get; }
    public ConfigEntry<float> OutlineWidth { get; }
    public ConfigEntry<float> TextOutlineWidth { get; }

    public ConfigEntry<bool> ShowBoundaries { get; }
    public ConfigEntry<float> BoundaryFillOpacity { get; }
    public ConfigEntry<float> BoundaryEdgeOpacity { get; }

    public ConfigEntry<bool> ShowAirbaseNames { get; }
    public ConfigEntry<Color> AirbaseNameColor { get; }
    public ConfigEntry<float> AirbaseNameSize { get; }

    public ConfigEntry<bool> CentreLine { get; }
    public ConfigEntry<Color> CentreLineColor { get; }
    public ConfigEntry<float> CentreLineWidth { get; }
    public ConfigEntry<float> CentreLineDash { get; }
    public ConfigEntry<float> CentreLineGap { get; }
    public ConfigEntry<float> ApproachRangeKm { get; }
    public ConfigEntry<float> ApproachLineLengthKm { get; }
    public ConfigEntry<Color> ApproachLineColor { get; }
    public ConfigEntry<float> ApproachLineWidth { get; }

    public ConfigEntry<bool> HudCallout { get; }
    public ConfigEntry<bool> HudGearDownOnly { get; }
    public ConfigEntry<bool> HudAirbaseName { get; }
    public ConfigEntry<Color> HudColor { get; }

    public ConfigEntry<bool> ShowMapTools { get; }
    public ConfigEntry<bool> MapSharingEnabled { get; }
    public ConfigEntry<string> MapBroadcastChannels { get; }
    public ConfigEntry<string> MapReceiveChannels { get; }
    public ConfigEntry<bool> MapToolShowOnMinimap { get; }

    /// <summary>
    /// True while the perf test runs: the map tools and their drawings show on both maps as if ShowTools and
    /// ShowOnMinimap were on, without writing either, so a crash mid-test can't save them on.
    /// </summary>
    public bool PerfTestShowsDrawings { get; set; }
    public ConfigEntry<bool> MapToolCirclesIn3D { get; }
    public ConfigEntry<Color> MapToolColor { get; }

    /// <summary>The menu's custom swatches as <c>#RRGGBB</c>, comma-separated (<see cref="MapTools.CustomColors"/>). Hidden from F1, like the sharing lists.</summary>
    public ConfigEntry<string> MapToolCustomColors { get; }
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
    public ConfigEntry<KeyboardShortcut>[] MapChannelKeys { get; }

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
