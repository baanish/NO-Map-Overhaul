# How it works

The plugin draws on top of the game and never changes its state, apart from switching the player's controls off while a map tool takes the mouse or keyboard. It has no Harmony patches. On each of the game's own map refreshes (ten times a second, plus every frame while the player pans or zooms the full map) it reads the game's objects, works out what to show, and updates a few UI elements it owns. If a game update breaks something it calls, the plugin logs the error, disables itself, and leaves the game running untouched. If one breaks the HUD label it copies by reflection, it logs a warning at startup and that feature stays off.

## One map refresh

```text
Plugin.RefreshMap   (on DynamicMap.onMapChanged: 10 Hz, and per frame during pan or zoom)
  ├─ collect runways    FactionHQ.GetAirbases() of DynamicMap.HQ, carriers skipped
  ├─ select approach    ApproachSelector.Select (pure logic, unit-tested)
  ├─ RunwayMapOverlay   strips, numbers, dashed approach line
  ├─ AirbaseLabelOverlay      every airbase's name, full map only (off by default)
  └─ AirbaseBoundaryOverlay   landing-zone circles (off by default)

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

The game's `AirbaseMapIcon` exists only for `DynamicMap.HQ.GetAirbases()`, the local faction's, and shows only while `DynamicMap.mapMaximized`. The names cover every airbase in `FactionRegistry.airbaseLookup` instead, skipping carriers, `Airbase.disabled`, and airbases with no `center` to place a label at, with one label per `DisplayName` (friendly airbases claim theirs first, so it lands under the icon; Ignus free flight has three Feldspar airbases), and follow the icon's full-map-only rule. The full map is north-up, so each label sits a fixed distance below `Airbase.center` in map space and needs no per-frame rotation. The text is `SavedAirbase.DisplayName`, the same name the game's map tooltip shows. Like the boundary, the names layer re-takes the icon layer's first slot every refresh, and it renders just before the boundary, so the names sit over the boundary and under the runways and unit icons.

## The HUD callout

`AirbaseOverlay` is the game's landing guide. Its private `airbaseLabel` field, read by reflection, is the "Taxi to Runway 21" text. The callout clones that label into the same parent, so it inherits the HUD canvas, font, and visibility. The callout projects the threshold with `CameraStateManager.mainCamera.WorldToScreenPoint`. It looks for the overlay at most once a second, including inactive objects, so the map numbers can get the font before the HUD is visible.

The mod never calls `AirbaseOverlay`'s landing methods, because they send network commands and change runway usage.

## Choosing the approach

`ApproachSelector` works on flat 2D runway lines (`RunwayLine`, meters, X east, Y north) and knows nothing about Unity, so the tests run without the game. It picks the nearest runway within range, measured to the strip, and the end of it closer to the pilot. It ignores heading on purpose: the line exists to judge the turn onto final, so it must show while the pilot is still side-on to the runway. Every heading-based rule tried before this (pattern-leg inference, then final-only) either picked the wrong end while manoeuvring or hid the line when it was needed.

## Map tools

A Tools button on the full map opens a menu of drawing tools. What they draw shows on both maps and, for some shapes, in the 3D view. Everything lives under `src/BaanishUiImprovements/MapTools/`, and `MapToolHost` ties it together:

```text
Plugin.LateUpdate → MapToolHost.Update   (every frame)
  ├─ mission check     a different DynamicMap, or none, clears the ShapeStore
  ├─ MapToolMenu       Tools button and menu on the full map; a click records a MenuCommand
  ├─ MapToolInput      left clicks, drags, and typing to the active MapTool
  ├─ undo and redo keys, while the full map is open
  ├─ MapTool.OnFrame   every tool, every frame: live state and 3D labels (WorldLabelPool)
  └─ MapShapeLayer     redraws what changed; live shapes and overlays on the 10 Hz refresh
