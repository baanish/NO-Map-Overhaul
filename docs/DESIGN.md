# How it works

The plugin draws overlays, switches the player's controls off while a map tool takes the mouse or keyboard, and can share fixed drawings with teammates. Its Harmony patches isolate Steam connection callbacks by ownership and hold back NOAutopilot right-clicks while drawing (see Patches). On each of the game's own map refreshes (ten times a second, plus every frame while the player pans or zooms the full map) it reads the game's objects, works out what to show, and updates a few UI elements it owns. If drawing throws, it logs the error and rebuilds its graphics after a pause. If a game update breaks something else it calls, it logs the error and disables itself. If the Steam ownership patch can't load, sharing stays off. If an update breaks the HUD label it copies by reflection, it logs a warning at startup and that feature stays off.

## One map refresh

```text
Plugin.RefreshMap   (on DynamicMap.onMapChanged: 10 Hz, and per frame during pan or zoom)
  ├─ collect runways    FactionHQ.GetAirbases() of DynamicMap.HQ, carriers skipped
  ├─ select approach    ApproachSelector.Select (pure logic, unit-tested)
  ├─ RunwayMapOverlay   strips, numbers, dashed approach line
  ├─ AirbaseLabelOverlay      friendly airbases' names, full map only (off by default)
  └─ AirbaseBoundaryOverlay   landing-zone circles (off by default)

Plugin.LateUpdate   (every frame)
  ├─ PerfTest.Update                    idle unless the player started the perf test
  ├─ RunwayHudCallout.Render            "RWY 27" follows the camera
  ├─ RunwayMapOverlay.KeepLabelsUpright numbers counter-rotate the minimap
  └─ MapToolHost.Update                 the map tools (see Map tools)
```

The game raises `DynamicMap.onMapChanged` from its own map update, on the same 10 Hz cadence that refreshes its airbase icons. It also fires every frame while the player pans or zooms the full map, which keeps zoom-dependent sizes current. Runways are re-read on every refresh, so captured airbases need no event wiring.

The handler runs inside the game's update, so it catches every exception rather than breaking the map. An exception while building or updating graphics takes all the mod's graphics down and builds them again after a pause (`DrawingRecovery`). The pause starts at 1 second, doubles up to 60 while failures repeat, and drops back to 1 after 30 healthy seconds. The drawings, teammates' drawings, and sharing carry on through it. Any other exception disables the plugin.

`LateUpdate` runs the per-frame work: the HUD label, which tracks the camera, the check that stands the numbers upright again as the heading-up minimap turns, and the map tools.

## Game data it reads

| What | Where in `Assembly-CSharp` |
| --- | --- |
| Friendly airbases | `DynamicMap.HQ.GetAirbases()` |
| Runway ends, width, flags | `Airbase.Runway.Start`, `.End`, `.GetWidth()`, `.Reversable`, `.Landing` |
| Runway numbers | The runway heading, rounded the way `Airbase.Runway.GetName` does for an unnamed runway, then `RunwayNames.PaintedNumbers` when the PaintedNumbers setting is on, then L, C or R from `RunwayNames.ParallelSide` among the airbase's parallel runways |
| Carrier check | `Airbase.AttachedAirbase` |
| Local aircraft | `SceneSingleton<CombatHUD>.i.aircraft` |
| Helicopter check | `aircraft.GetControlsFilter() is HeloControlsFilter` and no `TiltWingController`, since the Tarantula tiltrotor uses helicopter controls too |
| On the ground | `aircraft.radarAlt < 1`, the same threshold as the game's touchdown check |

The mod draws only runways with `Landing` set. The stock game's one takeoff-only runway, Harmony Sands' "Runway14L Short", lies on top of a landing runway.

The painted runway numbers live in the level textures, not in data. `RunwayNames.PaintedNumbers` records each runway end where the paint differs from the heading number. It keys them by airbase and heading number rather than runway index, since a mission can reorder an airbase's runways. Each entry is checked in game.

The mod ignores the runway `name` field, because a mission can set it. Ignus free flight sets names that disagree with the paint ("Runway 07" on a runway painted 06). That mission also adds two airbases on top of the stock Feldspar, with runways lying exactly over the stock ones. With heading numbers, the duplicates carry the same labels and overlap cleanly.

`tools/dump_runways.py` prints every airbase's runways from the asset files: name, flags, ends, and the heading number the game would compute.

A runway's `Start` is the end you land at when not reversed. `RunwayUsage.GetStart()` returns the threshold for either direction.

## Drawing on the map

`DynamicMap` is a single object that the game moves between the minimap anchor and the full-map anchor. The overlay parents one layer under `DynamicMap.iconLayer`, so it inherits pan, zoom, and the minimap's heading-up rotation. The layer is the first sibling, which keeps unit icons on top.

Each of the mod's map layers is a `Canvas` of its own, nested in the map's. Unity re-batches a whole canvas when any graphic in it changes or moves, and the game moves some of its icons every frame. Without a nested canvas, the runways would be re-batched every frame along with the icons. Nested, a layer is still clipped by the map's `RectMask2D` and still draws in its place among the icon layer's children.

Positions follow the game's icon convention: map-local `(x, y)` is the global `(x, z)` times `DynamicMap.mapDisplayFactor`. The overlay multiplies widths and font sizes by `1 / mapImage.localScale.x`, the inverse map scale, so they stay a constant size on screen as the player zooms. The game's unit icons use the same factor.

Strips and dashes are `FeatheredRect` meshes and the numbers are `TextMeshProUGUI`. The map canvas has no anti-aliasing, so every hard edge on a rotated shape stair-steps. `EdgeProfile` gives each shape its edge as nested rings (fill, the dark rim, then a fade to transparent), with each colour change spread across about one screen pixel. The boundary's `CircleGraphic` uses the same profile.

The numbers are upright when created. They reset their world rotation each time the heading-up minimap has turned a degree since they last did (`MinimapHeading`), so they stay upright while it turns. In flight the game turns the minimap a little every frame, and every transform write makes Unity re-batch the layer. Resetting on any change would redo every number every frame for a tilt too small to see.

Map text (runway numbers and airbase names) is an `OutlinedText`. It has the font and style of the HUD label the callout clones, with a one-unit rim in `OutlineColor`. The rim is a `TextRim`, one graphic whose mesh is the face's glyph quads copied eight times, offset around it, in the face's own font material. `TextRim` copies them from the face's `textInfo` right after `ForceMeshUpdate`, whenever the text, its style, or its world scale (which sets the glyphs' SDF scale) changes. Eight TextMeshPro copies would draw the same rim, but as eight more graphics for Unity to cull under the map's `RectMask2D` and batch every frame. Glyphs from a fallback font get no rim.

TextMeshPro's own SDF outline doesn't work with this font. An outline thin enough to fit its atlas padding eats into the thin glyphs, and a wider one draws each glyph's quad as a box. Underlay has the same limit, since it also reaches past the glyph into the padding.

The face and rim sit in a container scaled by the inverse map scale, so zooming changes no font size and regenerates no text meshes.

## The airbase boundary

The game ends a sortie as returned when a landed aircraft stops within `Airbase.GetRadius()` of `Airbase.center`. `FactionHQ.AnyNearAirbase` checks this in `Aircraft.ServerDisableUnit`. `GetRadius()` returns `SavedAirbase.CaptureRange`, so the landing zone and the capture zone are one circle.

