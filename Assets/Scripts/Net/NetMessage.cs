using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum NetMessageType : byte
{
    StartMatch = 1,
    FlickCommand = 2,
    PieceSnapshot = 3,
    TurnResult = 4,
    ClientReady = 5,
    LoadGameScene = 6
}

public struct PieceOwnerEntry
{
    public char PieceId;
    public int PlayerId;
}

public struct PieceTransform
{
    public char PieceId;
    public Vector3 Position;
    public Quaternion Rotation;
}

public struct RemovedPieceEntry
{
    public char PieceId;
    public int ScoredForPlayerId;
}

// Minimal binary envelope for the host/guest match protocol. Every message
// starts with a NetMessageType byte so the receiver can dispatch without a
// separate framing layer.
public static class NetMessage
{
    public static byte[] WriteStartMatch(int localPlayerId, IReadOnlyList<PieceOwnerEntry> pieceOwners)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.StartMatch);
        writer.Write(localPlayerId);
        writer.Write(pieceOwners.Count);
        foreach (var entry in pieceOwners)
        {
            writer.Write(entry.PieceId);
            writer.Write(entry.PlayerId);
        }
        return stream.ToArray();
    }

    public static (int assignedPlayerId, List<PieceOwnerEntry> owners) ReadStartMatch(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte(); // type
        var assignedPlayerId = reader.ReadInt32();
        var count = reader.ReadInt32();
        var owners = new List<PieceOwnerEntry>(count);
        for (var i = 0; i < count; i++)
        {
            owners.Add(new PieceOwnerEntry { PieceId = reader.ReadChar(), PlayerId = reader.ReadInt32() });
        }
        return (assignedPlayerId, owners);
    }

    public static byte[] WriteFlickCommand(char pieceId, Vector3 force)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.FlickCommand);
        writer.Write(pieceId);
        writer.Write(force.x);
        writer.Write(force.y);
        writer.Write(force.z);
        return stream.ToArray();
    }

    public static (char pieceId, Vector3 force) ReadFlickCommand(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var pieceId = reader.ReadChar();
        var force = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return (pieceId, force);
    }

    public static byte[] WritePieceSnapshot(IReadOnlyList<PieceTransform> transforms)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.PieceSnapshot);
        writer.Write(transforms.Count);
        foreach (var t in transforms)
        {
            writer.Write(t.PieceId);
            writer.Write(t.Position.x);
            writer.Write(t.Position.y);
            writer.Write(t.Position.z);
            writer.Write(t.Rotation.x);
            writer.Write(t.Rotation.y);
            writer.Write(t.Rotation.z);
            writer.Write(t.Rotation.w);
        }
        return stream.ToArray();
    }

    public static List<PieceTransform> ReadPieceSnapshot(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var count = reader.ReadInt32();
        var transforms = new List<PieceTransform>(count);
        for (var i = 0; i < count; i++)
        {
            var pieceId = reader.ReadChar();
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            transforms.Add(new PieceTransform { PieceId = pieceId, Position = position, Rotation = rotation });
        }
        return transforms;
    }

    public static byte[] WriteTurnResult(int nextPlayerId, bool matchOver, int winnerPlayerId, IReadOnlyList<RemovedPieceEntry> removed)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.TurnResult);
        writer.Write(nextPlayerId);
        writer.Write(matchOver);
        writer.Write(winnerPlayerId);
        writer.Write(removed.Count);
        foreach (var entry in removed)
        {
            writer.Write(entry.PieceId);
            writer.Write(entry.ScoredForPlayerId);
        }
        return stream.ToArray();
    }

    public static (int nextPlayerId, bool matchOver, int winnerPlayerId, List<RemovedPieceEntry> removed) ReadTurnResult(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var nextPlayerId = reader.ReadInt32();
        var matchOver = reader.ReadBoolean();
        var winnerPlayerId = reader.ReadInt32();
        var count = reader.ReadInt32();
        var removed = new List<RemovedPieceEntry>(count);
        for (var i = 0; i < count; i++)
        {
            removed.Add(new RemovedPieceEntry { PieceId = reader.ReadChar(), ScoredForPlayerId = reader.ReadInt32() });
        }
        return (nextPlayerId, matchOver, winnerPlayerId, removed);
    }

    public static byte[] WriteClientReady() => new[] { (byte)NetMessageType.ClientReady };

    public static byte[] WriteLoadGameScene() => new[] { (byte)NetMessageType.LoadGameScene };

    public static NetMessageType PeekType(byte[] data) => (NetMessageType)data[0];
}
