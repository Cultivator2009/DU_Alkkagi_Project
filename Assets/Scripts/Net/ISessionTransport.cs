using System;

// Abstracts the P2P message bus so gameplay code never touches Steam types
// directly. Swap the Steam implementation for another platform's transport
// by implementing this interface again.
public interface ISessionTransport
{
    ulong LocalId { get; }
    bool IsReady { get; }

    void ConnectPeer(ulong id);
    void Send(ulong targetId, byte[] data, bool reliable = true);
    void Broadcast(byte[] data, bool reliable = true);

    event Action<ulong, byte[]> OnMessageReceived;
}
