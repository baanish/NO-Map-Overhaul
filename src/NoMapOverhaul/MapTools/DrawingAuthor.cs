using System;
using NoMapOverhaul.Sharing;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Who drew the teammate's drawing under the cursor, for the label beside the cursor, so nobody can pass a drawing off as
/// another teammate's. The name comes from the game's roster for the drawing's owner; the player's own drawings name no
/// one. It's looked up only when the drawing under the cursor changes.
/// </summary>
public sealed class DrawingAuthor
{
    /// <summary>
    /// For a received drawing whose owner isn't on the roster as one player with a name: one who has just left, whose
    /// drawings go on the next sharing tick, or whom Steam hasn't named yet. It still says the drawing isn't the player's.
    /// </summary>
    public const string UnknownName = "Teammate";

    private MapShape? _shape;

    /// <summary>The owner's name while the cursor is on a teammate's drawing, else empty. The same string until the drawing changes.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <param name="nameOf">The owner's name on the roster, or null when the roster doesn't have one.</param>
    public void Hover(MapShape? shape, ReceivedDrawings received, Func<ulong, string?>? nameOf)
    {
        if (ReferenceEquals(shape, _shape))
        {
            return;
        }

        _shape = shape;
        Name = shape == null || !received.TryFindOwner(shape, out var owner, out _) ? string.Empty
            : nameOf?.Invoke(owner) is { Length: > 0 } name ? name : UnknownName;
    }
}
