using System;
using BepInEx.Configuration;

namespace BaanishUiImprovements.Diagnostics;

/// <summary>
/// The fields this mod uses from BepInEx ConfigurationManager's standard attributes class. ConfigurationManager finds a
/// setting's tag by this exact type name and copies the public fields it knows, so the mod needs no reference to it.
/// Without ConfigurationManager the tag does nothing.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>Draws the setting's value in the F1 window, inside its OnGUI.</summary>
    public Action<ConfigEntryBase>? CustomDrawer;

    public bool? HideDefaultButton;

    /// <summary>The name F1 shows instead of the config key.</summary>
    public string? DispName;

    /// <summary>Position within the section: F1 lists higher numbers first.</summary>
    public int? Order;

    /// <summary>Hidden until "Advanced settings" is ticked at the top of F1, or a search finds it.</summary>
    public bool? IsAdvanced;

    /// <summary>F1 shows a 0 to 1 range as a whole percentage with no text box unless this is false.</summary>
    public bool? ShowRangeAsPercent;
}
