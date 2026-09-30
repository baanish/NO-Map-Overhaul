# Changelog

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
