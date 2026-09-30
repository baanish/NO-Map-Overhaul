using BaanishUiImprovements.MapTools.BearingRange;
using BaanishUiImprovements.MapTools.Circle;
using BaanishUiImprovements.MapTools.Eraser;
using BaanishUiImprovements.MapTools.Pen;
using BaanishUiImprovements.MapTools.Text;
using BaanishUiImprovements.MapTools.Waypoint;
using TMPro;

namespace BaanishUiImprovements.MapTools;

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
    private readonly MapShapeLayer _layer;
    private readonly MapToolMenu _menu = new();
    private readonly MapToolInput _input = new();
    private readonly WorldLabelPool _worldLabels = new();
    private DynamicMap? _map;
    private int _active = -1;
    private int _unfinished = -1;

    public MapToolHost(ModSettings settings)
    {
        _settings = settings;
        _context = new MapToolContext(settings, _store);
        // Menu order; the menu opens on the first. Each tool lives in its own folder under MapTools.
        _tools = new MapTool[]
        {
            new WaypointTool(_context, settings),
            new PenTool(_context),
            new TextTool(_context),
            new BearingRangeTool(_context),
            new CircleTool(_context),
            new EraserTool(_context),
        };
        _layer = new MapShapeLayer(settings, _context, _tools.Length);
    }

    /// <summary>Per frame, after the HUD callout has found its label.</summary>
    public void Update(TextMeshProUGUI? hudStyle)
    {
        FinishDeactivation();
        var map = SceneSingleton<DynamicMap>.i;
        TrackMission(map);
        if (map == null || !_settings.ShowMapTools.Value)
        {
            Reset();
            return;
        }

        _store.MaxShapes = _settings.MapToolMaxShapes.Value;
        _store.MaxPoints = _settings.MapToolMaxPenPoints.Value;
        var open = DynamicMap.mapMaximized;
        if (open)
        {
            Apply(_menu.TakeCommand());
            _menu.Render(map, _tools, _active, _store, _context.Color, hudStyle);
        }
        else
        {
            Select(-1);
            _menu.Hide();
        }

        if (_input.Update(map, _menu.Catcher, _active >= 0 ? _tools[_active] : null))
        {
            _layer.MarkOverlayDirty(_active);
        }

        if (open && !_input.Typing && !CursorManager.GetFlag(CursorFlags.Chat | CursorFlags.GameMenu))
        {
            if (_settings.MapToolUndoKey.Value.IsDown())
            {
                _store.Undo();
            }
            else if (_settings.MapToolRedoKey.Value.IsDown())
            {
                _store.Redo();
            }
        }

        _worldLabels.Begin(hudStyle);
        foreach (var tool in _tools)
        {
            tool.OnFrame(_worldLabels);
        }

        _worldLabels.End();
        _layer.Render(map, _store, _tools, hudStyle);
        _layer.KeepUpright();
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

    /// <summary>A different map, or none (a destroyed one counts as none), means the player left the mission: drawings and history go.</summary>
    private void TrackMission(DynamicMap? map)
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
                Select(_active >= 0 ? -1 : 0);
                break;
            case MenuCommand.Tool:
                Select(command.Index);
                break;
            case MenuCommand.Option when _active >= 0:
                _tools[_active].OnOption(command.Index);
                _layer.MarkOverlayDirty(_active);
                break;
            case MenuCommand.Swatch:
                _settings.MapToolColor.Value = ShapeColor.Palette[command.Index].ToColor32();
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

    private void Select(int tool)
    {
        if (tool == _active)
        {
            return;
        }

        if (_active >= 0)
        {
            _input.Cancel();
            _tools[_active].OnDeactivate();
            _layer.MarkOverlayDirty(_active);
        }

        _active = tool;
        if (_active >= 0)
        {
            _tools[_active].OnActivate();
            _layer.MarkOverlayDirty(_active);
        }
    }
}
