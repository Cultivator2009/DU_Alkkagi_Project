using System;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

// Wraps Steam's Lobby (matchmaking) API. This is the only "server" involved
// in a match - Valve's own lobby/relay infrastructure - the match itself
// stays host/guest P2P via SteamTransport.
public class SteamLobbyManager : MonoBehaviour
{
    private const int MaxMembers = 2;

    public static SteamLobbyManager Instance { get; private set; }

    public Lobby? CurrentLobby { get; private set; }
    public bool IsHost => CurrentLobby.HasValue && CurrentLobby.Value.Owner.Id.Value == SteamClient.SteamId.Value;

    public event Action<Lobby> OnLobbyReady;
    public event Action<Friend> OnMemberJoined;
    public event Action<Friend> OnMemberLeft;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SteamMatchmaking.OnLobbyEntered += HandleLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined += HandleMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave += HandleMemberLeft;
    }

    private void OnDestroy()
    {
        SteamMatchmaking.OnLobbyEntered -= HandleLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= HandleMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= HandleMemberLeft;
    }

    public async void CreateLobby()
    {
        var result = await SteamMatchmaking.CreateLobbyAsync(MaxMembers);
        if (!result.HasValue)
        {
            Debug.LogError("Failed to create Steam lobby.");
            return;
        }
        result.Value.SetJoinable(true);
    }

    public async void JoinLobby(ulong lobbyId)
    {
        var result = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
        if (!result.HasValue) Debug.LogError($"Failed to join Steam lobby {lobbyId}.");
    }

    public void LeaveLobby()
    {
        if (!CurrentLobby.HasValue) return;
        CurrentLobby.Value.Leave();
        CurrentLobby = null;
    }

    private void HandleLobbyEntered(Lobby lobby)
    {
        CurrentLobby = lobby;
        foreach (var member in lobby.Members)
        {
            if (member.Id.Value == SteamClient.SteamId.Value) continue;
            SteamTransport.Instance?.ConnectPeer(member.Id.Value);
        }
        OnLobbyReady?.Invoke(lobby);
    }

    private void HandleMemberJoined(Lobby lobby, Friend friend)
    {
        SteamTransport.Instance?.ConnectPeer(friend.Id.Value);
        OnMemberJoined?.Invoke(friend);
    }

    private void HandleMemberLeft(Lobby lobby, Friend friend)
    {
        OnMemberLeft?.Invoke(friend);
    }
}
