using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace BaanishUiImprovements.MapTools.Waypoint;

/// <summary>
/// NOAutopilot's flight plan, read by reflection when that mod is loaded: <c>APData.NavQueue</c>, the waypoints still
/// to fly, next first, in the game's global meters (X east, Y up, Z north; Y is 0, since NOAutopilot takes them from the
/// map cursor). NOAutopilot plans the route with right clicks and advances it itself, so this only reads it.
/// If its internals aren't as expected, that's logged once and NOAutopilot counts as absent.
/// </summary>
internal sealed class NoAutopilotRoute
{
    /// <summary>NOAutopilot's <c>BepInPlugin</c> GUID.</summary>
    public const string PluginGuid = "com.qwerty1423.NOAutopilot";

    private const string DataTypeName = "NOAutopilot.Core.APData";
    private const string QueueFieldName = "NavQueue";

    private FieldInfo? _queueField;
    private List<Vector3>? _queue;
    private bool _searched;

    /// <summary>
    /// The queue, or null while NOAutopilot isn't usable. The first call must come after every plugin has loaded, so it
    /// searches on first use rather than in Awake, when NOAutopilot may not have loaded yet.
    /// </summary>
    public IReadOnlyList<Vector3>? Queue
    {
        get
        {
            if (!_searched)
            {
                _searched = true;
                Search();
            }

            return _queue;
        }
    }

    /// <summary>A new mission: reads the field again, in case a NOAutopilot version replaces the list rather than clearing it.</summary>
    public void Reload()
    {
        if (_queueField != null)
        {
            _queue = ReadQueue(_queueField);
        }
    }

    private void Search()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        {
            return; // not installed
        }

        var instance = info.Instance;
        var field = instance == null ? null : instance.GetType().Assembly.GetType(DataTypeName)?.GetField(QueueFieldName, BindingFlags.Public | BindingFlags.Static);
        var queue = field == null ? null : ReadQueue(field);
        var log = Logger.CreateLogSource(Plugin.PluginName); // once per game session, and only with NOAutopilot installed
        if (queue == null)
        {
            log.LogWarning($"NOAutopilot {info.Metadata.Version} is loaded, but {DataTypeName}.{QueueFieldName} isn't the waypoint list this mod knows: the Waypoint tool keeps its own route.");
        }
        else
        {
            _queueField = field;
            _queue = queue;
            log.LogInfo($"NOAutopilot {info.Metadata.Version} found: waypoint labels follow its route.");
        }
    }

    private static List<Vector3>? ReadQueue(FieldInfo field) => field.GetValue(null) as List<Vector3>;
}
