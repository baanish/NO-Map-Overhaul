using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.BearingRange;
using NoMapOverhaul.MapTools.Circle;
using NoMapOverhaul.MapTools.Pen;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.MapTools.Waypoint;

namespace NoMapOverhaul.Sharing;

public enum DrawingPacketKind : byte { Hello = 1, Acknowledgement = 2, Snapshot = 3, DeleteRequest = 4 }

public readonly struct SharedMissionEpoch : IEquatable<SharedMissionEpoch>
{
    public SharedMissionEpoch(uint networkId, double startTime, uint factionId)
    { NetworkId = networkId; StartTime = startTime; FactionId = factionId; }
    public uint NetworkId { get; }
    public double StartTime { get; }
    public uint FactionId { get; }
    public bool Equals(SharedMissionEpoch other) => NetworkId == other.NetworkId && StartTime.Equals(other.StartTime) && FactionId == other.FactionId;
    public override bool Equals(object? obj) => obj is SharedMissionEpoch other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(NetworkId, StartTime, FactionId);
}

public sealed class DrawingPacket
{
    public DrawingPacket(DrawingPacketKind kind, SharedMissionEpoch epoch, Guid senderSession, Guid recipientSession,
        uint sequence, string[] channels, MapShape[] shapes, string[]? broadcastChannels = null,
        int targetShapeId = 0, string targetChannel = "")
    {
        Kind = kind; Epoch = epoch; SenderSession = senderSession; RecipientSession = recipientSession;
        Sequence = sequence; Channels = channels; Shapes = shapes;
        BroadcastChannels = broadcastChannels ?? Array.Empty<string>();
        TargetShapeId = targetShapeId; TargetChannel = targetChannel;
    }

    public DrawingPacketKind Kind { get; }
    public SharedMissionEpoch Epoch { get; }
    public Guid SenderSession { get; }
    public Guid RecipientSession { get; }
    public uint Sequence { get; }
    /// <summary>Additional read-only subscriptions, independently bounded from broadcast channels.</summary>
    public string[] Channels { get; }
    public MapShape[] Shapes { get; }
    public string[] BroadcastChannels { get; }
    public int TargetShapeId { get; }
    public string TargetChannel { get; }
    internal Dictionary<int, DrawingProtocol.ShapeRecord>? SnapshotRecords { get; set; }
}

/// <summary>Bounded, little-endian drawing data. Invalid frames produce no partial packet.</summary>
public static class DrawingProtocol
{
    public const uint Magic = 0x444F4D4E;
    public const byte Version = 6;
    public const int MaxPacketBytes = 65536;
    /// <summary>Per owner snapshot. The local drawing and pen-point settings top out at 100 and 2,500, so they fit.</summary>
    public const int MaxShapes = 100;
    public const int MaxGeometryPoints = 3000;
    /// <summary>Waypoint routes per owner snapshot: each waypoint gets a label plate, the costliest thing drawn.</summary>
    public const int MaxRoutes = 5;
    public const float MaxCoordinate = 2000000f;
    public const float MaxElevation = 100000f;
    internal const int HeaderBytes = SequenceOffset + 4;
    /// <summary>Where the header's sequence starts: after magic, version, kind, epoch and both session nonces.</summary>
    public const int SequenceOffset = 4 + 1 + 1 + 4 + 8 + 4 + 16 + 16;
    // Canonical channel names are ASCII, so each character occupies one UTF-8 byte.
    private const int MaxChannelBytes = 2 + DrawingChannels.MaxNameLength;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>
    /// Encodes the longest run of a snapshot's shapes, oldest first, that fits the wire limits, so one drawing over a
    /// limit doesn't take every other drawing off teammates' maps. Returns how many newer shapes were left out.
    /// </summary>
    public static int EncodeFitting(DrawingPacket snapshot, out DrawingPacket sent, out byte[] bytes)
    {
        sent = snapshot;
        if (TryEncode(snapshot, out bytes)) return 0;
        int fits = 0, fails = snapshot.Shapes.Length; // an empty snapshot always encodes
        while (fails - fits > 1)
        {
            var middle = (fits + fails) / 2;
            if (TryEncodeLength(WithShapes(snapshot, middle), out _)) fits = middle; else fails = middle;
        }
        sent = WithShapes(snapshot, fits);
        TryEncode(sent, out bytes);
        return snapshot.Shapes.Length - fits;
    }

