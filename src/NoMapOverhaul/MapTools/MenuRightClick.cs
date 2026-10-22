using UnityEngine;
using UnityEngine.EventSystems;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Hears a right-click on a menu cell, which its <c>Button</c> ignores. Like <see cref="MenuHover"/>, it only records:
/// <see cref="Clicked"/> stores a <see cref="MenuCommand"/> for the host to apply in the plugin's guarded update.
/// </summary>
internal sealed class MenuRightClick : MonoBehaviour, IPointerClickHandler
{
    public System.Action? Clicked { get; set; }

    void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            Clicked?.Invoke();
        }
    }
}
