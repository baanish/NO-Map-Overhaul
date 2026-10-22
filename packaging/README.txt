NO Map Overhaul v@VERSION@

A map mod for Nuclear Option: runways, planning tools, and airbase names.
Up to 0.4.0 it was called Baanish UI Improvements. Its missile arrows are
now a mod of their own, NO Missile Indicators:
https://github.com/baanish/NO-Missile-Indicators

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

Map tools: on the full map, click Tools or press H to open a rail of
planning tools. Keys 1 to 6 pick Bearing/range, Circle, Text, Pen,
Waypoint, and Eraser; Z and Y undo and redo. Measure bearing, range, and a
contact's altitude, draw range circles that also show in the 3D view, tag
contacts with notes that follow them, and fly a waypoint route labelled in
the 3D view. Sharing is optional and off by default. Drawings on other units use only
what your side knows, and read "lost" when your side loses the contact.

Airbase names, off by default: your faction's airbases get a name on the
full map, including ones a mission adds and ones your side captures. Enemy
and neutral airbases get no name, since the game doesn't mark them.

Helicopters get no runways, approach line, or callout, and carriers are left
out. Sharing uses Steam Messages between compatible teammates. A compatibility
patch keeps drawing connection callbacks out of gameplay networking.
Multiplayer reliability is still under test.
On the full map, open Tools > Channels to set Broadcast, Receive only,
and Sharing, then click Apply. Each list takes max 10 comma-separated names. Shift+1 to
Shift+9 or Shift+0 picks only from the Broadcast list. Broadcast channels send
and receive drawings; Receive only adds read-only subscriptions.
Drawings attached to units use each recipient's own tracks and read "lost" at the
last known position, like local ones. Both clients need this updated protocol-6 build. Channel names are not passwords. Members who
broadcast a channel can request a shared erase from another broadcaster with Eraser or
right-click, then redraw a replacement. Receive-only channels stay read-only.
The drawing stays visible until the owner's update arrives. If NOAutopilot is installed, the mod holds back
its right-click waypoints while the map tools rail is open, so a right click
that deletes a drawing doesn't also move the autopilot route. Switch off
General > Enabled to hide everything the mod draws without uninstalling.

Requirements

- Nuclear Option 0.34.2
- BepInEx 5, tested with 5.4.23.4
- BepInEx Configuration Manager, optional, to change settings with F1

Multiplayer playtest

Install this updated build on both clients and the player-host. Older 0.6.0
previews may lack the connection ownership fix or use an incompatible handshake.
Join the same faction in a Steam-hosted mission. Compatibility with an unmodified
host remains unverified. Dedicated servers, UDP sessions, and spectators are not
supported by this sharing preview.

On both clients, open Tools > Channels. Set Broadcast to
"playtest", turn Sharing on, and click Apply. Select playtest under Draw to.
Draw a fixed line and note. Check that the other client sees them, can erase
them, and can draw a replacement. Also check joining after drawings exist
and switching Sharing off and on. For a read-only receiver, leave Broadcast
empty and set Receive only to playtest. Network polling runs at 5 Hz and supports
up to 32 other peers. Channel roles use the honor system.

F1 > Diagnostics > Log network defaults off. Enable it on both clients when
troubleshooting; earlier saved settings persist. It records connections,
handshakes, packet counts, drawing exclusions and rejection reasons in
BepInEx/LogOutput.log, capped at 128 event lines per five seconds. The mod's
Network lines omit Steam IDs, IPs, nonces, channel names, note text and coordinates.
Other mods and the game may log those elsewhere in the same file. Save both
players' LogOutput.log files before restarting the game. Turn Log network
off after testing; this does not disable sharing.

Install

The plugin-only archive contains the BepInEx folder tree. Extract it beside
NuclearOption.exe while the game is closed. It includes meta.json so NOMM
can manage the installed folder. Enable any existing NOMM copy first, then
overwrite its NoMapOverhaul folder. Keep one copy across plugins and
disabledPlugins. Restart NOMM after extracting it. Do not
select the ZIP in NOMM's Add from File; that does not extract archives.
Keep this folder enabled and remove any older standalone NoMapOverhaul.dll
to avoid loading two copies. Sharing starts off on a fresh install.

For a manual install from the flat NOMM archive, copy
NoMapOverhaul.dll and nomapoverhaul-shaders.bundle to:

  BepInEx/plugins/NoMapOverhaul/NoMapOverhaul.dll
  BepInEx/plugins/NoMapOverhaul/nomapoverhaul-shaders.bundle

Start the game once. BepInEx creates the settings file:

  BepInEx/config/com.baanish.nuclearoption.mapoverhaul.cfg

Upgrading from Baanish UI Improvements: close the game and delete the old
BepInEx/plugins/BaanishUiImprovements folder. Your settings carry over on
the first start.

Step-by-step install guide, including BepInEx:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/INSTALL.md

Every setting and the airbase abbreviations are documented here:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/USER_GUIDE.md

How to use the map tools:
https://github.com/baanish/NO-Map-Overhaul/blob/main/docs/MAP_TOOLS.md

Source and full documentation:
https://github.com/baanish/NO-Map-Overhaul

MIT license. See LICENSE.txt, and THIRD_PARTY_NOTICES.txt for code adapted
from NOAutopilot.
