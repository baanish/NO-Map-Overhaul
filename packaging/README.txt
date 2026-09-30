Baanish UI Improvements v@VERSION@

Map and HUD navigation aids for Nuclear Option. This release adds runway
markers:

- Friendly runways drawn on the minimap and full map, numbered at each end.
- A dashed approach line off the nearest runway end, within 5 km.
  Each dash plus gap is 500 m.
- A "RWY 27" label over that runway's threshold in the 3D view, with an
  optional airbase prefix for ATC calls, such as "NBSCLI RWY 27". Another
  option hides it until the landing gear is down.

An optional airbase boundary, off by default, shades each friendly airbase's
landing zone: stop inside it after landing and the sortie counts as returned.

Optional airbase names, off by default, label every airbase on the full map,
friendly, enemy, and neutral, so a base called out on comms is easy to find.

Helicopters see only the airbase boundary and names, and carriers are left out. The mod
only draws. It patches no game code and sends nothing over the network.
Switch off General > Enabled to hide everything it draws without uninstalling.

Coming soon: map markers and drawing, waypoints with HUD markers, and an
incoming-missile direction arrow.

Requirements

- Nuclear Option 0.34.2
- BepInEx 5, tested with 5.4.23.4
- BepInEx Configuration Manager, optional, to change settings with F1

Install

The plugin-only archive contains the BepInEx folder tree. Extract it beside
NuclearOption.exe.

For a manual install from the flat NOMM archive, copy
BaanishUiImprovements.dll to:

  BepInEx/plugins/BaanishUiImprovements/BaanishUiImprovements.dll

Start the game once. BepInEx creates the settings file:

  BepInEx/config/com.baanish.nuclearoption.uiimprovements.cfg

Every setting and the airbase abbreviations are documented here:
https://github.com/baanish/baanish-ui-improvements/blob/main/docs/USER_GUIDE.md

Source and full documentation:
https://github.com/baanish/baanish-ui-improvements

MIT license. See LICENSE.txt.
