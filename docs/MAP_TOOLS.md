# Map tools

The map tools let you plan on the full map: a waypoint route with markers in the 3D view, freehand lines, text notes, bearing and range measurements, and range circles. Only you see what you draw, and it all clears when you leave the mission.

Every setting mentioned here is listed in the [user guide's settings table](USER_GUIDE.md#settings), under **Map Tools**.

## Quick start

1. Open the full map.
2. Click **Tools** in the map's top-left corner. A slim green rail opens down the side of the map, with Waypoint picked.
3. Click the map to drop waypoints. A numbered route appears, and a line runs from your aircraft to the next waypoint.
4. Close the map. The next two waypoints show in the 3D view with their distance and bearing, such as `WP2 4.2nm 045°`.
5. Open the map again and click **Tools** to close the rail. Your clicks go back to the game.

## Mouse and keys

These work while the tools rail is open on the full map.

| Do this | To |
| --- | --- |
| Left click or drag | Use the picked tool. |
| Right click while drawing | Cancel what you're halfway through. |
| Right click a drawing | Delete it. Undo brings it back. |
| `Z` | Undo. |
| `Y` | Redo. |
| Drag on empty map | Pan the map, except with Pen and Circle, which draw by dragging. |

The keys don't act while you type a note, chat, or type in one of the game's text boxes. Change them with **UndoKey** and **RedoKey**.

A selected friendly unit keeps its right click: with one selected, right click gives it a move order as usual, and the tools leave that click alone.

## The rail

The rail has an icon for each tool: Waypoint, Pen, Text, Bearing/range, Circle, and Eraser. The picked tool is solid green. Hover over an icon to see its name. Below the tools are Undo, Redo, and Clear, and then six colour swatches: green, white, orange, red, magenta, and cyan. The swatch you pick colours everything you draw next.

The strip beside the **Tools** button names the picked tool and says what your next click does. It also holds that tool's buttons, such as **Skip** and **Restart** for a route, or the preset circle sizes. When something can't be done, such as when a limit is reached, the strip turns amber and says why.

## Waypoint

Click the map to add a waypoint to the end of your route. Waypoints are numbered in order and joined by one line. Click on or beside a unit's icon to put the waypoint on that unit, and it moves with the unit.

While you fly, a line runs from your aircraft to the next waypoint. A waypoint counts as reached when you fly within 2.5 km of it, or when you pass it with it still within 10 km behind you. The route then moves on to the next one. Taxiing doesn't count.

- **Skip** moves on to the following waypoint without flying the next one.
- **Restart** goes back to waypoint 1. A waypoint already behind you then waits until you've turned toward it.
- **Undo** takes back the last waypoint. The Eraser deletes the whole route.

The next two waypoints show in the 3D view with their distance and bearing from you. A waypoint on a unit that's destroyed, or no longer tracked by your side, reads `lost` and stays where the unit was last seen. A route holds up to 99 waypoints.

With NOAutopilot installed, both routes work side by side. Plan the autopilot's route with right clicks while the tools rail is closed. The rail holds NOAutopilot's right clicks back while it's open, so a right click to delete a drawing doesn't also drop an autopilot waypoint.

## Pen

Press and drag to draw a line. The map doesn't pan while you draw. A click without dragging leaves a dot.

Pen lines share a budget of points, set by **MaxPenPoints**. A line that uses up the budget stops where it is, and the strip turns amber. Erase or undo a line to draw more.

## Text

Click where the note should go, type it, and press Enter. Escape drops it. Clicking somewhere else places what you've typed and starts a new note there.

While you type, your keys go to the note, not to the aircraft. Close the chat box before you place a note, since both read the same keys. A note is one line of up to 64 characters, and it also shows in the 3D view at that spot on the ground.

## Bearing and range

Click where to measure from, then where to. An arrow joins the two points, and the label at its head reads the bearing in degrees true and the distance, such as `045° 4.2nm`. Before your second click, the arrow follows the cursor, so you can read a measurement without placing it.

Click on or beside a unit's icon to tie that end to the unit. The arrow and its numbers then follow the unit as the map updates. Tie one end to your own aircraft and the other to a target, and the label is always your bearing and range to it. The label also shows in the 3D view.

## Circle

Press on the centre and drag out to the radius, or click the centre and then the edge. The radius is written on the ring.

For a set size, pick **5**, **10**, or **20** in the strip, then click the centre. The sizes are in your distance unit. Put the centre on a unit and the circle follows it.

Each circle also shows in the 3D view as a thin ring, level with its centre: at the unit's altitude, or on the ground. Turn this off with **ShowCirclesIn3D**. The 3D view shows the 16 newest circles.

## Eraser, undo, and clear

With the Eraser, the drawing under the cursor turns red, and a click deletes it. A right click deletes a drawing with any tool picked. **Clear** deletes everything at once, as one step, so Undo brings it all back.

## Planning ideas

- **Stay out of a SAM's reach.** Put a circle on the SAM's icon, sized to its range. Its ring then shows in the 3D view, so you can see the edge of the threat from the cockpit.
- **Time on target from two sides.** Each pilot puts a circle of the same size around the target and picks an entry point on it, on opposite sides. Measure from each entry point to the target, and call "in" together so both arrive at once. Drawings aren't shared, so each pilot draws their own.
- **Keep a target's bearing handy.** Measure from your aircraft to a moving target, with both ends on units. The label keeps updating as you both move.
- **Brief a route.** Drop waypoints over the ingress, add notes such as `IP` or `EGRESS`, and fly it with the 3D labels.

## Where drawings show

Drawings show on the full map and on the minimap. Turn off **ShowOnMinimap** to keep the minimap clear. They sit under the game's unit icons, so they never hide a unit.

Measurements, radii, and waypoint numbers sit on small dark plates. On the full map, they move aside so they don't cover unit icons, airbase names, runway numbers, or each other.

Distances follow the game's unit setting: kilometres for metric, nautical miles for imperial. Set **DistanceUnits** to always use one unit.

## Keeping it fast

A few drawings cost almost nothing, and a map full of them costs frames. Two limits keep that in check. **MaxShapes** caps the number of drawings, 200 by default, and **MaxPenPoints** caps pen detail.

To measure the cost on your own machine, run the perf test described in the [user guide](USER_GUIDE.md#the-perf-test).