    private static DrawingPacket WithShapes(DrawingPacket packet, int count)
    {
        var shapes = new MapShape[count];
        Array.Copy(packet.Shapes, shapes, count);
        return new DrawingPacket(packet.Kind, packet.Epoch, packet.SenderSession, packet.RecipientSession, packet.Sequence,
            packet.Channels, shapes, packet.BroadcastChannels, packet.TargetShapeId, packet.TargetChannel);
    }

    /// <summary>
    /// A frame from this mod at a protocol version other than ours: a teammate on another release, told apart from
    /// malformed data so it isn't quarantined.
    /// </summary>
    public static bool IsOtherVersion(byte[] bytes) =>
        bytes.Length > 4 && BitConverter.ToUInt32(bytes, 0) == Magic && bytes[4] != Version;

    /// <summary>Where the header's sender session starts: after magic, version, kind and epoch. The recipient's follows it.</summary>
    private const int SenderSessionOffset = 4 + 1 + 1 + 4 + 8 + 4;

    /// <summary>
    /// Readdresses an encoded packet to another teammate: the session nonces and the sequence are the only parts of a
    /// packet that differ between teammates who get the same drawings, so one encoding serves them all.
    /// </summary>
    public static void Readdress(byte[] bytes, Guid senderSession, Guid recipientSession, uint sequence)
    {
        senderSession.TryWriteBytes(new Span<byte>(bytes, SenderSessionOffset, 16));
        recipientSession.TryWriteBytes(new Span<byte>(bytes, SenderSessionOffset + 16, 16));
        BitConverter.TryWriteBytes(new Span<byte>(bytes, SequenceOffset, 4), sequence);
    }

    public static bool TryReadHeader(IntPtr data, int size, out byte version, out DrawingPacketKind kind)
    {
        version = 0;
        kind = 0;
        if (data == IntPtr.Zero || size < 6 || (uint)Marshal.ReadInt32(data) != Magic) return false;
        version = Marshal.ReadByte(data, 4);
        kind = (DrawingPacketKind)Marshal.ReadByte(data, 5);
        return true;
    }

    internal static bool TryReadPacketHeader(byte[] bytes, int length, out DrawingPacket? header)
    {
        header = null;
        if (length < HeaderBytes || length > bytes.Length || length > MaxPacketBytes ||
            BitConverter.ToUInt32(bytes, 0) != Magic || bytes[4] != Version) return false;
        var kind = (DrawingPacketKind)bytes[5];
        var epoch = new SharedMissionEpoch(BitConverter.ToUInt32(bytes, 6), BitConverter.ToDouble(bytes, 10),
            BitConverter.ToUInt32(bytes, 18));
        var sender = new Guid(new ReadOnlySpan<byte>(bytes, SenderSessionOffset, 16));
        var recipient = new Guid(new ReadOnlySpan<byte>(bytes, SenderSessionOffset + 16, 16));
        try { CheckHeader(kind, epoch, sender, recipient); }
        catch (Exception error) when (InvalidFrame(error)) { return false; }
        header = new DrawingPacket(kind, epoch, sender, recipient, BitConverter.ToUInt32(bytes, SequenceOffset),
            Array.Empty<string>(), Array.Empty<MapShape>());
        return true;
    }

    /// <summary>
    /// Where every packet is written before its bytes are copied out, kept rather than allocated per packet: a snapshot
    /// may need the whole 64 KB, twice a second per teammate, and a search for the drawings that fit encodes it up to
    /// eight times. Sharing runs on the main thread alone.
    /// </summary>
    private static readonly byte[] EncodeScratch = new byte[MaxPacketBytes];

