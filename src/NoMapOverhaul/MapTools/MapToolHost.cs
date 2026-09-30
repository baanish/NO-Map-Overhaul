using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.MapTools.BearingRange;
using NoMapOverhaul.MapTools.Circle;
using NoMapOverhaul.MapTools.Eraser;
using NoMapOverhaul.MapTools.Pen;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.MapTools.Waypoint;
using BepInEx.Configuration;
using TMPro;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// The map tools: a Tools button on the full map opens a menu of drawing tools, and what they draw shows on both maps
/// and, for some shapes, in the 3D view. The host owns the drawings (<see cref="ShapeStore"/>) and the tools, and
/// connects them to the game through the menu, the input routing, the map layer, and the 3D labels.
/// Drawings are local and last until the player leaves the mission, which shows as the scene's DynamicMap changing.
/// </summary>
internal sealed class MapToolHost
{
    private readonly ModSettings _settings;
    private readonly ShapeStore _store = new();
    private readonly MapToolContext _context;
    private readonly MapTool[] _tools;
    private readonly RailIcon[] _icons;
    private readonly ConfigEntry<KeyboardShortcut>[] _keys;

    /// <summary>
    /// The tool the Tools button opens the rail on: the last one picked this session, across missions, since it's a
    /// preference rather than a drawing. Bearing/range, tool 1, until one is picked.
    /// </summary>
    private int _openingTool;
    private readonly MapShapeLayer _layer;
    private readonly MapToolMenu _menu;
    private readonly MapToolInput _input = new();
    private readonly WorldLabelPool _worldLabels;
    private readonly ShapeHitTest _hitTest;
    private DynamicMap? _map;
    private int _active = -1;
    private int _unfinished = -1;

    public MapToolHost(ModSettings settings)
    {
        _settings = settings;
        _context = new MapToolContext(settings, _store);
        _layer = new MapShapeLayer(settings, _context);
        _hitTest = new ShapeHitTest(_context, _layer);
        // Rail order, each with its icon and the key that picks it, 1 to 6 by default. Each tool lives in its own folder under MapTools.
        var tools = new (MapTool Tool, RailIcon Icon, ConfigEntry<KeyboardShortcut> Key)[]
        {
            (new BearingRangeTool(_context), RailIcon.BearingRange, settings.MapToolBearingRangeKey),
            (new CircleTool(_context), RailIcon.Circle, settings.MapToolCircleKey),
            (new TextTool(_context), RailIcon.Text, settings.MapToolTextKey),
            (new PenTool(_context), RailIcon.Pen, settings.MapToolPenKey),
            (new WaypointTool(_context, settings), RailIcon.Waypoint, settings.MapToolWaypointKey),
            (new EraserTool(_context, _layer), RailIcon.Eraser, settings.MapToolEraserKey),
        };
        _tools = System.Array.ConvertAll(tools, entry => entry.Tool);
        _openingTool = System.Array.FindIndex(_tools, tool => tool is BearingRange.BearingRangeTool);
        _icons = System.Array.ConvertAll(tools, entry => entry.Icon);
        _keys = System.Array.ConvertAll(tools, entry => entry.Key);
        _menu = new MapToolMenu(settings);
        _worldLabels = new WorldLabelPool(settings);
    }

    /// <summary>The drawings and the game lookups the tools use, for the perf test's generated drawings.</summary>
    public MapToolContext Context => _context;

    /// <summary>
    /// The menu is open and no selected unit would take a move order, so a right-click on the map is the tools' and
    /// NOAutopilot must not act on it too. Read by <see cref="AutopilotRightClickPatch"/> inside the game's map update,
    /// before this frame's <see cref="Update"/>, so the menu is as the last frame left it. False after <see cref="Reset"/>.
    /// </summary>
    public bool TakesRightClick(DynamicMap map) => RightClickRule.ToolsTake(_active >= 0, GameOrdersRightClick(map));

    /// <summary>Per frame, after the HUD callout has found its label.</summary>
    public void Update(TextMeshProUGUI? hudStyle)
    {
        FinishDeactivation();
        var map = SceneSingleton<DynamicMap>.i;
        TrackMission(map);
        if (map == null || !(_settings.ShowMapTools.Value || _settings.PerfTestShowsDrawings))
        {
            Reset();
            return;
        }

        var input = ModTimings.Start();
        _store.MaxShapes = _settings.MapToolMaxShapes.Value;
        _store.MaxPoints = _settings.MapToolMaxPenPoints.Value;
        var open = DynamicMap.mapMaximized;
        if (open)
        {
            Apply(_menu.TakeCommand());
            // Keys before the render, which switches the pointer catcher on or off, so the next click goes where the rail now says.
            if (!_input.Typing && !CursorManager.GetFlag(CursorFlags.Chat | CursorFlags.GameMenu) &&
                !NuclearOption.MissionEditorScripts.InputFieldChecker.InsideInputField)
            {
                if (_settings.MapToolUndoKey.Value.IsDown())
                {
                    _store.Undo();
                }
                else if (_settings.MapToolRedoKey.Value.IsDown())
                {
                    _store.Redo();
                }
                else if (_settings.MapToolsKey.Value.IsDown())
                {
                    Apply((MenuCommand.Toggle, 0));
                }
                else
                {
                    for (var i = 0; i < _keys.Length; i++)
                    {
                        if (_keys[i].Value.IsDown())
                        {
                            Apply((MenuCommand.Tool, i)); // opens the rail on that tool if it's closed
                            break;
                        }
                    }
                }
            }

            _menu.Render(map, _tools, _icons, _keys, _active, _store, _context.Color, hudStyle);
        }
        else
        {
            Select(-1);
            _menu.Hide();
        }

        _input.Update(map, _menu.Catcher, _active >= 0 ? _tools[_active] : null);
        if (_active >= 0 && _input.TakeRightClick(map, _menu.Catcher) is { } rightClick)
        {
            RightClick(map, _tools[_active], rightClick);
        }

        ModTimings.Stop(ModSection.InputAndMenu, input);
        var world = ModTimings.Start();
        _worldLabels.Begin(hudStyle);
        foreach (var tool in _tools)
        {
            tool.OnFrame(_worldLabels);
        }

        _worldLabels.End();
        ModTimings.Stop(ModSection.WorldLabels, world);
        _layer.Render(map, _store, _tools, hudStyle, _menu.Areas, _menu.LayoutVersion);
    }

