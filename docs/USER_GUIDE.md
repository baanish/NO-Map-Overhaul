# User guide

## Install

You need BepInEx 5 installed in Nuclear Option. Each release has two ZIPs:

- `BaanishUiImprovements-v<version>-plugin-only.zip` already contains the `BepInEx/plugins/BaanishUiImprovements/` folder. Extract it beside `NuclearOption.exe`.
- `BaanishUiImprovements-v<version>-nomm.zip` is flat. For a manual install, copy `BaanishUiImprovements.dll` to `BepInEx/plugins/BaanishUiImprovements/BaanishUiImprovements.dll`.

Start the game once. BepInEx then creates the settings file:

```text
BepInEx/config/com.baanish.nuclearoption.uiimprovements.cfg
```

To confirm the mod loaded, look for this line in `BepInEx/LogOutput.log`:

```text
[Info   :Baanish UI Improvements] Baanish UI Improvements 0.5.0 loaded.
```

To uninstall, delete the `BaanishUiImprovements` plugin folder and the `.cfg` file.

## What you see

Everything below applies to friendly airbases only, meaning your faction's. When an airbase changes hands, its runways appear or disappear within a tenth of a second. Helicopters get the airbase boundary and names, but no runways, approach line, or callout. The Tarantula tiltrotor lands on runways, so it gets everything a plane does. Carriers are left out entirely, since the ship icon already marks the deck.

### On the map

Runways show on both the minimap and the full map. The game uses one map object for both, so everything pans, zooms, and rotates with it. Each runway is a filled strip with a thin dark rim. When you zoom out, the strip keeps a minimum width so it never shrinks to nothing.

Each end carries the runway number you'd land on from that end. The mod works each number out from the runway heading, the same way the game numbers an unnamed runway in its landing clearance. Where the paint on the tarmac differs, the paint wins, so the clearance can name a different number from the map. The same goes for a runway a mission has named. So far that's one runway, Feldspar's crossing runway on Ignus, which the mod labels 16/34 where the heading gives 15/33. A mission can give a runway its own name, but the mod ignores it, because mission names don't always match the paint. Takeoff-only runways aren't drawn.

### The approach line

Within 5 km of a runway, a dashed line extends 5 km off the nearest end of the nearest runway. Each dash starts on a 500 m mark, so the tenth dash starts 4.5 km out. The last dash ends 200 m short of the line's full length. The line is fainter than the runway strip so the two don't read as one.

It's there to judge the turn onto final, so it shows whatever your heading. Over the runway it follows whichever end is closer, and it hides once you're on the ground.

### The HUD callout

The same runway end gets a label pinned just below its threshold in the 3D view, such as `RWY 27`. It uses the same font as the game's own "Taxi to Runway" label and hides with the HUD. It also hides when the threshold is behind you.

With **Include airbase name** on, the label adds the airbase: `NBSCLI RWY 27`.

With **Only with gear down** on, the label shows only while your gear is down. It appears as soon as you lower the gear lever.

### The airbase boundary

Turn on **Map / Airbase Boundary → Show landing zone** to shade each friendly airbase's landing zone with a faint circle and a thin edge line, in the game's friendly map colour.

The circle is the game's own rule. An aircraft that counts as landed inside it (radar altitude under 5 m, speed under 2.5 m/s) ends the sortie as returned rather than crashed. The same radius is the airbase's capture range, so the circle is also the capture zone.

### Airbase names

Turn on **Map / Airbase Names → Show airbase names** to label your faction's airbases on the full map, just under where the game draws their icon. Every airbase the game gives your side an icon for gets a name: the map's own, ones a mission adds, ones your side captures, and ones the mission switched off, which the game still marks. The name is the one the game shows when you hover over the icon. Enemy and neutral airbases get no name, since the game doesn't mark them, and carriers get none either, since the ship's icon marks the deck. The labels are a faint version of the runway numbers' green by default and don't show on the minimap. Airbases sharing a name get one label: Ignus free flight has three airbases called "Feldspar International Airport".

### Missile arrows

When a missile is incoming and outside your view, a red arrow on the screen edge points the way to turn toward it, so you can find it and shoot it down. Each missile gets its own arrow. A missile behind you gets an arrow on the side it's on, so a missile behind and to your left gets an arrow on the left edge. Once the missile is on screen, its arrow goes away.

Only missiles the game's missile warning already knows about get an arrow, and each arrow points where the game's flashing HUD marker for that missile is, so the arrows show nothing the game hasn't told you. When the game marks a missile's position as outdated, its arrow stays pointing at the last known position and fades to half, as the game's marker does. The arrow is a copy of the game's own off-screen target arrow in a different colour.

