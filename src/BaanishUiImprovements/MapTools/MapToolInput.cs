using System.Collections;
using BepInEx;
using Rewired;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Hands the left mouse button and, while a tool types, the keyboard to the active tool, and right clicks on the map to
/// the host. Nothing in the game is patched.
/// <para>
/// Clicks: the game has one left-click path on the map by default, the event system's click on a unit icon
/// (<c>MapIcon.OnPointerClick</c>), and <see cref="MapPointerCatcher"/> covers the icons to take it. The game's other
/// map selection, <c>DynamicMap.SelectFromMap</c>, runs on the Rewired "Select" action, which is bound to Enter, not
/// the mouse. The game reads right clicks itself for move orders, so the host decides whether one is the tools' (see
/// <see cref="RightClickRule"/>).
/// </para>
/// <para>
/// Drags pan the map (<c>DynamicMap.MapControls</c> reads the mouse axes while the button is held), which a click-only
/// tool keeps. For a tool that captures drags, the player's Rewired mouse maps are off until the button comes up, even
/// when the drag is cut short: the game would read a button still held as a pan, or as Fire once the map closes.
/// </para>
/// <para>
/// Typing: the keyboard maps and the Escape menu are off while the tool captures the keyboard, the way the game's chat
/// box does it, and the chat's cursor flag is raised, which is what other mods (NOAutopilot) check before reading their
/// hotkeys. All of it comes back once Enter and Escape are up, since Rewired would read a key still held as a fresh press.
/// </para>
/// <para>
/// The chat box, a dialogue, and the aircraft selection menu each turn the Escape menu off while open and back on as
/// they close. Typing doesn't start while one of them is open (the chat box would also read the same keys, and Enter
/// would send the text as chat), or while anything else holds the Escape menu off, so it always starts from Escape on
/// and chat off. On release, Escape comes back on only if none of them is open by then: one that opened meanwhile owns it.
/// </para>
/// </summary>
internal sealed class MapToolInput
{
    /// <summary>The game UIs that switch <c>GameplayUI.AllowPauseKeybind</c> off while they're open.</summary>
    private const CursorFlags PauseKeyOwners = CursorFlags.Chat | CursorFlags.Dialogue | CursorFlags.SelectionMenu;

    /// <summary>A press that moves further than this, in screen pixels, pans the map rather than clicking.</summary>
    private const float ClickSlop = 6f;

    /// <summary>How near a unit's map icon the cursor must be to anchor to it, in icon units: a little past a typical icon's edge.</summary>
    private const float UnitReach = 12f;

    private ControllerMapSuspension _mouse = new(ControllerType.Mouse);
    private readonly ControllerMapSuspension _keyboard = new(ControllerType.Keyboard);
    private bool _pressed;
    private bool _panned;
    private bool _dragging;
    private Vector2 _pressAt;
    private Vector2 _lastAt;

    /// <summary>The keyboard belongs to a tool, so the undo and redo keys stay quiet.</summary>
    public bool Typing => _keyboard.IsSuspended;

    /// <summary>Per frame. Delivers this frame's pointer and keyboard events to the active tool, if any.</summary>
    public void Update(DynamicMap map, MapPointerCatcher? catcher, MapTool? tool)
    {
        if (tool == null || catcher == null)
        {
            EndPress();
            ReleaseKeyboard(immediately: false);
            return;
        }

        UpdateKeyboard(tool);
        UpdatePointer(map, catcher, tool);
    }

    /// <summary>Where a right press on the map landed this frame, if there was one. A press on the menu doesn't count.</summary>
    public MapPointer? TakeRightClick(DynamicMap map, MapPointerCatcher? catcher) =>
        catcher != null && catcher.TakeRightPress() ? Pointer(map, Input.mousePosition) : null;

    /// <summary>The tool is being switched off: forget its press. Its half-drawn work is its own to drop.</summary>
    public void Cancel()
    {
        EndPress();
        ReleaseKeyboard(immediately: false);
    }

    /// <summary>Teardown: gives the keyboard straight back, since no later frame may come to do it, and the mouse once its button is up.</summary>
    public void Reset()
    {
        EndPress();
        ReleaseKeyboard(immediately: true);
    }