    public static bool TryEncode(DrawingPacket? packet, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!TryEncodeLength(packet, out var length)) return false;
        bytes = new byte[length];
        Array.Copy(EncodeScratch, bytes, length);
        return true;
    }

    /// <summary>Encodes into <see cref="EncodeScratch"/> and returns how many bytes it took.</summary>
    private static bool TryEncodeLength(DrawingPacket? packet, out int length)
    {
        length = 0;
        if (packet == null || packet.Channels == null || packet.Shapes == null || packet.BroadcastChannels == null) return false;
        try
        {
            CheckHeader(packet.Kind, packet.Epoch, packet.SenderSession, packet.RecipientSession);
            Require(packet.Shapes.Length <= MaxShapes && packet.Channels.Length <= DrawingChannels.MaxChannels &&
                packet.BroadcastChannels.Length <= DrawingChannels.MaxChannels);
            var capacity = packet.Kind switch
            {
                DrawingPacketKind.Snapshot => MaxPacketBytes,
                DrawingPacketKind.DeleteRequest => HeaderBytes + 4 + MaxChannelBytes,
                _ => HeaderBytes + 2 + (packet.Channels.Length + packet.BroadcastChannels.Length) * MaxChannelBytes
            };
            using var stream = new MemoryStream(EncodeScratch, 0, capacity, true, true);
            using var writer = new BinaryWriter(stream, Utf8, true);
            writer.Write(Magic); writer.Write(Version); writer.Write((byte)packet.Kind);
            writer.Write(packet.Epoch.NetworkId); writer.Write(packet.Epoch.StartTime);
            writer.Write(packet.Epoch.FactionId);
            writer.Write(packet.SenderSession.ToByteArray()); writer.Write(packet.RecipientSession.ToByteArray());
            writer.Write(packet.Sequence);
            if (packet.Kind == DrawingPacketKind.Snapshot)
            {
                Require(packet.Channels.Length == 0 && packet.BroadcastChannels.Length == 0 &&
                    packet.TargetShapeId == 0 && string.IsNullOrEmpty(packet.TargetChannel));
                writer.Write((ushort)packet.Shapes.Length);
                var points = 0;
                var routes = 0;
                var ids = new HashSet<int>();
                foreach (var shape in packet.Shapes)
                {
                    Require(shape is not WaypointRoute || ++routes <= MaxRoutes);
                    Require(shape != null && shape.Id > 0 && ids.Add(shape.Id));
                    WriteShape(writer, shape!, ref points);
                }
            }
            else if (packet.Kind == DrawingPacketKind.DeleteRequest)
            {
                Require(packet.Shapes.Length == 0 && packet.Channels.Length == 0 &&
                    packet.BroadcastChannels.Length == 0 && packet.TargetShapeId > 0 &&
                    CanonicalChannel(packet.TargetChannel));
                writer.Write(packet.TargetShapeId);
                WriteText(writer, packet.TargetChannel, DrawingChannels.MaxNameLength);
            }
            else
            {
                Require(packet.Shapes.Length == 0 && packet.TargetShapeId == 0 && string.IsNullOrEmpty(packet.TargetChannel));
                WriteChannels(writer, packet.Channels);
                WriteChannels(writer, packet.BroadcastChannels);
            }
            length = (int)stream.Position;
            return true;
        }
        catch (Exception error) when (InvalidFrame(error)) { return false; }
    }

    public static bool TryDecode(byte[]? bytes, out DrawingPacket? packet) => TryDecode(bytes, bytes?.Length ?? 0, out packet);

    /// <summary>A shape as decoded, and the record bytes it was decoded from.</summary>
    internal readonly struct ShapeRecord
    {
        public ShapeRecord(byte[] bytes, MapShape shape) { Bytes = bytes; Shape = shape; }
        public byte[] Bytes { get; }
        public MapShape Shape { get; }
    }

    /// <summary>
    /// The last snapshot accepted for each session, so a teammate's unchanged drawings are handed back instead of built
    /// again twice a second. Keyed by the recipient session: the nonce this client made for one teammate, which no other
    /// teammate learns, so nobody can put shapes into another teammate's snapshots. Sessions rotate on every pairing, so
    /// the table is emptied whenever it fills.
    /// </summary>
    private static readonly Dictionary<Guid, Dictionary<int, ShapeRecord>> LastSnapshots = new();
    private const int MaxLastSnapshots = 2 * ReceivedDrawings.MaxPeers;

    /// <summary>Lets go of every session's last snapshot, when sharing resets and its sessions end.</summary>
    public static void ForgetSnapshots() => LastSnapshots.Clear();

    internal static void RememberSnapshot(DrawingPacket packet)
    {
        if (packet.SnapshotRecords == null) return;
        if (!LastSnapshots.ContainsKey(packet.RecipientSession) && LastSnapshots.Count >= MaxLastSnapshots)
            LastSnapshots.Clear();
        LastSnapshots[packet.RecipientSession] = packet.SnapshotRecords;
    }

    /// <summary>
    /// Decodes the first <paramref name="length"/> bytes, so a receive buffer can be reused for every message. A
    /// snapshot's unchanged drawings are the shape objects of the last snapshot accepted for the same recipient session,
    /// as long as <see cref="ReceivedDrawings"/> kept none of them, so of two snapshots decoded for one session apply
    /// only the newer.
    /// </summary>
    public static bool TryDecode(byte[]? bytes, int length, out DrawingPacket? packet)
    {
        packet = null;
        if (bytes == null || length < 0 || length > bytes.Length || length > MaxPacketBytes) return false;
        try
        {
            using var stream = new MemoryStream(bytes, 0, length, false, true);
            using var reader = new BinaryReader(stream, Utf8);
            Require(reader.ReadUInt32() == Magic && reader.ReadByte() == Version);
            var kind = (DrawingPacketKind)reader.ReadByte();
            var epoch = new SharedMissionEpoch(reader.ReadUInt32(), reader.ReadDouble(), reader.ReadUInt32());
            var sender = new Guid(ReadSpan(reader, 16));
            var recipient = new Guid(ReadSpan(reader, 16));
            var sequence = reader.ReadUInt32();
            CheckHeader(kind, epoch, sender, recipient);
            var channels = Array.Empty<string>();
            var shapes = Array.Empty<MapShape>();
            var broadcastChannels = Array.Empty<string>();
            var targetShapeId = 0;
            var targetChannel = string.Empty;
            Dictionary<int, ShapeRecord>? records = null;
            if (kind == DrawingPacketKind.Snapshot)
            {
                var count = reader.ReadUInt16();
                Require(count <= MaxShapes);
                shapes = new MapShape[count];
                LastSnapshots.TryGetValue(recipient, out var last);
                records = new Dictionary<int, ShapeRecord>();
                var points = 0;
                var routes = 0;
                for (var i = 0; i < count; i++)
                {
                    var record = ReadRecord(reader, last, ref points);
                    shapes[i] = record.Shape;
                    Require(records.TryAdd(shapes[i].Id, record) && (shapes[i] is not WaypointRoute || ++routes <= MaxRoutes));
                }
            }
            else if (kind == DrawingPacketKind.DeleteRequest)
            {
                targetShapeId = reader.ReadInt32(); Require(targetShapeId > 0);
                targetChannel = ReadText(reader, DrawingChannels.MaxNameLength);
                Require(CanonicalChannel(targetChannel));
            }
            else
            {
                channels = ReadChannels(reader);
                broadcastChannels = ReadChannels(reader);
            }
            Require(stream.Position == stream.Length);
            packet = new DrawingPacket(kind, epoch, sender, recipient, sequence, channels, shapes,
                broadcastChannels, targetShapeId, targetChannel) { SnapshotRecords = records };
            return true;
        }
        catch (Exception error) when (InvalidFrame(error)) { return false; }
    }

    private static void CheckHeader(DrawingPacketKind kind, SharedMissionEpoch epoch, Guid sender, Guid recipient)
    {
        Require(kind is DrawingPacketKind.Hello or DrawingPacketKind.Acknowledgement or DrawingPacketKind.Snapshot or DrawingPacketKind.DeleteRequest);
        Require(epoch.NetworkId != 0 && epoch.FactionId != 0 && epoch.StartTime > 0 && !double.IsInfinity(epoch.StartTime));
        Require(sender != Guid.Empty && (kind == DrawingPacketKind.Hello || recipient != Guid.Empty));
    }

    private static bool CanonicalChannel(string? name) => DrawingChannels.IsCanonicalName(name);

    private static void WriteChannels(BinaryWriter writer, string[] channels)
    {
        writer.Write((byte)channels.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            Require(CanonicalChannel(channel) && names.Add(channel));
            WriteText(writer, channel, DrawingChannels.MaxNameLength);
        }
    }

    private static string[] ReadChannels(BinaryReader reader)
    {
        var count = reader.ReadByte(); Require(count <= DrawingChannels.MaxChannels);
        var channels = new string[count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            channels[i] = ReadText(reader, DrawingChannels.MaxNameLength);
            Require(CanonicalChannel(channels[i]) && names.Add(channels[i]));
        }

        return channels;
    }

    private static void WriteShape(BinaryWriter writer, MapShape shape, ref int points)
    {
        Require(CanonicalChannel(shape.Channel));
        byte kind = shape switch
        {
            BearingRangeShape => 1, CircleShape => 2, PenStroke => 3, TextNote => 4, WaypointRoute => 5,
            _ => throw new InvalidDataException()
        };
        writer.Write(kind); writer.Write(shape.Id); WriteText(writer, shape.Channel, DrawingChannels.MaxNameLength);
        Require(shape.Color.A > 0);
        writer.Write(shape.Color.R); writer.Write(shape.Color.G); writer.Write(shape.Color.B); writer.Write(shape.Color.A);
        switch (shape)
        {
            case BearingRangeShape bearing:
                WritePoint(writer, bearing.From, ref points); WritePoint(writer, bearing.To, ref points);
                WriteElevation(writer, bearing.From, bearing.FromElevation);
                WriteElevation(writer, bearing.To, bearing.ToElevation);
                break;
            case CircleShape circle:
                WritePoint(writer, circle.Center, ref points);
                Require(circle.Radius > 0); WriteFloat(writer, circle.Radius, MaxCoordinate);
                WriteElevation(writer, circle.Center, circle.CenterElevation);
                break;
            case PenStroke pen:
                Require(pen.Points.Count >= 1 && pen.Points.Count <= MaxGeometryPoints - points);
                writer.Write((ushort)pen.Points.Count);
                foreach (var point in pen.Points) WritePosition(writer, point, ref points);
                break;
            case TextNote note:
                WritePoint(writer, note.At, ref points); WriteElevation(writer, note.At, note.Elevation);
                Require(TextTool.IsPlaceable(note.Text)); WriteText(writer, note.Text, TextTool.MaxLength);
                break;
            case WaypointRoute route:
                Require(route.Count >= 1 && route.Count <= WaypointRoute.MaxWaypoints && route.Count <= MaxGeometryPoints - points);
                writer.Write((ushort)route.Count);
                for (var i = 0; i < route.Count; i++)
                {
                    WritePoint(writer, route[i].Point, ref points); WriteElevation(writer, route[i].Point, route[i].Elevation);
                }
                break;
        }
    }

    /// <summary>
    /// Reads one shape record, handing back the shape the same bytes made in the session's last snapshot if there is
    /// one. A record's bytes alone decide every check <see cref="ReadShape"/> makes apart from the snapshot's point
    /// total, so identical bytes pass them again and only the total is counted.
    /// </summary>
    private static ShapeRecord ReadRecord(BinaryReader reader, Dictionary<int, ShapeRecord>? last, ref int points)
    {
        var stream = (MemoryStream)reader.BaseStream;
        var buffer = stream.GetBuffer();
        var start = (int)stream.Position;
        var remaining = (int)stream.Length - start;
        // ReceivedDrawings gives every shape it keeps a local ID below zero. A kept shape is on the map, so another is
        // built rather than letting one object stand for two drawings.
        if (remaining >= 5 && last != null && last.TryGetValue(BitConverter.ToInt32(buffer, start + 1), out var record) &&
            record.Shape.Id > 0 && record.Bytes.Length <= remaining &&
            new ReadOnlySpan<byte>(buffer, start, record.Bytes.Length).SequenceEqual(record.Bytes))
        {
            points += GeometryPointCount(record.Shape); Require(points <= MaxGeometryPoints);
            stream.Position = start + record.Bytes.Length;
            return record;
        }

        var shape = ReadShape(reader, ref points);
        var bytes = new byte[(int)stream.Position - start];
        Array.Copy(buffer, start, bytes, 0, bytes.Length);
        return new ShapeRecord(bytes, shape);
    }

    private static MapShape ReadShape(BinaryReader reader, ref int points)
    {
        var kind = reader.ReadByte();
        Require(kind >= 1 && kind <= 5);
        var id = reader.ReadInt32(); Require(id > 0);
        var channel = ReadText(reader, DrawingChannels.MaxNameLength); Require(CanonicalChannel(channel));
        var color = new ShapeColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        Require(color.A > 0); // an invisible drawing would still draw its label plates, which the eraser skips
        MapShape shape;
        switch (kind)
        {
            case 1:
                var from = ReadPoint(reader, ref points);
                var to = ReadPoint(reader, ref points);
                shape = new BearingRangeShape(from, to, ReadElevation(reader, from), ReadElevation(reader, to), color);
                break;
            case 2:
                var center = ReadPoint(reader, ref points);
                var radius = ReadFloat(reader, MaxCoordinate); Require(radius > 0);
                shape = new CircleShape(center, radius, ReadElevation(reader, center), color);
                break;
            case 3:
                var count = reader.ReadUInt16(); Require(count >= 1 && count <= MaxGeometryPoints - points);
                Require(count * 8 <= reader.BaseStream.Length - reader.BaseStream.Position);
                var positions = new Vector2[count];
                for (var i = 0; i < count; i++) positions[i] = ReadPosition(reader, ref points);
                shape = new PenStroke(positions, color);
                break;
            case 4:
                var at = ReadPoint(reader, ref points); var elevation = ReadElevation(reader, at);
                var text = ReadText(reader, TextTool.MaxLength); Require(TextTool.IsPlaceable(text));
                shape = new TextNote(at, elevation, text, color);
                break;
            case 5:
                var stops = reader.ReadUInt16(); Require(stops >= 1 && stops <= WaypointRoute.MaxWaypoints && stops <= MaxGeometryPoints - points);
                Require(stops * 16 <= reader.BaseStream.Length - reader.BaseStream.Position);
                var waypoints = new RouteWaypoint[stops];
                for (var i = 0; i < stops; i++)
                {
                    var point = ReadPoint(reader, ref points);
                    waypoints[i] = new RouteWaypoint(point, ReadElevation(reader, point));
                }
                shape = WaypointRoute.Of(color, waypoints);
                break;
            default: throw new InvalidDataException();
        }
        shape.Id = id; shape.Channel = channel;
        return shape;
    }


    public static int GeometryPointCount(MapShape shape) => shape switch
    {
        BearingRangeShape => 2,
        CircleShape => 1,
        PenStroke pen => pen.Points.Count,
        TextNote => 1,
        WaypointRoute route => route.Count,
        _ => throw new ArgumentException("Unknown drawing shape.", nameof(shape))
    };

    private static void WritePoint(BinaryWriter writer, MapPoint point, ref int points)
    {
        WritePosition(writer, point.IsAnchored ? Vector2.Zero : point.Position, ref points);
        writer.Write(point.UnitId);
    }

    private static void WriteElevation(BinaryWriter writer, MapPoint point, float elevation) =>
        WriteFloat(writer, point.IsAnchored ? 0f : elevation, MaxElevation);

    private static MapPoint ReadPoint(BinaryReader reader, ref int points)
    {
        var position = ReadPosition(reader, ref points);
        var unitId = reader.ReadUInt32();
        Require(unitId == 0 || position == Vector2.Zero);
        return new MapPoint(position, unitId);
    }

    private static float ReadElevation(BinaryReader reader, MapPoint point)
    {
        var elevation = ReadFloat(reader, MaxElevation);
        Require(!point.IsAnchored || elevation == 0f);
        return elevation;
    }

    private static void WritePosition(BinaryWriter writer, Vector2 point, ref int points)
    {
        Require(++points <= MaxGeometryPoints);
        WriteFloat(writer, point.X, MaxCoordinate); WriteFloat(writer, point.Y, MaxCoordinate);
    }

    private static Vector2 ReadPosition(BinaryReader reader, ref int points)
    {
        Require(++points <= MaxGeometryPoints);
        return new Vector2(ReadFloat(reader, MaxCoordinate), ReadFloat(reader, MaxCoordinate));
    }

    private static void WriteFloat(BinaryWriter writer, float value, float bound)
    {
        Require(value >= -bound && value <= bound); writer.Write(value);
    }

    private static float ReadFloat(BinaryReader reader, float bound)
    {
        var value = reader.ReadSingle(); Require(value >= -bound && value <= bound); return value;
    }

    private static void WriteText(BinaryWriter writer, string text, int maxChars)
    {
        Require(text.Length <= maxChars);
        var bytes = Utf8.GetBytes(text); Require(bytes.Length <= maxChars * 4);
        writer.Write((ushort)bytes.Length); writer.Write(bytes);
    }

    private static string ReadText(BinaryReader reader, int maxChars)
    {
        var length = reader.ReadUInt16(); Require(length <= maxChars * 4);
        var text = Utf8.GetString(ReadSpan(reader, length)); Require(text.Length <= maxChars); return text;
    }

    /// <summary>The next bytes, in place in the buffer being decoded rather than copied out.</summary>
    private static ReadOnlySpan<byte> ReadSpan(BinaryReader reader, int count)
    {
        var stream = (MemoryStream)reader.BaseStream;
        var start = (int)stream.Position;
        Require(count <= stream.Length - start);
        stream.Position = start + count;
        return new ReadOnlySpan<byte>(stream.GetBuffer(), start, count);
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidDataException(); }
    private static bool InvalidFrame(Exception error) => error is InvalidDataException or IOException or ArgumentException or NotSupportedException;
}
