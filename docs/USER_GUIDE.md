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
[Info   :Baanish UI Improvements] Baanish UI Improvements 0.4.0 loaded.
```

To uninstall, delete the `BaanishUiImprovements` plugin folder and the `.cfg` file.

## What you see

Everything below except airbase names applies to friendly airbases only, meaning your faction's. When an airbase changes hands, its runways appear or disappear within a tenth of a second. Helicopters get the airbase boundary and names, but no runways, approach line, or callout. The Tarantula tiltrotor lands on runways, so it gets everything a plane does. Carriers are left out entirely, since the ship icon already marks the deck.

### On the map

Runways show on both the minimap and the full map. The game uses one map object for both, so everything pans, zooms, and rotates with it. Each runway is a filled strip with a thin dark rim. When you zoom out, the strip keeps a minimum width so it never shrinks to nothing.

Each end carries the runway number you'd land on from that end. The mod works each number out from the runway heading, the same way the game numbers an unnamed runway in its landing clearance. Where the paint on the tarmac differs, the paint wins, so the clearance can name a different number from the map. The same goes for a runway a mission has named. So far that's one runway, Feldspar's crossing runway on Ignus, which the mod labels 16/34 where the heading gives 15/33. A mission can give a runway its own name, but the mod ignores it, because mission names don't always match the paint. Takeoff-only runways aren't drawn.

### The approach line

Within 5 km of a runway, a dashed line extends 5 km off the nearest end of the nearest runway. Each dash starts on a 500 m mark, so the tenth dash starts 4.5 km out. The last dash ends 200 m short of the line's full length. The line is fainter than the runway strip so the two don't read as one.

It's there to judge the turn onto final, so it shows whatever your heading. Over the runway it follows whichever end is closer, and it hides once you're on the ground.

### The HUD callout

The same runway end gets a label pinned just below its threshold in the 3D view, such as `RWY 27`. It uses the same font as the game's own "Taxi to Runway" label and hides with the HUD. It also hides when the threshold is behind you.

With **IncludeAirbaseName** on, the label adds the airbase: `NBSCLI RWY 27`.

With **OnlyWithGearDown** on, the label shows only while your gear is down. It appears as soon as you lower the gear lever.

### The airbase boundary

Turn on **Airbase Boundary → ShowBoundary** to shade each friendly airbase's landing zone with a faint circle and a thin edge line, in the game's friendly map colour.

The circle is the game's own rule. An aircraft that counts as landed inside it (radar altitude under 5 m, speed under 2.5 m/s) ends the sortie as returned rather than crashed. The same radius is the airbase's capture range, so the circle is also the capture zone.

### Airbase names

Turn on **Airbase Names → ShowNames** to label every airbase on the full map, friendly, enemy, and neutral, just under where the game draws a friendly airbase's icon. The game gives enemy and neutral airbases no icon at all, so this is how to find "Agrapol" when someone calls it out. The labels are a faint version of the runway numbers' green by default and don't show on the minimap. Carriers and airbases the mission switched off get no label. Airbases sharing a name get one label, placed under your own faction's if it holds one: Ignus free flight has three airbases called "Feldspar International Airport".

### Missile arrows

When a missile is incoming and outside your view, a red arrow on the screen edge points the way to turn toward it, so you can find it and shoot it down. Each missile gets its own arrow. A missile behind you gets an arrow on the side it's on, so a missile behind and to your left gets an arrow on the left edge. Once the missile is on screen, its arrow goes away.

Only missiles the game's missile warning already knows about get an arrow, so the arrows show nothing the game hasn't told you. The arrow is a copy of the game's own off-screen target arrow in a different colour.

### Map tools

On the full map, a **Tools** button in the top-left corner opens the tools menu: Waypoint, Pen, Text, Bearing/range, Circle, and Eraser. The menu opens on Waypoint. Click a tool to switch to it.

While the menu is open, a left click on the map goes to the tool instead of selecting a unit, and unit tooltips wait until it closes. Dragging still pans the map, except with a tool that draws by dragging. Right clicks still belong to the game, so move orders for selected units and NOAutopilot's waypoints keep working. Click **Tools** again, or close the map, to give left clicks back to the game.

With the Pen, press and drag on the map to draw a line in the picked colour. The map doesn't pan while you draw. The line is thinned as you draw and smoothed slightly when you let go, so it keeps its shape with few points. A click without dragging leaves a dot. All pen lines together hold at most **MaxPenPoints** points. A line that reaches the limit stops there and the menu says so; erase or undo a line to draw more.

With Text, click the map where the text should go, type it, and press Enter to place it. Escape drops it, and clicking somewhere else places what you've typed and starts new text there. While you type, keys go to the text instead of the game's controls, the same way the game's chat box handles typing. The one exception is also the chat's: in the external chase camera, the number pad keys still change the camera angle. Close the chat box before you place text, since both would read the same keys. Text is one line of up to 64 characters. Each piece of text also shows in the 3D view at the ground under it, and hides when that point is behind you. The 3D view shows at most 32 labels from the map tools at once.

With the Eraser, click on or near a drawing to delete it. The drawing under the cursor turns red first, so you can see what goes. Clear removes everything as one step, so Undo brings it all back. The Z and Y keys also undo and redo while the full map is open, but not while you type map text or chat. The colour swatches set the colour of new lines, arrows, and text.

Drawings show on the minimap too. Only you see them, and they're gone when you leave the mission. Distances follow the game's unit setting, kilometres for metric and nautical miles for imperial, unless **DistanceUnits** picks one. When you reach the shape limit, the menu says so, and the tools can't add anything until you erase or undo something.

## How the runway end is chosen

Within range, the mod picks the runway nearest to you, measured to the runway strip itself, and then whichever of its two ends is closer. Heading plays no part. The map line and the HUD label always show the same end.

Between two runways or ends at about the same distance, the choice can switch as you move. An earlier version tried to guess the landing end from your heading and traffic-pattern leg, and it got the end wrong while manoeuvring near the field.

## Settings

Change settings in game with F1 if Configuration Manager is installed. Changes apply immediately. Without it, edit the `.cfg` file while the game is closed. Colours are hex `RRGGBBAA`.

| Section | Setting | Default | What it does |
| --- | --- | --- | --- |
| General | Enabled | `true` | Off hides everything the mod draws, as if it were not installed. Use it to check whether a problem comes from the mod or the game. |
| Map | ShowRunways | `true` | Draw runways and their numbers. |
| Map | RunwayColor | `00D900D9` | Strip fill. |
| Map | RunwayLabelColor | `99FF99FF` | Runway number colour. |
| Map | NumberBothEnds | `true` | Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from. |
| Map | RunwayLabelSize | `8` | Runway number size. |
| Map | OutlineColor | `051405D9` | Rim around strips, approach dashes, and map text. |
| Map | OutlineWidth | `0.75` | Rim width around strips and dashes. `0` turns it off. Text always gets a 1-unit rim. |
| Map | RunwayMinWidth | `6` | Narrowest a strip gets when zoomed out. |
| Airbase Boundary | ShowBoundary | `false` | Shade each friendly airbase's landing zone. |
| Airbase Boundary | FillOpacity | `0.01` | Opacity of the shaded zone. |
| Airbase Boundary | EdgeOpacity | `0.1` | Opacity of the edge line. `0` hides it. |
| Airbase Names | ShowNames | `false` | Name every airbase on the full map. |
| Airbase Names | Color | `99FF99BF` | Name colour. |
| Airbase Names | Size | `8` | Name size. |
| Approach | TriggerRangeKm | `5` | How close to a runway the line and callout appear. |
| Approach | LineLengthKm | `5` | Approach line length. |
| Approach | LineColor | `00D90066` | Approach line colour. |
| Approach | LineWidth | `1` | Approach line width. |
| HUD | ShowRunwayCallout | `true` | Show the `RWY 27` label. |
| HUD | OnlyWithGearDown | `false` | Show the label only while the landing gear is down. |
| HUD | IncludeAirbaseName | `false` | Prefix the label with the abbreviated airbase name. |
| HUD | CalloutColor | `33FF33FF` | Label colour. |
| Missile Arrows | ShowArrows | `true` | Point an arrow at each incoming missile outside the view. |
| Missile Arrows | Color | `FF4033FF` | Arrow colour. |
| Map Tools | ShowTools | `true` | Show the Tools button on the full map and everything drawn with it. Off hides both; drawings come back when it's on again, until you leave the mission. |
| Map Tools | Color | `FFDD33FF` | Colour of new lines, arrows, and text. The menu's swatches set it; any colour works here. |
| Map Tools | LineWidth | `2` | Width of drawn lines. |
| Map Tools | TextSize | `10` | Size of text the tools draw on the map. |
| Map Tools | DistanceUnits | `Game` | `Game` follows the game's unit setting. `NauticalMiles`, `Kilometres`, or `StatuteMiles` always use that unit. |
| Map Tools | MaxShapes | `200` | Most drawings kept at once. |
| Map Tools | MaxPenPoints | `5000` | Most freehand points kept across all pen strokes, so the map stays fast. At most `7500`. |
| Map Tools | UndoKey | `Z` | Undo while the full map is open. |
| Map Tools | RedoKey | `Y` | Redo while the full map is open. |

Widths and sizes use the same units as the game's own map icons, so they look the same at every zoom level.

## Airbase abbreviations

With **IncludeAirbaseName** on, the airbases on the stock maps use these abbreviations:

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
- If you've bound the game's Select control to the left mouse button, a click with a map tool also selects the nearest unit. The default binding is Enter.