    private void UpdatePointer(DynamicMap map, MapPointerCatcher catcher, MapTool tool)
    {
        Vector2 mouse = Input.mousePosition;
        if (catcher.TakePress())
        {
            EndPress();
            _pressed = true;
            _panned = false;
            _pressAt = _lastAt = mouse;
            _dragging = tool.CapturesDrag;
            if (_dragging)
            {
                _mouse.Suspend();
                tool.OnPointerDown(Pointer(map, mouse));
            }

            return;
        }

        if (_pressed)
        {
            _panned |= (mouse - _pressAt).sqrMagnitude > ClickSlop * ClickSlop;
            if (Input.GetMouseButton(0))
            {
                if (_dragging)
                {
                    _mouse.KeepSuspended();
                }

                if (!_dragging || mouse == _lastAt)
                {
                    return;
                }

                _lastAt = mouse;
                tool.OnPointerDrag(Pointer(map, mouse));
                return;
            }

            var dragging = _dragging;
            EndPress();
            if (dragging)
            {
                tool.OnPointerUp(Pointer(map, mouse));
                return;
            }

            if (_panned)
            {
                return;
            }

            tool.OnClick(Pointer(map, _pressAt));
            return;
        }

        if (!catcher.Hovered || mouse == _lastAt)
        {
            return;
        }

        _lastAt = mouse;
        tool.OnPointerMove(Pointer(map, mouse));
    }

    private void UpdateKeyboard(MapTool tool)
    {
        if (!tool.CapturesKeyboard)
        {
            ReleaseKeyboard(immediately: false);
            return;
        }

        if (!_keyboard.IsSuspended)
        {
            if (CursorManager.GetFlag(PauseKeyOwners) || !GameplayUI.AllowPauseKeybind)
            {
                tool.OnTextInput('\u001b');
                return;
            }

            _keyboard.Suspend();
            GameplayUI.AllowPauseKeybind = false;
            CursorManager.SetFlag(CursorFlags.Chat, true);
        }

        foreach (var character in Input.inputString)
        {
            tool.OnTextInput(character == '\r' ? '\n' : character);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            tool.OnTextInput('\u001b');
        }
    }

    private void ReleaseKeyboard(bool immediately)
    {
        if (!_keyboard.IsSuspended ||
            (!immediately && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKey(KeyCode.Escape))))
        {
            return;
        }

        _keyboard.Resume();
        CursorManager.SetFlag(CursorFlags.Chat, false);
        if (!CursorManager.GetFlag(PauseKeyOwners))
        {
            GameplayUI.AllowPauseKeybind = true;
        }
    }

    /// <summary>
    /// Gives the mouse maps back once the left button is up. A drag cut short while it's held (a right-click cancel,
    /// the map closing, the tools or the mod switched off) hands the maps to a coroutine on BepInEx's own object that
    /// waits for the release, since this may be the last frame the mod runs.
    /// </summary>
    private void EndPress()
    {
        _pressed = false;
        _dragging = false;
        if (!_mouse.IsSuspended)
        {
            return;
        }

        if (!Input.GetMouseButton(0))
        {
            _mouse.Resume();
            return;
        }

        ThreadingHelper.Instance.StartCoroutine(ResumeOnRelease(_mouse));
        _mouse = new ControllerMapSuspension(ControllerType.Mouse);
    }

    private static IEnumerator ResumeOnRelease(ControllerMapSuspension mouse)
    {
        while (Input.GetMouseButton(0))
        {
            mouse.KeepSuspended();
            yield return null;
        }

        mouse.Resume();
    }

    /// <summary>
    /// Screen to map through the icon layer, the same space the game places unit icons in and this mod draws in: map
    /// units are meters times <c>mapDisplayFactor</c>. The unit is the nearest unit icon within reach.
    /// </summary>
    private static MapPointer Pointer(DynamicMap map, Vector2 screen)
    {
        var layer = map.iconLayer.transform;
        return new MapPointer(ToMeters(map, layer, screen), NearestUnit(map, layer, screen));
    }

    private static MapPoint? NearestUnit(DynamicMap map, Transform layer, Vector2 screen)
    {
        var pixelsPerIconUnit = layer.lossyScale.x / map.mapImage.transform.localScale.x;
        var nearest = UnitReach * pixelsPerIconUnit * (UnitReach * pixelsPerIconUnit);
        UnitMapIcon? found = null;
        var icons = map.mapIcons;
        for (var i = 0; i < icons.Count; i++)
        {
            if (icons[i] is not UnitMapIcon icon || icon == null || icon.unit == null || !icon.isActiveAndEnabled ||
                icon.iconImage == null || !icon.iconImage.enabled)
            {
                continue;
            }

            var distance = ((Vector2)icon.iconImage.transform.position - screen).sqrMagnitude;
            if (distance < nearest)
            {
                nearest = distance;
                found = icon;
            }
        }

        return found == null ? null : new MapPoint(ToMeters(map, layer, found.iconImage.transform.position), found.unit.persistentID.Id);
    }

    private static FlatVector ToMeters(DynamicMap map, Transform layer, Vector2 screen)
    {
        var local = layer.InverseTransformPoint(new Vector3(screen.x, screen.y, 0f));
        return new FlatVector(local.x, local.y) / map.mapDisplayFactor;
    }
}