### Map tools

On the full map, a **Tools** button in the top-left corner opens a rail of planning tools: a waypoint route with markers in the 3D view, a pen, text notes, bearing and range measurements, range circles, and an eraser, with undo, redo, and colours. Only you see what you draw, and it clears when you leave the mission. The [map tools guide](MAP_TOOLS.md) covers every tool, the mouse and keys, and planning ideas.

## How the runway end is chosen

Within range, the mod picks the runway nearest to you, measured to the runway strip itself, and then whichever of its two ends is closer. Heading plays no part. The map line and the HUD label always show the same end.

Between two runways or ends at about the same distance, the choice can switch as you move. An earlier version tried to guess the landing end from your heading and traffic-pattern leg, and it got the end wrong while manoeuvring near the field.

## Settings

Change settings in game with F1 if Configuration Manager is installed. Changes apply immediately. Click the mod's name to open its settings; hover over a setting's name for a longer description. Settings marked *advanced* are tuning knobs that F1 shows only with **Advanced settings** ticked at the top of its window, or when a search finds them.

Without Configuration Manager, edit the `.cfg` file while the game is closed. Each section below is a `[Section]` heading in the file, and the key is the name before `=`. Colours are hex `RRGGBBAA`. A settings file from 0.4.0 or earlier moves its values into these sections the first time the game starts with this version.

The table lists the sections in the order F1 shows them.

