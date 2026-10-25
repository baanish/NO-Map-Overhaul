using System.Collections.Generic;

namespace NoMapOverhaul.MapTools;

/// <summary>Channel bindings treat either Shift key, or both together, as the same modifier. All other keys match exactly.</summary>
internal static class ChannelKeyModifiers
{
    public static bool Match<TKey>(IEnumerable<TKey> required, IEnumerable<TKey> held, TKey leftShift, TKey rightShift)
    {
        var wanted = new HashSet<TKey>(required);
        var pressed = new HashSet<TKey>(held);
        if (wanted.Remove(rightShift)) wanted.Add(leftShift);
        if (pressed.Remove(rightShift)) pressed.Add(leftShift);
        return wanted.SetEquals(pressed);
    }
}