`CircleGraphic` draws it as UI mesh, a filled disk plus an anti-aliased edge ring. The ring keeps a constant width at any zoom, which a scaled sprite couldn't do. The boundary layer re-takes the first sibling slot every frame, so it always sits under the runways.

## Airbase names

The game's `AirbaseMapIcon` exists only for the local faction's airbases, `DynamicMap.HQ.GetAirbases()` (`DynamicMap.RefreshAirbases`), and shows only while `DynamicMap.mapMaximized`. The names cover the same list the runways and boundary read, and follow the icon's full-map-only rule.

That list holds whatever the faction owns right now:

- the map's own airbases
- ones the mission editor placed (`Airbase.IsCustom`, with their `SavedAirbase` from the mission)
- ones captured (`Airbase.CaptureFaction` moves an airbase between factions' lists)

It holds a disabled airbase too, which the game still marks in its neutral colour. Carriers are skipped. The game spawns an airbase attached to a unit (`Airbase.AttachedAirbase`) only for a ship, and the ship's icon marks it. Other sides' airbases are never named, since the game doesn't mark them.

There's one label per name. An airbase in use claims a shared name before one switched off (Ignus free flight has three Feldspar airbases). The full map is north-up, so each label sits a fixed distance below `Airbase.center` in map space and needs no per-frame rotation.

The text is `SavedAirbase.DisplayName`, the same name the game's map tooltip and spawn menu show. A mission editor airbase's author can clear that name, so an empty one falls back to `SavedAirbase.UniqueName`, which the editor fills the name in from. An airbase with neither gets no label.

The boundary layer keeps the icon layer's first slot and the names layer the slot after it, so the names sit over the boundary and under the runways and unit icons. Each moves only when out of place. If both took the first slot every refresh, they would swap places ten times a second, and each move re-sorts the map's canvas.

## The HUD callout

`AirbaseOverlay` is the game's landing guide. Its private `airbaseLabel` field, read by reflection, is the "Taxi to Runway 21" text. The callout clones that label into the same parent, so it inherits the HUD canvas, font, and visibility. It projects the threshold with `CameraStateManager.mainCamera.WorldToScreenPoint`. It looks for the overlay at most once a second, including inactive objects, so the map numbers can get the font before the HUD is visible.

The mod never calls `AirbaseOverlay`'s landing methods, because they send network commands and change runway usage.

## Choosing the approach

`ApproachSelector` works on flat 2D runway lines (`RunwayLine`, meters, X east, Y north) and knows nothing about Unity, so the tests run without the game. It picks the nearest runway within range, measured to the strip, and the end of it closer to the pilot.

It ignores heading on purpose. The line exists to judge the turn onto final, so it must show while the pilot is still side-on to the runway. Two heading-based rules failed in game (see [testing](TESTING.md)). Pattern-leg inference picked the wrong end while manoeuvring, and final-only hid the line when it was needed.

## Settings

`ModSettings` binds every setting through its `Bind` helper, which tags each one with a `ConfigurationManagerAttributes` holding the name F1 shows, an `Order`, and whether it's advanced. ConfigurationManager 18.4.1 lists a plugin's sections in the order they're first bound and sorts settings by descending `Order`. The helper counts `Order` down, so the constructor reads top to bottom like the F1 window.

ConfigurationManager has no nested or collapsible sections, and only the plugin's own header folds, so `SettingSections` names each section `Group / Feature`. Advanced settings hide until the player ticks **Advanced settings** or searches. A range becomes a slider, and a 0 to 1 range shows as a whole percentage with no text box unless `ShowRangeAsPercent` is false. An enum becomes a dropdown labelled by each value's `[Description]`, or by its name split into words.

BepInEx keeps a saved value only under its exact section and key, and writes back every saved value no setting claims. So a setting that moves gets a row in `SettingsMigration.Moves`, and one that's gone gets a row in `SettingsMigration.Removed`. Before anything binds, `Plugin` hands BepInEx's private dictionary of saved but unbound values to `SettingsMigration.MoveSavedValues`. It re-files each moved value under its new place, so the setting picks it up as it binds, and drops each removed one.

The old plugin (see The old plugin) had its own GUID and so its own settings file. On the first start while the new file holds nothing, `SettingsMigration.CarryOver` copies every value from the old file into that dictionary, read the way BepInEx reads a file. The moves and removals then apply to it as to any old file. The old file stays, since NO Missile Indicators reads its missile arrow settings from it.

## Measuring cost

Everything under `Diagnostics/` is off unless the player asks for it. `PerformanceLog` times `Plugin.LateUpdate` and the `onMapChanged` handler with `Stopwatch` timestamps, next to `Time.unscaledDeltaTime`, and hands each sample to a `FrameStats`. `FrameStats` is Unity-free and preallocated. It keeps running sums and worsts, plus a ring of 4096 frame times sorted once per window for the 1% low (`FrameStatsTests`).

The mod's own times cover its scripts only. Canvas rebuilds and draw calls for its graphics show up only in the frame time, so the honest comparison is frame time with the mod on and off.

`PerfTest` runs that comparison from an F1 button. Conditions measured one after another while flying drift apart for reasons unrelated to the mod. So within each view the four conditions (mod off, mod on, typical drawings, heavy drawings) take turns in short slices (`PerfSchedule`). The order reverses every other round, so each condition's slices sit at the same average time, and each condition's frames go into one `FrameStats`. Both drawing sets ride on the same nearest live units, at most 10 of them (the typical set uses 3), so runs in busy and quiet places redraw about the same amount.

`ModTimings` times each part of the mod's frame. `ModGraphic`, the base of the mod's own meshes, times their rebuilds in the canvas update. A section nested in another counts only in the inner one.

The button is a `ConfigurationManagerAttributes.CustomDrawer`, which ConfigurationManager finds by the class's name, so the mod needs no reference to it. The test switches the mod through `Plugin.ModOn`, never by writing General.Enabled. It shows the drawings on both maps through `ModSettings.PerfTestShowsDrawings`, never by writing ShowTools or ShowOnMinimap, so a crash can't save any of them. An error that disables the mod mid-test puts the map back too, while the mission's map is still the one the test started on.

The test opens and closes the full map with `DynamicMap.Maximize` and `Minimize`, the calls the game's own map key makes. It swaps the player's drawings out with `ShapeStore.Save` and `Restore`, which keep the undo history and add no step.

`StressDrawings` builds the typical and heavy sets from plain shapes, sized to the minimap: the rect's world width over one map meter in world units (`mapDisplayFactor` times the icon layer's scale). The minimap follows the aircraft, so drawings built once would slide out of it and leave later slices less to draw. A set is built again around the minimap's centre each time a slice switches to it, on the units picked for the first set.

`RenderCounters` adds the rendering table. Unity's counters come from `ProfilerRecorder`, looked up by name through `ProfilerRecorderHandle.GetAvailable`, since a release player records the render counters but not the `Canvas.*` markers. Canvas time is measured without the profiler. While the test runs, two timestamp steps in the player loop wrap `PostLateUpdate.PlayerUpdateCanvases` (layout, graphic rebuilds, and `RectMask2D` culling) and `PlayerEmitCanvasGeometry` (batching). They come out when it ends.

