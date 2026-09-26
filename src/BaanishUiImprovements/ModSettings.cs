using BepInEx.Configuration;
using UnityEngine;

namespace BaanishUiImprovements;

/// <summary>Every player-facing knob, editable in BepInEx/config or live through ConfigurationManager (F1).</summary>
internal sealed class ModSettings
{
    public ModSettings(ConfigFile config)
    {
        MapRunways = config.Bind("Map", "ShowRunways", true, "Draw friendly runways and their numbers on the minimap and full map.");
        RunwayColor = config.Bind("Map", "RunwayColor", new Color(0f, 0.85f, 0f, 0.85f), "Fill colour of runway strips.");
        RunwayLabelColor = config.Bind("Map", "RunwayLabelColor", new Color(0.6f, 1f, 0.6f, 1f), "Colour of the runway numbers on the map.");
        RunwayBothEnds = config.Bind("Map", "NumberBothEnds", true, "Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from.");
        RunwayLabelSize = config.Bind("Map", "RunwayLabelSize", 13f, new ConfigDescription("Runway number size on the map.", new AcceptableValueRange<float>(4f, 64f)));
        OutlineColor = config.Bind("Map", "OutlineColor", new Color(0.02f, 0.08f, 0.02f, 0.85f), "Dark rim around runways and approach dashes so they stay readable over bright map linework.");
        OutlineWidth = config.Bind("Map", "OutlineWidth", 0.75f, new ConfigDescription("Rim width around runways and approach dashes, in map icon units. 0 turns it off.", new AcceptableValueRange<float>(0f, 6f)));
        RunwayMinWidth = config.Bind("Map", "RunwayMinWidth", 6f, new ConfigDescription("Minimum drawn runway width when zoomed out, in map icon units.", new AcceptableValueRange<float>(1f, 30f)));

        ShowBoundaries = config.Bind("Airbase Boundary", "ShowBoundary", false, "Shade each friendly airbase's landing zone: stop inside it after landing and the sortie ends as returned instead of crashed. The same circle is the capture zone. Uses the game's friendly map colour.");
        BoundaryFillOpacity = config.Bind("Airbase Boundary", "FillOpacity", 0.01f, new ConfigDescription("Opacity of the shaded zone. The game blends in linear colour space, so small values already read strongly over the dark map.", new AcceptableValueRange<float>(0f, 1f)));
        BoundaryEdgeOpacity = config.Bind("Airbase Boundary", "EdgeOpacity", 0.1f, new ConfigDescription("Opacity of the zone's edge line. 0 hides it.", new AcceptableValueRange<float>(0f, 1f)));

        ApproachRangeKm = config.Bind("Approach", "TriggerRangeKm", 5f, new ConfigDescription("Show the approach line and HUD callout for the nearest runway within this distance.", new AcceptableValueRange<float>(0.5f, 30f)));
        ApproachLineLengthKm = config.Bind("Approach", "LineLengthKm", 5f, new ConfigDescription("Length of the dashed centerline drawn off the nearest runway end. Each dash plus gap is 500 m.", new AcceptableValueRange<float>(0.5f, 30f)));
        ApproachLineColor = config.Bind("Approach", "LineColor", new Color(0f, 0.85f, 0f, 0.4f), "Colour of the dashed approach line on the map. Keep it fainter than the runway so the two read apart.");
        ApproachLineWidth = config.Bind("Approach", "LineWidth", 1f, new ConfigDescription("Approach line width, in map icon units.", new AcceptableValueRange<float>(0.5f, 10f)));

        HudCallout = config.Bind("HUD", "ShowRunwayCallout", true, "Label the nearest runway end in the 3D view, for ATC calls.");
        HudAirbaseName = config.Bind("HUD", "IncludeAirbaseName", false, "Prefix the callout with the abbreviated airbase name, e.g. NBSCLI RWY 27.");
        HudColor = config.Bind("HUD", "CalloutColor", new Color(0.2f, 1f, 0.2f, 1f), "Colour of the HUD runway callout.");
    }

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

    public ConfigEntry<float> ApproachRangeKm { get; }
    public ConfigEntry<float> ApproachLineLengthKm { get; }
    public ConfigEntry<Color> ApproachLineColor { get; }
    public ConfigEntry<float> ApproachLineWidth { get; }

    public ConfigEntry<bool> HudCallout { get; }
    public ConfigEntry<bool> HudAirbaseName { get; }
    public ConfigEntry<Color> HudColor { get; }
}
