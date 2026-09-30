using System.Collections.Generic;
using Rewired;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// Switches the local player's Rewired maps for one kind of controller off, and back on. The game's own chat box
/// does the same to keep typing from flying the plane (<c>ChatBox.OnEnable</c>). Only maps this switched off are
/// switched back on, so it never re-enables one the game turned off.
/// </summary>
internal sealed class ControllerMapSuspension
{
    private readonly ControllerType _type;
    private readonly List<ControllerMap> _maps = new();
    private readonly List<ControllerMap> _suspended = new();

    public ControllerMapSuspension(ControllerType type) => _type = type;

    public bool IsSuspended { get; private set; }

    public void Suspend()
    {
        if (IsSuspended || !ReInput.isReady || ReInput.players.GetPlayer(0) is not { } player)
        {
            return;
        }

        IsSuspended = true;
        _maps.Clear();
        player.controllers.maps.GetAllMaps(_type, _maps);
        foreach (var map in _maps)
        {
            if (map.enabled)
            {
                map.enabled = false;
                _suspended.Add(map);
            }
        }

        _maps.Clear();
    }

    /// <summary>Nothing to restore once Rewired itself has shut down, as the game quits.</summary>
    public void Resume()
    {
        if (!IsSuspended)
        {
            return;
        }

        IsSuspended = false;
        if (ReInput.isReady)
        {
            foreach (var map in _suspended)
            {
                map.enabled = true;
            }
        }

        _suspended.Clear();
    }
}
