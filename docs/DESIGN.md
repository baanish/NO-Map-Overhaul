# How it works

The plugin draws on top of the game and never changes its state. It has no Harmony patches. On each of the game's own map refreshes (ten times a second, plus every frame while the player pans or zooms the full map) it reads the game's objects, works out what to show, and updates a few UI elements it owns. If a game update breaks something it calls, the plugin logs the error, disables itself, and leaves the game running untouched. If one breaks the HUD label it copies by reflection, it logs a warning at startup and that feature stays off.

## One map refresh

```text
Plugin.RefreshMap   (on DynamicMap.onMapChanged: 10 Hz, and per frame during pan or zoom)
  ├─ collect runways    FactionHQ.GetAirbases() of DynamicMap.HQ, carriers skipped
  ├─ select approach    ApproachSelector.Select (pure logic, unit-tested)
  ├─ RunwayMapOverlay   strips, numbers, dashed approach line
  ├─ AirbaseBoundaryOverlay   landing-zone circles (off by default)
  └─ AirbaseLabelOverlay      every airbase's name, full map only (off by default)

Plugin.LateUpdate   (every frame)
  ├─ RunwayHudCallout.Render            "RWY 27" follows the camera
  └─ RunwayMapOverlay.KeepLabelsUpright numbers counter-rotate the minimap
```

`DynamicMap.onMapChanged` is the event the game raises from its own map update, the same 10 Hz cadence that refreshes its airbase icons. It also fires every frame while the player pans or zooms the full map, which keeps zoom-dependent sizes current. The handler runs inside the game's update, so every exception is caught there and disables the plugin rather than breaking the map. Two things still run every frame in `LateUpdate`: the HUD label, which tracks the camera, and the upright rotation of the numbers, which the heading-up minimap turns every frame. Runways are re-read on every refresh, so captured airbases need no event wiring.

## Game data it reads

| What | Where in `Assembly-CSharp` |
| --- | --- |
| Friendly airbases | `DynamicMap.HQ.GetAirbases()` |
| Runway ends, width, flags | `Airbase.Runway.Start`, `.End`, `.GetWidth()`, `.Reversable`, `.Landing` |
| Runway numbers | The runway heading, rounded the way `Airbase.Runway.GetName` does for an unnamed runway, then `RunwayNames.PaintedNumbers` |
| Carrier check | `Airbase.AttachedAirbase` |
| Local aircraft | `SceneSingleton<CombatHUD>.i.aircraft` |
| Helicopter check | `aircraft.GetControlsFilter() is HeloControlsFilter` and no `TiltWingController`, since the Tarantula tiltrotor uses helicopter controls too |
| On the ground | `aircraft.radarAlt < 1`, the same threshold as the game's touchdown check |

Only runways with `Landing` set are drawn. The stock game's one takeoff-only runway, Harmony Sands' "Runway14L Short", lies on top of a landing runway. The painted runway numbers live in the level textures, not in data. So `RunwayNames.PaintedNumbers` records each runway end where the paint differs from the heading number, keyed by airbase and heading number rather than runway index, since a mission can reorder an airbase's runways. Each entry is checked in game. The runway `name` field is ignored. A mission can set it, and Ignus free flight sets names that disagree with the paint ("Runway 07" on a runway painted 06). That mission also adds two airbases on top of the stock Feldspar, with runways lying exactly over the stock ones. With heading numbers, the duplicates carry the same labels and overlap cleanly. `tools/dump_runways.py` prints every airbase's runways from the asset files: name, flags, ends, and the heading number the game would compute.

A runway's `Start` is the end you land at when not reversed. `RunwayUsage.GetStart()` returns the threshold for either direction.

## Drawing on the map

`DynamicMap` is a single object that the game moves between the minimap anchor and the full-map anchor. The overlay parents one layer under `DynamicMap.iconLayer`, so it inherits pan, zoom, and the minimap's heading-up rotation. Setting the layer as first sibling keeps unit icons on top.

Positions follow the game's icon convention. Map-local `(x, y)` is the global `(x, z)` times `DynamicMap.mapDisplayFactor`. The overlay multiplies widths and font sizes by `1 / mapImage.localScale.x`, the inverse map scale, so they stay a constant size on screen as the player zooms. The game's unit icons use the same factor. Strips and dashes are `FeatheredRect` meshes and the numbers are `TextMeshProUGUI`. The map canvas has no anti-aliasing, so every hard edge on a rotated shape stair-steps. `EdgeProfile` gives each shape its edge as nested rings: fill, the dark rim, then a fade to transparent, with each colour change spread across about one screen pixel. The boundary's `CircleGraphic` uses the same profile. The numbers reset their world rotation every frame so they stay upright while the minimap rotates.

Map text (runway numbers and airbase names) is an `OutlinedText`: the font and style of the HUD label the callout clones, with a one-unit rim in `OutlineColor` made of eight dark copies offset around the face. TextMeshPro's own SDF outline doesn't work with this font. An outline thin enough to fit its atlas padding eats into the thin glyphs, and a wider one draws each glyph's quad as a box. The copies sit in a container scaled by the inverse map scale, so zooming changes no font size and regenerates no text meshes.

## The airbase boundary

The game ends a sortie as returned when a landed aircraft stops within `Airbase.GetRadius()` of `Airbase.center`, checked by `FactionHQ.AnyNearAirbase` in `Aircraft.ServerDisableUnit`. `GetRadius()` returns `SavedAirbase.CaptureRange`, so the landing zone and the capture zone are one circle. `CircleGraphic` draws it as UI mesh, a filled disk plus an anti-aliased edge ring. The ring keeps a constant width at any zoom, which a scaled sprite couldn't do. The boundary layer re-takes the first sibling slot every frame, so it always sits under the runways.

## Airbase names

The game's `AirbaseMapIcon` exists only for `DynamicMap.HQ.GetAirbases()`, the local faction's, and shows only while `DynamicMap.mapMaximized`. The names cover every airbase in `FactionRegistry.airbaseLookup` instead, skipping carriers and `Airbase.disabled`, with one label per `DisplayName` (friendly airbases claim theirs first, so it lands under the icon; Ignus free flight has three Feldspar airbases), and follow the icon's full-map-only rule. The full map is north-up, so each label sits a fixed distance below `Airbase.center` in map space and needs no per-frame rotation. The text is `SavedAirbase.DisplayName`, the same name the game's map tooltip shows.

## The HUD callout

`AirbaseOverlay` is the game's landing guide. Its private `airbaseLabel` field, read by reflection, is the "Taxi to Runway 21" text. The callout clones that label into the same parent, so it inherits the HUD canvas, font, and visibility. The callout projects the threshold with `CameraStateManager.mainCamera.WorldToScreenPoint`. It looks for the overlay at most once a second, including inactive objects, so the map numbers can get the font before the HUD is visible.

The mod never calls `AirbaseOverlay`'s landing methods, because they send network commands and change runway usage.

## Choosing the approach

`ApproachSelector` works on flat 2D runway lines (`RunwayLine`, meters, X east, Y north) and knows nothing about Unity, so the tests run without the game. It picks the nearest runway within range, measured to the strip, and the end of it closer to the pilot. It ignores heading on purpose: the line exists to judge the turn onto final, so it must show while the pilot is still side-on to the runway. Every heading-based rule tried before this (pattern-leg inference, then final-only) either picked the wrong end while manoeuvring or hid the line when it was needed.