```

### Shapes and the store

A drawing is a `MapShape` subclass holding plain data only: meters (X east, Y north, the game's global x and z), unit anchors by `PersistentID.Id`, and colour bytes. None of it is a Unity type, so drawings could be saved or shared with other players later without a rewrite. A `MapPoint` with a unit id follows that unit. `ShapeStore` holds every tool's shapes in draw order. Each change stores a new snapshot of the list, so every Add, Remove, Replace, and Clear is exactly one undo step. Shapes are immutable, so snapshots share them, and an edit is a `Replace` with a changed copy. State that shouldn't be undone, like how far along a route the aircraft is, stays in the tool. The MaxShapes and MaxPenPoints settings cap the shape count and the total freehand points. The store keeps 100 undo steps and resets when the scene's `DynamicMap` changes, which is how leaving the mission shows.

A shape draws itself in meters through `IMapCanvas`, which has lines, arrows, polylines, rings, the game's waypoint marker, and labels. The same `Draw` call renders the shape, hit-tests it for the eraser, and redraws it red under the eraser's cursor. `ShapeHitTest` does the hit test by measuring how close each stroke passes to the click. When a shape reads a unit through `TryResolve`, or reads `OwnAircraft`, the layer marks it live and redraws it on each of the game's 10 Hz map refreshes. Other shapes redraw only when added, when the zoom changes, or when a drawing setting changes. Panning rebuilds nothing, because the layer moves with the map. An anchored point resolves the way the unit's map icon does in `UnitMapIcon.UpdateIcon`. A tracked unit gives the faction's tracked position, a friendly unit its own position, and an untracked enemy nothing, so a drawing reveals nothing the map doesn't. Once the unit is destroyed or no longer tracked, `TryResolve` returns false with the last seen position.

### Drawing on the map

`MapShapeLayer` is one layer under `DynamicMap.iconLayer`, like the runways, and is the icon layer's last child, so drawings sit over unit icons the way the game's own waypoints do. It has its own `Canvas`, so redrawing a shape doesn't re-batch the game's icons, and icons moving every frame don't re-batch a long pen stroke. Each shape gets its own container, and all its lines are one `StrokeGraphic` mesh. Across its width, each line has the `EdgeProfile` fill, dark rim, and anti-aliased fade on both sides of its centre. Joins are mitred, and open ends fade out over the rim. A `StrokeGraphic` fits its rect around its lines, because the map's `RectMask2D` culls a graphic by its rect, and a default-sized rect would vanish once scrolled out of view. Markers are copies of `DynamicMap.mapWaypoint`, the game's 20-unit steerpoint sprite, and labels are `OutlinedText`. Widths and text sizes use the inverse map scale, like every other map graphic here, and the layer turns text and markers upright every frame for the heading-up minimap.

### Input

While a tool is active, a transparent `MapPointerCatcher` covers the full map, above the icons and below the menu. By default the game has one left-click path on the map. Unity's event system hands the click to a unit or airbase icon, and `MapIcon.OnPointerClick` selects it. The catcher takes that click instead. The game's other map selection, `DynamicMap.SelectFromMap`, runs on the Rewired "Select" action, and the Rewired `InputManager` data in `resources.assets` binds Select to Enter, not the mouse. So nothing is patched. A player who has rebound Select to the left mouse button will see the game select a unit near a tool's click as well.

A left press and release that moves no more than a few pixels is a click. A longer drag still pans the map, since `DynamicMap.MapControls` pans on the mouse axes while the button is held. A tool that captures drags gets down, drag, and up events instead, and the player's Rewired mouse maps stay off until the button comes up, the same switch the game's chat box uses. The tools never touch right clicks, so move orders for selected units and NOAutopilot's waypoints keep working with a tool open. A tool that captures the keyboard gets `Input.inputString` a character at a time. Meanwhile the input does what the chat box does: the Rewired keyboard maps are off, `GameplayUI.AllowPauseKeybind` is false, and `CursorFlags.Chat` is set, which NOAutopilot checks before reading its hotkeys. All three come back once Enter and Escape are released, since Rewired would read a key still held as a fresh press.

The undo and redo keys, Z and Y by default, act only while the full map is open, and not while a tool takes typing, the chat is open, or the game menu is up. The game's default keyboard map binds W, S, A, D, Q, E, Shift, Ctrl, Space, B, Enter, Backspace, Tab, V, G, X, F, N, L, M, R, T, Escape, comma, the arrow keys, Page Up and Down, the number pad, and F9 and F10; the only code that reads Y directly runs in the Unity editor. NOAutopilot's default keys don't use Z or Y either.

Menu buttons and the catcher only record what happened. `MapToolHost` acts on it in the plugin's guarded update, since a throw inside Unity's event system would bypass the guard.

### 3D labels

`WorldLabelPool` generalises the HUD callout. Each label is a copy of the callout's label, so it shares the HUD canvas, font, and visibility, and hides with the HUD and while the map is open. Tools request labels in `MapTool.OnFrame` with a global position in meters, Y being height above sea level. The pool projects each through the camera, hides the ones behind it, and reuses its copies from frame to frame, up to 32 at once.

### Adding a tool

1. Give the tool its own folder under `MapTools/`, holding a `MapTool` subclass and its `MapShape` subclasses. Waypoint, Pen, Text, Bearing/range, and Circle already have a stub class in `Waypoint/`, `Pen/`, `Text/`, `BearingRange/`, and `Circle/`.
2. Add the tool to the list in `MapToolHost`'s constructor, which sets the menu order. That's the only shared line a new tool needs.
3. React to `OnClick`, or set `CapturesDrag` and use `OnPointerDown`, `OnPointerDrag`, and `OnPointerUp`. `MapPointer.Point` is the unit under the cursor, if any, else the ground. Set `CapturesKeyboard` while typing. Show hints in `Status` and extra buttons in `Options`.
4. Add shapes with `Context.Shapes.Add` in `Context.Color`; each add is an undo step, and a false return means a cap was hit. Draw anything that isn't a stored shape, like a rubber band, in `DrawOverlay`. Request 3D labels in `OnFrame`, placed with `Context.TryResolveWorld` for a unit, or at a height from `Context.GroundElevation` taken once when the shape is made.
5. Format distances and bearings with `NavFormat` in `Context.Units`. `Draw`, `DrawOverlay`, and `OnFrame` run often, so they mustn't allocate. Cache label strings, and format again only when `NavFormat.DistanceKey` changes.
6. Keep logic that doesn't need Unity in files that don't reference it, link them in the test project, and add a `<Tool>Tests.cs` suite with an `All` list plus one `tests.AddRange` line in `Program.Main`. Settings go in their own section of `ModSettings`, with a row in the settings table in `USER_GUIDE.md`.

| What | Where in `Assembly-CSharp` |
| --- | --- |
| Full map open | `DynamicMap.mapMaximized` |
| 10 Hz refresh | `DynamicMap.mapLastUpdated`, set by `UpdateMap` just before it raises `onMapChanged` |
| Unit under the cursor | `DynamicMap.mapIcons`, each `UnitMapIcon.iconImage` position and `unit.persistentID` |
| Waypoint marker | `DynamicMap.mapWaypoint`, the prefab `DynamicMap.MapControls` places for a move order |
| Unit positions | `FactionHQ.GetTrackingData(id).GetPosition()`, else `UnitRegistry.TryGetUnit` |
| Ground height | A ray down from 10 km, as in `DynamicMap.JumpCameraTo` |
| Unit system | `PlayerSettings.unitSystem` |
| Chat or game menu open | `CursorManager.GetFlag(CursorFlags.Chat \| CursorFlags.GameMenu)` |
