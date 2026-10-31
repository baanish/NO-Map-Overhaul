# User guide

## Install

The [install guide](INSTALL.md) covers installing BepInEx and the mod, checking that it loaded, upgrading from Baanish UI Improvements, updating, and uninstalling.

## What you see

The runways, approach line, callout, boundary, and names show only at friendly airbases, meaning your faction's. When an airbase changes hands, its runways appear or disappear within a tenth of a second. Helicopters get the airbase boundary and names, but no runways, approach line, or callout. The Tarantula tiltrotor lands on runways, so it gets everything a plane does. Carriers get nothing, since the ship icon already marks the deck.

### On the map

Runways show on both the minimap and the full map. The game uses one map object for both, so everything pans, zooms, and rotates with it. Each runway is a filled strip with a thin dark rim. When you zoom out, the strip keeps a minimum width so it never shrinks to nothing. Once the full map is zoomed far out, all runway numbers hide together until you zoom back in. The minimap always shows them.

Each end carries the runway number you'd land on from that end. The mod works each number out from the runway heading, the same way the game numbers an unnamed runway in its landing clearance. Two runways on Ignus are painted differently from the game's number, and the map shows the game's, the one ATC calls. Feldspar's crossing runway reads 15/33 where it's painted 16/34, and the southwest one of Harmony Sands' parallel pair reads 13/31 where it's painted 14/32. To show the paint instead, turn on **Map / Runways → Number as painted**. Parallel runways get L, C or R for the side the runway lies on as you land, so Feldspar's two long runways read 06L and 06R landing northeast, and 24R and 24L landing southwest. The paint has no letters. The mod adds them so you can tell the two runways apart. A mission can give a runway its own name, which the clearance uses. The mod ignores it, because mission names don't always match the paint. The mod doesn't draw takeoff-only runways.

### The approach line

Within 5 km of a runway, a dashed line extends 5 km off the nearest end of the nearest runway. Each dash starts on a 500 m mark, so the tenth dash starts 4.5 km out. The last dash ends 200 m short of the line's full length. The line is fainter than the runway strip so the two don't read as one.

It's there to judge the turn onto final, so it shows whatever your heading. Over the runway it follows whichever end is closer, and it hides once you're on the ground.

### The HUD callout

The same runway end gets a label pinned just below its threshold in the 3D view, such as `RWY 27`. It uses the same font as the game's own "Taxi to Runway" label and hides with the HUD. It also hides when the threshold is behind you.

With **Include airbase name** on, the label adds the airbase: `NBSCLI RWY 27`.

With **Only with gear down** on, the label shows only while your gear is down. It appears as soon as you lower the gear lever.

### The airbase boundary

**Map / Airbase Boundary → Show landing zone**, on by default, shades each friendly airbase's landing zone with a faint circle and a thin edge line, in the game's friendly map colour.

The circle is the game's own rule. An aircraft that counts as landed inside it (radar altitude under 5 m, speed under 2.5 m/s) ends the sortie as returned rather than crashed. The same radius is the airbase's capture range, so the circle is also the capture zone.

### Airbase names

**Map / Airbase Names → Show airbase names**, on by default, labels your faction's airbases on the full map, under where the game draws their icon. As you zoom in, the name moves down to the edge of the airbase's capture circle, the same circle as the landing zone, so it stays clear of the base's units. A limit keeps it on screen.

Every airbase the game gives your side an icon for gets a name. That covers the map's own airbases, ones a mission adds, ones your side captures, and ones the mission switched off, which the game still marks. The name is the one the game shows when you hover over the icon. Enemy and neutral airbases get no name, since the game doesn't mark them. Carriers get none either, since the ship's icon marks the deck. Airbases sharing a name get one label: Ignus free flight has three airbases called "Feldspar International Airport".

By default the labels are pale green with a black outline. They don't show on the minimap.

### Map tools

On the full map, the **Tools** button just outside the map's top-left corner, or the `H` key, opens a rail of planning tools beside the map. Keys `1` to `6` pick bearing and range measurements, range circles, text notes that can tag a contact, a pen, a waypoint route with markers in the 3D view, and an eraser. `Z` and `Y` undo and redo. Drawings start local and clear when you leave the mission. The [map tools guide](MAP_TOOLS.md) covers every tool, the mouse and keys, and planning ideas.

## How the runway end is chosen

