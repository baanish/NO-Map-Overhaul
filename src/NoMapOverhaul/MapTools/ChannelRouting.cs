using System.Collections.Generic;

namespace NoMapOverhaul.MapTools;

/// <summary>Broadcast channels allow collaboration; receive-only channels only subscribe to drawings.</summary>
public static class ChannelRouting
{
    public static bool CanSend(string channel, IReadOnlyList<string> localBroadcast,
        IReadOnlyList<string> remoteBroadcast, IReadOnlyList<string> remoteReceiveOnly) =>
        Contains(localBroadcast, channel) && CanReceive(channel, remoteBroadcast, remoteReceiveOnly);

    public static bool CanReceive(string channel, IReadOnlyList<string> broadcast,
        IReadOnlyList<string> receiveOnly) => Contains(broadcast, channel) || Contains(receiveOnly, channel);

    public static bool CanCollaborate(string channel, IReadOnlyList<string> localBroadcast,
        IReadOnlyList<string> remoteBroadcast) =>
        Contains(localBroadcast, channel) && Contains(remoteBroadcast, channel);

    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++) if (names[i] == name) return true;
        return false;
    }
}
