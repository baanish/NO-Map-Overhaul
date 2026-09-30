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

`DynamicMap` is a single object that the game moves between the minimap anchor and the full-map anchor. The overlay parents one layer under `DynamicMap.iconLayer`, so it inherits pan, zoom, and the minimap's heading-up rotation. Setting the layer as first sibling keeps unit icons on top. Each of the mod's map layers is a `Canvas` of its own, nested in the map's. Unity re-batches a whole canvas when any graphic in it changes or moves, and the game moves some of its icons every frame, so without one the runways would be re-batched every frame along with the icons. Nested, a layer is still clipped by the map's `RectMask2D` and still draws in its place among the icon layer's children.

Positions follow the game's icon convention. Map-local `(x, y)` is the global `(x, z)` times `DynamicMap.mapDisplayFactor`. The overlay multiplies widths and font sizes by `1 / mapImage.localScale.x`, the inverse map scale, so they stay a constant size on screen as the player zooms. The game's unit icons use the same factor. Strips and dashes are `FeatheredRect` meshes and the numbers are `TextMeshProUGUI`. The map canvas has no anti-aliasing, so every hard edge on a rotated shape stair-steps. `EdgeProfile` gives each shape its edge as nested rings: fill, the dark rim, then a fade to transparent, with each colour change spread across about one screen pixel. The boundary's `CircleGraphic` uses the same profile. The numbers are upright when created and reset their world rotation whenever the layer's rotation changes, which on the heading-up minimap is every frame, so they stay upright while it turns.

Map text (runway numbers and airbase names) is an `OutlinedText`: the font and style of the HUD label the callout clones, with a one-unit rim in `OutlineColor`. The rim is a `TextRim`, one graphic whose mesh is the face's glyph quads copied eight times, offset around it, in the face's own font material. It is copied from the face's `textInfo` right after `ForceMeshUpdate`, whenever the text, its style, or its world scale (which sets the glyphs' SDF scale) changes. Eight TextMeshPro copies drew the same rim before, but as eight more graphics for Unity to cull under the map's `RectMask2D` and batch every frame. Glyphs from a fallback font get no rim. TextMeshPro's own SDF outline doesn't work with this font. An outline thin enough to fit its atlas padding eats into the thin glyphs, and a wider one draws each glyph's quad as a box. Underlay has the same limit, since it too reaches past the glyph into the padding. The face and rim sit in a container scaled by the inverse map scale, so zooming changes no font size and regenerates no text meshes.

## The airbase boundary

The game ends a sortie as returned when a landed aircraft stops within `Airbase.GetRadius()` of `Airbase.center`, checked by `FactionHQ.AnyNearAirbase` in `Aircraft.ServerDisableUnit`. `GetRadius()` returns `SavedAirbase.CaptureRange`, so the landing zone and the capture zone are one circle. `CircleGraphic` draws it as UI mesh, a filled disk plus an anti-aliased edge ring. The ring keeps a constant width at any zoom, which a scaled sprite couldn't do. The boundary layer re-takes the first sibling slot every frame, so it always sits under the runways.

## Airbase names

The game's `AirbaseMapIcon` exists only for `DynamicMap.HQ.GetAirbases()`, the local faction's, and shows only while `DynamicMap.mapMaximized`. The names cover every airbase in `FactionRegistry.airbaseLookup` instead, skipping carriers, `Airbase.disabled`, and airbases with no `center` to place a label at, with one label per `DisplayName` (friendly airbases claim theirs first, so it lands under the icon; Ignus free flight has three Feldspar airbases), and follow the icon's full-map-only rule. The full map is north-up, so each label sits a fixed distance below `Airbase.center` in map space and needs no per-frame rotation. The text is `SavedAirbase.DisplayName`, the same name the game's map tooltip shows. The boundary layer keeps the icon layer's first slot, and the names layer the slot after it, so the names sit over the boundary and under the runways and unit icons. Each moves only when out of place: when both took the first slot every refresh they swapped places ten times a second, and each move re-sorts the map's canvas.

## The HUD callout