`PerformanceLog` keeps a `RenderCounters` of its own while LogPerformance is on, for its second line (`PerfLogLines`, unit-tested). The timestamp steps stay in the player loop while either one runs, and while the test runs the log leaves the canvas time to it. That line also counts what `ModActivity` saw in the window: label layouts, mesh rebuilds, received-drawing syncs, and the most 3D rings in a frame and which path drew them. Each count is a plain increment where the work happens. A third line gives each teammate's sharing, by the network log's `peer N` label, with Steam's quick status of the session (`IDrawingTransport.TryGetSessionStatus`), one Steam call per teammate per window.

The test's sharing modes (`PerfSharing`) add a `perf-test` channel in memory only. With Sharing on, the channel joins the player's Broadcast list when the test sends and the Receive only list when it only receives. With Sharing off, `MapToolHost.SharingBroadcast` and `SharingReceive` hold that channel alone, so only test traffic goes out. While the test's drawings are in the store, `MapToolHost.HoldOutgoing` keeps sending the player's own drawings as they were, with the test's after them.

In these modes slices run on the wall clock from the next half minute (`PerfSchedule.FirstSliceAt`), so two PCs started within the same half minute measure each condition together. `PerfSharingStats` reads each slice's traffic and Steam time from sharing's `DrawingTraffic` counters, and allocation from Mono's used heap size, which counts the game's allocations too.

## Map tools

A Tools button beside the full map, or the Tools key, opens a menu of drawing tools: a rail of icons and a strip of hints and options. What the tools draw shows on both maps and, for some shapes, in the 3D view. Everything lives under `src/NoMapOverhaul/MapTools/`, and `MapToolHost` ties it together:

```text
Plugin.LateUpdate → MapToolHost.Update   (every frame)
  ├─ mission check     a different DynamicMap, or none, clears the ShapeStore
  ├─ Tools, tool, undo, and redo keys, while the full map is open
  ├─ MapToolMenu       the rail and strip on the full map; a click records a MenuCommand
  ├─ MapToolInput      left clicks, drags, and typing to the active MapTool
  ├─ right click       cancels the active tool's drawing or deletes one (RightClickRule)
  ├─ MapTool.OnFrame   every tool, every frame: live state, 3D labels and rings (WorldLabelPool)
  └─ MapShapeLayer     redraws what changed; live shapes and live overlays on the 10 Hz refresh
       └─ LabelLayout  places the labels when one changes, or once the full map comes to rest
```

### The menu

`MapToolMenu` builds the menu. Its sizes are design pixels at 2560x1440, turned into map canvas units by `Px`, 0.75, since the map canvas scales by 4/3 at that resolution. So the menu scales with the game's UI like the rest of the map.

- **Rail.** Columns are 48 pixels wide (`RailGrid`). From the top: the Tools head (48 tall), a 44-pixel cell per tool, 40-pixel Undo, Redo, and Clear cells, then 22-pixel rows of swatch cells. The swatches are the six built-in ones, then six slots for custom colours from the next row, with no gap between the two. Groups are 6 pixels apart with a divider between them. In one column the swatches sit two to a row. With more columns, the cells read left to right in rows, each group starting a row, and the swatches spread evenly across the width. The head sits alone at the right end of the first row, so the closed Tools button sits nearest the map.
- **Strip.** 48 pixels tall, shown while a tool is picked. It holds the tool's name, a divider, the hint (`MapTool.Status`), then the option buttons (`Options`, with `PickedOption` solid), their unit (`OptionSuffix`), and a count (`Counter`). It grows to fit, up to its placement's width, which is 640 pixels inside the map. Past that, the buttons, unit, and count move to a second row, starting under the hint, with the same 10-pixel margin below them that one row has. So a short hint keeps its one line. Only a hint too long for its row by itself wraps, growing the strip taller (`MenuLayout.FitStrip`). While `MapTool.Warning` is true, or the store is full, the name gives way to an amber sign and hint, and the tool's cell gets an amber badge.
- **Hover tag.** The hovered cell's name, 6 pixels beside that cell and centred on it, with its key in a box. The keys are the Tools key on the head, each tool's key, and Undo's and Redo's. The head's tag stays hidden while the rail and strip both sit inside the map and the strip is open, since the strip then runs right of the head and the tag would cover it. The tag points into free space: left, off the map, while the rail stands outside it and the screen has room for the tag left of the cell, else right (`MenuLayout.HoverTag`). A tag for a cell past the first column covers the cells beside it while it shows. It takes no clicks, so moving onto it leaves the cell and hides it.
- **Author label.** While the cursor is on a teammate's drawing on the full map, with the rail open or closed, the owner's name shows beside it (`DrawingAuthor.Name`, `Teammate` when the roster has none). It's an `OutlinedText` in the HUD font at 15 pixels, hint-coloured with the map text's dark rim and rich text off. It sits on the map's canvas, so it scales with the map and is clipped to it. Its top-left corner goes 22 pixels right of the cursor's hot spot and 6 below, clear of the pointer's arrow. Past the map's right edge it flips to end 8 pixels left of the cursor, and past the bottom edge to end 4 above it, keeping 4 pixels inside the map. Of those four places it takes the first clear of the menu's parts inside the map and the game's HUD boxes (`MenuLayout.AuthorLabel`). While a tool is picked, the pointer catcher says whether the cursor is on the map. With the rail closed, the game's `DynamicMap.IsCursorInMapRectangle` does, off the Tools button. The eraser's picker finds the drawing, so a drawing of the player's over a teammate's names no one. The name is looked up and typeset only when the drawing under the cursor changes. Each frame only moves the label.

`MenuLayout` places the menu each time the full map opens, and again if the map's rect on screen or the screen's size changes. It is Unity-free and unit-tested (`MenuLayoutTests`), and works in screen pixels from the map's rect and the map canvas's scale. Where there's room it keeps the map clear:

