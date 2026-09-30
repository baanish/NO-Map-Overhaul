# Testing

This page separates automated checks from in-game observations. A screenshot from one flight shows the tested setup; it doesn't prove every map or aircraft.

## Automated

```powershell
dotnet run --project tests/NoMapOverhaul.Tests -c Release
```

150 tests pass. They cover:

- the approach: nothing beyond range, the closer end from either side and over the runway, a base leg flown side-on, and the nearest of parallel and crossing runways;
- callout text, painted-number overrides, the abbreviation table, and the fallback abbreviation rule;
- the drawing store: undo and redo, Clear as one step, the shape and point caps, replace, reset, save and restore, and the 100-step history;
- bearing, distance, and altitude text in each unit, which unit the game's setting picks, and the straight-line range, so a target overhead reads its height;
- the eraser and right-click: hit testing lines, rings, and labels (where the map placed them), the topmost shape winning, reach, undoing an erase, and when a right-click is the game's, cancels, or deletes;
- the waypoint route: when a waypoint counts as reached or passed, advancing, restarting, progress across undo, an erase, and undoing back to an older route, the numbered drawing, and the 3D label text;
- the pen: point spacing, simplifying, the point budget, and undo and erase; text notes: typing, Escape, Backspace, the length cap, and placing;
- notes on units: a click putting the note on a unit, following it, reading lost while stale and resuming, going when the unit is destroyed, and undo, redo, and undoing a Clear never bringing it back;
- bearing and range, and circles: clicks and drags, cancelling, presets, the edge staying at the cursor beside a unit, following a unit and freezing when it's lost, and the 3D labels and rings;
- label placement: each kind's candidate slots, a note beside its unit's icon, priority order, keeping a slot, leaders, a label drawn twice, crowded bearings, and the minimap's upright and relayout steps;
- the rail and strip layout: outside the map at 2560x1440, 1920x1080, 3440x1440, and 1280x1024, falling back inside on a square screen, with the map near the screen's top, or with the HUD in the way, the strip's rows and hint wrapping, and each hover tag beside its cell;
- the perf test's frame statistics, report, slice order, per-part timings, and typical and heavy drawings;
- 3D ring projection: near-plane and screen-edge clipping, heading, the chord count, and moving a ring rather than rebuilding it;
- tracked positions: live, stale, first seen stale, and never known;
- settings migration from a real 0.4.0 file, a new value winning, dropping a removed setting, and moving the tool keys;
- carrying settings over from the Baanish UI Improvements file on the first start, without the missile arrows, and never over settings already saved.

The suite doesn't start Unity or the game. It doesn't cover drawing, map scale, font borrowing, input handling, 3D label stacking, the perf test switching the mod and map, or game-version compatibility.

## In-game status for v0.5.0

Tested on Nuclear Option 0.34.2 (Steam build 24724372), Unity 2022.3.62, BepInEx 5.4.23.4, in single-player free flight and the Escalation mission, with other client mods loaded (NOAutopilot among them). Status recorded on 2026-09-26, and on 2026-09-30 for the gear-down callout, the master switch, the map tools and their keys, the settings layout, and the perf test. Everything below was confirmed in game by the author.