`AirbaseOverlay` is the game's landing guide. Its private `airbaseLabel` field, read by reflection, is the "Taxi to Runway 21" text. The callout clones that label into the same parent, so it inherits the HUD canvas, font, and visibility. The callout projects the threshold with `CameraStateManager.mainCamera.WorldToScreenPoint`. It looks for the overlay at most once a second, including inactive objects, so the map numbers can get the font before the HUD is visible.

The mod never calls `AirbaseOverlay`'s landing methods, because they send network commands and change runway usage.

## Choosing the approach

`ApproachSelector` works on flat 2D runway lines (`RunwayLine`, meters, X east, Y north) and knows nothing about Unity, so the tests run without the game. It picks the nearest runway within range, measured to the strip, and the end of it closer to the pilot. It ignores heading on purpose: the line exists to judge the turn onto final, so it must show while the pilot is still side-on to the runway. Every heading-based rule tried before this (pattern-leg inference, then final-only) either picked the wrong end while manoeuvring or hid the line when it was needed.

## Measuring cost

Everything under `Diagnostics/` is off unless the player asks for it. `PerformanceLog` times `Plugin.LateUpdate` and the `onMapChanged` handler with `Stopwatch` timestamps, next to `Time.unscaledDeltaTime`, and hands each sample to a `FrameStats`. That's Unity-free and preallocated: running sums and worsts, plus a ring of 4096 frame times sorted once per window for the 1% low (`FrameStatsTests`). The mod's own times cover its scripts only. Canvas rebuilds and draw calls for its graphics show up in the frame time alone, so the honest comparison is frame time with the mod on and off.