- **Rail outside.** Left of the map, 8 pixels from its edge (the grid's row letters sit just inside it), its top level with the map's. Where the HUD below leaves too little room, it grows up past the map's top edge by whole pixels instead, as far as the HUD above allows. It takes the fewest columns that fit on screen and clear of the game's HUD, each grown by 8 pixels. The MFD buttons below leave about 225 pixels at 2560x1440, so it takes three columns, 239 tall, its top 14 pixels above the map's and 16 under the speed readout (two would be 367). The swatch rows and the gaps between groups are that tight to keep the rise small.
- **Strip outside.** Above the map, its bottom 8 pixels above the map's top edge (the column numbers sit just inside it). It goes in the leftmost stretch clear of the HUD and at least 360 pixels wide, which fits the widest tool's name beside a hint wrapped to its narrowest, with the buttons on a second row. That's the stretch between the speed readout and the mission clock, about 430 pixels. A second row or a wrapped hint grows it up, as far as 8 pixels below the screen's top.
- **Fallback.** Each one goes back inside the map's top-left corner on its own when it doesn't fit. The rail sits 32 by 38 pixels in from the corner, one column, and the strip flush right of its head, so together they frame the corner as an L. With the rail outside, the strip takes the rail's corner. The rail falls back on a screen too narrow for it beside the map (narrower than about 1.06:1), the strip when the map is too near the screen's top, and either when the game's HUD leaves no room.

The HUD it keeps clear of is the game's `VirtualMFD`, which shows while the full map is open: the speed, altitude, and attitude readouts above the map and the MFD button columns beside it. While the player has no aircraft (`CombatHUD.aircraft` is null, as before spawning), the readouts give way to the mission clock, `VirtualMFD.missionTime` in its `clockPanel`. The clock is centred above the map where the attitude ball goes, but twice as wide. The layout avoids both at once, so the strip stays put when the player spawns.

`MenuLayout.HudBoxes` holds their boxes from the game's level1 scene, which `tools/dump_map_ui.py` prints. The clock's panel fits its text at runtime (91 units plus 5 of padding either side), so its box is widened a little for wider digits. Its canvas and the map's are both 1920x1080 references matched to the screen's height. So every box keeps its place beside the map at any resolution, and outside placement is the same in design pixels at 16:9, 21:9, and 5:4. An MFD screen the player opens from those buttons slides in left of the map and may sit partly under a rail outside it.

Inside the map, the menu sits on the map's own canvas after the map, so it takes clicks before the map under it. Outside, the map's `RectMask2D` would clip it, so it goes under the map's parent canvas (`MaximizedMapCanvas`). Its rect there is kept the same as the map's, so both count design pixels from the map's corner. That canvas sorts above the HUD's (`GameplayUICanvas`), so the menu draws over the game's UI and wins its clicks. The menu moves back after the map each time the game reparents the map to open it. Menus above that, such as the Escape menu, still cover it.

The ground of the rail and strip is two `PolygonGraphic` fills under a `StrokeGraphic` rim. The rail's fill has one 6-pixel chamfer on its head's outer corner. Joined as an L inside the map, one rim goes round both. Apart, each has its own rim and chamfer. Each fill is the raycast target for its own rect, so clicks in the gaps between cells stay off the map, and the empty map right of the rail and under the strip still takes them. While inside the map, the fills are also areas map labels keep clear of.

Icons are `StrokeGraphic` lines drawn by `RailIcons` from SVG paths, rebuilt only when a cell's look changes. A filled dot is a ring as thick as its radius, and the warning triangle is a thick closed stroke inset by half its inradius, so every edge is anti-aliased. A cell is a `Button` whose fill the button tints through a `ColorBlock`: the HUD green at 0% at rest, 16% hovered, and 32% pressed, with a solid swap for the picked cell. `MenuHover` records the hover, since a `Button` keeps it protected.

Colours come from the game's MFD palette:

- ground `#050E07` at 78%
- rim `#2BE127` at 32%
- icons and labels `#41FF52` (72% at rest, full on hover and in the strip, 22% disabled)
- picked fill `#2BE127` at 90% with a `#03140A` icon
- hint text `#C8F5CD`
- warnings `#FFB23E`

The custom row shows a chip per colour in the hidden CustomColors setting, which `CustomColors` reads and writes (unit-tested). A **+** outline fills the first free slot, with nothing after it. The picker's Add on an RGB a swatch already has, built-in or custom, picks that swatch and saves no copy (`CustomColors.With`). A right-click on a chip reaches `MenuRightClick`, since a `Button` ignores it, and records a command like any click.

The **+** opens `MapColorPanel`, a plate on the map's canvas styled like the Channels panel, so it takes clicks before the map. It opens in the map's top-left corner, 8 pixels clear of the map's edge, or of the rail and strip where they're inside. Its wheel is a `RawImage` of a 256-texel texture, built the first time the picker opens and kept for the session (`HideFlags.DontUnloadUnusedAsset`). The brightness slider is a black-to-white ramp built the same way. Brightness tints the wheel grey and the ramp takes the picked hue at full brightness, so neither texture is rebuilt, since a colour's RGB scales with its value. `ColorWheel`, Unity-free and unit-tested, holds the HSV maths and the hit test. A press counts on the wheel only inside its rim, and a drag past the rim keeps the rim's colour in its direction.

The panel only records presses. It follows the cursor in the guarded update while the button is held, and moves its marker and recolours only when the pick changes. Closed, it does no work per frame. Open, it holds only the game's Escape menu off (`MapToolInput.HoldPauseKey`), so Escape cancels the panel rather than opening that menu. Every other key reaches the game, and the map key closes the map, which cancels the panel too. The Channels panel has text fields, so it takes the whole keyboard instead. A press on the picker, its buttons included, holds the player's Rewired mouse maps off until the button comes up (`MapToolInput.HoldMouseUntilRelease`), since `DynamicMap.MapControls` pans the map on any held left button. The two panels close each other.

### Shapes and the store

A drawing is a `MapShape` subclass holding plain data only: meters (X east, Y north, the game's global x and z), unit anchors by `PersistentID.Id`, and colour bytes. None of it is a Unity type, so drawings can be saved, or encoded for teammates (`DrawingProtocol`), without Unity. A `MapPoint` with a unit id follows that unit.

`ShapeStore` holds every tool's shapes in draw order. Each change stores a new snapshot of the list, so every Add, Remove, Replace, and Clear is exactly one undo step. Shapes are immutable, so snapshots share them, and an edit is a `Replace` with a changed copy. State that shouldn't be undone, like how far along a route the aircraft is, stays in the tool.

A note on a unit goes when the unit does (see What the mod may show), through `Forget`. `Forget` takes the note out of every undo and redo step and merges steps left the same as the one before. So neither undo nor redo, nor undoing a Clear, can put it back on a dead unit, and no step is left doing nothing. Having undo skip such notes instead would leave steps that change nothing visible, and a store holding drawings no one sees. The Text tool checks its notes' units each frame in `OnFrame`, which runs before the map draws. So a note an undo brings back onto a unit that went meanwhile goes the same frame.

The MaxShapes and MaxPenPoints settings cap the shape count and the total freehand points. The store keeps 100 undo steps and resets when the scene's `DynamicMap` changes, which is how leaving the mission shows.

A shape draws itself in meters through `IMapCanvas`, which has lines, arrows, polylines, rings, the game's waypoint marker, and labels. A label says what it's attached to, and the layer decides where it sits (see Labels on the map). The same `Draw` call renders the shape, hit-tests it for the eraser, and redraws it red under the eraser's cursor. `ShapeHitTest` measures how close each stroke passes to the click, and checks each label's box where the layer last placed it on the full map (`ILabelPlacements`).

When a shape reads a unit through `TryResolve`, or reads `OwnAircraft`, the layer marks it live and redraws it on each of the game's 10 Hz map refreshes. Other shapes redraw only when added, when the zoom changes, or when a drawing setting changes. A tool's overlay follows the same rule, and also redraws when the drawings change or the tool calls `InvalidateOverlay`. So the eraser redraws its highlight only when a different shape comes under the cursor. It looks every frame, since a drawing on a unit can move under a still cursor. Panning rebuilds nothing, because the layer moves with the map.

An anchored point resolves through the faction's knowledge, as the unit's map icon does (see What the mod may show). Once the unit is destroyed or its track goes stale, `TryResolve` returns false with the last known position, and the shape says lost.

### Drawing on the map

`MapShapeLayer` is one layer under `DynamicMap.iconLayer`, like the runways. The mod's other layers lead the icon layer's children. The runways take the first slot when they're created, then the boundary keeps the first slot and the names the one after it (see Airbase names). The game appends its unit icons, waypoints, and radar pings after them. On each refresh the drawings layer takes the slot just past the last child named `Baanish…`, so drawings cover the runways, names, and boundary but never hide a unit icon. The full map and the minimap are the same `DynamicMap`, so the order holds on both.

The pointer catcher is a child of the map itself, above the icon layer, so it still takes left clicks. With the ShowOnMinimap setting off, the layer is inactive while the full map is closed and skips every redraw. It redraws everything when the full map opens.

The layer has its own `Canvas`, so redrawing a shape doesn't re-batch the game's icons, and icons moving every frame don't re-batch a long pen stroke. Unity also re-batches a canvas when a graphic in it turns, and treats every transform write as a change, even to the same value. So the markers and labels, which turn upright as the minimap turns, sit in a second canvas inside the first, and the lines' canvas re-batches only when a shape redraws. The layer skips writes that would repeat a transform's value.

Each shape gets its own container, kept by the shape's id, and all its lines are one `StrokeGraphic` mesh. An edit, an undo, or a teammate's update replaces the shape object under the same id, so its container redraws in place. Containers are made only when an id appears and destroyed when it goes. A container with no lines, such as a note or an idle tool's overlay, switches its graphic off. The map's `RectMask2D` culls every enabled graphic under it on every frame, whether it moved or not (`RectMask2D.PerformClipping`), so each graphic costs a little every frame even when nothing changes.

Lines are flat: one colour across the width with a one-pixel anti-aliased edge, the `EdgeProfile` without its rim. On a line this thin, the runways' fade from fill to dark rim to clear would take up most of the width and read as a bevel. Joins are mitred, open ends fade out over the anti-aliased edge, and an arrowhead is five line widths long. A `StrokeGraphic` fits its rect around its lines, because the map's `RectMask2D` culls a graphic by its rect, and a default-sized rect would vanish once scrolled out of view.

Markers are copies of `DynamicMap.mapWaypoint`, the game's 20-unit steerpoint sprite. Each shape's markers are one `ImageBatch` mesh at the start of the label canvas, so markers draw over every line, in the shapes' order. The batch copies the mesh that a hidden `ImageTemplate` (an `Image` with the prefab's sprite, type, and size) would draw, so each copy looks like the prefab. The batch is maskable, which the prefab isn't. With a copy of the prefab per marker, a full route would be 99 graphics for the map's mask to cull every frame.

Labels are `MapLabel`s after the markers, so no line or marker crosses a label. Widths and text sizes use the inverse map scale, like every other map graphic here. Each label and marker is made upright when it's created. The layer turns them all upright again each time the heading-up minimap has turned a degree, as it does the runway numbers, and in between they tilt with it by less than that.

Each shape's container keeps the bounds of what it last drew. The view is the part of the layer the map shows, grown by a sixth of its size and by 80 icon units (about a label's reach). A drawing wholly outside it has its lines, markers, and labels switched off and its labels left out of the layout. So the mask doesn't cull them every frame, and a pile of teammates' drawings far off costs nothing. A drawing on units out of view redraws only once its bounds, stretched to where its units are now, reach the view again.