    /// <summary>
    /// Master switch teardown: every graphic goes and the controls come straight back. The drawings stay for when it's
    /// on again. No tool code runs here, since a broken tool may be why the plugin is shutting down; the active tool
    /// hears it was switched off on the next update, if there is one.
    /// </summary>
    public void Reset()
    {
        _input.Reset();
        _menu.Reset();
        _layer.Reset();
        _worldLabels.Reset();
        if (_active >= 0)
        {
            _unfinished = _active;
            _active = -1;
        }
    }

    private void FinishDeactivation()
    {
        if (_unfinished >= 0)
        {
            var tool = _unfinished;
            _unfinished = -1;
            _tools[tool].OnDeactivate();
        }
    }

    /// <summary>
    /// A different map, or none (a destroyed one counts as none), means the player left the mission: drawings and history
    /// go. Checked every update, which doesn't run while the mod is off, so the perf test checks before saving the drawings.
    /// </summary>
    public void TrackMission(DynamicMap? map)
    {
        var current = map == null ? null : map;
        if (ReferenceEquals(current, _map))
        {
            return;
        }

        _map = current;
        Select(-1);
        _store.Reset();
        _context.ForgetUnits();
        foreach (var tool in _tools)
        {
            tool.OnMissionStart();
        }
    }

    private void Apply((MenuCommand Command, int Index) command)
    {
        switch (command.Command)
        {
            case MenuCommand.Toggle:
                Select(_active >= 0 ? -1 : _openingTool);
                break;
            case MenuCommand.Tool:
                Select(command.Index);
                break;
            case MenuCommand.Option when _active >= 0:
                _tools[_active].OnOption(command.Index);
                _tools[_active].InvalidateOverlay();
                break;
            case MenuCommand.Swatch:
                _settings.MapToolColor.Value = ShapeColor.Palette[command.Index].ToColor32();
                if (_active >= 0)
                {
                    _tools[_active].InvalidateOverlay(); // a preview draws in the picked colour
                }

                break;
            case MenuCommand.Undo:
                _store.Undo();
                break;
            case MenuCommand.Redo:
                _store.Redo();
                break;
            case MenuCommand.Clear:
                _store.Clear();
                break;
        }
    }

    /// <summary>
    /// A right-click on the map with the menu open, from any tool: cancels what's half-drawn, else deletes the drawing
    /// in the eraser's reach. The game reads the same click for a move order, which wins (see <see cref="RightClickRule"/>).
    /// </summary>
    private void RightClick(DynamicMap map, MapTool tool, MapPointer pointer)
    {
        var shape = _hitTest.Find(_store.Shapes, pointer.Position, EraserTool.Reach * _context.MetersPerIconUnit);
        switch (RightClickRule.Decide(menuOpen: true, GameOrdersRightClick(map), tool.InProgress, shape != null))
        {
            case RightClickAction.Cancel:
                Drop(tool);
                break;
            case RightClickAction.Delete:
                _store.Remove(shape!);
                break;
        }
    }

    /// <summary>
    /// The test <c>DynamicMap.MapControls</c> makes before a right-click move order: the first selected icon is a
    /// friendly unit that takes commands, outside the mission editor.
    /// </summary>
    private static bool GameOrdersRightClick(DynamicMap map) =>
        GameManager.gameState != GameState.Editor && map.selectedIcons.Count > 0 &&
        map.selectedIcons[0] is UnitMapIcon icon && icon.unit != null && icon.unit is ICommandable &&
        DynamicMap.GetFactionMode(icon.unit.NetworkHQ) == FactionMode.Friendly;

    private void Drop(MapTool tool)
    {
        _input.Cancel();
        tool.OnDeactivate();
        tool.InvalidateOverlay();
    }

    private void Select(int tool)
    {
        if (tool == _active)
        {
            return;
        }

        if (_active >= 0)
        {
            Drop(_tools[_active]);
        }

        _active = tool;
        if (_active >= 0)
        {
            _openingTool = _active;
            _tools[_active].OnActivate();
            _tools[_active].InvalidateOverlay();
        }
    }
}
