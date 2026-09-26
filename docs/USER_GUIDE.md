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
[Info   :Baanish UI Improvements] Baanish UI Improvements 0.1.0 loaded.
```

To uninstall, delete the `BaanishUiImprovements` plugin folder and the `.cfg` file.

## What you see

Everything below applies to friendly airbases only, meaning your faction's. When an airbase changes hands, its runways appear or disappear within a tenth of a second. Helicopters get only the airbase boundary. The Tarantula tiltrotor lands on runways, so it gets everything a plane does. Carriers are left out entirely, since the ship icon already marks the deck.

### On the map

Runways show on both the minimap and the full map. The game uses one map object for both, so everything pans, zooms, and rotates with it. Each runway is a filled strip with a thin dark rim. When you zoom out, the strip keeps a minimum width so it never shrinks to nothing.

Each end carries the runway number you'd land on from that end. The mod works each number out from the runway heading, the same way the game numbers an unnamed runway in its landing clearance. Where the paint on the tarmac differs, the paint wins, so the clearance can name a different number from the map. The same goes for a runway a mission has named. So far that's one runway, Feldspar's crossing runway on Ignus, which the mod labels 16/34 where the heading gives 15/33. A mission can give a runway its own name, but the mod ignores it, because mission names don't always match the paint. Takeoff-only runways aren't drawn.

### The approach line

Within 5 km of a runway, a dashed line extends 5 km off the nearest end of the nearest runway. Each dash starts on a 500 m mark, so the tenth dash starts 4.5 km out. The last dash ends 200 m short of the line's full length. The line is fainter than the runway strip so the two don't read as one.

It's there to judge the turn onto final, so it shows whatever your heading. Over the runway it follows whichever end is closer, and it hides once you're on the ground.

### The HUD callout

The same runway end gets a label pinned just below its threshold in the 3D view, such as `RWY 27`. It uses the same font as the game's own "Taxi to Runway" label and hides with the HUD. It also hides when the threshold is behind you.

With **IncludeAirbaseName** on, the label adds the airbase: `NBSCLI RWY 27`.

### The airbase boundary

Turn on **Airbase Boundary → ShowBoundary** to shade each friendly airbase's landing zone with a faint circle and a thin edge line, in the game's friendly map colour.

The circle is the game's own rule. An aircraft that counts as landed inside it (radar altitude under 5 m, speed under 2.5 m/s) ends the sortie as returned rather than crashed. The same radius is the airbase's capture range, so the circle is also the capture zone.

## How the runway end is chosen

Within range, the mod picks the runway nearest to you, measured to the runway strip itself, and then whichever of its two ends is closer. Heading plays no part. The map line and the HUD label always show the same end.

Between two runways or ends at about the same distance, the choice can switch as you move. An earlier version tried to guess the landing end from your heading and traffic-pattern leg, and it got the end wrong while manoeuvring near the field.

## Settings

Change settings in game with F1 if Configuration Manager is installed. Changes apply immediately. Without it, edit the `.cfg` file while the game is closed. Colours are hex `RRGGBBAA`.

| Section | Setting | Default | What it does |
| --- | --- | --- | --- |
| Map | ShowRunways | `true` | Draw runways and their numbers. |
| Map | RunwayColor | `00D900D9` | Strip fill. |
| Map | RunwayLabelColor | `99FF99FF` | Runway number colour. |
| Map | NumberBothEnds | `true` | Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from. |
| Map | RunwayLabelSize | `13` | Runway number size. |
| Map | OutlineColor | `051405D9` | Rim around strips and approach dashes. |
| Map | OutlineWidth | `0.75` | Rim width. `0` turns it off. |
| Map | RunwayMinWidth | `6` | Narrowest a strip gets when zoomed out. |
| Airbase Boundary | ShowBoundary | `false` | Shade each friendly airbase's landing zone. |
| Airbase Boundary | FillOpacity | `0.01` | Opacity of the shaded zone. |
| Airbase Boundary | EdgeOpacity | `0.1` | Opacity of the edge line. `0` hides it. |
| Approach | TriggerRangeKm | `5` | How close to a runway the line and callout appear. |
| Approach | LineLengthKm | `5` | Approach line length. |
| Approach | LineColor | `00D90066` | Approach line colour. |
| Approach | LineWidth | `1` | Approach line width. |
| HUD | ShowRunwayCallout | `true` | Show the `RWY 27` label. |
| HUD | IncludeAirbaseName | `false` | Prefix the label with the abbreviated airbase name. |
| HUD | CalloutColor | `33FF33FF` | Label colour. |

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