### Labels on the map

A shape names what a label is attached to with a `LabelAnchor`: a note's point, a note's unit, a waypoint marker, an arrow's head and tail, or a ring. A note, fixed or on a unit, is text with a dark one-unit rim. Every other label sits on a plate in the menu's ground colour, with a 1-pixel rim in the drawing's colour at 55%.

The plates and leaders of every label are one `FeatheredRectBatch` mesh, drawn after the markers and under every label's text, and rebuilt whenever the labels are placed. One graphic per plate would be one more for Unity to cull every frame. Where labels overlap, alternating plate and text materials would also split the labels into a draw call each. The cost is that two overlapping labels print their text over one merged plate, where a plate of its own would hide the text under it.

The text on the plates is one mesh too, `PlateText`. One `TextMeshProUGUI`, switched off so it never draws, typesets a label's text when it changes. Each glyph's quad is copied, as `TextRim` copies them, into one `GlyphBatch` per font material, rebuilt with the plates. The typesetter sits at the labels' scale, since TextMeshPro bakes the text's world scale into each glyph's SDF scale (in its second UV). A label is typeset again if that scale drifts by 1%. A text object per label would cost every frame whether anything changed or not: the mask culls each one (`RectMask2D.PerformClipping`) and TextMeshPro's update manager checks each one's scale (`TextMeshProUGUI.InternalUpdate`).

Notes are typeset the same way and drawn after the plated text by `NoteText`. Each note's rim (its glyphs copied nine times, as `TextRim` does) and then its face go into a few glyph batches, one per font material. The exception is a fallback-font glyph that would change which note draws over which (`NoteRuns`, unit-tested). The layout keeps notes apart on both maps, so that happens only where no slot is clear. A note's vertices are rebuilt only when it changes, and they're built upright. The batches' root turns them with the heading-up minimap, so a turn rebuilds nothing. Fallback-font materials are pinned, since TextMeshPro destroys one once no text object of its own uses it.

A shape's draw only sets the text. Once every changed shape has drawn, the layer measures each label, turns its anchor into icon units, and hands them all to `LabelLayout`, which is Unity-free and unit-tested (`LabelLayoutTests`).

`LabelLayout` places labels in priority order, and a later label never pushes an earlier one. Notes and waypoint numbers go first, since the pilot put them there. Next come labels in a tool's overlay, such as a measurement still being picked, then bearings newest first, then radii newest first. Each kind tries its candidate slots in order and takes the first one clear of the obstacles and of every label already placed:

- A bearing tries past its arrowhead with an 8-pixel gap. Next it tries beside the head, on the side the shaft leans away from, then slid back along the shaft, above then below it, up to 40% of its length. Last it tries the nearest free slot within 48 pixels, joined to the head by a 1.5-pixel leader at 70%.
- A radius tries its ring at 12, 2, 10, 6, 4, and 8 o'clock.
- A waypoint number tries upper right, upper left, lower right, then lower left of its marker.
- A note on a unit tries right, left, above, then below the unit, 8 pixels clear of an icon the size of the waypoint marker. Then it tries the same again 13 pixels further out, clear of an icon twice that size. It goes with the other notes. A fixed note never moves.

With no clear slot, a label takes its first. A label keeps the slot it had while that slot stays clear. A bearing whose slot is blocked may try all 59 of its slots. So it first gathers the obstacles and labels that overlap any of them and checks each slot against those alone, which chooses the same slot as checking every icon on the map.

An overlay label identical to a stored one takes the stored one's slot. That covers the eraser's highlight, which redraws the hovered drawing. The eraser names that drawing (`MapTool.RedrawnShape`), so of two identical drawings, the highlight takes the slot of the one a click would delete.

`LabelObstacles` collects the obstacles, in the layer's icon units:

- the unit and airbase icons in `DynamicMap.mapIcons`, grown by 6 pixels
- the text of the mod's other map layers (the face of each `OutlinedText`, by its `textBounds`)
- the game's grid letters and numbers under `DynamicMap.gridLabels`, boxed by their character count, since the game leaves their rects at 100 units
- the menu's rail and strip while they sit inside the map

The layer places labels:

