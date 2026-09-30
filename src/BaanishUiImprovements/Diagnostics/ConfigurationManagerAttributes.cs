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
}