| Area | Status | Evidence or remaining work |
| --- | --- | --- |
| Plugin loads | Observed | `NO Map Overhaul <version> loaded.` in `LogOutput.log`. |
| Master switch | Observed | Turning General → Enabled off in F1 removes the runways, approach line, and HUD label. Turning it back on restores them. Airbase names and the boundary weren't turned on for this check. |
| Strip alignment | Observed | North Boscali (Heartland): strips sit on the runway outlines painted on the map, both runways. |
| Runway numbers | Observed | North Boscali 03, 21, 12, 30 at the correct ends in the HUD font; Opal with both ends numbered. |
| Contrast on busy map art | Observed | Feldspar: the dark rim keeps strips visible over bright linework. |
| Dashed approach line | Observed | Opal: dashes off the lined-up end at the approach width and opacity. |
| Carriers excluded | Observed | No runway graphics on ships. |
| HUD callout | Observed | `RWY xx` sits below the lined-up threshold, with and without IncludeAirbaseName. |
| Callout with gear down only | Observed | With OnlyWithGearDown on, the label stays hidden with the gear up and shows once the gear lever is down. |
| Map tools rail | Observed | Every tool, swatch, Undo, Redo, and Clear work; left clicks go to the tool and not to unit selection; a Pen drag doesn't pan the map. |
| Rail and strip placement | Observed | The rail stands left of the map and the strip above it, both outside the map, and the strip stays clear of the mission clock; each hover tag shows beside the cell it names. |
| Map tool keys | Observed | `H` opens and closes the rail; `1` to `6` pick Bearing/range, Circle, Text, Pen, Waypoint, and Eraser, opening the rail when it's closed. |
| Waypoint route | Observed | Numbered route with the next two waypoints labelled in the 3D view; the route moves on as waypoints are flown; Restart goes back to waypoint 1 without skipping waypoints behind; undo back to an erased route resumes its progress; markers clip at the minimap edge. |
| Text notes and typing | Observed | Typing a note with W, A, S, D, and M leaves the aircraft alone, and the controls, the Escape pause menu, and NOAutopilot's hotkeys work again after Enter. |
| Notes on units | Observed | A note placed on a contact follows it on the map and in the 3D view, reads `lost` when the track goes stale, and goes when the contact is destroyed. |
| 3D labels on one unit | Observed | A bearing label and a note on the same unit stack in the 3D view instead of overlapping, with the bearing on top. |
| Bearing, circle, and 3D rings | Observed | Arrows, circles, and labels draw flat and thin in the default green; a circle's radius stays at the cursor when dragged over a unit; circles show as rings in the 3D view. |
| BRA labels | Observed | An arrow ending on a unit adds the unit's altitude to its label, and the range is the straight line through the air to it. |
| Right click | Observed | With the rail open, right click cancels a drawing in progress or deletes a drawing, and NOAutopilot places no waypoint; with a friendly unit selected, the game's move order wins; with the rail closed, NOAutopilot's right click works. |
| Map labels on the turning minimap | Observed | Bearings, radii, and close waypoint numbers stay upright, clear of each other and of their arrowheads while the minimap turns; plated text renders sharp. |
| Fair play | Observed | An arrow tied to an enemy freezes and reads `lost` about 4 s after the radar track breaks, and resumes on reacquiring it. |
| Airbase names, friendly only | Observed | Only your own side's airbases get names, including captured and mission-added ones. |
| Settings layout | Observed | F1 shows the grouped sections in order, with the advanced rows behind "Advanced settings"; saved values carried over to the new sections. |
| Perf test | Observed | Escalation, level flight, interleaved slices. Frame rate against the mod off: no drawings -1.3% (minimap) and -0.6% (full map); typical drawings, 11 shapes, -6.3% and +2.5% (within noise); heavy drawings, 71 shapes, -17.8% and -6.3%. |
| Approach follows the nearest end | Observed | The line and callout show off the nearest runway end, including on a base leg flown side-on to the runway. Earlier heading-based rules failed in game three times: a closed pattern switched ends, circling flip-flopped, and final-only hid the line during the turn onto final. |
| Helicopter hides runways | Observed | A helicopter sees no strips, line, or callout; the Tarantula tiltrotor sees runways. |
| Minimap rotation | Observed | Numbers stay upright while the heading-up minimap turns. |
| Airbase capture | Observed | Runways appear and disappear as an airbase changes faction. |
| Airbase boundary | Observed | The circle centers on each friendly airbase, also in a helicopter, and the tuned 1% fill and 10% edge read well. |
| Harmony Sands takeoff-only lane | Observed | The takeoff-only lane is hidden; its garbled custom-name label is gone. |
| Feldspar painted numbers | Observed | Ignus: 16/34 on the crossing runway, and the free-flight mission's duplicate airbases carry the same numbers. Other fields' paint hasn't been compared. |
| Airbase names | Observed | Full map with ShowNames on: one name per airbase under its centre, clear of the friendly icon, and Ignus free flight's three Feldspar airbases share one label. |
| Map text rim | Observed | Runway numbers and airbase names at size 8 read over bright map linework with the 1-unit dark rim. |
| Multiplayer | Observed for v0.4.0 | A multiplayer session showed no problems. Join direction and lobby size weren't recorded. The v0.5.0 map tools haven't been flown in multiplayer yet. |

"Observed" means seen in game during manual testing. It doesn't mean an automated test covers it.
