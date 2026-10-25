# Changelog

## 0.6.0 RC 1

- New look by default: black runways with white numbers and a black outline, airbase names (size 14) and landing zones shown, and the HUD callout naming the airbase. The first start moves each of these settings still at its old default to the new one, once. Any you changed stay as you set them.
- The dark outline around runway numbers, airbase names and notes now draws. It used the game's additive HUD text material, which can only brighten, so it never showed. **Map / Outline → Text outline width** sets it, from 0 (none) to 3.
- Zoomed in far enough, each runway number moves onto the runway at its end, the way it's painted, and stays upright. **Map / Runways → Numbers on runway** turns it off, **On runway from** sets how far you zoom in before it happens, and **Turn with runway** turns it to read for a pilot landing there.
- New **Map / Runway Centre Line** settings draw a dashed centre line down each runway, off by default. Dashes are 30 m with 60 m gaps on the ground, so they grow as you zoom in like the paint, and zoomed out the line isn't drawn. Colour, width, dash and gap are all settings.
- Runway number size is set separately for the full map (**Number size**, 14) and the minimap (**Minimap number size**, 8).
- Parallel runways get L, C or R for the side they lie on as you land, so Feldspar's long runways read 06L and 06R instead of two 06s.
- Runway numbers show the number ATC calls. **Map / Runways → Number as painted** shows the paint instead where the two differ: Feldspar's crossing runway (16/34 painted, 15/33 called) and Harmony Sands' southwest runway (14R/32L painted, 13R/31L called).
- Broadcast channel keys can be rebound in **F1 → Map Tools / Keys and Limits**, with **Advanced settings** ticked. **Channel 1 key** to **Channel 10 key** default to Shift+1 through Shift+9 and Shift+0, with either Shift key. Clear a binding to disable its shortcut.

## 0.6.0 beta 6

- Sharing is harder to abuse. A Steam ID that the server lists on more than one player, or on the other faction, gets no drawings, so a modified server can't simply add an enemy's account beside their real player. A teammate flooding messages no longer slows other teammates' drawings, and a snapshot from the wrong session is rejected before its drawings are decoded.
- Notes can't contain line breaks or text-direction characters, which could push a note's text away from where it's attached. A character the map font can't draw shows as a question mark instead of making the game log a warning for each one.
- Pointing at a teammate's drawing on the full map shows who drew it, by their in-game name, beside the cursor. Nobody can pass a drawing off as another teammate's.
- On dedicated servers, drawing sharing uses Steam's relays instead of ICE, Steam's direct-route search, to avoid ICE setup holding Steam's networking lock and freezing the game. This also keeps teammates from learning each other's IP address. On player-hosted games, the mod leaves ICE on for the game's own connection. The mod still requests relay network access once, as soon as sharing could run. Each teammate without the mod is asked once per mission instead of three times, so a big server sees about a third as many connection attempts.

## 0.6.0 beta 5

- Sharing works on Steam dedicated servers as well as player-hosted games. Drawings still go directly between teammates, and the server needs no mod.

## 0.6.0 beta 4

- **Log performance** and **Log network** are on by default for this beta. Its first start switches them on once, even if an earlier build saved them off. After a session that slows down, save `BepInEx/LogOutput.log` before restarting the game.
- New settings **Other channels opacity** and **Other channels brightness** set how faint drawings on other channels look. The defaults keep the old look.
- Drawings cost less, especially teammates' drawings and drawings on units:
  - A drawing on units redraws only when one of them moves.
  - Drawings outside the map's view switch their graphics off.
  - Teammates' drawings are worked out again on the game's 10 Hz map refresh instead of every frame.
  - 3D labels and rings re-batch on their own canvases instead of the game's whole HUD.
  - A shader draws the 3D view's rings instead of the CPU rebuilding them each frame. Diagnostics → Draw 3D rings on the CPU switches back.
  - Map notes draw from shared meshes instead of two graphics each.
  - Map labels are placed against nearby boxes only, and lines, labels and notes build their meshes with less work.
  - Runway markings and the HUD callout stop rewriting positions that didn't change.
- Sharing makes far less garbage, so the game pauses to collect it less often. A teammate's unchanged drawings are kept instead of decoded again, and a change is encoded once for all teammates on the same channels. With three teammates drawing heavily, the mod's allocation fell from about 30 KB a frame to about 5 KB.
- The colour swatches get a second row for up to six colours of your own. **+** opens a colour wheel with a brightness slider and a preview. **Add** saves the colour and draws with it, and a right click removes one. Your colours are kept between games. Add on a colour a swatch already has picks that swatch instead of saving a copy. The rail keeps its three columns beside the map. Its rows sit a little closer, and where the MFD buttons leave too little room, its top rises slightly above the map's.
- **Log performance** now splits the mod's time per frame into its parts, each with its worst frame, and the perf test adds a worst-frame table. Together they find what drags down the 1% low.
- **Log performance** adds a second line with Unity's canvas time and render counters, how often the mod rebuilt labels, meshes and teammates' drawings, the 3D rings drawn, and the session around it. While sharing, it also logs a line with each teammate's drawings, traffic and Steam connection status.
- A drawing error no longer switches the mod off. The mod rebuilds its graphics after a pause, 1 second at first and doubling to 60 while errors repeat, and your drawings, teammates' drawings, and sharing carry on. A text outline no longer throws when its text hasn't been laid out yet. In beta 3 that error switched the mod off.
- Fixed freezes of up to 1.5 seconds inside Steam's receive call. Sharing starts Steam's relay network access early, so Steam doesn't fetch its network config mid-session while holding its networking lock.
- Map labels on moving units keep their places instead of jumping between slots. 3D rings go to the circles in view first, so a circle in front of you always shows its ring. Teammates' drawings keep a stable stacking order, by when each teammate first paired.
- A message from someone who isn't a current teammate closes their drawing session.
- The perf test keeps sharing your drawings and teammates' while it runs, and keeps the waypoint your route was on. Two new advanced settings: **Perf test sharing** measures sharing between two PCs (`Send`, `Receive`, `Bidirectional`), and **Perf test cycles** runs longer tests that log each cycle to show drift. `-nomo-perftest <mode>` starts a test by itself.

