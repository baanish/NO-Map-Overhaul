using System;
using Mirage;
using NuclearOption.Networking;

namespace NoMapOverhaul.Sharing;

/// <summary>One received message, valid until <see cref="IDrawingTransport.ReleaseReceived"/>.</summary>
internal readonly struct DrawingMessage
{
    public DrawingMessage(ulong sender, int channel, IntPtr data, int size)
    {
        Present = true;
        Sender = sender;
        Channel = channel;
        Data = data;
        Size = size;
    }

    /// <summary>False for a slot the transport left empty; a present message may still carry no data, which is rejected.</summary>
    public bool Present { get; }

    public ulong Sender { get; }
    public int Channel { get; }
    public IntPtr Data { get; }
    public int Size { get; }
}

/// <summary>
/// How <see cref="SteamDrawingSharing"/> reaches teammates. Players are known by a 64-bit ID: their Steam ID on
/// <see cref="SteamDrawingTransport"/>, the shipped one. A test build swaps in a loopback transport so several game
/// instances on one PC can share as separate players.
/// </summary>
internal interface IDrawingTransport
{
    /// <summary>Why sharing can't run on this transport right now, or null when it can.</summary>
    string? Unavailable(NetworkManagerNuclearOption manager);

    /// <summary>The local player's ID and the server's, checked against the game's connection, or false with a reason.</summary>
    bool TryIdentify(NetworkManagerNuclearOption manager, Player local, out ulong server, out string reason);

    /// <summary>The player's ID, or 0 when it can't take part (no individual account).</summary>
    ulong IdOf(Player player);

    /// <summary>Set-up worth doing before the first teammate, once; false when it couldn't run yet and should be tried again.</summary>
    bool Prepare();

    /// <summary>Fills <paramref name="into"/> with up to <paramref name="max"/> messages on <paramref name="channel"/>.</summary>
    int Receive(int channel, DrawingMessage[] into, int max);

    /// <summary>Frees the last <see cref="Receive"/>'s messages.</summary>
    void ReleaseReceived();

    /// <summary>Queues a reliable message; the result text is for the network log.</summary>
    bool Send(ulong id, int channel, byte[] bytes, out string result);

    /// <summary>The connection's state after a failed send, for the network log.</summary>
    string ConnectionState(ulong id);

    /// <summary>The transport's own status of the session with a teammate, for the performance log; false when it has none to give.</summary>
    bool TryGetSessionStatus(ulong id, out DrawingSessionStatus status);

    bool Accept(ulong id);

    void Close(ulong id, int channel);

    /// <summary>Starts reporting teammates asking for a session and sessions that failed, with the state text for the log.</summary>
    void Listen(Action<ulong> sessionRequested, Action<ulong, string> sessionFailed);

    void StopListening();

    bool Listening { get; }
}
