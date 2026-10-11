# Changelog

## 0.6.0 - local preview

- A Steam transport compatibility patch filters gameplay callbacks by connection ownership, preventing Mirage from accepting or closing drawing connections. Sharing stays unavailable if that protection cannot load.
- Asynchronous Steam failures rotate the drawing session and back off for 30 seconds. Incoming handshakes remain eligible during backoff. A teammate not yet known to run the mod gets three probes per mission, at 0, 60 and 300 seconds, and channel changes don't renew them.
- Paired teammates exchange a small heartbeat every 5 seconds; full drawings are resent only after an edit, a new pairing, or a heartbeat showing the teammate is behind.
- Handshake, acknowledgement and erase messages are never dropped at the receive limit, and only the newest of a teammate's queued snapshots is decoded. A burst over 40 messages a second resets that teammate's channel instead of quarantining them. A teammate on another protocol version is shown as such rather than quarantined.
- A frame gap over 5 seconds and any Steam call blocking past 250 ms are logged, with network logging off.
- Diagnostics → Log network records sharing handshakes, resets, snapshot counts, exclusions and rejection reasons. Off by default, with anonymous peer labels and bounded output when enabled.

- Optional sharing of fixed map drawings and unit-linked BRA arrows with compatible Steam teammates. Broadcast channels send, receive and collaborate; Receive only adds read-only subscriptions. Each list supports ten channels; sharing starts off.
- Tools → Channels on the full map edits both lists and sharing on/off. A visible Draw to indicator and numbered Broadcast buttons select the drawing destination; F1 is no longer needed.
- Shift+1 through Shift+9, and Shift+0, select only Broadcast channels for subsequent drawings. The tool strip shows the destination and sharing status.
- Receive-only channels are read-only. Shared routes appear on the map without changing the receiver's navigation.
- Mission, faction, subscription, and peer-session checks reject stale or unrelated packets. Bounded packets and received-drawing budgets limit remote work.
- Periodic sharing work runs at most five times per second and supports 32 peers. Disablement and local session changes remain immediate; frame stalls do not trigger catch-up batches.
- Shared BRA arrows transmit unit references without unit positions or altitude. Recipients calculate measurements from their own live tracks; arrows hide while either endpoint is untracked. Other unit-anchored drawings stay local-only, and incoming anchored fallback coordinates and elevation are rejected. Protocol 5 requires this updated build on both clients. Opposing factions, spectators, and dedicated-server sessions receive no drawings in this client-only preview.
- Eraser and right-click allow broadcasters to request removal of another broadcaster's drawing on the same channel through its owner. The drawing remains visible until the owner publishes the deletion; redraw a replacement to edit it.
- A team change keeps existing drawings and undo history local-only. Performance-test drawings stay local.
- Drawings on a channel other than the one you're drawing to show half as opaque and darker, yours and teammates', with the Waypoint tool's guide line dimming with its route.
- Limits: 100 drawings and 2,500 pen points by default and at most; routes of up to 50 waypoints, five per shared snapshot. If your drawings exceed what one snapshot carries, the oldest that fit are shared and the status counts the rest. Teammates' drawings share the received space evenly.
- The Waypoint tool keeps working on the route you fly when the drawing channel changes. The Text tool refuses notes with nothing visible in them.
- Label plates stop below Unity's mesh vertex limit instead of failing to draw.

Built for private playtesting. Two-PC Steam tests covered pairing in both join orders, a teammate without the mod as client and as host, opposite teams, reconnects, channel changes, late enablement, switching sharing off and on, owner and collaborative erase, and received drawings on the map, minimap and HUD.

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
