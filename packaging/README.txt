NO Map Overhaul v@VERSION@

Map and HUD navigation aids for Nuclear Option.

Runway markers:

- Friendly runways drawn on the minimap and full map, numbered at each end.
- A dashed approach line off the nearest runway end, within 5 km.
  Each dash plus gap is 500 m.
- A "RWY 27" label over that runway's threshold in the 3D view, with an
  optional airbase prefix for ATC calls, such as "NBSCLI RWY 27". Another
  option hides it until the landing gear is down.
- An optional airbase boundary, off by default, shading each friendly
  airbase's landing zone: stop inside it after landing and the sortie counts
  as returned.

Missile arrows: a red arrow on the screen edge points toward each incoming
missile outside your view, for any missile the game's missile warning
already knows about.

Map tools: on the full map, click Tools or press H to open a rail of
planning tools. Keys 1 to 6 pick Bearing/range, Circle, Text, Pen,
Waypoint, and Eraser; Z and Y undo and redo. Measure bearing, range, and a
contact's altitude, draw range circles that also show in the 3D view, tag
contacts with notes that follow them, and fly a waypoint route labelled in
the 3D view. Only you see what you draw. Drawings on other units use only
what your side knows, and read "lost" when your side loses the contact.

Airbase names, off by default: your faction's airbases get a name on the
full map, including ones a mission adds and ones your side captures. Enemy
and neutral airbases get no name, since the game doesn't mark them.

Helicopters get no runways, approach line, or callout, and carriers are left
out. The mod only draws on your screen. It doesn't patch the game and sends
nothing over the network. If NOAutopilot is installed, the mod holds back
its right-click waypoints while the map tools rail is open, so a right click
that deletes a drawing doesn't also move the autopilot route. Switch off
General > Enabled to hide everything the mod draws without uninstalling.

Requirements

- Nuclear Option 0.34.2
- BepInEx 5, tested with 5.4.23.4
- BepInEx Configuration Manager, optional, to change settings with F1

Install

The plugin-only archive contains the BepInEx folder tree. Extract it beside
NuclearOption.exe.

For a manual install from the flat NOMM archive, copy
NoMapOverhaul.dll to:

  BepInEx/plugins/NoMapOverhaul/NoMapOverhaul.dll

Start the game once. BepInEx creates the settings file:

  BepInEx/config/com.baanish.nuclearoption.mapoverhaul.cfg

Every setting and the airbase abbreviations are documented here:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/USER_GUIDE.md

How to use the map tools:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/MAP_TOOLS.md

Source and full documentation:
https://github.com/baanish/NO-Map-Overhaul

MIT license. See LICENSE.txt, and THIRD_PARTY_NOTICES.txt for code adapted
from NOAutopilot.