`PerfTest` runs that comparison from an F1 button. The button is a `ConfigurationManagerAttributes.CustomDrawer`, which ConfigurationManager finds by the class's name, so the mod needs no reference to it. The test switches the mod through `Plugin.ModOn`, never by writing General.Enabled, so no crash can save the mod as off. It opens and closes the full map with `DynamicMap.Maximize` and `Minimize`, the calls the game's own map key makes. It swaps the player's drawings out with `ShapeStore.Save` and `Restore`, which keep the undo history and add no step. `StressDrawings` builds the heavy set from plain shapes, sized to the minimap: the rect's world width over one map meter in world units (`mapDisplayFactor` times the icon layer's scale).

`RenderCounters` adds the rendering table. Unity's counters come from `ProfilerRecorder`, looked up by name through `ProfilerRecorderHandle.GetAvailable`, since a release player records the render counters but not the `Canvas.*` markers. Canvas time is measured without the profiler: while the test runs, two timestamp steps wrap `PostLateUpdate.PlayerUpdateCanvases` (layout, graphic rebuilds, and `RectMask2D` culling) and `PlayerEmitCanvasGeometry` (batching) in the player loop, and come out again when it ends.

## Map tools

A Tools button on the full map opens a menu of drawing tools: a rail of icons and a strip beside it. What they draw shows on both maps and, for some shapes, in the 3D view. Everything lives under `src/BaanishUiImprovements/MapTools/`, and `MapToolHost` ties it together:

```text
Plugin.LateUpdate → MapToolHost.Update   (every frame)
  ├─ mission check     a different DynamicMap, or none, clears the ShapeStore
  ├─ MapToolMenu       the rail and strip on the full map; a click records a MenuCommand
  ├─ MapToolInput      left clicks, drags, and typing to the active MapTool
  ├─ undo and redo keys, while the full map is open
  ├─ MapTool.OnFrame   every tool, every frame: live state, 3D labels and rings (WorldLabelPool)
  └─ MapShapeLayer     redraws what changed; live shapes and live overlays on the 10 Hz refresh
       └─ LabelLayout  places the labels after a redraw, or once the full map comes to rest
```

### The menu

`MapToolMenu` builds the menu from the design mock (design C, "minimal rail"). Its sizes are the mock's pixels at 2560x1440, turned into map canvas units by `Px`, 0.75, since the map canvas scales by 4/3 at that resolution. So the menu scales with the game's UI like the rest of the map. The rail's corner sits 32 by 38 pixels in from the map's top-left corner, just clear of the grid's row letters and column numbers.

- **Rail.** 48 pixels wide: the Tools head (48 tall), a 44-pixel cell per tool, then 40-pixel Undo, Redo, and Clear cells, then a two-by-three grid of 24-pixel swatch cells. Groups are 8 pixels apart with a divider between them.
- **Strip.** 48 pixels tall, flush right of the head, shown while a tool is picked. It holds the tool's name, a divider, the hint (`MapTool.Status`), then the option buttons (`Options`, with `PickedOption` solid), their unit (`OptionSuffix`), and a count (`Counter`). It grows to fit up to 640 pixels, past which the hint wraps onto a second line. While `MapTool.Warning` is true, or the store is full, the name gives way to an amber sign and hint, and the tool's cell gets an amber badge.
- **Hover tag.** The hovered cell's name, 6 pixels right of the rail, with the key in a box for Undo and Redo. It takes no clicks, so moving onto it leaves the cell and hides it.

The ground of the rail and strip is two `PolygonGraphic` fills, the rail's with one 6-pixel chamfer on the L's outer corner, under one `StrokeGraphic` rim around the whole L. Each fill is the raycast target for its own rect, so clicks in the gaps between cells stay off the map, and the empty map right of the rail and under the strip still takes them. The fills are also the areas map labels keep clear of. Icons are `StrokeGraphic` lines drawn by `RailIcons` from the mock's SVG paths, and are rebuilt only when a cell's look changes. A filled dot is a ring as thick as its radius, and the warning triangle is a thick closed stroke inset by half its inradius, so every edge is anti-aliased. A cell is a `Button` whose fill the button tints through a `ColorBlock`: the HUD green at 0% at rest, 16% hovered, and 32% pressed, with a solid swap for the picked cell. `MenuHover` records the hover, since a `Button` keeps it protected.

Colours come from the game's MFD palette: ground `#050E07` at 78%, rim `#2BE127` at 32%, icons and labels `#41FF52` (72% at rest, full on hover and in the strip, 22% disabled), picked fill `#2BE127` at 90% with a `#03140A` icon, hint text `#C8F5CD`, and warnings `#FFB23E`.

### Shapes and the store

A drawing is a `MapShape` subclass holding plain data only: meters (X east, Y north, the game's global x and z), unit anchors by `PersistentID.Id`, and colour bytes. None of it is a Unity type, so drawings could be saved or shared with other players later without a rewrite. A `MapPoint` with a unit id follows that unit. `ShapeStore` holds every tool's shapes in draw order. Each change stores a new snapshot of the list, so every Add, Remove, Replace, and Clear is exactly one undo step. Shapes are immutable, so snapshots share them, and an edit is a `Replace` with a changed copy. State that shouldn't be undone, like how far along a route the aircraft is, stays in the tool. The MaxShapes and MaxPenPoints settings cap the shape count and the total freehand points. The store keeps 100 undo steps and resets when the scene's `DynamicMap` changes, which is how leaving the mission shows.

A shape draws itself in meters through `IMapCanvas`, which has lines, arrows, polylines, rings, the game's waypoint marker, and labels. A label says what it's attached to, and the layer decides where it sits (see Labels on the map). The same `Draw` call renders the shape, hit-tests it for the eraser, and redraws it red under the eraser's cursor. `ShapeHitTest` does the hit test by measuring how close each stroke passes to the click. When a shape reads a unit through `TryResolve`, or reads `OwnAircraft`, the layer marks it live and redraws it on each of the game's 10 Hz map refreshes. Other shapes redraw only when added, when the zoom changes, or when a drawing setting changes. A tool's overlay follows the same rule, and also redraws when the drawings change or the tool calls `InvalidateOverlay`, so the eraser redraws its highlight only when the cursor passes onto a different shape. Panning rebuilds nothing, because the layer moves with the map. An anchored point resolves the way the unit's map icon does in `UnitMapIcon.UpdateIcon`. A tracked unit gives the faction's tracked position, a friendly unit its own position, and an untracked enemy nothing, so a drawing reveals nothing the map doesn't. Once the unit is destroyed or no longer tracked, `TryResolve` returns false with the last seen position.

### Drawing on the map

`MapShapeLayer` is one layer under `DynamicMap.iconLayer`, like the runways. The mod's other layers each take the icon layer's first slot, so they lead its children, and the game appends its unit icons, waypoints, and radar pings after them. On each refresh the drawings layer takes the slot just past the last child named `Baanish…`, so drawings cover the runways, names, and boundary but never hide a unit icon. The full map and the minimap are the same `DynamicMap`, so the order holds on both. The pointer catcher is a child of the map itself, above the icon layer, so it still takes left clicks. With the ShowOnMinimap setting off, the layer is inactive while the full map is closed and skips every redraw, and it redraws everything when the full map opens. It has its own `Canvas`, so redrawing a shape doesn't re-batch the game's icons, and icons moving every frame don't re-batch a long pen stroke. Unity re-batches a whole canvas when any graphic in it changes, moves, or turns, and treats every transform write as a change, even to the same value. So the markers and labels, which the minimap turns upright every frame, sit in a second canvas inside the first, and the lines' canvas re-batches only when a shape redraws. Writes that would repeat a transform's value are skipped. Each shape gets its own container, and all its lines are one `StrokeGraphic` mesh. Lines are flat: one colour across the width with a one-pixel anti-aliased edge, the `EdgeProfile` without its rim. On a line this thin the runways' fade from fill to dark rim to clear would take up most of the width and read as a bevel. Joins are mitred, open ends fade out over the anti-aliased edge, and an arrowhead is five line widths long. A `StrokeGraphic` fits its rect around its lines, because the map's `RectMask2D` culls a graphic by its rect, and a default-sized rect would vanish once scrolled out of view. Markers are copies of `DynamicMap.mapWaypoint`, the game's 20-unit steerpoint sprite, in a container per shape at the start of the label canvas, so markers draw over every line, in the shapes' order. Labels are `MapLabel`s after them, so no line or marker crosses a label. Widths and text sizes use the inverse map scale, like every other map graphic here. Each label and marker is made upright when it's created, and the layer turns them all upright again whenever the layer's rotation changes, which on the heading-up minimap is every frame.

### Labels on the map

A shape names what a label is attached to with a `LabelAnchor`: a note's point, a waypoint marker, an arrow's head and tail, or a ring. A note is `OutlinedText` with its dark rim. Every other label sits on a plate: the menu's ground colour with a 1-pixel rim in the drawing's colour at 55%. A shape's draw only sets the text. Once every changed shape has drawn, the layer measures each label, turns its anchor into icon units, and hands them all to `LabelLayout`, which is Unity-free and unit-tested (`LabelLayoutTests`).

`LabelLayout` places labels in priority order, and a later label never pushes an earlier one. Notes and waypoint numbers go first, since the pilot put them there. Next come labels in a tool's overlay, such as a measurement still being picked, then bearings newest first, then radii newest first. Each kind tries its candidate slots in order and takes the first one clear of the obstacles and of every label already placed. A bearing tries past its arrowhead with an 8-pixel gap. Next it tries beside the head, on the side the shaft leans away from, then slid back along the shaft, above then below it, up to 40% of its length. Last it tries the nearest free slot within 48 pixels, joined to the head by a 1.5-pixel leader at 70%. A radius tries its ring at 12, 2, 10, 6, 4, and 8 o'clock. A waypoint number tries upper right, upper left, lower right, then lower left of its marker. With no clear slot a label takes its first. A label keeps the slot it had while that slot stays clear. An overlay label identical to a stored one takes the stored one's slot. That covers the eraser's highlight, which redraws the hovered drawing.

`LabelObstacles` collects the obstacles, in the layer's icon units. They are the unit and airbase icons in `DynamicMap.mapIcons`, grown by 6 pixels, and the text of the mod's other map layers (the face of each `OutlinedText`, by its `textBounds`). The game's grid letters and numbers under `DynamicMap.gridLabels` count too, boxed by their character count, since the game leaves their rects at 100 units. So do the menu's rail and strip. The layer places labels when a shape has redrawn, when the full map opens or closes, when the menu changes shape, and on the first still frame after a pan or zoom. That's when it collects the obstacles, never per frame. While the map moves, and on the turning minimap, where the game's labels aren't collected, each label keeps its slot, or takes its first.

### Input

While a tool is active, a transparent `MapPointerCatcher` covers the full map, above the icons and below the menu. By default the game has one left-click path on the map. Unity's event system hands the click to a unit or airbase icon, and `MapIcon.OnPointerClick` selects it. The catcher takes that click instead. The game's other map selection, `DynamicMap.SelectFromMap`, runs on the Rewired "Select" action, and the Rewired `InputManager` data in `resources.assets` binds Select to Enter, not the mouse. So nothing is patched. A player who has rebound Select to the left mouse button will see the game select a unit near a tool's click as well.

A left press and release that moves no more than a few pixels is a click. A longer drag still pans the map, since `DynamicMap.MapControls` pans on the mouse axes while the button is held. A tool that captures drags gets down, drag, and up events instead, and the player's Rewired mouse maps stay off until the button comes up, the same switch the game's chat box uses. The tools never touch right clicks, so move orders for selected units and NOAutopilot's waypoints keep working with a tool open. The Waypoint tool's route is its own for the same reason: it never reads NOAutopilot's route, and NOAutopilot never reads it. A tool that captures the keyboard gets `Input.inputString` a character at a time. Meanwhile the input does what the chat box does: the Rewired keyboard maps are off, `GameplayUI.AllowPauseKeybind` is false, and `CursorFlags.Chat` is set, which NOAutopilot checks before reading its hotkeys. All three come back once Enter and Escape are released, since Rewired would read a key still held as a fresh press. The chat box, `DialogueBox`, and `AircraftSelectionMenu` each switch `AllowPauseKeybind` off while open and on as they close, with their cursor flag (`Chat`, `Dialogue`, `SelectionMenu`) set meanwhile. Typing doesn't start while any of those flags is set or `AllowPauseKeybind` is already off, so it always starts from the pause key on and the chat flag off. On release the pause key comes back only if none of the three flags is set by then, since a UI that opened while the player typed owns it.

The undo and redo keys, Z and Y by default, act only while the full map is open, and not while a tool takes typing, the chat is open, the game menu is up, or a text field has focus (`InputFieldChecker.InsideInputField`, which the game's own `ExtraUiInput` checks), such as a name field in the mission editor. The game's default keyboard map binds W, S, A, D, Q, E, Shift, Ctrl, Space, B, Enter, Backspace, Tab, V, G, X, F, N, L, M, R, T, Escape, comma, the arrow keys, Page Up and Down, the number pad, and F9 and F10; the only code that reads Y directly runs in the Unity editor. NOAutopilot's default keys don't use Z or Y either.

Menu buttons and the catcher only record what happened. `MapToolHost` acts on it in the plugin's guarded update, since a throw inside Unity's event system would bypass the guard.

### 3D labels and rings

`WorldLabelPool` generalises the HUD callout. Each label is a copy of the callout's label, so it shares the HUD canvas, font, and visibility, and hides with the HUD and while the map is open. Tools request labels in `MapTool.OnFrame` with a global position in meters, Y being height above sea level. The pool projects each through the camera, hides the ones behind it or more than 200 pixels off screen, and reuses its copies from frame to frame, up to 32 at once.

The Circle tool also requests each circle as a ring through `IWorldLabels.Ring`, newest first, level with the centre: the unit's altitude, or the ground height the tool reads once when the circle is made. `WorldRingPool` draws up to 16, each a flat `StrokeGraphic` in a container under the labels' parent, so a ring shares their visibility and draws under their text. The container sits at the world origin of the screen-space HUD canvas, where world units are pixels. `RingProjection`, which is Unity-free and unit-tested (`RingProjectionTests`), turns the ring into chords in camera space and clips each against the near plane and the screen's four sides grown by 32 pixels before projecting it. It uses 128 chords for a ring seen from inside or close by, and halves that, down to 16, while each chord still keeps within a quarter pixel of the true circle, judged from the ring's nearest possible point, so a small ring far off is a much smaller mesh to rebuild each frame. So a ring that passes behind the camera is cut rather than mirrored the way `WorldToScreenPoint` mirrors points behind it, and no vertex lands so far off screen that float precision bends the line. A ring's mesh is rebuilt only when the camera, the circle, or the line width changed since the frame before. There is no radius label in the 3D view: at the ring's nearest point it would hop between chords, or vanish while flying inside the ring, where that point is beside or behind the aircraft.

### Adding a tool

1. Give the tool its own folder under `MapTools/`, holding a `MapTool` subclass and its `MapShape` subclasses. Waypoint, Pen, Text, Bearing/range, and Circle already have a stub class in `Waypoint/`, `Pen/`, `Text/`, `BearingRange/`, and `Circle/`.
2. Add the tool and its `RailIcon` to the list in `MapToolHost`'s constructor, which sets the rail order. That's the only shared line a new tool needs, unless it needs a new icon, which is a case in `RailIcons.Draw`.
3. React to `OnClick`, or set `CapturesDrag` and use `OnPointerDown`, `OnPointerDrag`, and `OnPointerUp`. `MapPointer.Point` is the unit under the cursor, if any, else the ground. Set `CapturesKeyboard` while typing. Show hints in `Status`, with `Warning` set when the hint says why a click did nothing. Extra buttons go in `Options`, with `PickedOption`, `OptionSuffix`, and `Counter` if needed.
4. Add shapes with `Context.Shapes.Add` in `Context.Color`; each add is an undo step, and a false return means a cap was hit. Draw anything that isn't a stored shape, like a rubber band, in `DrawOverlay`, and call `InvalidateOverlay` from any event that changes it. The layer redraws an overlay on its own only when the tool is switched, the drawings or the zoom change, and on the 10 Hz refresh while it reads a unit or the aircraft, so a preview that follows the cursor invalidates on `OnPointerMove`. Request 3D labels in `OnFrame`, placed with `Context.TryResolveWorld` for a unit, or at a height from `Context.GroundElevation` taken once when the shape is made.
5. Format distances and bearings with `NavFormat` in `Context.Units`. `Draw`, `DrawOverlay`, and `OnFrame` run often, so they mustn't allocate. Cache label strings, and format again only when `NavFormat.DistanceKey` changes.
6. Keep logic that doesn't need Unity in files that don't reference it, link them in the test project, and add a `<Tool>Tests.cs` suite with an `All` list plus one `tests.AddRange` line in `Program.Main`. Settings go in their own section of `ModSettings`, with a row in the settings table in `USER_GUIDE.md`.

| What | Where in `Assembly-CSharp` |
| --- | --- |
| Full map open | `DynamicMap.mapMaximized` |
| 10 Hz refresh | `DynamicMap.mapLastUpdated`, set by `UpdateMap` just before it raises `onMapChanged` |
| Unit under the cursor | `DynamicMap.mapIcons`, each `UnitMapIcon.iconImage` position and `unit.persistentID` |
| Icons labels avoid | `DynamicMap.mapIcons`, each `MapIcon.iconImage` rect |
| Grid letters and numbers | `DynamicMap.gridLabels`, legacy `Text` children placed by `GridLabels.GridLabels_OnMapChanged` |
| Waypoint marker | `DynamicMap.mapWaypoint`, the prefab `DynamicMap.MapControls` places for a move order |
| Unit positions | `FactionHQ.GetTrackingData(id).GetPosition()`, else `UnitRegistry.TryGetUnit` |
| Ground height | A ray down from 10 km, as in `DynamicMap.JumpCameraTo` |
| Unit system | `PlayerSettings.unitSystem` |
| Chat or game menu open | `CursorManager.GetFlag(CursorFlags.Chat \| CursorFlags.GameMenu)` |
