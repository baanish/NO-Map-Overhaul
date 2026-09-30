# Testing

This page separates automated checks from in-game observations. A screenshot from one flight shows the tested setup; it doesn't prove every map or aircraft.

## Automated

```powershell
dotnet run --project tests/BaanishUiImprovements.Tests -c Release
```

121 tests pass. They cover:

- the approach: nothing beyond range, the closer end from either side and over the runway, a base leg flown side-on, and the nearest of parallel and crossing runways;
- callout text, painted-number overrides, the abbreviation table, and the fallback abbreviation rule;
- missile arrow placement: none on screen, the matching edge off screen, and the correct side for a missile behind you;
- the drawing store: undo and redo, Clear as one step, the shape and point caps, replace, reset, save and restore, and the 100-step history;
- bearing and distance text in each unit, and which unit the game's setting picks;
- the eraser and right-click: hit testing lines, rings, and labels (where the map placed them), the topmost shape winning, reach, undoing an erase, and when a right-click is the game's, cancels, or deletes;
- the waypoint route: when a waypoint counts as reached or passed, advancing, restarting, progress across undo, an erase, and undoing back to an older route, the numbered drawing, and the 3D label text;
- the pen: point spacing, simplifying, the point budget, and undo and erase; text notes: typing, Escape, Backspace, the length cap, and placing;
- bearing and range, and circles: clicks and drags, cancelling, presets, the edge staying at the cursor beside a unit, following a unit and freezing when it's lost, and the 3D labels and rings;
- label placement: each kind's candidate slots, priority order, keeping a slot, leaders, a label drawn twice, and the minimap's upright and relayout steps;
- the perf test's frame statistics, report, slice order, per-part timings, and heavy drawings;
- 3D ring projection: near-plane and screen-edge clipping, heading, and the chord count;
- tracked positions: live, stale, first seen stale, and never known;
- settings migration from a real 0.4.0 file, a new value winning, and dropping a removed setting.

The suite doesn't start Unity or the game. It doesn't cover drawing, map scale, font borrowing, input handling, the perf test switching the mod and map, or game-version compatibility.

## In-game status for v0.4.0

Tested on Nuclear Option 0.34.2 (Steam build 24724372), Unity 2022.3.62, BepInEx 5.4.23.4, in single-player free flight, with other client mods loaded. Status recorded on 2026-09-26, and on 2026-09-30 for the gear-down callout, the master switch, and missile arrows. Everything below was confirmed in game by the author.

| Area | Status | Evidence or remaining work |
| --- | --- | --- |
| Plugin loads | Observed | `Baanish UI Improvements <version> loaded.` in `LogOutput.log`. |
| Master switch | Observed | Turning General → Enabled off in F1 removes the runways, approach line, and HUD label. Turning it back on restores them. Airbase names and the boundary weren't turned on for this check. |
| Strip alignment | Observed | North Boscali (Heartland): strips sit on the runway outlines painted on the map, both runways. |
| Runway numbers | Observed | North Boscali 03, 21, 12, 30 at the correct ends in the HUD font; Opal with both ends numbered. |
| Contrast on busy map art | Observed | Feldspar: the dark rim keeps strips visible over bright linework. |
| Dashed approach line | Observed | Opal: dashes off the lined-up end at the approach width and opacity. |
| Carriers excluded | Observed | No runway graphics on ships. |
| HUD callout | Observed | `RWY xx` sits below the lined-up threshold, with and without IncludeAirbaseName. |
| Callout with gear down only | Observed | With OnlyWithGearDown on, the label stays hidden with the gear up and shows once the gear lever is down. |
| Missile arrows | Partly observed | An arrow appeared for an incoming missile off screen. Getting shot down then disabled the mod, a bug since fixed. Surviving a shoot-down and respawn, a missile behind you, and two missiles at once aren't checked in game yet. |
| Approach follows the nearest end | Observed | The line and callout show off the nearest runway end, including on a base leg flown side-on to the runway. Earlier heading-based rules failed in game three times: a closed pattern switched ends, circling flip-flopped, and final-only hid the line during the turn onto final. |
| Helicopter hides runways | Observed | A helicopter sees no strips, line, or callout; the Tarantula tiltrotor sees runways. |
| Minimap rotation | Observed | Numbers stay upright while the heading-up minimap turns. |
| Airbase capture | Observed | Runways appear and disappear as an airbase changes faction. |
| Airbase boundary | Observed | The circle centers on each friendly airbase, also in a helicopter, and the tuned 1% fill and 10% edge read well. |
| Harmony Sands takeoff-only lane | Observed | The takeoff-only lane is hidden; its garbled custom-name label is gone. |
| Feldspar painted numbers | Observed | Ignus: 16/34 on the crossing runway, and the free-flight mission's duplicate airbases carry the same numbers. Other fields' paint hasn't been compared. |
| Airbase names | Observed | Full map with ShowNames on: one name per airbase under its centre, clear of the friendly icon, and Ignus free flight's three Feldspar airbases share one label. |
| Map text rim | Observed | Runway numbers and airbase names at size 8 read over bright map linework with the 1-unit dark rim. |
| Multiplayer | Observed | A multiplayer session showed no problems. Join direction and lobby size weren't recorded. |

"Observed" means seen in game during manual testing. It doesn't mean an automated test covers it.
