using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Mirage.SteamworksSocket;
using Steamworks;

namespace NoMapOverhaul.Sharing;

internal static class SteamConnectionOwnershipPatch
{
    // Messages sessions can outlive the plugin. A separate owner keeps this guard installed during disposal.
    private const string HarmonyOwner = Plugin.PluginGuid + ".steam-connection-ownership";
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    public static bool IsApplied { get; private set; }
    private static ManualLogSource? _logger;

    public static bool Apply(ManualLogSource logger)
    {
        if (IsApplied) return true;
        _logger = logger;
        Harmony? harmony = null;
        try
        {
            var serverHandler = CallbackHandler(typeof(Server));
            var clientHandler = CallbackHandler(typeof(Client));
            RequireField(typeof(Server), "listenSocket", typeof(HSteamListenSocket));
            RequireField(typeof(Server), "connections", typeof(Dictionary<HSteamNetConnection, SteamConnection>));
            RequireField(typeof(Client), "connection", typeof(SteamConnection));
            harmony = new Harmony(HarmonyOwner);
            harmony.Patch(serverHandler, prefix: new HarmonyMethod(typeof(SteamConnectionOwnershipPatch), nameof(ServerOwnsCallback)));
            harmony.Patch(clientHandler, prefix: new HarmonyMethod(typeof(SteamConnectionOwnershipPatch), nameof(ClientOwnsCallback)));
            IsApplied = true;
            logger.LogInfo("Steam gameplay callbacks now require their transport's own connection or listen socket.");
            return true;
        }
        catch (Exception exception)
        {
            harmony?.UnpatchSelf();
            IsApplied = false;
            logger.LogError($"Steam callback ownership protection unavailable; drawing sharing is disabled. {exception}");
            return false;
        }
    }

    private static MethodInfo CallbackHandler(Type type)
    {
        var method = type.GetMethod("OnConnectionStatusChanged", InstancePrivate, null,
            new[] { typeof(SteamNetConnectionStatusChangedCallback_t) }, null);
        if (method == null || method.ReturnType != typeof(void) || method.IsGenericMethod)
            throw new MissingMethodException(type.FullName, "OnConnectionStatusChanged");
        return method;
    }

    private static void RequireField(Type type, string name, Type fieldType)
    {
        if (type.GetField(name, InstancePrivate)?.FieldType != fieldType)
            throw new MissingFieldException(type.FullName, name);
    }

    private static bool ServerOwnsCallback(SteamNetConnectionStatusChangedCallback_t __0,
        HSteamListenSocket ___listenSocket, Dictionary<HSteamNetConnection, SteamConnection> ___connections)
    {
        var terminal = __0.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
            __0.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;
        return SteamConnectionOwnership.ServerOwns(___listenSocket.m_HSteamListenSocket,
            __0.m_info.m_hListenSocket.m_HSteamListenSocket, __0.m_hConn.m_HSteamNetConnection,
            terminal && ___connections.ContainsKey(__0.m_hConn), terminal);
    }

    private static bool ClientOwnsCallback(SteamNetConnectionStatusChangedCallback_t __0, SteamConnection? ___connection)
    {
        var owned = ___connection?.ConnId.m_HSteamNetConnection ?? 0;
        var owns = SteamConnectionOwnership.ClientOwns(owned, __0.m_hConn.m_HSteamNetConnection);
        if (!owns && IsTerminal(__0.m_info.m_eState))
        {
            // Normally a drawing session closing. Logged with both handles because dropping a close meant for the game's
            // own connection would leave the client hanging until the server timed it out.
            _logger?.LogInfo($"Steam {__0.m_info.m_eState} for connection {__0.m_hConn.m_HSteamNetConnection} kept from the game; " +
                $"its connection is {owned}.");
        }
        return owns;
    }

    private static bool IsTerminal(ESteamNetworkingConnectionState state) =>
        state is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
            or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;
}
