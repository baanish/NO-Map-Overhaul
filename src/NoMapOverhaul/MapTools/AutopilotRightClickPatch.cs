using System;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// The mod's one Harmony patch, and it patches NOAutopilot, not the game. NOAutopilot adds an autopilot waypoint on
/// every right-click on the full map, in its postfix on <c>DynamicMap.MapControls</c>
/// (<c>NOAutopilot.Core.Map.MapInteractionPatch.Postfix</c>), and has no setting to stop it. While the Tools menu is
/// open the map tools take right-clicks (<see cref="RightClickRule"/>), so without this one click would both delete a
/// drawing and reroute the autopilot. This prefix skips NOAutopilot's handler only while
/// <see cref="MapToolHost.TakesRightClick"/>: with the menu closed, or a unit selected that the game would order,
/// NOAutopilot runs as before.
/// <para>
/// NOAutopilot is optional, so nothing references it at compile time: the handler is found by name in the assembly of
/// the plugin registered as <see cref="AutopilotGuid"/>, which the plugin's soft dependency loads first. The patch comes
/// off in <see cref="Remove"/> when the plugin is destroyed or disables itself after an error. With the master switch
/// off the host is reset, so its menu is closed and every click goes through. A throw lets the click through too, since
/// the prefix runs inside the game's map update.
/// </para>
/// </summary>
internal static class AutopilotRightClickPatch
{
    public const string AutopilotGuid = "com.qwerty1423.NOAutopilot";
    private const string HandlerType = "NOAutopilot.Core.Map.MapInteractionPatch";
    private const string HandlerMethod = "Postfix";

    private static Harmony? _harmony;
    private static MapToolHost? _host;
    private static ManualLogSource? _logger;

    public static void Apply(MapToolHost host, ManualLogSource logger)
    {
        if (!Chainloader.PluginInfos.TryGetValue(AutopilotGuid, out var autopilot) || autopilot.Instance == null)
        {
            logger.LogInfo("NOAutopilot isn't loaded: no autopilot right-clicks to hold back.");
            return;
        }

        var type = autopilot.Instance.GetType().Assembly.GetType(HandlerType);
        var handler = type == null ? null : AccessTools.Method(type, HandlerMethod);
        if (handler == null)
        {
            logger.LogInfo($"NOAutopilot {autopilot.Metadata.Version} has no {HandlerType}.{HandlerMethod}: " +
                           "a right-click with the Tools menu open may also add an autopilot waypoint.");
            return;
        }

        _host = host;
        _logger = logger;
        _harmony = new Harmony(Plugin.PluginGuid);
        _harmony.Patch(handler, prefix: new HarmonyMethod(typeof(AutopilotRightClickPatch), nameof(SkipWhileToolsTakeClick)));
        logger.LogInfo("NOAutopilot's right-click waypoints are held back while the Tools menu is open.");
    }

    public static void Remove()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        _host = null;
    }

    /// <summary>False skips NOAutopilot's handler for this frame.</summary>
    private static bool SkipWhileToolsTakeClick()
    {
        try
        {
            var map = SceneSingleton<DynamicMap>.i;
            return !Input.GetMouseButtonDown(1) || _host == null || map == null || !_host.TakesRightClick(map);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"NOAutopilot keeps this right-click after an error. {exception}");
            return true;
        }
    }
}