Within range, the mod picks the runway nearest to you, measured to the runway strip itself, and then whichever of its two ends is closer. Heading plays no part. The map line and the HUD label always show the same end.

Between two runways or ends at about the same distance, the choice can switch as you move. Guessing the landing end from your heading and traffic-pattern leg picks the wrong end while you manoeuvre near the field, so the mod doesn't guess.

## Settings

With Configuration Manager installed, press F1 in game to change settings. Changes apply immediately. Click the mod's name to open its settings, and hover over a setting's name for a longer description. Settings marked *advanced* are tuning knobs. F1 shows them only with **Advanced settings** ticked at the top of its window, or when a search finds them.

Without Configuration Manager, edit `BepInEx/config/com.baanish.nuclearoption.mapoverhaul.cfg` while the game is closed. The game's first start with the mod creates it. Each section below is a `[Section]` heading in the file, and the key is the name before `=`. Colours are hex `RRGGBBAA`.

Up to 0.4.0 this mod was called Baanish UI Improvements. Its saved settings carry over into these sections the first time the game starts with NO Map Overhaul. The missile arrows are the exception: they moved to [NO Missile Indicators](https://github.com/baanish/NO-Missile-Indicators).

The table lists the sections in the order F1 shows them.

| Section | F1 name | Key | Default | What it does |
| --- | --- | --- | --- | --- |
| General | Enabled | Enabled | `true` | Off hides everything the mod draws, as if it were not installed. Use it to check whether a problem comes from the mod or the game. |
| Map / Runways | Show runways | ShowRunways | `true` | Draw runways and their numbers. |
| Map / Runways | Runway colour | RunwayColor | `000000FF` | Strip fill. |
| Map / Runways | Number colour | RunwayLabelColor | `FFFFFFFF` | Runway number colour. |
| Map / Runways | Number both ends | NumberBothEnds | `true` | Number both ends of every runway. Off hides the number at the end of a one-way runway that the game never lands you from. |
| Map / Runways | Numbers on runway | NumbersOnRunway | `true` | Once you zoom in far enough for a number to fit across its runway, draw it on the runway at that end, the way it's painted, upright like the rest of the map's text. |
| Map / Runways | On runway from | NumbersOnRunwayFrom | `0.75` | How large a number must be able to show across its runway, as a multiple of its size beside it, before it moves onto it, from `0.5` to `5`. Higher waits until you're zoomed in further. |
| Map / Runways | Turn with runway | NumbersOnRunwayTurn | `false` | Turn a number on the runway to read for a pilot landing there, like the paint, instead of keeping it upright. |
| Map / Runways | Number as painted | PaintedNumbers | `false` | Where the paint on a runway differs from the game's number, show the paint: 34/16 rather than 33/15 on Feldspar's crossing runway, 14/32 rather than 13/31 on Harmony Sands' southwest runway. Off shows the number ATC calls. Applies to the map and the HUD callout. |
| Map / Runways | Number size, *advanced* | RunwayLabelSize | `10` | Runway number size on the full map. |
| Map / Runways | Minimap number size, *advanced* | MinimapRunwayLabelSize | `8` | Runway number size on the minimap. |
| Map / Runways | Minimum width, *advanced* | RunwayMinWidth | `6` | Narrowest a strip gets when zoomed out. |
| Map / Runways | Numbers from zoom, *advanced* | NumbersMinGridSquare | `100` | On the full map, hide every runway number while one grid square is narrower than this on screen, so zoomed far out they don't pile onto the airbases. The minimap always shows them. `0` always shows them. |
| Map / Runway Centre Line | Show centre line | ShowCentreLine | `true` | A dashed line down the middle of each runway. |
| Map / Runway Centre Line | Line colour | Color | `FFFFFFFF` | Centre line colour. |
| Map / Runway Centre Line | Line width | Width | `1` | Centre line width, in map icon units. |
| Map / Runway Centre Line | Dash length (m) | DashLength | `30` | Length of each dash on the ground, so dashes grow as you zoom in like the paint. Zoomed out, once a dash would be shorter than 2 map icon units, the line isn't drawn. |
| Map / Runway Centre Line | Gap length (m) | GapLength | `60` | Gap between dashes on the ground. |
| Map / Approach Line | Show within (km) | TriggerRangeKm | `5` | How close to a runway the approach line and the HUD callout appear. |
| Map / Approach Line | Line length (km) | LineLengthKm | `5` | Approach line length. |
| Map / Approach Line | Line colour | LineColor | `00D90066` | Approach line colour. |
| Map / Approach Line | Line width, *advanced* | LineWidth | `1` | Approach line width. |
| Map / Airbase Names | Show airbase names | ShowNames | `true` | Name your faction's airbases on the full map. |
| Map / Airbase Names | Name colour | Color | `BFFFBFFF` | Name colour. |
| Map / Airbase Names | Name size, *advanced* | Size | `14` | Name size. |
| Map / Airbase Boundary | Show landing zone | ShowBoundary | `true` | Shade each friendly airbase's landing zone. |
| Map / Airbase Boundary | Fill opacity, *advanced* | FillOpacity | `0.01` | Opacity of the shaded zone, from 0 to 1. |
| Map / Airbase Boundary | Edge opacity, *advanced* | EdgeOpacity | `0.1` | Opacity of the edge line, from 0 to 1. `0` hides it. |
| Map / Outline | Outline colour, *advanced* | OutlineColor | `000000FF` | Rim around strips, approach dashes, and map text. |
| Map / Outline | Text outline width, *advanced* | TextOutlineWidth | `2` | Rim width around runway numbers and airbase names, in the outline colour, from `0` (none) to `3`. |
| Map / Outline | Outline width, *advanced* | OutlineWidth | `1` | Rim width around strips and dashes. `0` turns it off. |
| HUD / Runway Callout | Show runway callout | ShowRunwayCallout | `true` | Show the `RWY 27` label. |
| HUD / Runway Callout | Only with gear down | OnlyWithGearDown | `false` | Show the label only while the landing gear is down. |
| HUD / Runway Callout | Include airbase name | IncludeAirbaseName | `true` | Prefix the label with the abbreviated airbase name. |
| HUD / Runway Callout | Callout colour | CalloutColor | `33FF33FF` | Label colour. |
| Map Tools / General | Show map tools | ShowTools | `true` | Show the Tools button on the full map and everything drawn with it. Off hides both. Drawings come back when it's on again, until you leave the mission. |
| Map Tools / General | Show on minimap | ShowOnMinimap | `true` | Show drawings on the minimap as well as the full map. Off keeps the minimap clear. The full map and the 3D labels still show them. |
| Map Tools / General | Circles in 3D view | ShowCirclesIn3D | `true` | Draw each circle in the 3D view as well, as a thin ring level with its centre. |
| Map Tools / General | Distance units | DistanceUnits | `Game` | **Game setting** follows the game's unit setting. Nautical miles, kilometres, or statute miles always use that unit. Altitudes show in metres with kilometres and in feet with miles. In the file: `Game`, `NauticalMiles`, `Kilometres`, or `StatuteMiles`. |
| Map Tools / Drawing | Drawing colour, *advanced* | Color | `33FF33FF` | Colour of new lines, arrows, and text. The rail's swatches set it, and any colour works here. |
| Map Tools / Drawing | Other channels opacity | OtherChannelsOpacity | `0.5` | How opaque drawings on a channel other than the one you're drawing to show, as a share of their own. `1` shows them like the rest. |
| Map Tools / Drawing | Other channels brightness | OtherChannelsBrightness | `0.6` | How bright those drawings show, as a share of their own colour. `1` keeps their colour. |
| Map Tools / Drawing | Line width, *advanced* | LineWidth | `1.5` | Width of drawn lines. Arrowheads grow with it. |
| Map Tools / Drawing | Text size, *advanced* | TextSize | `10` | Size of text the tools draw on the map. |
| Map Tools / Waypoints | Reached within (km), *advanced* | WaypointReachKm | `2.5` | How close to a waypoint counts as reaching it. |
| Map Tools / Waypoints | Passed within (km), *advanced* | WaypointPassedKm | `10` | How far behind you a waypoint still counts as reached. `0` turns it off. |
| Map Tools / Keys and Limits | Tools key | ToolsKey | `H` | Open or close the tools rail while the full map is open, like clicking Tools. |
| Map Tools / Keys and Limits | Undo key | UndoKey | `Z` | Undo while the full map is open. |
| Map Tools / Keys and Limits | Redo key | RedoKey | `Y` | Redo while the full map is open. |
| Map Tools / Keys and Limits | Bearing/range key, *advanced* | BearingRangeKey | `Alpha1` | Pick Bearing/range while the full map is open, opening the rail if it's closed. |
| Map Tools / Keys and Limits | Circle key, *advanced* | CircleKey | `Alpha2` | Pick Circle, the same way. |
| Map Tools / Keys and Limits | Text key, *advanced* | TextKey | `Alpha3` | Pick Text, the same way. |
| Map Tools / Keys and Limits | Pen key, *advanced* | PenKey | `Alpha4` | Pick Pen, the same way. |
| Map Tools / Keys and Limits | Waypoint key, *advanced* | WaypointKey | `Alpha5` | Pick Waypoint, the same way. |
| Map Tools / Keys and Limits | Eraser key, *advanced* | EraserKey | `Alpha6` | Pick Eraser, the same way. |
| Map Tools / Keys and Limits | Channel 1 key, *advanced* | ChannelKey1 | `LeftShift + Alpha1` | Pick Broadcast slot 1 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 2 key, *advanced* | ChannelKey2 | `LeftShift + Alpha2` | Pick Broadcast slot 2 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 3 key, *advanced* | ChannelKey3 | `LeftShift + Alpha3` | Pick Broadcast slot 3 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 4 key, *advanced* | ChannelKey4 | `LeftShift + Alpha4` | Pick Broadcast slot 4 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 5 key, *advanced* | ChannelKey5 | `LeftShift + Alpha5` | Pick Broadcast slot 5 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 6 key, *advanced* | ChannelKey6 | `LeftShift + Alpha6` | Pick Broadcast slot 6 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 7 key, *advanced* | ChannelKey7 | `LeftShift + Alpha7` | Pick Broadcast slot 7 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 8 key, *advanced* | ChannelKey8 | `LeftShift + Alpha8` | Pick Broadcast slot 8 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 9 key, *advanced* | ChannelKey9 | `LeftShift + Alpha9` | Pick Broadcast slot 9 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Channel 10 key, *advanced* | ChannelKey10 | `LeftShift + Alpha0` | Pick Broadcast slot 10 for new drawings while the full map is open. Either Shift key works. Clear the binding to disable the shortcut. |
| Map Tools / Keys and Limits | Most drawings, *advanced* | MaxShapes | `100` | Most drawings kept at once. At most `100`, what one teammate can share. |
| Map Tools / Keys and Limits | Most pen points, *advanced* | MaxPenPoints | `2500` | Most freehand points kept across all pen strokes, so the map stays fast. At most `2500`. |
| Diagnostics | Perf test | PerfTest | button | **Run perf test** in F1 compares the frame rate with and without the mod automatically. See [The perf test](#the-perf-test). |
| Diagnostics | Perf test sharing, *advanced* | PerfTestSharing | `LocalOnly` | Where the perf test's drawings go: `LocalOnly`, `Send`, `Receive`, or `Bidirectional`. See [Testing with sharing](#testing-with-sharing). |
| Diagnostics | Perf test cycles, *advanced* | PerfTestCycles | `4` | How many times the perf test measures each condition: `4`, `8`, `16`, `32`, `64`, or `128`. More cycles measure sustained performance. |
| Diagnostics | Log performance, *advanced* | LogPerformance | `false` | Log the frame rate, the mod's own time, Unity's UI and render costs, and each teammate's sharing every 5 seconds. See [Measuring the mod's cost](#measuring-the-mods-cost). |
| Diagnostics | Log network | LogNetwork | `false` | Log drawing-sharing events and rejection reasons when troubleshooting. |
| Diagnostics | Draw 3D rings on the CPU, *advanced* | RingsOnCpu | `false` | Build the circles' rings in the 3D view on the CPU instead of drawing them with the mod's shader, as it does when `nomapoverhaul-shaders.bundle` is missing. For comparing the two, or if the rings look wrong on your graphics card. |

Both logs start off. The 0.6.0 betas switched them on to find what slowed some multiplayer sessions down, so 0.6.0 switches them back off once for anyone who ran one. Turn either on and it stays on.

Widths and sizes use the same units as the game's own map icons, so they look the same at every zoom level.

The **+** under the rail's swatches adds up to six colours of your own. Right click one to remove it (see [the rail](MAP_TOOLS.md#the-rail)). The file keeps them under the key `CustomColors` in `[Map Tools / Drawing]` as comma-separated `#RRGGBB`, such as `#FF8000,#8040FF`. F1 doesn't show this key. The mod ignores entries that aren't `#RRGGBB`.

Sharing's channels aren't in F1. On the full map, open the tool rail, click **Channels**, edit **Broadcast** and **Receive only**, turn sharing on, and press **Apply**. The [sharing guide](SHARING_GUIDE.md) covers setting it up with your flight, picking a channel with the channel keys, erasing a teammate's drawing, and troubleshooting. [Sharing](SHARING.md) is the technical reference.

The config file keeps the keys `Enabled`, `BroadcastChannels`, and `ReceiveChannels` in `[Map Tools / Sharing]`, hidden from F1. Sharing starts off, with both lists empty. Invalid channel names in the file turn sharing off until you correct them.

A perf test keeps sharing your drawings and showing your teammates' as before. Its own drawings stay local unless **Perf test sharing** sends them. A team or mission change during a test cancels it and discards the test drawings.

## Logging multiplayer sharing

**F1 → Diagnostics → Log network** writes timestamped `Network` lines to `BepInEx/LogOutput.log`. It's off by default. To troubleshoot, turn it on for both clients before you reproduce the issue.

The log records mission eligibility, channel-change resets, anonymous peer labels, handshakes, channel-match counts, Steam send results, connection failure codes, snapshot counts and bytes, exclusions, receive limits, quarantines, and erase results. A successful Steam send only means the message was queued locally. A receiver's `snapshot applied` line confirms that its drawing store accepted the update. Local-only drawing exclusions and channel mismatches have separate counts.

Each client picks its own peer labels, so labels don't match between players. Use the build identifier and protocol version to check that both clients run the same DLL. The log omits Steam IDs, IPs, session nonces, channel names, note text, and drawing coordinates. It keeps at most 128 event lines per five seconds, plus a count of the events it dropped. Turning logging off stops those lines and doesn't change sharing.

Reproduce the issue, then save both players' `BepInEx/LogOutput.log` files before restarting the game, which overwrites them. Include each player's Broadcast and Receive only setup in the bug report. Other mods and the game may write personal information to the same file. The omissions above apply only to this mod's `Network` lines.

## Measuring the mod's cost

With **Diagnostics → Log performance** on (an advanced setting), the mod writes two lines to `BepInEx/LogOutput.log` every 5 seconds, and a third while you share with teammates. The first looks like this:

```text
[Info   :NO Map Overhaul] Perf 5.0 s, mod on: 1679 frames, 335.7 fps avg (2.98 ms), 1% low 216.7 fps, worst frame 17.7 ms. Mod per frame 0.022 ms avg, 0.141 ms worst. Map refresh 49x, 0.054 ms avg, 0.089 ms worst. Mod allocated 0.1 KB per frame. GC 1x, heap 93 of 104 MB. Drawings 0 own, 0 received, 0 shown. Mod graphics 26 drawn, 8 TMP, 34 held. Sharing sent 0 (0.0 KB), received 0 (0.0 KB), 0 snapshots applied, Steam calls 0.0 ms, worst 0.0 ms. Mod ms per frame by part: Draw 0.004, Labels 0.001, Upright 0.001, 3D text 0.003, 3D rings 0.003, Input 0.002, Runways 0.002, Sharing 0.003, Meshes 0.000.
```

- **mod on** or **mod off** is whether Enabled was on for those 5 seconds, or **on and off** if you flipped it partway.
- **fps avg**, **1% low**, and **worst frame** are the whole game's frame rate. The 1% low is the rate that 99% of frames beat, so it shows stutter the average hides.
- **Mod per frame** is the time the mod's own code takes each frame. **Map refresh** is the time it takes on each of the game's map updates, about 10 a second. Neither counts what the graphics card spends drawing the mod's lines and text. That cost shows only in the frame rate, which is why the comparison below flips the mod off.
- The rest counts what the mod holds and does. That's memory allocated and garbage collections, your drawings and teammates', the mod's graphics, sharing traffic, the time spent in Steam's calls, and the mod's time per frame. The time per frame splits into the parts the perf test's third table names, each as average/worst frame (see [The perf test](#the-perf-test)). A number that keeps climbing while the frame rate falls points at the part to blame.

The **Perf detail** line after it covers the same 5 seconds:

- **Unity canvas update** is what Unity spends each frame laying out, rebuilding and batching every UI canvas, the game's HUD and map as well as the mod's, as an average and the worst frame. While the perf test runs it takes this timer, and the line says so.
- **batches**, **SetPass calls**, **draw calls** and **vertices** are Unity's render counters per frame, averaged. They count what the frame asked the graphics card to draw. **n/a** marks a counter this build of the game doesn't record.
- **Rebuilt** counts how often, in those 5 seconds, the mod laid out the map labels again, rebuilt its own meshes, and took a changed set of teammates' drawings.
- **3D rings** is the most circles drawn in the cockpit view in one frame, and whether the mod's shader (**GPU**) or the CPU drew them. **not drawn** means no frame got that far, such as with the map tools off.
- Then comes the game's state as the line is written. That's the players on your side counting you, the icons on the map (units and airbases), whether the minimap or the full map was showing, and the mission time.

While you share with teammates, a **Perf teammates** line follows, with one entry per teammate. Each is labelled **peer 1**, **peer 2** and so on, the same labels the network log uses. They're local to your game, and the log never names a player or a Steam ID.

- **ready** means the two games have paired, so drawings flow. **not ready** means they're still looking for each other. Teammates not ready and silent for the whole 5 seconds, usually players without the mod, only get a count at the end: **3 not ready with no traffic**.
- **drawings held, shown** is how many drawings the teammate's last update left with you, and how many of those your map draws. It shows fewer when there are more than 400 teammates' drawings to share out, or a drawing is on a unit your side has never seen.
- **snapshots applied** is how many of their updates your game took. **in** and **out** are the messages and kilobytes received from them and sent to them.
- The rest is Steam's own view of the connection. That's its state (**Connected** when all is well), **ping**, **quality** (the share of packets that arrived, **n/a** while Steam can't tell), the reliable bytes **pending** (queued, not yet sent) and **unacked** (sent, not yet confirmed), and the **queue** time a new message would wait. Pending bytes or queue time that climb mean Steam can't send as fast as sharing queues. **Steam n/a** means Steam has no session with that teammate. The loopback test build always reads it.

When the frame rate drops, compare the lines from before the drop with those during it.

- If only the first line's frame rate falls, with the mod's times and the canvas update flat, look at the game or the graphics card. Check the render counters and the context.
- If the canvas update grows with the **Rebuilt** counts, look at UI rebuilds.
- If a mod time grows, the first line names its part.
- If a teammate's queue or pending bytes climb, or their messages arrive in bursts, look at the connection with that teammate.

Here is an example from the host of a four-player test on one PC, just after every player ran the perf test. Its teammates connect over the loopback test build, so Steam has nothing to report.

```text
[Info   :NO Map Overhaul] Perf 5.0 s, mod on: 297 frames, 59.4 fps avg (16.84 ms), 1% low 59.3 fps, worst frame 65.4 ms. Mod per frame 0.049 ms avg, 0.409 ms worst. Map refresh 46x, 0.079 ms avg, 0.120 ms worst. Mod allocated 0.3 KB per frame. GC 0x, heap 136 of 168 MB. Drawings 0 own, 0 received, 0 shown. Mod graphics 26 drawn, 8 TMP, 34 held. Sharing sent 6 (0.4 KB), received 6 (0.4 KB), 0 snapshots applied, Steam calls 0.5 ms, worst 0.1 ms. Mod ms per frame by part, average/worst frame: Draw 0.011/0.06, Labels 0.001/0.01, Upright 0.001/0.00, 3D text 0.005/0.02, 3D rings 0.005/0.03, Input 0.003/0.00, Runways 0.003/0.01, Sharing 0.013/0.36, Meshes 0.000/0.00.
[Info   :NO Map Overhaul] Perf detail: Unity canvas update 0.67 ms avg, 1.15 ms worst frame; per frame 300 batches, 205 SetPass calls, 300 draw calls, 387720 vertices. Rebuilt label layouts 0x, meshes 0x, received drawings 0x. 3D rings 0 at most, on the GPU. Multiplayer, 4 on the team, 47 map icons, minimap, mission time 3:42.
[Info   :NO Map Overhaul] Perf teammates: peer 7 ready: drawings 0 held, 0 shown; 0 snapshots applied; in 2 (0.1 KB), out 2 (0.1 KB); Steam n/a. peer 8 ready: drawings 0 held, 0 shown; 0 snapshots applied; in 2 (0.1 KB), out 2 (0.1 KB); Steam n/a. peer 9 ready: drawings 0 held, 0 shown; 0 snapshots applied; in 2 (0.1 KB), out 2 (0.1 KB); Steam n/a.
```

Over Steam, each teammate's entry ends with the connection instead, such as `Steam Connected, ping 48 ms, quality 100%, reliable 0 B pending, 0 B unacked, queue 0.0 ms.`

To compare with and without the mod in one session:

1. Turn on Log performance in F1, and close F1, since its window costs frames too.
2. Fly something repeatable for at least 20 seconds, such as straight and level over the same area, or sit still on the runway. Keep the camera still.
3. Open F1, turn **Enabled** off, close F1, and hold the same view for another 20 seconds.
4. Turn Enabled back on and Log performance off.

Compare the **mod on** lines with the **mod off** lines, skipping any line that reads **on and off**. The difference in fps avg and 1% low is the mod's full cost. With Enabled off, the mod's own times read near zero.

### The perf test

The perf test runs the comparison for you, and adds a typical and a heavy load of map drawings. It takes about 85 seconds.

1. Load the preset Escalation mission and spawn in an aircraft.
2. Fly straight and level, or sit still on the runway, in the cockpit view. The game shows the minimap only in the cockpit view.
3. Open F1 and press **Diagnostics → Run perf test**. Close F1 within 5 seconds, since its window costs frames too.
4. Don't touch the camera or the map until the result appears. The test opens and closes the full map itself.

It measures eight conditions: the minimap with the mod off, with it on, with it on plus typical drawings, and with it on plus heavy drawings, then the same four on the full map. Within each map the four take turns in slices of 2 seconds, four times over, reversing the order every other round. Each slice starts after half a second for the switch to settle. So each condition gets 8 seconds, spread across the same stretch of flight, and a frame rate that drifts as you fly affects all four alike.

The typical drawings are about what you'd keep up in a mission: a 7-waypoint route, 3 bearing arrows, 2 circles, 3 notes, and 2 short pen lines. Each arrow ends on one of the 3 nearest units the map shows, and the first one starts on another. One circle sits on the nearest unit, so its 3D ring moves with it.

The heavy drawings fill the minimap: ten long pen lines using 90% of **Most pen points**, a full 50-waypoint route, 20 bearing arrows, 20 circles, and 20 notes. Arrows, half the circles, and every tenth waypoint sit on the nearest units the map shows, up to 10 of them, so they move with those units like drawings on real targets.

The header says how many units each set rode on. Fewer anchors means fewer drawings redrawing ten times a second, so compare runs with the same count.

When it's done, the game's message feed shows a short summary of how much the average and 1% low fps dropped in each condition, against the same map with the mod off. `BepInEx/LogOutput.log` gets the full tables, with each condition's fps, 1% low, and the mod's own time per frame and per map refresh:

```text
[Info   :NO Map Overhaul] Perf test: each condition measured in 4 slices of 2.0 s, taking turns with the others in its view, 0.5 s to settle after each switch. Drawings: typical 11 shapes on 3 live units, heavy 71 shapes on 10 live units (at most 10). Changes are against the same view with the mod off.
Phase                                 Avg fps   1% low Mod ms avg/max   Refresh ms avg/max   Avg fps change    1% low change
Minimap, mod off                        140.7    118.2    0.001/0.004      0.001/0.003 80x         baseline         baseline
Minimap, mod on                         138.8    116.9    0.080/0.312      0.300/0.910 80x     -1.9 (-1.4%)     -1.3 (-1.1%)
Minimap, mod on, typical drawings       136.4    114.3    0.120/0.418      0.540/1.320 80x     -4.3 (-3.1%)     -3.9 (-3.3%)
Minimap, mod on, heavy drawings         129.6    104.0    0.210/0.655      1.200/2.410 80x    -11.1 (-7.9%)    -14.2 (-12.0%)
...
Rendering per frame. Canvas ms is every canvas's rebuild and batching, the game's too. n/a is a stat this build doesn't record.
Phase                                 Canvas ms  BuildBatch ms  WillRender ms  Batches  SetPass Draw calls   Vertices  Mod graphics/TMP
Minimap, mod off                          0.310            n/a            n/a      212       64        240     410233               0/0
...
Mod ms per frame by part. Meshes is the mod's graphics rebuilding their meshes, which counts in Canvas ms, not Mod ms. Other is the rest of Mod ms.
Phase                                     Draw    Labels   Upright   3D text  3D rings     Input   Runways   Sharing    Meshes     Other
Minimap, mod on                          0.004     0.002     0.001     0.003     0.000     0.004     0.010     0.003     0.004     0.014
...
Worst frame ms by part: each part's costliest single frame without a garbage collection, the spikes behind a 1% low.
Phase                                     Draw    Labels   Upright   3D text  3D rings     Input   Runways   Sharing    Meshes
Minimap, mod on                          0.120     0.210     0.004     0.140     0.020     0.020     0.020     0.300     0.050
...
```

The second table is for finding where a cost comes from. **Canvas ms** is the time Unity spends rebuilding and batching all UI each frame, the game's included, so compare it with the mod-off phase like the fps. **Batches**, **SetPass**, **Draw calls**, and **Vertices** are Unity's render counters for the whole frame, and the two marker columns read n/a unless the game is a development build. **Mod graphics/TMP** counts the mod's graphics at the end of the condition's last slice, and how many of them are TextMeshPro text.

The third table splits the mod's own time per frame:

- **Draw** is drawing shapes into their meshes.
- **Labels** is placing map labels.
- **Upright** is turning markers and notes upright on the minimap.
- **3D text** is the tools' per-frame work and 3D labels.
- **3D rings** is the circles in the 3D view.
- **Input** is the tools rail and strip and their input.
- **Runways** is the HUD callout and runway numbers.
- **Sharing** is sending and receiving teammates' drawings.
- **Meshes** is rebuilding the mod's own meshes. Unity does that in its canvas update, so it counts in Canvas ms, not Mod ms.
- **Other** is whatever Mod ms the named parts don't cover.

The fourth table gives each part's single costliest frame. The 1% low comes from slow frames, which an average hides, so a part that is cheap on average but spikes shows up here. A frame with a garbage collection is left out, since the collection pauses whichever part happens to be running.

To cancel, press the button again. The test also stops on its own if you leave the mission, lose the aircraft, or open or close the map. Your own drawings then come back with their undo history, and the map goes back to how you had it. A route you were flying keeps the waypoint you were on. Leaving the mission is the exception: your drawings clear, as they always do when you leave.

While it runs, the test shows your drawings on the minimap and switches the mod off and on. It does this without changing **Show map tools**, **Show on minimap**, or **Enabled**, so even a crash can't leave them changed.

#### Testing with sharing

**Perf test sharing**, an advanced setting, measures what sharing costs between two PCs. `Send` sends the test's drawings, `Receive` draws none and shows what the other PC sends, and `Bidirectional` does both.

To run it, join a Steam multiplayer mission on the same team on both PCs, then start the test on each within the same half minute. Slices start on the next half-minute mark of the clock, so the two PCs measure the same condition at the same time. A test that sends replaces one note every half second while its drawings show, about as often as a busy teammate changes one.

The test uses a `perf-test` channel of its own and needs no channel set up. With Sharing on, your own drawings and your teammates' keep travelling as before. With Sharing off, only the test's drawings go out.

Every test logs a **Sharing and memory** table. For each condition it gives sharing traffic per second, the time the game spends inside Steam's calls, managed memory allocated per frame (the game's too), garbage collections, and how many teammates' drawings showed.

**Perf test cycles** repeats each condition more times. `4` takes about 85 seconds, and each step up doubles it. A test of more than 4 cycles also logs each cycle's average fps per condition, so a frame rate that falls as the test runs shows up as a trend down the rows.

For unattended runs, start the game with `-nomo-perftest <mode>`, such as `-nomo-perftest bidirectional`. The test starts by itself 15 seconds after you're first in an aircraft. `-nomo-perftest bidirectional@<unix seconds>` names when its first slice measures, to line two PCs up exactly.

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

The mod shortens any other airbase, including those in custom missions, by the same rules:

- It drops the words Airbase, Airport, Airstrip, Airfield, and Naval.
- International, Auxiliary, Highway, Heliport, and the compass directions get standard short forms: INT, AUX, HWY, HELI, N, S, E, W.
- Every other word keeps its first letter and its consonants.

So Dustbowl Highway Strip becomes `DSTBWL HWY STRP`.

## Known limits

- Only runways at your own faction's airbases are shown.
- The HUD label hides when the runway threshold is behind the camera. It doesn't pin to the screen edge.
- If you've bound the game's Select control to the left mouse button, a click with a map tool also selects the nearest unit. The default binding is Enter. The drawing tools don't intercept the game's selection binding.
