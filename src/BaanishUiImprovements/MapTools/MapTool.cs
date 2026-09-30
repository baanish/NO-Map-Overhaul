using System;
using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools;

/// <summary>A left-button event on the full map, in meters.</summary>
public readonly struct MapPointer
{
    public MapPointer(Vector2 position, MapPoint? unit)
    {
        Position = position;
        Unit = unit;
    }

    /// <summary>The ground under the cursor: X east, Y north.</summary>
    public Vector2 Position { get; }

    /// <summary>The unit whose map icon is on or just beside the cursor, as a point anchored to it, or null.</summary>
    public MapPoint? Unit { get; }

    /// <summary>The unit under the cursor if there is one, else the fixed point under the cursor.</summary>
    public MapPoint Point => Unit ?? new MapPoint(Position);
}

/// <summary>What the tools share: the drawings, the colour picked in the menu, and the map.</summary>
public interface IMapToolContext : IMapView
{
    /// <summary>Every tool's drawings. Each Add, Remove, Replace, and Clear is one undo step, and the map redraws on its own.</summary>
    ShapeStore Shapes { get; }

    /// <summary>The colour picked in the menu, for new shapes.</summary>
    ShapeColor Color { get; }

    /// <summary>
    /// <see cref="IMapView.TryResolve"/> in 3D, for a label in the 3D view: global meters, X east, Y up from sea level,
    /// Z north, with the unit's own altitude. Same rule and same false case; a fixed point gets sea level.
    /// </summary>
    bool TryResolveWorld(MapPoint point, out Vector3 position);

    /// <summary>
    /// Ground or sea height at a point, in meters above sea level, found the way the game's own map jump finds it. It
    /// casts a ray, so call it once per new shape, not per frame. 0 where nothing is under the point.
    /// </summary>
    float GroundElevation(Vector2 position);
}

/// <summary>Labels in the 3D view. Requested every frame from <see cref="MapTool.OnFrame"/>, each shows for that frame only.</summary>
public interface IWorldLabels
{
    /// <summary>
    /// Centres text on a point in the 3D view, in the game's HUD label font and size. Position in the game's global
    /// meters: X east, Y up from sea level, Z north. Hidden while the point is behind the camera, and with the HUD.
    /// </summary>
    void Add(Vector3 position, string text, ShapeColor color);
}

/// <summary>
/// One entry in the map tools menu. Unity-free, so a tool can be tested with a fake context. Everything is called from
/// the plugin's guarded update, so a throw disables the mod rather than the game.
/// While the tool is active, left clicks on the full map come here instead of the game. Right clicks always stay with
/// the game (move orders, and NOAutopilot's waypoints).
/// </summary>
public abstract class MapTool
{
    protected MapTool(IMapToolContext context) => Context = context;

    /// <summary>The menu label.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// False (the default): dragging still pans the map, and the tool gets <see cref="OnClick"/> for a press and
    /// release that didn't move. True: the tool gets <see cref="OnPointerDown"/>, <see cref="OnPointerDrag"/>, and
    /// <see cref="OnPointerUp"/> instead, and the map stops panning and zooming while the button is held.
    /// </summary>
    public virtual bool CapturesDrag => false;

    /// <summary>
    /// True while the tool takes typed text through <see cref="OnTextInput"/>. Meanwhile the game's keyboard controls,
    /// its Escape menu, and the undo and redo keys are off, the way the game's chat box silences them.
    /// </summary>
    public virtual bool CapturesKeyboard => false;

    /// <summary>The hint in the menu's strip while active, such as the next step or why nothing happened. Return a cached string: the menu reads it every frame.</summary>
    public virtual string Status => string.Empty;

    /// <summary>True while <see cref="Status"/> says why the tool can't do what was asked, such as a limit reached. The strip turns amber and badges the tool.</summary>
    public virtual bool Warning => false;

    /// <summary>Extra buttons in the menu while active, such as preset radii. Return the same list until a label changes: the menu reads it every frame.</summary>
    public virtual IReadOnlyList<string> Options => Array.Empty<string>();

    /// <summary>The option shown as picked, such as an armed preset radius, or -1.</summary>
    public virtual int PickedOption => -1;

    /// <summary>A unit written after the option buttons, such as "nm" after preset radii. Return a cached string.</summary>
    public virtual string OptionSuffix => string.Empty;

    /// <summary>A short count at the end of the strip, such as characters typed out of the most allowed. Return a cached string.</summary>
    public virtual string Counter => string.Empty;

    /// <summary>Set by <see cref="InvalidateOverlay"/>; the map layer clears it once it has redrawn the overlay.</summary>
    internal bool OverlayInvalid { get; set; }

    protected IMapToolContext Context { get; }

    /// <summary>Picked in the menu, or the menu opened on this tool.</summary>
    public virtual void OnActivate()
    {
    }

    /// <summary>Another tool was picked, or the menu or map closed. Drop anything half-drawn.</summary>
    public virtual void OnDeactivate()
    {
    }

    /// <summary>A new mission: the store is already empty and unit ids mean new units. Forget any per-mission state.</summary>
    public virtual void OnMissionStart()
    {
    }

    /// <summary>The button at this index of <see cref="Options"/> was clicked.</summary>
    public virtual void OnOption(int index)
    {
    }

    public virtual void OnClick(MapPointer pointer)
    {
    }

    public virtual void OnPointerDown(MapPointer pointer)
    {
    }

    public virtual void OnPointerDrag(MapPointer pointer)
    {
    }

    public virtual void OnPointerUp(MapPointer pointer)
    {
    }

    /// <summary>The cursor moved over the map with no button held.</summary>
    public virtual void OnPointerMove(MapPointer pointer)
    {
    }

    /// <summary>A typed character while <see cref="CapturesKeyboard"/>: '\b' is Backspace, '\n' Enter, '\u001b' Escape.</summary>
    public virtual void OnTextInput(char character)
    {
    }

    /// <summary>
    /// Map drawing the tool owns that isn't a stored shape: a rubber-band line while placing, the leg from the aircraft
    /// to the next waypoint, the eraser's highlight. Not erasable and not undone. Called for every tool, active or not,
    /// only when something may have changed it: after <see cref="InvalidateOverlay"/>, when the tool is switched on or
    /// off, a menu option or colour is picked, the drawings or the zoom change, and on each of the game's 10 Hz map
    /// refreshes while it shows a unit or the aircraft. So an event that changes what it draws must invalidate it.
    /// </summary>
    public virtual void DrawOverlay(IMapCanvas canvas)
    {
    }

    /// <summary>Redraws <see cref="DrawOverlay"/> this frame. Call it only when the drawing changes: a long pen stroke is costly to redraw.</summary>
    protected internal void InvalidateOverlay() => OverlayInvalid = true;

    /// <summary>
    /// Every frame, whichever tool is active and whether the map is open or not: advance live state, such as a route
    /// passing a waypoint, and request this frame's 3D labels. No allocations here.
    /// </summary>
    public virtual void OnFrame(IWorldLabels labels)
    {
    }
}
