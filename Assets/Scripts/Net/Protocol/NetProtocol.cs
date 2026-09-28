using System;
using System.Collections.Generic;
using UnityEngine;

// Every message the builds exchange. The number is the message's first
// byte on the wire: give a new message a new one and never reuse one.
public enum NetMessageType : byte
{
    StartMatch = 1,
    Flick = 2,
    PieceSnapshot = 3,
    TurnResult = 4,
    ClientReady = 5,
    LoadGameScene = 6,
    RematchRequest = 7,
    ReturnToLobby = 8,
    Pass = 9,
    PlacementState = 10,
    PlaceRequest = 11,
    PlacementReady = 12,
    Kick = 13,
    BoardSound = 14,
    Concede = 15,
    MatchState = 16, // the match between turns, when it changes mid-turn (a side conceding, leaving)
    FastForward = 17
}

// Who may send a message to whom. NetFilters.Authority drops anything
// that arrives the wrong way: a guest's word on the match, a message for
// the host at a guest.
public enum NetRoute : byte
{
    HostToGuests, // the match's (or lobby's) host tells the others
    GuestToHost,  // a player asks the host, who decides
    Peers         // anyone in the match to anyone
}

public interface INetMessage
{
    void Write(NetWriter writer);
    void Read(NetReader reader);
}

// The table of messages: each one's number, route and whether it must
// arrive. A message goes out as its number then its fields, and comes in
// as a fresh instance of its class.
public static class NetProtocol
{
    // Lobbies advertise it and a build only joins lobbies on its own. The
    // messages stand fields added at their end (NetWriter), so it only has
    // to go up when a field changes or goes, or a message's meaning does.
    public const int Version = 7;

    public readonly struct Entry
    {
        public readonly NetMessageType Type;
        public readonly NetRoute Route;
        public readonly bool Reliable;
        public readonly Func<INetMessage> Create;

        public Entry(NetMessageType type, NetRoute route, bool reliable, Func<INetMessage> create)
        {
            Type = type;
            Route = route;
            Reliable = reliable;
            Create = create;
        }
    }

    private static readonly Dictionary<NetMessageType, Entry> byType = new Dictionary<NetMessageType, Entry>();
    private static readonly Dictionary<Type, Entry> byClass = new Dictionary<Type, Entry>();

    static NetProtocol()
    {
        Add<Msg.StartMatch>(NetMessageType.StartMatch, NetRoute.HostToGuests);
        Add<Msg.Flick>(NetMessageType.Flick, NetRoute.GuestToHost);
        Add<Msg.PieceSnapshot>(NetMessageType.PieceSnapshot, NetRoute.HostToGuests, reliable: false);
        Add<Msg.TurnResult>(NetMessageType.TurnResult, NetRoute.HostToGuests);
        Add<Msg.ClientReady>(NetMessageType.ClientReady, NetRoute.GuestToHost);
        Add<Msg.LoadGameScene>(NetMessageType.LoadGameScene, NetRoute.HostToGuests);
        Add<Msg.RematchRequest>(NetMessageType.RematchRequest, NetRoute.Peers);
        Add<Msg.ReturnToLobby>(NetMessageType.ReturnToLobby, NetRoute.Peers);
        Add<Msg.Pass>(NetMessageType.Pass, NetRoute.GuestToHost);
        Add<Msg.PlacementState>(NetMessageType.PlacementState, NetRoute.HostToGuests);
        Add<Msg.PlaceRequest>(NetMessageType.PlaceRequest, NetRoute.GuestToHost);
        Add<Msg.PlacementReady>(NetMessageType.PlacementReady, NetRoute.GuestToHost);
        Add<Msg.Kick>(NetMessageType.Kick, NetRoute.HostToGuests);
        Add<Msg.BoardSound>(NetMessageType.BoardSound, NetRoute.HostToGuests, reliable: false);
        Add<Msg.Concede>(NetMessageType.Concede, NetRoute.GuestToHost);
        Add<Msg.MatchStateUpdate>(NetMessageType.MatchState, NetRoute.HostToGuests);
        Add<Msg.FastForward>(NetMessageType.FastForward, NetRoute.HostToGuests);
    }

    private static void Add<T>(NetMessageType type, NetRoute route, bool reliable = true) where T : INetMessage, new()
    {
        var entry = new Entry(type, route, reliable, () => new T());
        byType.Add(type, entry);
        byClass.Add(typeof(T), entry);
    }

    public static Entry Of(INetMessage message) => byClass[message.GetType()];

    public static byte[] Encode(INetMessage message)
    {
        var writer = new NetWriter();
        writer.Byte((byte)Of(message).Type);
        message.Write(writer);
        return writer.ToArray();
    }

    // False for an empty packet or a message this build doesn't know (a
    // newer build's): it's passed over.
    public static bool TryDecode(byte[] data, out Entry entry, out INetMessage message)
    {
        message = null;
        entry = default;
        if (data == null || data.Length == 0 || !byType.TryGetValue((NetMessageType)data[0], out entry)) return false;
        message = entry.Create();
        try
        {
            message.Read(new NetReader(data, 1));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Dropped a {entry.Type} that didn't read: {e.Message}");
            return false;
        }
        return true;
    }
}
