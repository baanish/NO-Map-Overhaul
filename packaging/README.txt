NO Map Overhaul v@VERSION@

A map mod for Nuclear Option: runways, planning tools, and airbase names.
Up to 0.4.0 it was called Baanish UI Improvements. Its missile arrows are
now a mod of their own, NO Missile Indicators:
https://github.com/baanish/NO-Missile-Indicators

Switch off General > Enabled to hide everything the mod draws without
uninstalling.

Runway markers

- Friendly runways drawn on the minimap and full map, numbered at each end.
- A dashed approach line off the nearest runway end, within 5 km.
  Each dash plus gap is 500 m.
- A label over that runway's threshold in the 3D view for ATC calls, with
  the airbase, such as "NBSCLI RWY 27". Options drop the airbase and hide
  the label until the landing gear is down.
- An airbase boundary, shading each friendly
  airbase's landing zone. Stop inside it after landing and the sortie counts
  as returned.

Helicopters get no runways, approach line, or callout, and carriers are left
out.

Map tools

On the full map, click Tools or press H to open a rail of planning tools.
Keys 1 to 6 pick Bearing/range, Circle, Text, Pen, Waypoint, and Eraser.
Z undoes and Y redoes. Measure bearing, range, and a contact's altitude,
draw range circles that also show in the 3D view, tag contacts with notes
that follow them, and fly a waypoint route labelled in the 3D view.
Drawings on other units use only what your side knows, and read "lost"
when your side loses the contact.

If NOAutopilot is installed, the mod holds back its right-click waypoints
while the map tools rail is open, so a right click that deletes a drawing
doesn't also move the autopilot route.

Airbase names

Your faction's airbases get a name on the full map,
including ones a mission adds and ones your side captures. Enemy and
neutral airbases get no name, since the game doesn't mark them.

Sharing drawings

Sharing is off by default. Turn it on and name a channel, such as "viper",
and teammates on your side who run the mod on the same channel see each
other's drawings. It works on player-hosted games and Steam dedicated
servers, and the server needs no mod. UDP servers and spectators get none.
Everyone who shares needs 0.6.0. Channel names are not passwords.

On the full map, press H, click Channels, type a name under Broadcast, turn
Sharing on, and click Apply. The sharing guide covers the rest:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/SHARING_GUIDE.md

Requirements

- Nuclear Option 0.34.2
- BepInEx 5, tested with 5.4.23.4
- BepInEx Configuration Manager, optional, to change settings with F1

Reporting a sharing problem

Both players turn on F1 > Diagnostics > Log network, reproduce the problem,
and save BepInEx/LogOutput.log before restarting the game. The mod's
Network lines leave out Steam IDs, IP addresses, channel names, note text,
and positions. Other mods and the game may log those elsewhere in the same
file, so read it before posting it.

Install

The plugin-only archive contains the BepInEx folder tree. Extract it beside
NuclearOption.exe while the game is closed. It includes meta.json so NOMM
can manage the installed folder. If NOMM has a copy switched off, enable it
first, then overwrite its NoMapOverhaul folder. Keep one copy across plugins
and disabledPlugins, and remove any older standalone NoMapOverhaul.dll so
two copies don't load. Restart NOMM after extracting. Don't select the ZIP
in NOMM's Add from File, which doesn't extract archives.

For a manual install from the flat NOMM archive, copy NoMapOverhaul.dll and
nomapoverhaul-shaders.bundle to:

  BepInEx/plugins/NoMapOverhaul/NoMapOverhaul.dll
  BepInEx/plugins/NoMapOverhaul/nomapoverhaul-shaders.bundle

Start the game once. BepInEx creates the settings file:

  BepInEx/config/com.baanish.nuclearoption.mapoverhaul.cfg

Upgrading from Baanish UI Improvements: close the game and delete the old
BepInEx/plugins/BaanishUiImprovements folder. Your settings carry over on
the first start.

Step-by-step install guide, including BepInEx:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/INSTALL.md

Every setting and the airbase abbreviations:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/USER_GUIDE.md

How to use the map tools:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/MAP_TOOLS.md

Source and full documentation:
https://github.com/baanish/NO-Map-Overhaul

MIT license. See LICENSE.txt, and THIRD_PARTY_NOTICES.txt for code adapted
from NOAutopilot.