- when a label changed (its anchor, text, size, or colour, or how many a shape draws)
- when the full map opens or closes
- when the menu changes shape
- when the full map comes to rest

A drawing on units redraws on a map refresh only when one of its units moved, or gained or lost its live track. So a parked, lost, or destroyed unit's drawings cost nothing, and their labels are placed again only then.

The full map holds still while it moves less than a pixel a frame. While it follows the aircraft zoomed out, the game moves it a small fraction of a pixel a frame, and a pan moves it by more. It comes to rest a tenth of a second after a pan or zoom, or once it has drifted about 11 pixels while holding still. Only the menu and the grid labels stay put on screen while the icons and every other label move with the map, so the map creeping after the aircraft doesn't have every label placed again several times a second. The layer collects obstacles only when it places labels on the still full map, never per frame. While the full map moves, each label keeps its slot, or takes its first.

On the minimap the game's labels aren't collected, so labels keep clear only of each other. The layout runs when a label changed and each time the minimap has turned 5 degrees (`MinimapHeading`). The layout works in the frame the labels stand upright in, the screen's. So on the heading-up minimap a bearing's label sits past its arrowhead as the arrow shows on screen, and a radius sits at 12 o'clock on screen. Each time the labels are turned upright, every label takes its slot again in the turned frame.

### Input

While a tool is active, a transparent `MapPointerCatcher` covers the full map, above the icons and below the menu. By default the game has one left-click path on the map: Unity's event system hands the click to a unit or airbase icon, and `MapIcon.OnPointerClick` selects it. The catcher takes that click instead. The game's other map selection, `DynamicMap.SelectFromMap`, runs on the Rewired "Select" action. The Rewired `InputManager` data in `resources.assets` binds Select to Enter, not the mouse, so nothing is patched. A player who has rebound Select to the left mouse button will see the game select a unit near a tool's click as well.

The active tool hears where the cursor is on the map whenever the mouse moves. It also hears when the map carries that point more than a pixel from under a still mouse, as the full map does while it follows the aircraft, so the eraser's highlight and a measurement's rubber band stay under the cursor.

A left press and release that moves no more than a few pixels is a click. A longer drag still pans the map, since `DynamicMap.MapControls` pans on the mouse axes while the button is held. A tool that captures drags gets down, drag, and up events instead. The player's Rewired mouse maps stay off until the button comes up, the same switch the game's chat box uses. That holds when the drag is cut short too, by a right-click, the map closing, or the tools or the mod switching off. A coroutine on BepInEx's `ThreadingHelper` waits for the release, since the mod may not run another frame. With the maps back on while the button is held, `DynamicMap.MapControls` would pan the map, and once the map closes and the cursor hides, a Fire bound to that button would fire. The chat box turns every map back on as it closes, so while the button is held, each frame switches off again the maps the drag had switched off.

The catcher also records right presses on the map, and a right press on the menu never reaches it. `RightClickRule`, which is Unity-free and unit-tested, decides what a right press does:

1. If the first selected icon is a friendly unit that takes commands (the test `DynamicMap.MapControls` makes before its move order), the order has the click and the tools do nothing.
2. Otherwise, the host drops the active tool's `InProgress` work through `OnDeactivate`.
3. With nothing in progress, it removes the drawing `ShapeHitTest` finds within the eraser's reach, as one undo step.

Whenever the tools take a right press, on the map or on the menu, NOAutopilot is kept from adding a waypoint on it too (see Patches). The Waypoint tool's route is its own. It never reads NOAutopilot's route, and NOAutopilot never reads it.

A tool that captures the keyboard gets `Input.inputString` a character at a time. A click that starts typing takes the keyboard in the same frame, so a key pressed straight after it never reaches the game. Meanwhile the input does what the chat box does: the Rewired keyboard maps are off, `GameplayUI.AllowPauseKeybind` is false, and `CursorFlags.Chat` is set, which NOAutopilot checks before reading its hotkeys. All three come back once Enter and Escape are released, since Rewired would read a key still held as a fresh press.

The chat box, `DialogueBox`, and `AircraftSelectionMenu` each switch `AllowPauseKeybind` off while open and on as they close, with their cursor flag (`Chat`, `Dialogue`, `SelectionMenu`) set meanwhile. Typing doesn't start while any of those flags is set or `AllowPauseKeybind` is already off, so it always starts from the pause key on and the chat flag off. On release, the pause key comes back only if none of the three flags is set by then, since a UI that opened while the player typed owns it.

The map tool keys are H for Tools, 1 to 6 for the tools in rail order, and Z and Y for undo and redo by default. They act only while the full map is open, and not while a tool takes typing, the chat is open, the game menu is up, or a text field has focus (`InputFieldChecker.InsideInputField`, which the game's own `ExtraUiInput` checks), such as a name field in the mission editor.

The game's default keyboard map binds W, S, A, D, Q, E, Shift, Ctrl, Space, B, Enter, Backspace, Tab, V, G, X, F, N, L, M, R, T, Escape, comma, the arrow keys, Page Up and Down, the number pad, and F9 and F10. The only code that reads Y directly runs in the Unity editor. Nothing there binds the number row or H. The game reads H itself only in the mission editor's chase camera, where it hides the HUD, and 5 and 6 only in a network debugging overlay. NOAutopilot's default keys don't use any of them either.

The Tools key does what clicking Tools does, and a tool's key picks it, opening the rail on it if it's closed. Pressing the Bearing/range key twice within 0.35 s (`DoubleTap`, unit-tested) also ties the measurement's start to `OwnAircraft`, as a click on the player's own icon would. `KeyboardShortcut.IsDown` refuses a key pressed with Shift or Ctrl held, so the throttle's modifiers don't trigger them.

Menu buttons and the catcher only record what happened. `MapToolHost` acts on it in the plugin's guarded update, since a throw inside Unity's event system would bypass the guard.

### 3D labels and rings

`WorldLabelPool` generalises the HUD callout. Each label is a copy of the callout's label, so it shares the HUD canvas, font, and visibility, and hides with the HUD and while the map is open. Tools request labels in `MapTool.OnFrame` with a global position in meters, Y being height above sea level. The pool projects each through the camera and hides the ones behind it, or more than 200 pixels plus half the text's longest possible width off screen. It reuses its copies from frame to frame, up to 32 at once.

Labels whose points land within 4 pixels of each other on screen, such as a bearing and a note on the same unit, stack downward in the order the tools add them. So the host's tool order puts a bearing above a note. A label within 4 pixels of any label in a stack joins the bottom of that stack.

The labels' container has a `Canvas` of its own, so a label moved or retyped re-batches the labels alone rather than the game's whole HUD. It asks for TextMeshPro's extra vertex channels, as the HUD's canvas does, since a canvas passes on only those it asks for.

The Circle tool also requests each circle as a ring through `IWorldLabels.Ring`, newest first, level with the centre. The ring's height is the unit's altitude, or the ground height the tool reads once when the circle is made. `WorldRingPool` draws up to 16. Each is a flat `StrokeGraphic` in a container under the labels' parent with its own `Canvas`, so a ring shares their visibility, draws under their text, and re-batches the rings alone. Only a ring inside the camera's view takes a slot, so a ring in front of the player always shows. Each ring is kept by its drawing, so it keeps its slot and mesh from frame to frame however the requests are ordered. The container sits at the world origin of the screen-space HUD canvas, where world units are pixels.

`RingProjection`, which is Unity-free and unit-tested (`RingProjectionTests`), turns the ring into chords in camera space. It clips each chord against the near plane and the screen's four sides grown by 32 pixels before projecting it. So a ring that passes behind the camera is cut rather than mirrored the way `WorldToScreenPoint` mirrors points behind it, and no vertex lands so far off screen that float precision bends the line.

It uses 128 chords for a ring seen from inside or close by. It halves that, down to 16, while each chord still keeps within a quarter pixel of the true circle. That error is judged from the ring's nearest possible point by a camera looking straight at it (off the screen's centre, perspective can stretch it to about half a pixel). So a small ring far off is a much smaller mesh to rebuild each frame. A ring whose bounding sphere lies wholly outside one of the five clip planes projects to nothing without being sampled.

