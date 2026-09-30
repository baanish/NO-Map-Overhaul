using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.Tracking;

/// <summary>What the player's side knows about a unit this frame, in the game's terms (<c>FactionHQ</c>).</summary>
public enum TrackState
{
    /// <summary>Friendly, or spotted within the game's 4 s window (<c>FactionHQ.IsTargetBeingTracked</c>): where it is now.</summary>
    Live,

    /// <summary>Tracked once but not spotted since: the game keeps its icon at the last known position.</summary>
    Stale,

    /// <summary>Never tracked, destroyed, or gone: the game shows no icon.</summary>
    Unknown,
}

/// <summary>
/// The last position the player's side knew for each unit, so nothing drawn on a unit moves once its track goes stale
/// or is lost. Only a live track moves a unit; otherwise it stays where it was last known, and the caller says it's
/// lost. Positions are global meters (X east, Y up, Z north).
/// </summary>
public sealed class KnownPositions
{
    private readonly Dictionary<uint, Vector3> _last = new();

    /// <summary>
    /// The position to draw <paramref name="unitId"/> at: <paramref name="reported"/> while the track is live, else the
    /// one remembered from the last live frame. A stale track seen for the first time takes the game's last known
    /// position. False when the unit was never known, and <paramref name="position"/> is then meaningless.
    /// </summary>
    public bool TryResolve(uint unitId, TrackState state, Vector3 reported, out Vector3 position)
    {
        if (state == TrackState.Live || (state == TrackState.Stale && !_last.ContainsKey(unitId)))
        {
            _last[unitId] = reported;
        }

        return _last.TryGetValue(unitId, out position);
    }

    /// <summary>A new mission reuses unit ids.</summary>
    public void Forget() => _last.Clear();
}
