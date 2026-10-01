# Changelog

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
