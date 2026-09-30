# Testing

This page separates automated checks from in-game observations. A screenshot from one flight shows the tested setup; it doesn't prove every map or aircraft.

## Automated

```powershell
dotnet run --project tests/BaanishUiImprovements.Tests -c Release
```

9 tests pass. They cover:

- nothing beyond range, and the closer end from either side, including over the runway;
- a base leg flown side-on to the runway showing the approach end;
- the nearest runway winning between parallel and crossing runways;
- callout text, painted-number overrides, the abbreviation table, and the fallback abbreviation rule.

The suite doesn't start Unity or the game. It doesn't cover drawing, map scale, font borrowing, or game-version compatibility.

## In-game status for v0.2.0

Tested on Nuclear Option 0.34.2 (Steam build 24724372), Unity 2022.3.62, BepInEx 5.4.23.4, in single-player free flight, with other client mods loaded. Status recorded on 2026-09-26. Everything below was confirmed in game by the author.

| Area | Status | Evidence or remaining work |
| --- | --- | --- |
| Plugin loads | Observed | `Baanish UI Improvements <version> loaded.` in `LogOutput.log`. |
| Strip alignment | Observed | North Boscali (Heartland): strips sit on the runway outlines painted on the map, both runways. |
| Runway numbers | Observed | North Boscali 03, 21, 12, 30 at the correct ends in the HUD font; Opal with both ends numbered. |
| Contrast on busy map art | Observed | Feldspar: the dark rim keeps strips visible over bright linework. |
| Dashed approach line | Observed | Opal: dashes off the lined-up end at the approach width and opacity. |
| Carriers excluded | Observed | No runway graphics on ships. |
| HUD callout | Observed | `RWY xx` sits below the lined-up threshold, with and without IncludeAirbaseName. |
| Callout with gear down only | Observed | With OnlyWithGearDown on, the label stays hidden with the gear up and shows once the gear lever is down. |
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
