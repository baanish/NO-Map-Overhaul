namespace NoMapOverhaul.Sharing;

internal static class SteamConnectionOwnership
{
    // A peer can have both a gameplay connection and a Messages connection. Only handles establish ownership.
    public static bool ClientOwns(uint connection, uint callbackConnection) =>
        connection != 0 && connection == callbackConnection;

    public static bool ServerOwns(uint listenSocket, uint callbackListenSocket, uint callbackConnection,
        bool knownConnection, bool terminal) =>
        callbackConnection != 0 &&
        ((listenSocket != 0 && listenSocket == callbackListenSocket) || (terminal && knownConnection));
}
