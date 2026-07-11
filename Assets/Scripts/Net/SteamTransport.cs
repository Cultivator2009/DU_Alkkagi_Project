using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

// Owns the Steam client lifecycle and implements P2P messaging on top of
// Steamworks.Networking (session-based packet API). This is the only class
// that references the Facepunch.Steamworks types directly.
public class SteamTransport : MonoBehaviour, ISessionTransport
{
    private const int Channel = 0;
    private const uint DummyAppId = 480; // Spacewar test app, replaced at Steam release time

    public static SteamTransport Instance { get; private set; }

    public ulong LocalId => IsReady ? SteamClient.SteamId.Value : 0;
    public bool IsReady { get; private set; }

    public event Action<ulong, byte[]> OnMessageReceived;

    private readonly HashSet<ulong> knownPeers = new HashSet<ulong>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        try
        {
            SteamClient.Init(DummyAppId, true);
            IsReady = true;
            SteamNetworking.OnP2PSessionRequest += HandleSessionRequest;
        }
        catch (Exception e)
        {
            Debug.LogError($"Steam client failed to initialize: {e.Message}");
            IsReady = false;
        }
    }

    private void Update()
    {
        if (!IsReady) return;
        SteamClient.RunCallbacks();
        DrainIncomingPackets();
    }

    private void OnDestroy()
    {
        if (!IsReady) return;
        SteamNetworking.OnP2PSessionRequest -= HandleSessionRequest;
        SteamClient.Shutdown();
    }

    private void HandleSessionRequest(SteamId remoteId)
    {
        if (knownPeers.Contains(remoteId.Value)) SteamNetworking.AcceptP2PSessionWithUser(remoteId);
    }

    public void ConnectPeer(ulong id)
    {
        if (id == LocalId) return;
        knownPeers.Add(id);
        SteamNetworking.AcceptP2PSessionWithUser(id);
    }

    public void Send(ulong targetId, byte[] data, bool reliable = true)
    {
        if (!IsReady) return;
        var sendType = reliable ? P2PSend.Reliable : P2PSend.UnreliableNoDelay;
        SteamNetworking.SendP2PPacket(targetId, data, data.Length, Channel, sendType);
    }

    public void Broadcast(byte[] data, bool reliable = true)
    {
        foreach (var peer in knownPeers) Send(peer, data, reliable);
    }

    private void DrainIncomingPackets()
    {
        while (SteamNetworking.IsP2PPacketAvailable(Channel))
        {
            var packet = SteamNetworking.ReadP2PPacket(Channel);
            if (packet.HasValue) OnMessageReceived?.Invoke(packet.Value.SteamId.Value, packet.Value.Data);
        }
    }
}