A ring whose mesh is the whole ring in view is moved rather than rebuilt while it keeps its shape on screen. Each frame the pool projects 16 points around it. If moving the old mesh by their mean shift leaves every one within 0.4 pixels of its place, the mesh just moves. The shape changes smoothly around the ring, so between those points it strays barely further, about as far as the chords stray from the circle. A turn of the camera, which moves a ring furthest on screen, slides it with almost no change of shape, so a ring far off is rebuilt only now and then in flight. A ring cut by the screen's edge or the near plane is rebuilt whenever the camera moves, and any ring is rebuilt when its circle or the line width changes. While the HUD's canvas is switched off, as it is over the cockpit while the full map is open, no ring is projected at all.

There is no radius label in the 3D view. At the ring's nearest point it would hop between chords, or vanish while flying inside the ring, where that point is beside or behind the aircraft.

When the shader bundle loads, the GPU draws the rings instead, and the path above is the fallback. `RingShader` loads the WorldRing shader from `nomapoverhaul-shaders.bundle` beside the DLL when the plugin starts. It logs which way the rings are drawn: by the shader, or on the CPU and why (the file missing, not loading, or the graphics API unable to run it). The advanced Diagnostics setting RingsOnCpu draws them on the CPU anyway, for comparing the two.

With the shader, each ring slot is a `GpuRing`: a bare `CanvasRenderer` in the same container, holding the `RingMesh` layout and a material of its own. The mesh is built once per slot and holds no position the shader reads. Each vertex names a point of the ring's 128, which way its chord runs, and its lane across the line. The lanes are the same four as a flat `StrokeGraphic` line: fill to its edge, then a one-pixel fade. Its positions span far past the screen only because the canvas skips a mesh whose positions are all one point.

Each frame the pool sets five vectors on the material: the ring's centre, east radius, and north radius in camera space; the screen's half size, focal length, and near distance; and the half width with the guarded view's slopes. `RingProjection` works out the camera-space values from the centre relative to the camera, so the numbers stay small.

The vertex stage works out its chord's two points. It clips the chord against the near plane and the screen grown by 32 pixels, as `RingProjection` does, from the lower-numbered end, so both ends agree on what shows. A point in view is mitred with the chord on its other side, as `StrokeGraphic` joins a closed line. A point cut by a clip plane ends square on its chord's normal, off screen or past the near plane. The stage goes straight from screen pixels to clip space, since the matrices Unity draws a screen-space canvas with don't map pixels there.

Setting material properties doesn't touch the canvas, so a moving ring re-batches nothing and rebuilds no mesh. The colour is in the vertex colours, as on the CPU, so the canvas converts it to linear the same way, and a change of colour sets the mesh again. Each ring is a draw call of its own, where the CPU's rings share one. The shader always uses all 128 chords, so a small ring far off is a little rounder than the CPU's 16 to 128.

### Adding a tool

1. Give the tool its own folder under `MapTools/`, holding a `MapTool` subclass and its `MapShape` subclasses, as `BearingRange/`, `Circle/`, `Text/`, `Pen/`, `Waypoint/`, and `Eraser/` do.
2. Add the tool, its `RailIcon`, and the setting for the key that picks it (`ModSettings.BindToolKey`) to the list in `MapToolHost`'s constructor. That list sets the rail order and the order its 3D labels stack in. It's the only shared line a new tool needs, unless it needs a new icon, which is a case in `RailIcons.Draw`.
3. React to `OnClick`, or set `CapturesDrag` and use `OnPointerDown`, `OnPointerDrag`, and `OnPointerUp`. `MapPointer.Point` is the unit under the cursor, if any, else the ground. Set `CapturesKeyboard` while typing, and `InProgress` while anything is half-drawn, so a right-click drops it through `OnDeactivate`. Show hints in `Status`, with `Warning` set when the hint says why a click did nothing. Extra buttons go in `Options`, with `PickedOption`, `OptionSuffix`, and `Counter` if needed.
4. Add shapes with `Context.Shapes.Add` in `Context.Color`. Each add is an undo step, and a false return means a cap was hit. Draw anything that isn't a stored shape, like a rubber band, in `DrawOverlay`, and call `InvalidateOverlay` from any event that changes it. The layer redraws an overlay on its own only when the tool is switched, when the drawings or the zoom change, and on the 10 Hz refresh while it reads a unit or the aircraft. So a preview that follows the cursor invalidates on `OnPointerMove`. Request 3D labels in `OnFrame`, placed with `Context.TryResolveWorld` for a unit, or at a height from `Context.GroundElevation` taken once when the shape is made.
5. Format distances and bearings with `NavFormat` in `Context.Units`. `Draw`, `DrawOverlay`, and `OnFrame` run often, so they mustn't allocate. Cache label strings, and format again only when `NavFormat.DistanceKey` changes.
6. Keep logic that doesn't need Unity in files that don't reference it, link them in the test project, and add a `<Tool>Tests.cs` suite with an `All` list plus one `tests.AddRange` line in `Program.Main`. Settings bind through `ModSettings.Bind` in one of the `Map Tools / …` sections (see Settings), with a row in the settings table in `USER_GUIDE.md`.

| What | Where in `Assembly-CSharp` |
| --- | --- |
| Full map open | `DynamicMap.mapMaximized` |
| 10 Hz refresh | `DynamicMap.mapLastUpdated`, set by `UpdateMap` just before it raises `onMapChanged` |
| Unit under the cursor | `DynamicMap.mapIcons`, each `UnitMapIcon.iconImage` position and `unit.persistentID` |
| Icons labels avoid | `DynamicMap.mapIcons`, each `MapIcon.iconImage` rect |
| Grid letters and numbers | `DynamicMap.gridLabels`, legacy `Text` children placed by `GridLabels.GridLabels_OnMapChanged` |
| Waypoint marker | `DynamicMap.mapWaypoint`, the prefab `DynamicMap.MapControls` places for a move order |
| Right-click move order | `DynamicMap.selectedIcons[0]` a `UnitMapIcon` whose unit is `ICommandable` and friendly by `DynamicMap.GetFactionMode`, outside `GameState.Editor`, as in `DynamicMap.MapControls` |
| Unit positions | `FactionHQ.TryGetKnownPosition`, live while `FactionHQ.IsTargetBeingTracked` |
| Ground height | A ray down from 10 km, as in `DynamicMap.JumpCameraTo`, passing through units and trigger colliders |
| Unit system | `PlayerSettings.unitSystem` |
| Chat or game menu open | `CursorManager.GetFlag(CursorFlags.Chat \| CursorFlags.GameMenu)` |

