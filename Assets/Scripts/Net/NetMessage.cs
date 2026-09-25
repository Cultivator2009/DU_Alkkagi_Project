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
    LoadGameScene = 6,
    RematchRequest = 7,
    ReturnToLobby = 8,
    PassCommand = 9,     // guest -> host: skip my turn
    PlacementState = 10, // host -> guest: the placement phase as the guest may see it
    PlaceRequest = 11,   // guest -> host: put/move one of my stones
    PlacementReady = 12, // guest -> host: my stones are final
    Kick = 13,           // host -> guest: leave my lobby
    BoardSound = 14,     // host -> guest: a knock, hinge, flick or fall to play, and where (unreliable)
    Concede = 15,        // guest -> host: I give up
    PlayerOut = 16       // host -> guests: a side is out and the match goes on (three or four)
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
    // Bump whenever a message changes shape. Lobbies advertise it, and a
    // build only lists and joins lobbies on its own version: two builds that
    // disagree here would misread each other's messages mid-match.
    public const int ProtocolVersion = 4;

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
        WriteTransforms(writer, transforms);
        return stream.ToArray();
    }

    public static List<PieceTransform> ReadPieceSnapshot(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        return ReadTransforms(reader);
    }

    // Carries the settled position of every piece still in play, so the
    // guest converges on the host's board every turn even if some of the
    // unreliable mid-turn snapshots were dropped.
    // turnEnd: how the turn that just finished ended (None for the opening
    // turn and for a resync after a rejected command). kills: the kill feed
    // of the shot that just resolved, empty for anything else.
    public static byte[] WriteTurnResult(int nextPlayerId, TurnEnd turnEnd, bool matchOver, int winnerPlayerId, MatchEndReason reason, IReadOnlyList<RemovedPieceEntry> removed, IReadOnlyList<KillEvent> kills, IReadOnlyList<PieceTransform> finalTransforms)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.TurnResult);
        writer.Write(nextPlayerId);
        writer.Write((byte)turnEnd);
        writer.Write(matchOver);
        writer.Write(winnerPlayerId);
        writer.Write((byte)reason);
        writer.Write(removed.Count);
        foreach (var entry in removed)
        {
            writer.Write(entry.PieceId);
            writer.Write(entry.ScoredForPlayerId);
        }
        writer.Write(kills.Count);
        foreach (var kill in kills)
        {
            writer.Write(kill.ShooterId);
            writer.Write(kill.ShotPieceId);
            writer.Write(kill.VictimId);
            writer.Write(kill.VictimOwnerId);
            writer.Write((byte)kill.Kind);
        }
        WriteTransforms(writer, finalTransforms);
        return stream.ToArray();
    }

    public static (int nextPlayerId, TurnEnd turnEnd, bool matchOver, int winnerPlayerId, MatchEndReason reason, List<RemovedPieceEntry> removed, List<KillEvent> kills, List<PieceTransform> finalTransforms) ReadTurnResult(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var nextPlayerId = reader.ReadInt32();
        var turnEnd = (TurnEnd)reader.ReadByte();
        var matchOver = reader.ReadBoolean();
        var winnerPlayerId = reader.ReadInt32();
        var reason = (MatchEndReason)reader.ReadByte();
        var count = reader.ReadInt32();
        var removed = new List<RemovedPieceEntry>(count);
        for (var i = 0; i < count; i++)
        {
            removed.Add(new RemovedPieceEntry { PieceId = reader.ReadChar(), ScoredForPlayerId = reader.ReadInt32() });
        }
        var killCount = reader.ReadInt32();
        var kills = new List<KillEvent>(killCount);
        for (var i = 0; i < killCount; i++)
        {
            kills.Add(new KillEvent
            {
                ShooterId = reader.ReadInt32(),
                ShotPieceId = reader.ReadChar(),
                VictimId = reader.ReadChar(),
                VictimOwnerId = reader.ReadInt32(),
                Kind = (KillKind)reader.ReadByte(),
            });
        }
        return (nextPlayerId, turnEnd, matchOver, winnerPlayerId, reason, removed, kills, ReadTransforms(reader));
    }

    private static void WriteTransforms(BinaryWriter writer, IReadOnlyList<PieceTransform> transforms)
    {
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
    }

    private static List<PieceTransform> ReadTransforms(BinaryReader reader)
    {
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

    // The host's rules and roster travel with the scene change, so every
    // side spawns and plays the same match whatever state its copy of the
    // lobby data is in.
    public static byte[] WriteLoadGameScene(MatchSettings settings, MatchRoster roster)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.LoadGameScene);
        settings.Write(writer);
        roster.Write(writer);
        return stream.ToArray();
    }

    public static (MatchSettings settings, MatchRoster roster) ReadLoadGameScene(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var settings = MatchSettings.Read(reader);
        return (settings, MatchRoster.Read(reader));
    }

    public static byte[] WritePlayerOut(int playerId, MatchEndReason reason) => new[] { (byte)NetMessageType.PlayerOut, (byte)playerId, (byte)reason };

    public static (int playerId, MatchEndReason reason) ReadPlayerOut(byte[] data) => (data[1], (MatchEndReason)data[2]);

    public static byte[] WritePlacementState(PlacementSnapshot state)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.PlacementState);
        writer.Write(state.Placer);
        writer.Write(state.Done);
        writer.Write(state.Clocks.Length);
        for (var i = 0; i < state.Clocks.Length; i++)
        {
            writer.Write(state.Clocks[i]);
            writer.Write(state.Ready[i]);
        }
        writer.Write(state.Stones.Count);
        foreach (var stone in state.Stones)
        {
            writer.Write(stone.PieceId);
            writer.Write(stone.X);
            writer.Write(stone.Z);
        }
        return stream.ToArray();
    }

    public static PlacementSnapshot ReadPlacementState(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var state = new PlacementSnapshot { Placer = reader.ReadInt32(), Done = reader.ReadBoolean() };
        var players = reader.ReadInt32();
        state.Clocks = new float[players];
        state.Ready = new bool[players];
        for (var i = 0; i < players; i++)
        {
            state.Clocks[i] = reader.ReadSingle();
            state.Ready[i] = reader.ReadBoolean();
        }
        var count = reader.ReadInt32();
        state.Stones = new List<PlacedStone>(count);
        for (var i = 0; i < count; i++)
            state.Stones.Add(new PlacedStone { PieceId = reader.ReadChar(), X = reader.ReadSingle(), Z = reader.ReadSingle() });
        return state;
    }

    public static byte[] WritePlaceRequest(char pieceId, Vector3 position)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.PlaceRequest);
        writer.Write(pieceId);
        writer.Write(position.x);
        writer.Write(position.z);
        return stream.ToArray();
    }

    public static (char pieceId, float x, float z) ReadPlaceRequest(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        return (reader.ReadChar(), reader.ReadSingle(), reader.ReadSingle());
    }

    public static byte[] WriteClientReady() => new[] { (byte)NetMessageType.ClientReady };

    public static byte[] WritePassCommand() => new[] { (byte)NetMessageType.PassCommand };

    public static byte[] WritePlacementReady() => new[] { (byte)NetMessageType.PlacementReady };

    public static byte[] WriteRematchRequest() => new[] { (byte)NetMessageType.RematchRequest };

    public static byte[] WriteReturnToLobby() => new[] { (byte)NetMessageType.ReturnToLobby };

    public static byte[] WriteKick() => new[] { (byte)NetMessageType.Kick };

    public static byte[] WriteConcede() => new[] { (byte)NetMessageType.Concede };

    // Where it happened, for the pan and for a knock's burst on the guest's board.
    public static byte[] WriteBoardSound(BoardSoundEvent sound)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)NetMessageType.BoardSound);
        writer.Write((byte)sound.Kind);
        writer.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(sound.Volume) * 255));
        writer.Write(sound.Position.x);
        writer.Write(sound.Position.y);
        writer.Write(sound.Position.z);
        return stream.ToArray();
    }

    public static BoardSoundEvent ReadBoardSound(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var kind = (BoardSound)reader.ReadByte();
        var volume = reader.ReadByte() / 255f;
        var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return new BoardSoundEvent { Kind = kind, Volume = volume, Pan = BoardSounds.Pan(position), Owner = -1, Position = position };
    }

    public static NetMessageType PeekType(byte[] data) => (NetMessageType)data[0];
}
