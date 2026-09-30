using UnityEngine;
using UnityEngine.EventSystems;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Records whether the cursor is over a rail cell, for the hover name tag and the brighter icon. A Button tracks the
/// same thing but keeps it protected. Like <see cref="MapPointerCatcher"/>, it only records; the menu reads it.
/// </summary>
internal sealed class MenuHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public bool Hovered { get; private set; }

    void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData) => Hovered = true;

    void IPointerExitHandler.OnPointerExit(PointerEventData eventData) => Hovered = false;

    /// <summary>Hidden with the rail closed or the map closed: no exit event arrives.</summary>
    private void OnDisable() => Hovered = false;
}
