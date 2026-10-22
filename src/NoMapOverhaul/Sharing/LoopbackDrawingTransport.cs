#if SHARING_LOOPBACK
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace NoMapOverhaul.Sharing;

/// <summary>
/// Sharing between game instances on one PC, for tests only: built into the SHARING_LOOPBACK build
/// (<c>build/Build-Loopback.ps1</c>), never the release. Steam Messages need a Steam account per player, so a framework
/// group run of several instances can't share over Steam; here each player is known by its network ID, and each
/// instance takes a UDP port on 127.0.0.1 from its own. Messages over a datagram's size go in pieces. Nothing here is
/// authenticated: anything on the PC could send drawings.
/// </summary>
internal sealed class LoopbackDrawingTransport : IDrawingTransport
{
    private const ulong IdBase = 0x7E00_0000_0000_0000UL;
    private const int BasePort = 41000;
    private const int Ports = 10000;
    private const int PieceBytes = 60000;
    private const int HeaderBytes = 8 + 4 + 4 + 2 + 2;

    private readonly ManualLogSource _log;
    private readonly byte[] _datagram = new byte[65536];
    private readonly byte[] _outgoing = new byte[HeaderBytes + PieceBytes];
    private readonly Dictionary<ulong, IPEndPoint> _endpoints = new();
    private EndPoint _from = new IPEndPoint(IPAddress.Any, 0);
    private readonly byte[][] _slots = new byte[DrawingReceiveBudget.MaxMessagesPerTick][];
    private readonly GCHandle[] _pins = new GCHandle[DrawingReceiveBudget.MaxMessagesPerTick];
    private readonly Dictionary<(ulong Sender, uint Message), byte[]?[]> _partial = new();
    private Socket? _socket;
    private ulong _localId;
    private uint _nextMessage;

    public LoopbackDrawingTransport(ManualLogSource log)
    {
        _log = log;
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new byte[DrawingProtocol.MaxPacketBytes];
            _pins[i] = GCHandle.Alloc(_slots[i], GCHandleType.Pinned);
        }

