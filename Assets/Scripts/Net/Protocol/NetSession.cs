using System;
using System.Collections.Generic;
using UnityEngine;

// A message as it arrives: who sent it, what it is, and its route.
public readonly struct NetEnvelope
{
    public readonly ulong Sender;
    public readonly NetProtocol.Entry Entry;
    public readonly INetMessage Message;

    public NetEnvelope(ulong sender, NetProtocol.Entry entry, INetMessage message)
    {
        Sender = sender;
        Entry = entry;
        Message = message;
    }
}

// Middleware: false drops the message before any handler of the scope sees it.
public delegate bool NetFilter(in NetEnvelope envelope);

// The message bus over a transport. A packet is read once, then offered to
// every scope: a scope is one screen's handlers (the lobby's, a match's)
// with its own filters, and goes with that screen (Dispose). Sending picks
// each message's reliability from NetProtocol.
public sealed class NetSession
{
    // Over SteamTransport, from NetworkServices; null while Steam isn't up.
    public static NetSession Current { get; set; }

    public ISessionTransport Transport { get; }
    public ulong LocalId => Transport.LocalId;

    private readonly List<NetScope> scopes = new List<NetScope>();

    public NetSession(ISessionTransport transport)
    {
        Transport = transport;
        transport.OnMessageReceived += Receive;
    }

    public NetScope Scope(string name)
    {
        var scope = new NetScope(this, name);
        scopes.Add(scope);
        return scope;
    }

    internal void Remove(NetScope scope) => scopes.Remove(scope);

    public void Send(ulong target, INetMessage message) => Transport.Send(target, NetProtocol.Encode(message), NetProtocol.Of(message).Reliable);

    public void Broadcast(INetMessage message) => Transport.Broadcast(NetProtocol.Encode(message), NetProtocol.Of(message).Reliable);

    private void Receive(ulong sender, byte[] data)
    {
        if (!NetProtocol.TryDecode(data, out var entry, out var message)) return;
        var envelope = new NetEnvelope(sender, entry, message);
        // A handler may open or close a scope (a scene load): go over a copy.
        foreach (var scope in scopes.ToArray()) scope.Deliver(envelope);
    }
}

public sealed class NetScope : IDisposable
{
    private readonly NetSession session;
    private readonly string name;
    private readonly List<NetFilter> filters = new List<NetFilter>();
    private readonly Dictionary<Type, Action<NetEnvelope>> handlers = new Dictionary<Type, Action<NetEnvelope>>();
    private bool disposed;

    internal NetScope(NetSession session, string name)
    {
        this.session = session;
        this.name = name;
    }

    public NetScope Use(NetFilter filter)
    {
        filters.Add(filter);
        return this;
    }

    public NetScope On<T>(Action<ulong, T> handler) where T : INetMessage
    {
        handlers.TryGetValue(typeof(T), out var existing);
        handlers[typeof(T)] = existing + (e => handler(e.Sender, (T)e.Message));
        return this;
    }

    internal void Deliver(in NetEnvelope envelope)
    {
        if (disposed || !handlers.TryGetValue(envelope.Message.GetType(), out var handler)) return;
        foreach (var filter in filters)
            if (!filter(envelope)) return;
        handler(envelope);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        session.Remove(this);
    }

    public override string ToString() => name;
}

// The filters every scope of the game uses.
public static class NetFilters
{
    // Only from these players (the match's roster, the lobby's members).
    public static NetFilter From(Func<ulong, bool> allowed) => (in NetEnvelope e) => allowed(e.Sender);

    // Each message only the way its route goes: the host's word only from
    // the host, and only on a guest; a request for the host only on the host.
    public static NetFilter Authority(Func<ulong> host, Func<bool> localIsHost) => (in NetEnvelope e) =>
    {
        switch (e.Entry.Route)
        {
            case NetRoute.HostToGuests: return e.Sender == host() && !localIsHost();
            case NetRoute.GuestToHost: return localIsHost();
            default: return true;
        }
    };

    // Nothing from a player this one blocked (BlockList).
    public static NetFilter NotBlocked() => (in NetEnvelope e) => !BlockList.IsBlocked(e.Sender);

    // Every message in the console, in the editor and development builds.
    public static NetFilter Trace(string scope) => (in NetEnvelope e) =>
    {
        if (Debug.isDebugBuild && e.Entry.Reliable) Debug.Log($"[net:{scope}] {e.Entry.Type} from {e.Sender}");
        return true;
    };
}
