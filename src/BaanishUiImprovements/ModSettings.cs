using BepInEx.Configuration;
using UnityEngine;

namespace BaanishUiImprovements;

/// <summary>Every player-facing knob, editable in BepInEx/config or live through ConfigurationManager (F1).</summary>
internal sealed class ModSettings
{
    public ModSettings(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Off hides everything this mod draws, as if it were not installed. Useful for checking whether a problem comes from the mod or the game.");

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
    }

    public ConfigEntry<bool> Enabled { get; }

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
}