## 0.6.0 beta 3

- Circles, notes and waypoint routes attached to units now share, as BRA arrows already did. They carry only the unit's ID, and each recipient places them on its own tracks. Protocol 6 requires this build on both clients.
- Shared drawings on units, BRA arrows included, now stop at the recipient's last known position and read `lost` when the recipient's side loses the unit, as local ones do. They hide only while that side has never known the unit.
- Quitting the game from a mission with sharing on no longer crashes Steam. Drawing sessions close before the game shuts Steam down.
- A teammate's snapshots apply at most once every 0.5 s, and added or changed drawings spend from per-teammate and shared budgets, so a teammate can't keep your map busy rebuilding. An over-budget snapshot waits and still arrives. Over 256 KiB from one teammate in a second resets their drawing channel, and a frame from another version is skipped before it's copied.
- Changed drawings, yours and teammates', redraw in place instead of being rebuilt.

## 0.6.0 beta 2

- A compatibility patch for Steam's transport checks which connection each gameplay callback belongs to, so Mirage can't accept or close drawing connections. If that protection can't load, sharing stays unavailable.
- When Steam reports a failure after a send, the drawing session rotates and backs off for 30 seconds. Incoming handshakes still go through during backoff. A teammate not yet known to run the mod gets three probes per mission, at 0, 60 and 300 seconds, and channel changes don't renew them.
- Paired teammates exchange a small heartbeat every 5 seconds. Full drawings go out again only after an edit, a new pairing, or a heartbeat showing the teammate is behind.
- Handshake, acknowledgement and erase messages are never dropped at the receive limit, and only the newest of a teammate's queued snapshots is decoded. A burst over 40 messages a second resets that teammate's channel instead of quarantining them. A teammate on another protocol version shows as such instead of being quarantined.
- The mod logs a frame gap over 5 seconds and any Steam call blocking past 250 ms, even with network logging off.
- Diagnostics → Log network records sharing handshakes, resets, snapshot counts, exclusions and rejection reasons. It's off by default, and when on it labels peers anonymously and caps its output.
- You can share fixed map drawings and unit-linked BRA arrows with Steam teammates who run a compatible build. Broadcast channels send, receive and collaborate. Receive only channels add read-only subscriptions. Each list holds ten channels, and sharing starts off.
- Tools → Channels on the full map edits both lists and turns sharing on or off. A Draw to indicator and numbered Broadcast buttons pick where new drawings go, so F1 is no longer needed.
- Shift+1 through Shift+9, and Shift+0, pick the Broadcast channel new drawings go to. They never pick a Receive only channel. The tool strip shows where drawings go and whether sharing is on.
- Receive only channels are read-only. Shared routes appear on the map without changing the receiver's navigation.
- Mission, faction, subscription, and peer-session checks reject stale or unrelated packets. Limits on packet size and on received drawings cap the work a teammate can cause.
- Periodic sharing work runs at most five times a second and supports 32 peers. Switching sharing off and local session changes still take effect at once, and a frame stall doesn't trigger a catch-up burst.
- Shared BRA arrows send unit references without the units' positions or altitude. Recipients measure from their own live tracks, and an arrow hides while either end is untracked. Other drawings on units stay local, and incoming fallback coordinates and elevation on anchored drawings are rejected. Protocol 5 requires this build on both clients. Opposing factions, spectators, and dedicated-server sessions receive no drawings in this client-only preview.
- With the Eraser or a right click, a broadcaster can ask the owner to remove another broadcaster's drawing on the same channel. The drawing stays visible until the owner publishes the deletion. To edit someone else's drawing, draw a replacement.
- After a team change, existing drawings and undo history stay local-only. Perf test drawings stay local.
- Drawings on a channel other than the one you're drawing to show half as opaque and darker, yours and teammates'. The Waypoint tool's guide line dims with its route.
- You can have 100 drawings and 2,500 pen points, by default and at most, and routes of up to 50 waypoints, five routes per shared snapshot. If your drawings are more than one snapshot carries, the oldest that fit are shared and the status counts the rest. Teammates' drawings split the received space evenly.
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
