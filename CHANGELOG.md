# Changelog

## 0.6.0 - 2026-10-31

### Added

- Drawing sharing, off by default. Turn it on in **Tools → Channels**, name a channel, and teammates on your side who run the mod on the same channel see each other's pen lines, circles, notes, waypoint routes and Bearing/range arrows. It works on player-hosted games and Steam dedicated servers, and the server needs no mod. **Receive only** channels let you watch without drawing, and players who both broadcast a channel can erase each other's drawings on it. A drawing on a unit sends only which unit it's on, so each player places it from their own side's tracks. Pointing at a teammate's drawing shows who drew it.
- Channel keys: Shift+1 to Shift+9 and Shift+0 pick the channel new drawings go to. They're rebindable in **F1 → Map Tools / Keys and Limits**.
- Drawings on other channels show fainter, set by **Other channels opacity** and **Other channels brightness**.
- Up to six drawing colours of your own, picked on a colour wheel from the **+** under the rail's swatches.
- Runway numbers move onto the runway when you zoom in, and stay upright. **Map / Runways** has **Numbers on runway**, **On runway from** and **Turn with runway**.
- A dashed centre line down each runway, with 30 m dashes and 60 m gaps that grow as you zoom in, in **Map / Runway Centre Line**.
- Parallel runways get L, C or R, so Feldspar's long runways read 06L and 06R.
- **Number as painted** shows the painted number on the two runways where it differs from the one ATC calls.
- **Minimap number size**, separate from the full map's **Number size**.
- **Map / Outline → Text outline width**.
- **Log network** and a per-part breakdown in **Log performance**, both off by default, and a perf test that can measure sharing between two PCs.

### Changed

- A new default look: black runways with white numbers in a black outline, a centre line, airbase names at size 14, landing zones shown, and the HUD callout naming the airbase. The first start switches each of these still at its old default to the new one, once. Anything you changed stays as you set it.
- Runway numbers match the number ATC calls in its landing clearance.
- Drawing costs much less. A shader draws the 3D view's rings, using a new file, `nomapoverhaul-shaders.bundle`, beside the DLL. Notes draw from shared meshes, drawings off the map switch off, and drawings on units redraw only when a unit moves.
- The 0.6.0 betas switched both logs on to find a multiplayer slowdown. 0.6.0 switches them back off once for anyone who ran one.

### Fixed

- The dark outline around runway numbers, airbase names and notes never drew, because it used the game's additive HUD text material, which can only brighten. It draws now.
- A drawing error no longer switches the whole mod off. The mod rebuilds its graphics after a short pause.
- Map labels on moving units keep their places instead of jumping between slots.
- Label plates stop below Unity's mesh vertex limit instead of failing to draw.
- Leaving a mission undoes each of the mod's changes even when one of them fails.

## 0.5.1 - 2026-10-01

### Changed

- Airbase names move down to the edge of the airbase's capture circle as the full map zooms in, so they stay clear of the base's units.
- Runway numbers on the full map all hide together once the map is zoomed far out, so they don't pile onto the airbases. The minimap always shows them. The cutoff is the advanced setting Map / Runways → Numbers from zoom.

## 0.5.0 - 2026-09-30

The mod is renamed NO Map Overhaul, from Baanish UI Improvements, and is now a map mod.

### Added

- Map tools on the full map (`H`, keys `1` to `6`): bearing and range arrows with the contact's altitude and straight-line range, range circles that also show as rings in the 3D view, text notes that can tag a contact and follow it, a pen, waypoint routes labelled in the 3D view, an eraser, undo, redo, and clear. Right click deletes a drawing or cancels one in progress. Double-tapping the Bearing/range key measures from your own aircraft.
- Drawings on other units use only what your side knows, and freeze and read `lost` when the track goes stale.
- A one-button perf test and an optional performance log.
- An install guide, while the Nuclear Option Mod Manager listing is pending.

### Changed

- Renamed to NO Map Overhaul: new plugin ID, DLL, and settings file. Saved settings carry over from Baanish UI Improvements, and a leftover install of it is switched off with a log line naming the folder to delete.
- F1 settings are grouped into ordered sections, with tuning knobs marked advanced.
- Airbase names show your own side's airbases only, including captured and mission-added ones.

### Removed

- Missile arrows. They are now their own mod, NO Missile Indicators.

## 0.4.0 - 2026-09-30

### Added

- Missile arrows, on by default: a red arrow on the screen edge toward each incoming missile outside the view, on the correct side even when the missile is behind you. Only missiles the game's missile warning knows about get one.

## 0.3.0 - 2026-09-30

### Added

- An option, off by default, to show the HUD runway callout only while the landing gear is down.
- A master switch, `General → Enabled`, that hides everything the mod draws without uninstalling it.

## 0.2.0 - 2026-09-26

### Added

- Optional airbase names on the full map, off by default: a faint label under every airbase, friendly, enemy, and neutral, so a base named on comms is easy to find.

### Changed

- Runway numbers and airbase names get a thin dark rim, so they read at a smaller size: both now default to 8.

## 0.1.0 - 2026-09-26

First release: runway markers.

### Added

- Friendly runways drawn on the minimap and full map as filled strips with a thin dark rim, numbered at each end from the runway heading, with painted numbers overriding where they differ.
- A dashed approach line off the nearest end of the nearest runway, within 5 km. Each dash plus gap is 500 m.
- A `RWY 27` HUD label pinned over that runway's threshold, with an optional abbreviated airbase prefix for ATC calls.
- The approach line and label follow the nearest runway end, with no guess about the landing direction, so they're there while you turn onto final. Earlier heading-based guesses got the end wrong while manoeuvring near the field.
- An optional airbase boundary, off by default: a faint circle in the game's friendly map colour marking where a landed, stopped aircraft counts as returned. It is also the capture zone.
- BepInEx settings for visibility, colours, sizes, ranges, and line length, editable live with Configuration Manager.

### Known limits

- Helicopters see only the airbase boundary; the Tarantula tiltrotor counts as a plane. Carriers are excluded by design.
- The HUD label hides when the threshold is behind the camera.
