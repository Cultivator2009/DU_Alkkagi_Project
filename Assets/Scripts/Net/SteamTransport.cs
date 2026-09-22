using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

// Owns the Steam client lifecycle and implements P2P messaging on top of
// Steamworks.Networking (session-based packet API). This is the only class
// that references the Facepunch.Steamworks types directly.
public class SteamTransport : MonoBehaviour, ISessionTransport
{
    private const int Channel = 0;
    private const uint FallbackAppId = 480; // Spacewar test app, used only if steam_appid.txt is missing

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
            SteamClient.Init(ReadAppId(), true);
            // Init can return without throwing while Steam is still starting up
            // or not logged in, leaving the API interfaces null - every call
            // then throws. Probe one before claiming to be ready.
            SteamNetworking.IsP2PPacketAvailable(Channel);
            IsReady = true;
            SteamNetworking.OnP2PSessionRequest += HandleSessionRequest;
        }
        catch (Exception e)
        {
            Debug.LogError($"Steam client failed to initialize: {e.Message}");
            if (SteamClient.IsValid) SteamClient.Shutdown();
            IsReady = false;
        }
    }

    // Reads the App ID from steam_appid.txt next to the executable (or the
    // project root in-Editor, since Application.dataPath is "<root>/Assets"
    // there too). Keeps the real App ID out of source control entirely -
    // only the gitignored text file needs to hold it. See steam_appid.txt.
    private static uint ReadAppId()
    {
        var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");
        if (File.Exists(path) && uint.TryParse(File.ReadAllText(path).Trim(), out var id)) return id;

        Debug.LogWarning($"steam_appid.txt missing or invalid at {path} - falling back to the Spacewar test app ({FallbackAppId}).");
        return FallbackAppId;
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
        // Unreliable, not UnreliableNoDelay: NoDelay drops a packet outright
        // whenever it can't go out immediately, which over a relayed
        // connection loses a large share of the mid-turn snapshots.
        var sendType = reliable ? P2PSend.Reliable : P2PSend.Unreliable;
        SteamNetworking.SendP2PPacket(targetId, data, data.Length, Channel, sendType);
    }

    public void Broadcast(byte[] data, bool reliable = true)
    {
        foreach (var peer in knownPeers) Send(peer, data, reliable);
    }

    private void DrainIncomingPackets()
    {
        while (true)
        {
            P2Packet? packet;
            try
            {
                if (!SteamNetworking.IsP2PPacketAvailable(Channel)) return;
                packet = SteamNetworking.ReadP2PPacket(Channel);
            }
            catch (Exception e)
            {
                // Steam went away underneath us (client quit or logged out).
                // Stop polling rather than throwing every frame.
                Debug.LogError($"Steam networking stopped responding: {e.Message}");
                IsReady = false;
                return;
            }
            // Outside the try: a handler bug should surface as itself, not
            // shut networking down.
            if (packet.HasValue) OnMessageReceived?.Invoke(packet.Value.SteamId.Value, packet.Value.Data);
        }
    }
}
