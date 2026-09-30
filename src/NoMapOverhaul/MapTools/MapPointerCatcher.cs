using UnityEngine;
using UnityEngine.EventSystems;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// A transparent image over the full map while a tool is active. It sits above the game's unit and airbase icons, so
/// Unity's event system hands it the click the icons would get (<c>MapIcon.OnPointerClick</c> selects a unit), and
/// below the tool menu, so the menu still works. It only records what happened; <see cref="MapToolInput"/> acts on it
/// in the plugin's guarded update, since a throw in here would land in Unity's event system.
/// </summary>
internal sealed class MapPointerCatcher : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    private bool _pressed;
    private bool _rightPressed;

    /// <summary>The cursor is over the map, not over the menu or another panel on top of it.</summary>
    public bool Hovered { get; private set; }

    /// <summary>True once per left press on the map.</summary>
    public bool TakePress()
    {
        var pressed = _pressed;
        _pressed = false;
        return pressed;
    }

    /// <summary>True once per right press on the map. One on the menu never gets here, so the menu ignores right clicks.</summary>
    public bool TakeRightPress()
    {
        var pressed = _rightPressed;
        _rightPressed = false;
        return pressed;
    }

    void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
    {
        _pressed |= eventData.button == PointerEventData.InputButton.Left;
        _rightPressed |= eventData.button == PointerEventData.InputButton.Right;
    }

    void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData) => Hovered = true;

    void IPointerExitHandler.OnPointerExit(PointerEventData eventData) => Hovered = false;

    /// <summary>Hidden with the menu closed: no exit event arrives, and a stale press must not fire when it reopens.</summary>
    private void OnDisable()
    {
        Hovered = false;
        _pressed = false;
        _rightPressed = false;
    }
}