        log.LogWarning("Drawing sharing runs over the loopback test transport: this build is for tests on one PC only.");
    }

    public bool Listening { get; private set; }

    public string? Unavailable(NetworkManagerNuclearOption manager) => null;

    public bool TryIdentify(NetworkManagerNuclearOption manager, Player local, out ulong server, out string reason)
    {
        server = 1; // one loopback group per PC
        reason = "local player has no network ID yet";
        var id = IdOf(local);
        if (id == 0) return false;
        if (id != _localId || _socket == null) Bind(id);
        reason = "active loopback team session";
        return _socket != null;
    }

    public ulong IdOf(Player player) => player.NetId == 0 ? 0 : IdBase | player.NetId;

    private static int PortOf(ulong id) => BasePort + (int)((id & 0xFFFF_FFFFUL) % Ports);

    private void Bind(ulong id)
    {
        _socket?.Close();
        _socket = null;
        _localId = id;
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveBufferSize = 8 << 20;
            // Windows reports an earlier datagram to a closed port as a reset on the next receive; ignore those.
            try { socket.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception) { }
            socket.Bind(new IPEndPoint(IPAddress.Loopback, PortOf(id)));
            socket.Blocking = false;
            _socket = socket;
            _log.LogInfo($"Loopback sharing listens on port {PortOf(id)}.");
        }
        catch (Exception error)
        {
            _log.LogWarning($"Loopback sharing couldn't take port {PortOf(id)}: {error.Message}");
        }
    }

    public bool Prepare() => true;

    public int Receive(int channel, DrawingMessage[] into, int max)
    {
        var count = 0;
        if (_socket == null) return 0;
        // Pieces and bad datagrams don't fill a slot, so they get their own bound on the work per call.
        for (var datagrams = 0; count < max && datagrams < 8 * max && _socket.Available > 0; datagrams++)
        {
            int length;
            try { length = _socket.ReceiveFrom(_datagram, ref _from); }
            catch (SocketException) { break; }
            if (length < HeaderBytes) continue;
            var sender = BitConverter.ToUInt64(_datagram, 0);
            var messageChannel = BitConverter.ToInt32(_datagram, 8);
            var message = BitConverter.ToUInt32(_datagram, 12);
            var index = BitConverter.ToUInt16(_datagram, 16);
            var pieces = BitConverter.ToUInt16(_datagram, 18);
            if (messageChannel != channel || pieces == 0 || index >= pieces) continue;
            if (pieces == 1)
            {
                // Most messages fit one datagram: straight into the slot, with nothing allocated, as Steam hands them over.
                if (length - HeaderBytes > DrawingProtocol.MaxPacketBytes) continue;
                Buffer.BlockCopy(_datagram, HeaderBytes, _slots[count], 0, length - HeaderBytes);
                into[count] = new DrawingMessage(sender, channel, _pins[count].AddrOfPinnedObject(), length - HeaderBytes);
                count++;
                continue;
            }

            var piece = new byte[length - HeaderBytes];
            Buffer.BlockCopy(_datagram, HeaderBytes, piece, 0, piece.Length);
            var key = (sender, message);
            if (!_partial.TryGetValue(key, out var parts))
            {
                if (_partial.Count > 64) _partial.Clear();
                parts = new byte[pieces][];
                _partial.Add(key, parts);
            }

            if (parts.Length != pieces) continue;
            parts[index] = piece;
            var size = 0;
            var whole = true;
            foreach (var part in parts)
            {
                if (part == null) { whole = false; break; }
                size += part.Length;
            }

            if (!whole) continue;
            _partial.Remove(key);
            if (size > DrawingProtocol.MaxPacketBytes) continue;
            var slot = _slots[count];
            var at = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part!, 0, slot, at, part!.Length);
                at += part.Length;
            }

            into[count] = new DrawingMessage(sender, channel, _pins[count].AddrOfPinnedObject(), size);
            count++;
        }

        return count;
    }

    public void ReleaseReceived() { }

    public bool Send(ulong id, int channel, byte[] bytes, out string result)
    {
        if (_socket == null)
        {
            result = "not bound";
            return false;
        }

        var pieces = Math.Max(1, (bytes.Length + PieceBytes - 1) / PieceBytes);
        var message = ++_nextMessage;
        if (!_endpoints.TryGetValue(id, out var to))
        {
            to = new IPEndPoint(IPAddress.Loopback, PortOf(id));
            _endpoints.Add(id, to);
        }

        try
        {
            var datagram = _outgoing;
            for (var index = 0; index < pieces; index++)
            {
                var offset = index * PieceBytes;
                var length = Math.Min(PieceBytes, bytes.Length - offset);
                BitConverter.TryWriteBytes(new Span<byte>(datagram, 0, 8), _localId);
                BitConverter.TryWriteBytes(new Span<byte>(datagram, 8, 4), channel);
                BitConverter.TryWriteBytes(new Span<byte>(datagram, 12, 4), message);
                BitConverter.TryWriteBytes(new Span<byte>(datagram, 16, 2), (ushort)index);
                BitConverter.TryWriteBytes(new Span<byte>(datagram, 18, 2), (ushort)pieces);
                Buffer.BlockCopy(bytes, offset, datagram, HeaderBytes, length);
                _socket.SendTo(datagram, 0, HeaderBytes + length, SocketFlags.None, to);
            }

            result = "OK";
            return true;
        }
        catch (SocketException error)
        {
            result = error.SocketErrorCode.ToString();
            return false;
        }
    }

    public string ConnectionState(ulong id) => $"loopback port {PortOf(id)}";

    /// <summary>UDP on one PC keeps no session to report.</summary>
    public bool TryGetSessionStatus(ulong id, out DrawingSessionStatus status)
    {
        status = default;
        return false;
    }

    public bool Accept(ulong id) => true;

    public void Close(ulong id, int channel) { }

    public void Listen(Action<ulong> sessionRequested, Action<ulong, string> sessionFailed) => Listening = true;

    public void StopListening() => Listening = false;
}
#endif