## What the mod may show

Nothing the mod shows or works out about another unit may tell the player more than the game already does. Every position, altitude, and fact about another unit comes from what the player's own side knows. The mod reads it through the calls the game uses to show that unit to this player, never from the unit's transform. When the side's knowledge goes stale, whatever depends on it stops where the unit was last known and says so.

What the game shows the player, in `Assembly-CSharp`:

- Friendly units show where they are. `FactionHQ.GetKnownPosition` returns a friendly's own position, and `UnitMapIcon.UpdateIcon` draws a unit with no tracking entry where it is. With no faction (spectating), the map shows every unit that way.
- Enemies tracked live show where they are. Each spot by a sensor (`TargetDetector.DetectTarget`, `RadarLocator`, `LaserDesignator`, `MissileWarning`) reaches `FactionHQ.RpcUpdateTrackingInfo`, which stamps `TrackingInfo.lastSpottedTime`. For 4 s after, `TrackingInfo.GetPosition` returns the unit's real position and `FactionHQ.IsTargetBeingTracked` is true.
- Enemies whose track is stale show where they were last seen. After 4 s without a spot, `GetPosition` returns `lastKnownPosition` and stops updating. The map icon stays there and stops turning (`UnitMapIcon.UpdateIcon`). The HUD marker switches to its outdated sprite at half opacity, pinned to `FactionHQ.TryGetKnownPosition` (`HUDUnitMarker.UpdatePosition` and `SetOutdated`).
- Enemies never tracked show nothing. There's no icon or marker, and `TryGetKnownPosition` fails.
- Destroyed units lose their icon and marker (`UnitMapIcon_OnUnitDisabled`, `HUDIcon_OnDisableUnit`), and their track (`FactionHQ.DeregisterTrackedUnit`).

What the mod reads, and why each is fair:

| Shows | Reads | Fair because |
| --- | --- | --- |
| Drawings on a unit, and their 3D labels and rings | `FactionHQ.TryGetKnownPosition`, live while `IsTargetBeingTracked`; every unit when `DynamicMap.HQ` is null | The same position, altitude included, that the unit's icon and marker use |
| A drawing on a stale or destroyed unit | `KnownPositions`: the last live position, with `TryResolve` false, so the label says lost | Matches the frozen icon, and nothing moves until the unit is spotted again |
| A note on a destroyed unit going | `IMapToolContext.IsUnitGone`: `Unit.disabled`, or the unit gone from `UnitRegistry` | Setting `disabled` (synced to every client) or destroying the unit raises `Unit.onDisableUnit`, which removes its map icon (`UnitMapIcon_OnUnitDisabled`) and its track (`FactionHQ.DeregisterTrackedUnit`), so the note goes as the icon does |
| The unit a click lands on | Active `UnitMapIcon`s in `DynamicMap.mapIcons`, at their on-screen position | Only units whose icon the player can see |
| Your aircraft | `CombatHUD.aircraft` | Your own |
| Ground height under a fixed point | A ray down that passes through units | Terrain and sea only, so a hidden ship or building doesn't lift a label |
| Runways, boundaries, the HUD callout, airbase names | `DynamicMap.HQ.GetAirbases()`, carriers skipped | Your side's airbases, which the game already marks |

The perf test anchors its stress drawings through the same `TryResolve`, so it only uses units live on the player's map. The freeze logic, `Tracking/KnownPositions`, is Unity-free. `KnownPositionsTests` covers it directly, and `MeasureToolTests` covers an arrow and its label going stale and being spotted again.

## Shared drawings

Received drawings stay out of `ShapeStore`, so local tools and navigation keep ownership of the player's own drawings. `SteamDrawingSharing` uses a separate Steam Messages channel. It validates the current game roster, mission, faction, Broadcast and Receive only subscriptions, and peer session. Broadcast membership includes receiving and collaboration. Receive only adds read-only subscriptions.

Every drawing kind may share. Unit anchors carry unit IDs with zero position and elevation. A received anchor needs a position from the recipient's faction, live or last known. The mod checks this before rendering or input whenever a snapshot arrives, and on each of the game's 10 Hz map refreshes. Once known, received anchors resolve like local ones, freezing and reading `lost` when the track goes stale. An endpoint the recipient's faction never knew hides the whole received drawing.

The eraser and right-click share one picker across local and received drawings, and received drawings win equal-distance ties. Permission is checked after picking, so a read-only drawing blocks a local drawing beneath it. Changed received snapshots render before input, so the labels' hit boxes are current. A bounded erase request goes to the original owner, whose next snapshot publishes the result. Received drawings are never removed optimistically or placed in local undo.

[The sharing contract](SHARING.md) describes the protocol, bounds, and remaining validation.

## Patches

`SteamConnectionOwnershipPatch` filters the installed Mirage client and server connection callbacks by their gameplay connection or listener handle. Steam Messages connections must never enter gameplay acceptance, disconnection, or poll groups. Sharing requires both ownership prefixes to load. They have a separate Harmony owner, which keeps the guard in place through plugin disposal. [Drawing sharing](SHARING.md#transport-and-authorization) describes the transport and authorization checks.

The optional NOAutopilot patch holds back its right-click waypoints. NOAutopilot adds an autopilot waypoint on every right-click on the full map, in its postfix on `DynamicMap.MapControls` (`NOAutopilot.Core.Map.MapInteractionPatch.Postfix`), and has no setting to turn that off. With the Tools menu open a right-click belongs to the tools, so without the patch one click would delete a drawing and reroute the autopilot too. `AutopilotRightClickPatch` puts a prefix on that postfix which skips it while `MapToolHost.TakesRightClick`, meaning the menu is open and no selected unit would take a move order. Otherwise NOAutopilot runs as before.

NOAutopilot is optional and isn't referenced at build time. The plugin declares it a soft dependency, so when it's installed BepInEx loads it first. `Awake` then finds the handler by name in the assembly of the plugin registered as `com.qwerty1423.NOAutopilot`. Without NOAutopilot, or with a version that lacks the handler, it logs one info line and patches nothing.

The patch comes off when the plugin is destroyed or disables itself after an error. With General.Enabled off the host resets, which closes its menu, so the prefix lets every click through. A throw in the prefix lets the click through too, since it runs inside the game's map update.

## The old plugin

Up to 0.4.0 the mod was Baanish UI Improvements, GUID `com.baanish.nuclearoption.uiimprovements`, in `BepInEx/plugins/BaanishUiImprovements/`. A player who installs NO Map Overhaul without deleting that folder has both loaded, and both would draw the runways, names, and map tools.

The plugin declares the old GUID a soft dependency, so BepInEx loads the old plugin first and `Awake` finds it in `Chainloader.PluginInfos`. It logs a warning naming the folder to delete and destroys the old plugin's component. Every release of the old plugin takes its overlays down and leaves the map's refresh event in `OnDestroy`. A build with the NOAutopilot patch applied it under its own GUID, so removing it can't take this plugin's patch off.

The new plugin is the one kept because it's the one the player just installed. Standing down instead would leave the old version running with no visible sign of why.
