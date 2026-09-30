namespace NoMapOverhaul.MapTools;

/// <summary>What a right-click on the full map does.</summary>
public enum RightClickAction
{
    /// <summary>The tools leave it alone: the game's move order, and NOAutopilot's waypoint.</summary>
    Game,

    /// <summary>The tools take it and do nothing, such as on empty map.</summary>
    Nothing,

    /// <summary>Drops the active tool's half-drawn work.</summary>
    Cancel,

    /// <summary>Deletes the drawing under the cursor, as one undo step.</summary>
    Delete,
}

/// <summary>
/// Who gets a right-click on the full map. With the Tools menu closed it's never the tools'. A selected friendly unit
/// the game would order keeps its move order, since few players have one selected unless they mean to order it. Otherwise,
/// with the menu open, the tools take it, and NOAutopilot doesn't get it.
/// </summary>
public static class RightClickRule
{
    /// <summary>The tools take the click, so NOAutopilot must not act on it.</summary>
    public static bool ToolsTake(bool menuOpen, bool unitOrdered) => menuOpen && !unitOrdered;

    /// <summary>A click on the map mid-draw cancels, even over a drawing; otherwise it deletes the drawing under it.</summary>
    public static RightClickAction Decide(bool menuOpen, bool unitOrdered, bool inProgress, bool onDrawing) =>
        !ToolsTake(menuOpen, unitOrdered) ? RightClickAction.Game
        : inProgress ? RightClickAction.Cancel
        : onDrawing ? RightClickAction.Delete
        : RightClickAction.Nothing;
}
