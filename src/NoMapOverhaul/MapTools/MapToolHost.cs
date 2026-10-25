using NoMapOverhaul.Diagnostics;
using NoMapOverhaul.MapTools.BearingRange;
using NoMapOverhaul.MapTools.Circle;
using NoMapOverhaul.MapTools.Eraser;
using NoMapOverhaul.MapTools.Pen;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.MapTools.Waypoint;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using NoMapOverhaul.Sharing;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// The map tools: a Tools button on the full map opens a menu of drawing tools, and what they draw shows on both maps
/// and, for some shapes, in the 3D view. The host owns the drawings (<see cref="ShapeStore"/>) and the tools, and
/// connects them to the game through the menu, the input routing, the map layer, and the 3D labels.
/// Local drawings and undo belong to this player; received drawings never enter the store.
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

    /// <summary>The Bearing/range key pressed twice puts the measurement's start on the player's aircraft.</summary>
    private readonly DoubleTap _bearingKeyTap = new();
    private readonly WorldLabelPool _worldLabels;
    private readonly EraserTool _eraser;
    private readonly DrawingAuthor _author = new();
    private DynamicMap? _map;
    private FactionHQ? _hq;
    private MissionManager? _mission;
    private double _missionStart;
    private List<MapShape> _visibleReceived = new();
    private List<MapShape> _receivedCandidates = new();
    private readonly System.Func<uint, bool> _knowsSharedUnit;
    private int _visibleReceivedVersion;
    private (int Version, float MapTick, bool Testing, bool ShowsReceived) _receivedKey = (-1, -1f, false, false);
    private int _active = -1;
    private int _unfinished = -1;
    private string? _broadcastConfiguration;
    private string? _receiveConfiguration;
    private PerfSharing? _configuredPerf;
    private (string Channel, string Error, bool Enabled, string Transport) _sharingStatusKey;
    private string _sharingStatus = string.Empty;
    private bool _editingChannels;
    private bool _pickingColor;
    private string? _customConfiguration;
    private IReadOnlyList<ShapeColor> _customColors = CustomColors.None;

    /// <summary>The Channels panel or the colour picker is open: the map and the tools' keys take no input.</summary>
    private bool MenuPanelOpen => _editingChannels || _pickingColor;

    public DrawingChannels Channels { get; } = new();
    public DrawingChannels ReceiveChannels { get; } = new();
    public bool ChannelsValid { get; private set; }
    public string ChannelError { get; private set; } = string.Empty;
    public bool SharingEligible => (PerfOnlyLists || ChannelsValid) && (SharingBroadcast.Count > 0 || SharingReceive.Count > 0) &&
        (_settings.MapSharingEnabled.Value || PerfSharing != null) &&
        _settings.Enabled.Value && (_settings.ShowMapTools.Value || _settings.PerfTestShowsDrawings);

    /// <summary>
    /// The perf test's own sharing while it runs one of its sharing modes, in memory only, so no setting is written. With
    /// Sharing on, its channel joins the Broadcast list when it sends, or the Receive only list when it only receives. With
    /// Sharing off, sharing runs for the test alone: on the test's channel and nothing else, so the player's channels and
    /// drawings stay off the network as Sharing says. Null otherwise.
    /// </summary>
    public PerfSharing? PerfSharing { get; set; }

    private static readonly IReadOnlyList<string> PerfChannelOnly = System.Array.AsReadOnly(new[] { Diagnostics.PerfSharing.Channel });
    private static readonly IReadOnlyList<string> NoChannels = System.Array.AsReadOnly(System.Array.Empty<string>());

    /// <summary>A perf test runs with Sharing off: sharing gets the test's channel alone.</summary>
    private bool PerfOnlyLists => PerfSharing != null && !_settings.MapSharingEnabled.Value;

    /// <summary>The Broadcast channels sharing advertises and sends on: the player's, or during a test with Sharing off, the test's.</summary>
    public IReadOnlyList<string> SharingBroadcast => PerfOnlyLists ? (PerfSharing!.Sends ? PerfChannelOnly : NoChannels) : Channels.Names;

    /// <summary>The Receive only channels sharing advertises, chosen as <see cref="SharingBroadcast"/> is.</summary>
    public IReadOnlyList<string> SharingReceive => PerfOnlyLists ? (PerfSharing!.Sends ? NoChannels : PerfChannelOnly) : ReceiveChannels.Names;

    /// <summary>
    /// While the perf test has swapped its drawings into the store: the player's own, which sharing keeps sending as
    /// they were, and whether the store's test drawings go out after them. Null otherwise.
    /// </summary>
    private IReadOnlyList<MapShape>? _heldShapes;
    private bool _sendsStore;
    private List<MapShape>? _outgoing;
    private (object? Held, int Store) _outgoingKey = (null, -1);
    private int _outgoingVersion;

    /// <summary>Has sharing send <paramref name="held"/>, after the store's drawings if <paramref name="sendStore"/>, until <see cref="ReleaseOutgoing"/>.</summary>
    public void HoldOutgoing(IReadOnlyList<MapShape> held, bool sendStore)
    {
        _heldShapes = held;
        _sendsStore = sendStore;
    }

    public void ReleaseOutgoing() => _heldShapes = null;

    /// <summary>True while the perf test holds the player's drawings aside: a teammate's erase request then has nothing it may remove.</summary>
    public bool OutgoingHeld => _heldShapes != null;

    /// <summary>What sharing sends: the store's drawings, or while the perf test holds them aside, see <see cref="HoldOutgoing"/>.</summary>
    public IReadOnlyList<MapShape> OutgoingShapes
    {
        get
        {
            RefreshOutgoing();
            return _outgoing ?? _store.Shapes;
        }
    }

    /// <summary>Changes whenever <see cref="OutgoingShapes"/> may have, and never repeats, so a peer sent an older set is sent again.</summary>
    public int OutgoingVersion
    {
        get
        {
            RefreshOutgoing();
            return _outgoingVersion;
        }
    }

    private void RefreshOutgoing()
    {
        var key = (_heldShapes as object, _heldShapes == null || _sendsStore ? _store.Version : -1);
        if (ReferenceEquals(key.Item1, _outgoingKey.Held) && key.Item2 == _outgoingKey.Store)
        {
            return;
        }

        _outgoingKey = key;
        _outgoingVersion++;
        if (_heldShapes == null)
        {
            _outgoing = null;
            return;
        }

        // The test's drawings first: a snapshot past the wire limits keeps its first drawings, and a teammate testing too
        // must get the whole test set. One who isn't testing gets only the player's own, the test's channel filtered out.
        _outgoing = new List<MapShape>(_heldShapes.Count + (_sendsStore ? _store.Shapes.Count : 0));
        if (_sendsStore)
        {
            _outgoing.AddRange(_store.Shapes);
        }

        _outgoing.AddRange(_heldShapes);
    }

    /// <summary>
    /// Which received drawings show while the perf test runs: none, so the player's teammates' drawings stay out of the
    /// numbers, or with <see cref="PerfShowsReceived"/> only those on the test's channel. Every received drawing is kept
    /// and shows again once the test ends.
    /// </summary>
    public bool PerfShowsReceived { get; set; }
    public string SharingStatus { get; set; } = "waiting for session";
    public ReceivedDrawings Received { get; } = new();
    public IReadOnlyList<MapShape> VisibleReceived => _visibleReceived;
    public int MissionVersion { get; private set; }
    public System.Func<MapShape, bool>? RequestSharedErase { private get; set; }
    public System.Func<MapShape, bool>? CanRequestSharedErase { private get; set; }

    /// <summary>A received drawing's owner's name on the game's roster, or null; see <see cref="DrawingAuthor"/>.</summary>
    public System.Func<ulong, string?>? OwnerName { private get; set; }

    public MapToolHost(ModSettings settings)
    {
        _settings = settings;
        _context = new MapToolContext(settings, _store);
        _knowsSharedUnit = _context.KnowsSharedUnit;
        _layer = new MapShapeLayer(settings, _context);
        _eraser = new EraserTool(_context, _layer, () => _visibleReceived, CanEraseReceived,
            shape => RequestSharedErase?.Invoke(shape) ?? false);
        // Rail order, each with its icon and the key that picks it, 1 to 6 by default. Each tool lives in its own folder under MapTools.
        var tools = new (MapTool Tool, RailIcon Icon, ConfigEntry<KeyboardShortcut> Key)[]
        {
            (new BearingRangeTool(_context), RailIcon.BearingRange, settings.MapToolBearingRangeKey),
            (new CircleTool(_context), RailIcon.Circle, settings.MapToolCircleKey),
            (new TextTool(_context), RailIcon.Text, settings.MapToolTextKey),
            (new PenTool(_context), RailIcon.Pen, settings.MapToolPenKey),
            (new WaypointTool(_context, settings), RailIcon.Waypoint, settings.MapToolWaypointKey),
            (_eraser, RailIcon.Eraser, settings.MapToolEraserKey),
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

    /// <summary>How far the player has flown their route, which the perf test's own route would otherwise reset.</summary>
    public RouteProgress RouteProgress => System.Array.Find(_tools, tool => tool is WaypointTool) is WaypointTool waypoints
        ? waypoints.Progress : throw new System.InvalidOperationException("No waypoint tool.");

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
        RefreshChannels();
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
            RefreshCustomColors();
            Apply(_menu.TakeCommand());
            ApplySharingMenu();
            ApplyColorPanel();
            // Keys before the render, which switches the pointer catcher on or off, so the next click goes where the rail now says.
            if (!MenuPanelOpen && !_input.Typing && !CursorManager.GetFlag(CursorFlags.Chat | CursorFlags.GameMenu | CursorFlags.Dialogue | CursorFlags.SelectionMenu) &&
                !NuclearOption.MissionEditorScripts.InputFieldChecker.InsideInputField)
            {
                if (TakeChannelShortcut())
                {
                    // Channel keys belong to channel selection even when that channel is not configured.
                }
                else if (_settings.MapToolUndoKey.Value.IsDown())
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
                            if (_tools[i] is BearingRangeTool bearing && _bearingKeyTap.Press(UnityEngine.Time.unscaledTime))
                            {
                                bearing.StartAtOwnAircraft();
                            }

                            break;
                        }
                    }
                }
            }

            _menu.Render(map, _tools, _icons, _keys, _active, _store, _context.Color, _customColors, hudStyle, ChannelStatus(), Channels,
                ReceiveChannels);
        }
        else
        {
            Select(-1);
            _menu.Hide();
            _editingChannels = false;
            _pickingColor = false;
        }

        var receivedVersion = _visibleReceivedVersion;
        RefreshReceived(map);
        if (receivedVersion != _visibleReceivedVersion)
        {
            // Snapshot replacements must have their displayed label boxes before a click can select them.
            _layer.Render(map, _store, _tools, hudStyle, _menu.Areas, _menu.LayoutVersion,
                _visibleReceived, _visibleReceivedVersion);
        }

        _input.Update(map, _menu.Catcher, _active >= 0 ? _tools[_active] : null, MenuPanelOpen, _editingChannels);
        // With the rail closed no catcher covers the map, so the cursor is on it where the game's own map takes it to be, off the Tools button.
        var cursor = _active >= 0 ? _input.Hover
            : open && !_menu.HeadHovered && map.IsCursorInMapRectangle() ? MapToolInput.CursorMeters(map) : null;
        // Without teammates' drawings there's nothing to name, so no hit test.
        _author.Hover(_visibleReceived.Count > 0 && cursor is { } at ? _eraser.FindAt(at) : null, Received, OwnerName);
        if (open)
        {
            _menu.ShowAuthor(_author.Name, hudStyle);
        }
        if (!MenuPanelOpen && _active >= 0 && _input.TakeRightClick(map, _menu.Catcher) is { } rightClick)
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

        foreach (var shape in _visibleReceived)
        {
            switch (shape)
            {
                case BearingRangeShape bearing: bearing.AddWorldLabel(_context, _worldLabels); break;
                case TextNote note: note.AddWorldLabel(_context, _worldLabels); break;
                case CircleShape circle: circle.AddWorldRing(_context, _worldLabels); break;
            }
        }

        _worldLabels.End();
        ModTimings.Stop(ModSection.WorldLabels, world);
        _layer.Render(map, _store, _tools, hudStyle, _menu.Areas, _menu.LayoutVersion,
            _visibleReceived, _visibleReceivedVersion);
    }

    /// <summary>
    /// Master switch teardown: every graphic goes and the controls come straight back. The drawings stay for when it's
    /// on again. No tool code runs here, since a broken tool may be why the plugin is shutting down; the active tool
    /// hears it was switched off on the next update, if there is one.
    /// </summary>
    public void Reset()
    {
        _editingChannels = false;
        _pickingColor = false;
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

    public void RefreshChannels()
    {
        var broadcast = _settings.MapBroadcastChannels.Value;
        var receive = _settings.MapReceiveChannels.Value;
        // With Sharing off, a test's channel stays out of the player's lists: sharing uses the test's alone (SharingBroadcast).
        var perfJoins = _settings.MapSharingEnabled.Value ? PerfSharing : null;
        if (broadcast == _broadcastConfiguration && receive == _receiveConfiguration && ReferenceEquals(perfJoins, _configuredPerf))
        {
            return;
        }

        _broadcastConfiguration = broadcast;
        _receiveConfiguration = receive;
        _configuredPerf = perfJoins;
        if (perfJoins is { } perf)
        {
            // Last in its list, so the player's channels keep their shortcut slots.
            if (perf.Sends) broadcast += "," + PerfSharing.Channel;
            else receive += "," + PerfSharing.Channel;
        }

        ChannelsValid = DrawingChannels.TryConfigurePair(Channels, ReceiveChannels, broadcast, receive, out var error);
        ChannelError = error;
        SetDrawingChannel(ChannelsValid ? Channels.Selected : string.Empty);
    }

    private void ApplySharingMenu()
    {
        var panel = _menu.Sharing;
        if (panel == null)
        {
            _editingChannels = false;
            return;
        }

        var command = panel.TakeCommand();
        if (command.Command == SharingPanelCommand.Apply)
        {
            if (!DrawingChannels.TryConfigurePair(Channels, ReceiveChannels, panel.DraftBroadcast, panel.DraftReceive, out var error))
            {
                panel.ShowError(error);
            }
            else
            {
                _settings.MapBroadcastChannels.Value = string.Join(",", Channels.Names);
                _settings.MapReceiveChannels.Value = string.Join(",", ReceiveChannels.Names);
                _settings.MapSharingEnabled.Value = panel.DraftEnabled;
                RefreshChannels();
                panel.Close();
            }
        }
        else if (command.Command == SharingPanelCommand.SelectChannel && ChannelsValid && Channels.TrySelect(command.Index))
        {
            SetDrawingChannel(Channels.Selected);
        }

        if (panel.Open && !_editingChannels && _active >= 0)
        {
            Drop(_tools[_active]);
        }

        if (panel.Open && (Input.GetKeyDown(KeyCode.Escape) || !_input.CaptureMenuKeyboard()))
        {
            panel.Close();
        }

        _editingChannels = panel.Open;
    }

    /// <summary>
    /// The colour picker: Add saves the colour in the next custom slot and draws with it, or for a colour a swatch already
    /// has, just draws with it, which picks that swatch (<see cref="CustomColors.With"/>). Escape cancels. Opening the
    /// Channels panel closes it. It has no text field, so while it's open only Escape is held from the game's menu
    /// (<see cref="MapToolInput.Update"/>) and the map key still closes the map, which cancels it. A press on it holds the
    /// mouse until release, so dragging on the wheel doesn't pan the map.
    /// </summary>
    private void ApplyColorPanel()
    {
        var panel = _menu.ColorPanel;
        if (panel == null)
        {
            _pickingColor = false;
            return;
        }

        if (panel.Open && _menu.Sharing is { Open: true })
        {
            panel.Close();
        }

        if (panel.TakeAdd())
        {
            var color = panel.Picked;
            SaveCustomColors(CustomColors.With(_customColors, color));
            SetDrawingColor(color);
            panel.Close();
        }

        if (panel.Open && !_pickingColor && _active >= 0)
        {
            Drop(_tools[_active]);
        }

        if (panel.Open && panel.Pressed)
        {
            _input.HoldMouseUntilRelease();
        }

        if (panel.Open && Input.GetKeyDown(KeyCode.Escape))
        {
            panel.Close();
        }

        _pickingColor = panel.Open;
    }

    /// <summary>Reads the custom colours again when the setting's text changes, as when Add or a right-click saves it.</summary>
    private void RefreshCustomColors()
    {
        var text = _settings.MapToolCustomColors.Value;
        if (text == _customConfiguration)
        {
            return;
        }

        _customConfiguration = text;
        _customColors = CustomColors.Parse(text);
    }

    private void SaveCustomColors(IReadOnlyList<ShapeColor> colors)
    {
        if (ReferenceEquals(colors, _customColors))
        {
            return;
        }

        _settings.MapToolCustomColors.Value = CustomColors.Format(colors);
        RefreshCustomColors();
    }

    private void SetDrawingColor(ShapeColor color)
    {
        _settings.MapToolColor.Value = color.ToColor32();
        if (_active >= 0)
        {
            _tools[_active].InvalidateOverlay(); // a preview draws in the picked colour
        }
    }

    private void SetDrawingChannel(string channel)
    {
        if (_store.DrawingChannel == channel)
        {
            return;
        }

        if (_active >= 0 && _tools[_active].InProgress)
        {
            Drop(_tools[_active]);
        }

        _store.DrawingChannel = channel;
    }

    private bool TakeChannelShortcut()
    {
        for (var index = 0; index < _settings.MapChannelKeys.Length; index++)
        {
            if (!ChannelKeyDown(_settings.MapChannelKeys[index].Value))
            {
                continue;
            }

            if (ChannelsValid && Channels.TrySelect(index))
            {
                SetDrawingChannel(Channels.Selected);
            }

            return true;
        }

        return false;
    }

    private static bool ChannelKeyDown(KeyboardShortcut shortcut)
    {
        if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey)) return false;
        if (!shortcut.Modifiers.Any(key => key == KeyCode.LeftShift || key == KeyCode.RightShift)) return shortcut.IsDown();

        // Match BepInEx's exact combination check, ignoring unbound mouse buttons as it does, but accepting either Shift.
        var held = BepInEx.UnityInput.Current.SupportedKeyCodes.Where(key => key != shortcut.MainKey && key != KeyCode.None &&
            (key < KeyCode.Mouse0 || key > KeyCode.Mouse6 || shortcut.Modifiers.Contains(key)) && Input.GetKey(key));
        return ChannelKeyModifiers.Match(shortcut.Modifiers, held, KeyCode.LeftShift, KeyCode.RightShift);
    }

    private string ChannelStatus()
    {
        var key = (_store.DrawingChannel, ChannelError, _settings.MapSharingEnabled.Value, SharingStatus);
        if (key != _sharingStatusKey || _sharingStatus.Length == 0)
        {
            _sharingStatusKey = key;
            _sharingStatus = !ChannelsValid ? "Local. " + ChannelError
                : _store.DrawingChannel.Length == 0 ? "Receive only. " + (_settings.MapSharingEnabled.Value ? SharingStatus : "Sharing off.")
                : "Broadcast: " + _store.DrawingChannel + ". " + (_settings.MapSharingEnabled.Value ? SharingStatus : "Sharing off.");
        }

        return _sharingStatus;
    }

    /// <summary>
    /// Missions discard drawings; team changes make all prior work local-only, including undo, and discard cached knowledge.
    /// </summary>
    public void TrackMission(DynamicMap? map)
    {
        var current = map == null ? null : map;
        var hq = current != null ? current.HQ : null;
        var mission = MissionManager.i;
        var start = mission != null ? mission.multiplayerStartTime : 0;
        var newMission = !ReferenceEquals(current, _map) || !ReferenceEquals(mission, _mission) || start != _missionStart;
        if (!newMission && ReferenceEquals(hq, _hq))
        {
            return;
        }

        _map = current;
        _hq = hq;
        _mission = mission;
        _missionStart = start;
        MissionVersion++;
        Select(-1);
        FinishDeactivation();
        if (newMission) _store.Reset();
        else _store.KeepLocal();
        Received.Clear();
        _context.ForgetUnits();
        if (newMission) foreach (var tool in _tools)
        {
            tool.OnMissionStart();
        }
    }

    /// <summary>
    /// Which received drawings show, worked out again when they change, on each of the game's 10 Hz map refreshes, and
    /// when the perf test changes what it shows. A drawing hides only while a unit it's on was never known to the
    /// player's side, which the map refresh is quick enough for; a track going stale leaves it showing, as lost.
    /// </summary>
    private void RefreshReceived(DynamicMap map)
    {
        var key = (Received.Version, map.mapLastUpdated, _settings.PerfTestShowsDrawings, PerfShowsReceived);
        if (key.Equals(_receivedKey))
        {
            return;
        }

        _receivedKey = key;
        _receivedCandidates.Clear();
        var received = Received.Shapes;
        var testing = _settings.PerfTestShowsDrawings;
        for (var i = 0; i < received.Count; i++) // foreach over the interface would allocate an enumerator every frame
        {
            if (testing && !(PerfShowsReceived && received[i].Channel == PerfSharing.Channel))
            {
                continue;
            }

            if (SharedShapeVisibility.HasKnownAnchors(received[i], _knowsSharedUnit))
            {
                _receivedCandidates.Add(received[i]);
            }
        }

        var changed = _receivedCandidates.Count != _visibleReceived.Count;
        for (var i = 0; !changed && i < _receivedCandidates.Count; i++)
        {
            changed = !ReferenceEquals(_receivedCandidates[i], _visibleReceived[i]);
        }

        if (changed)
        {
            (_visibleReceived, _receivedCandidates) = (_receivedCandidates, _visibleReceived);
            _visibleReceivedVersion++;
        }
    }

    private bool CanEraseReceived(MapShape shape) => SharingEligible && RequestSharedErase != null &&
        (CanRequestSharedErase?.Invoke(shape) ?? false);

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
                SetDrawingColor(ShapeColor.Palette[command.Index]);
                break;
            case MenuCommand.CustomSwatch when command.Index < _customColors.Count:
                SetDrawingColor(_customColors[command.Index]);
                break;
            case MenuCommand.CustomSwatch when command.Index == _customColors.Count && _menu.ColorPanel != null:
                _menu.Sharing?.Close();
                _menu.ColorPanel.Show(_context.Color);
                break;
            case MenuCommand.RemoveCustomSwatch when _map != null && !GameOrdersRightClick(_map):
                // The drawing colour stays as it is, even if it was this one.
                SaveCustomColors(CustomColors.Without(_customColors, command.Index));
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
        var shape = _eraser.FindAt(pointer.Position);
        switch (RightClickRule.Decide(menuOpen: true, GameOrdersRightClick(map), tool.InProgress, shape != null))
        {
            case RightClickAction.Cancel:
                Drop(tool);
                break;
            case RightClickAction.Delete:
                _eraser.TryErase(shape!);
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