| Section | F1 name | Key | Default | What it does |
| --- | --- | --- | --- | --- |
| General | Enabled | Enabled | `true` | Off hides everything the mod draws, as if it were not installed. Use it to check whether a problem comes from the mod or the game. |
| Map / Runways | Show runways | ShowRunways | `true` | Draw runways and their numbers. |
| Map / Runways | Runway colour | RunwayColor | `00D900D9` | Strip fill. |
| Map / Runways | Number colour | RunwayLabelColor | `99FF99FF` | Runway number colour. |
| Map / Runways | Number both ends | NumberBothEnds | `true` | Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from. |
| Map / Runways | Number size, *advanced* | RunwayLabelSize | `8` | Runway number size. |
| Map / Runways | Minimum width, *advanced* | RunwayMinWidth | `6` | Narrowest a strip gets when zoomed out. |
| Map / Approach Line | Show within (km) | TriggerRangeKm | `5` | How close to a runway the approach line and the HUD callout appear. |
| Map / Approach Line | Line length (km) | LineLengthKm | `5` | Approach line length. |
| Map / Approach Line | Line colour | LineColor | `00D90066` | Approach line colour. |
| Map / Approach Line | Line width, *advanced* | LineWidth | `1` | Approach line width. |
| Map / Airbase Names | Show airbase names | ShowNames | `false` | Name your faction's airbases on the full map. |
| Map / Airbase Names | Name colour | Color | `99FF99BF` | Name colour. |
| Map / Airbase Names | Name size, *advanced* | Size | `8` | Name size. |
| Map / Airbase Boundary | Show landing zone | ShowBoundary | `false` | Shade each friendly airbase's landing zone. |
| Map / Airbase Boundary | Fill opacity, *advanced* | FillOpacity | `0.01` | Opacity of the shaded zone, from 0 to 1. |
| Map / Airbase Boundary | Edge opacity, *advanced* | EdgeOpacity | `0.1` | Opacity of the edge line, from 0 to 1. `0` hides it. |
| Map / Outline | Outline colour, *advanced* | OutlineColor | `051405D9` | Rim around strips, approach dashes, and map text. |
| Map / Outline | Outline width, *advanced* | OutlineWidth | `0.75` | Rim width around strips and dashes. `0` turns it off. Text always gets a 1-unit rim. |
| HUD / Runway Callout | Show runway callout | ShowRunwayCallout | `true` | Show the `RWY 27` label. |
| HUD / Runway Callout | Only with gear down | OnlyWithGearDown | `false` | Show the label only while the landing gear is down. |
| HUD / Runway Callout | Include airbase name | IncludeAirbaseName | `false` | Prefix the label with the abbreviated airbase name. |
| HUD / Runway Callout | Callout colour | CalloutColor | `33FF33FF` | Label colour. |
| HUD / Missile Arrows | Show missile arrows | ShowArrows | `true` | Point an arrow at each incoming missile outside the view. |
| HUD / Missile Arrows | Arrow colour | Color | `FF4033FF` | Arrow colour. |
| Map Tools / General | Show map tools | ShowTools | `true` | Show the Tools button on the full map and everything drawn with it. Off hides both; drawings come back when it's on again, until you leave the mission. |
| Map Tools / General | Show on minimap | ShowOnMinimap | `true` | Show drawings on the minimap as well as the full map. Off keeps the minimap clear; the full map and the 3D labels still show them. |
| Map Tools / General | Circles in 3D view | ShowCirclesIn3D | `true` | Draw each circle in the 3D view as well, as a thin ring level with its centre. |
| Map Tools / General | Distance units | DistanceUnits | `Game` | **Game setting** follows the game's unit setting. Nautical miles, kilometres, or statute miles always use that unit. In the file: `Game`, `NauticalMiles`, `Kilometres`, or `StatuteMiles`. |
| Map Tools / Drawing | Drawing colour, *advanced* | Color | `33FF33FF` | Colour of new lines, arrows, and text. The menu's swatches set it; any colour works here. |
| Map Tools / Drawing | Line width, *advanced* | LineWidth | `1.5` | Width of drawn lines. Arrowheads grow with it. |
| Map Tools / Drawing | Text size, *advanced* | TextSize | `10` | Size of text the tools draw on the map. |
| Map Tools / Waypoints | Reached within (km), *advanced* | WaypointReachKm | `2.5` | How close to a waypoint counts as reaching it. |
| Map Tools / Waypoints | Passed within (km), *advanced* | WaypointPassedKm | `10` | How far behind you a waypoint still counts as reached. `0` turns it off. |
| Map Tools / Keys and Limits | Undo key | UndoKey | `Z` | Undo while the full map is open. |
| Map Tools / Keys and Limits | Redo key | RedoKey | `Y` | Redo while the full map is open. |
| Map Tools / Keys and Limits | Most drawings, *advanced* | MaxShapes | `200` | Most drawings kept at once. |
| Map Tools / Keys and Limits | Most pen points, *advanced* | MaxPenPoints | `5000` | Most freehand points kept across all pen strokes, so the map stays fast. At most `7500`. |
| Diagnostics | Perf test | PerfTest | button | **Run perf test** in F1: an automatic with and without comparison. See [The perf test](#the-perf-test). |
| Diagnostics | Log performance, *advanced* | LogPerformance | `false` | Log the frame rate and the mod's own time every 5 seconds. See [Measuring the mod's cost](#measuring-the-mods-cost). |

Widths and sizes use the same units as the game's own map icons, so they look the same at every zoom level.

## Measuring the mod's cost

Turn on **Diagnostics → Log performance**, an advanced setting, and every 5 seconds the mod writes one line to `BepInEx/LogOutput.log`:

```text
[Info   :Baanish UI Improvements] Perf 5.0 s, mod on: 612 frames, 122.4 fps avg (8.17 ms), 1% low 88.1 fps, worst frame 21.3 ms. Mod per frame 0.084 ms avg, 0.412 ms worst. Map refresh 50x, 0.310 ms avg, 1.204 ms worst.
```

- **mod on** or **mod off** is whether Enabled was on for those 5 seconds, or **on and off** if you flipped it partway.
- **fps avg**, **1% low**, and **worst frame** are the whole game's frame rate. The 1% low is the rate that 99% of frames beat, so it shows stutter the average hides.
- **Mod per frame** is the time the mod's own code takes each frame, and **Map refresh** the time it takes on each of the game's map updates, about 10 a second. Neither counts what the graphics card spends drawing the mod's lines and text. That cost shows only in the frame rate, which is why the comparison below flips the mod off.

To compare with and without the mod in one session:

1. Turn on Log performance in F1, and close F1, since its window costs frames too.
2. Fly something repeatable for at least 20 seconds, such as straight and level over the same area, or sit still on the runway. Keep the camera still.
3. Open F1, turn **Enabled** off, close F1, and hold the same view for another 20 seconds.
4. Turn Enabled back on and Log performance off.

Compare the **mod on** lines with the **mod off** lines, skipping any line that reads **on and off**. The difference in fps avg and 1% low is the mod's full cost. With Enabled off, the mod's own times read near zero.

### The perf test

The perf test does the comparison for you, including a heavy load of map drawings. It takes about 75 seconds.

1. Load the preset Escalation mission and spawn in an aircraft.
2. Fly straight and level, or sit still on the runway, in the cockpit view. The game shows the minimap only in the cockpit view.
3. Open F1 and press **Diagnostics → Run perf test**. Close F1 within 5 seconds, since its window costs frames too.
4. Don't touch the camera or the map until the result appears. The test opens and closes the full map itself.

It measures six phases of 10 seconds, each after 2 seconds for the switch to settle: the minimap with the mod off, with it on, and with it on plus heavy drawings, then the same three on the full map. The heavy drawings fill the minimap: ten long pen lines using 90% of **Most pen points**, a full 99-waypoint route, 20 bearing arrows, 20 circles, and 20 notes. Arrows, half the circles, and every tenth waypoint sit on units the map shows nearby, when there are any, so they move with them like drawings on real targets.

When it's done, a short summary appears in the game's message feed: how much the average and 1% low fps dropped in each phase, against the same map with the mod off. `BepInEx/LogOutput.log` gets the full table, with each phase's fps, 1% low, and the mod's own time per frame and per map refresh:

```text
[Info   :Baanish UI Improvements] Perf test: 10 s per phase after 2 s to settle. Heavy drawings: 71 shapes, anchored to 3 live units. Changes are against the same view with the mod off.
Phase                                 Avg fps   1% low Mod ms avg/max   Refresh ms avg/max   Avg fps change    1% low change
Minimap, mod off                        140.7    118.2    0.001/0.004     0.001/0.003 100x         baseline         baseline
Minimap, mod on                         138.8    116.9    0.080/0.312     0.300/0.910 100x     -1.9 (-1.4%)     -1.3 (-1.1%)
Minimap, mod on, heavy drawings         129.6    104.0    0.210/0.655     1.200/2.410 100x    -11.1 (-7.9%)    -14.2 (-12.0%)
...
Rendering per frame. Canvas ms is every canvas's rebuild and batching, the game's too. n/a is a stat this build doesn't record.
Phase                                 Canvas ms  BuildBatch ms  WillRender ms  Batches  SetPass Draw calls   Vertices  Mod graphics/TMP
Minimap, mod off                          0.310            n/a            n/a      212       64        240     410233               0/0
...
```

The second table is for finding where a cost comes from. **Canvas ms** is the time Unity spends rebuilding and batching all UI each frame, the game's included, so compare it with the mod-off phase like the fps. **Batches**, **SetPass**, **Draw calls**, and **Vertices** are Unity's render counters for the whole frame, and the two marker columns read n/a unless the game is a development build. **Mod graphics/TMP** counts the mod's graphics at the end of the phase, and how many of them are TextMeshPro text.

Press the button again to cancel. The test also stops on its own if you leave the mission, lose the aircraft, or open or close the map. Your own drawings then come back with their undo history, and the map goes back to how you had it. Leaving the mission is the exception: your drawings clear, as they always do when you leave. Once the first heavy-drawings phase has started, a route you were flying starts again from waypoint 1; use **Skip** to move it on. Cancel before that, and it keeps the waypoint you were on. While it runs, the test shows your drawings on the minimap and switches the mod off and on, all without changing **Show map tools**, **Show on minimap**, or **Enabled**, so even a crash can't leave them changed.

## Airbase abbreviations

With **Include airbase name** on, the airbases on the stock maps use these abbreviations:

| Airbase | Callout prefix |
| --- | --- |
| North Boscali Airbase | NBSCLI |
| South Boscali General Aviation | SBGA |
| Harmony Sands Airstrip | HMNY SNDS |
| Vigil Cay Naval Airbase | VGL CAY |
| Broken Atoll Airbase | BRKN ATL |
| Hogshead Airbase | HGSHD |
| Agrapol Airbase | AGRPL |
| Feldspar International Airport | FLDSPR INT |
| Maris Airport | MARIS |
| Cliffline Airbase | CLFFLNE |
| Sandrift Airbase | SNDRFT |
| Ashwood Airbase | ASHWD AB |
| Ashwood Auxiliary Airstrip | ASHWD AUX |

Any other airbase, including those in custom missions, is shortened the same way:

- The words Airbase, Airport, Airstrip, Airfield, and Naval are dropped.
- International, Auxiliary, Highway, Heliport, and the compass directions get standard short forms: INT, AUX, HWY, HELI, N, S, E, W.
- Every other word keeps its first letter and its consonants.

So Dustbowl Highway Strip becomes `DSTBWL HWY STRP`.

## Known limits

- Only runways at your own faction's airbases are shown.
- The HUD label hides when the runway threshold is behind the camera. It doesn't pin to the screen edge.
- If you've bound the game's Select control to the left mouse button, a click with a map tool also selects the nearest unit. The default binding is Enter. The mod doesn't patch the game, so it can't hold that click back.
