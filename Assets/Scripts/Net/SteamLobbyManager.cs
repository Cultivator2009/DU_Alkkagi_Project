using System;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

// Wraps Steam's Lobby (matchmaking) API. This is the only "server" involved
// in a match - Valve's own lobby/relay infrastructure - the match itself
// stays host/guest P2P via SteamTransport.
public class SteamLobbyManager : MonoBehaviour
{
    private const int MaxMembers = 2;

    public static SteamLobbyManager Instance { get; private set; }

    public Lobby? CurrentLobby { get; private set; }
    public bool IsHost => CurrentLobby.HasValue && CurrentLobby.Value.Owner.Id.Value == SteamClient.SteamId.Value;
    public bool IsJoining { get; private set; }

    public event Action<Lobby> OnLobbyReady;
    public event Action<Friend> OnMemberJoined;
    public event Action<Friend> OnMemberLeft;
    public event Action OnLobbyFailed;
    public event Action OnLobbyDataChanged;

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
        // Steam reports a member that dropped (crash, lost connection) as a
        // disconnect rather than a leave; to the game both mean they're gone.
        SteamMatchmaking.OnLobbyMemberDisconnected += HandleMemberLeft;
        SteamMatchmaking.OnLobbyDataChanged += HandleLobbyDataChanged;
        SteamFriends.OnGameLobbyJoinRequested += HandleJoinRequested;
    }

    private void Start()
    {
        TryJoinFromCommandLine();
    }

    private void OnDestroy()
    {
        SteamMatchmaking.OnLobbyEntered -= HandleLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= HandleMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= HandleMemberLeft;
        SteamMatchmaking.OnLobbyMemberDisconnected -= HandleMemberLeft;
        SteamMatchmaking.OnLobbyDataChanged -= HandleLobbyDataChanged;
        SteamFriends.OnGameLobbyJoinRequested -= HandleJoinRequested;
    }

    // Leave explicitly on quit so the other player is told right away;
    // otherwise Steam only notices once the connection times out.
    private void OnApplicationQuit()
    {
        LeaveLobby();
    }

    public async void CreateLobby()
    {
        var result = await SteamMatchmaking.CreateLobbyAsync(MaxMembers);
        if (!result.HasValue)
        {
            Debug.LogError("Failed to create Steam lobby.");
            OnLobbyFailed?.Invoke();
            return;
        }
        result.Value.SetJoinable(true);
        // The host's own settings become the lobby's match options.
        result.Value.SetData(MatchOptions.AimGuideLobbyKey, MatchOptions.LocalAimGuide ? "1" : "0");
    }

    // Match options live in lobby data: only the owner may write them,
    // every member reads the same values.
    public void SetLobbyOption(string key, bool value)
    {
        if (IsHost) CurrentLobby.Value.SetData(key, value ? "1" : "0");
    }

    public bool GetLobbyOption(string key, bool fallback)
    {
        if (!CurrentLobby.HasValue) return fallback;
        var value = CurrentLobby.Value.GetData(key);
        return string.IsNullOrEmpty(value) ? fallback : value == "1";
    }

    private void HandleLobbyDataChanged(Lobby lobby)
    {
        if (CurrentLobby.HasValue && lobby.Id == CurrentLobby.Value.Id) OnLobbyDataChanged?.Invoke();
    }

    public async void JoinLobby(ulong lobbyId)
    {
        IsJoining = true;
        var result = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
        if (result.HasValue) return; // HandleLobbyEntered finishes the join

        IsJoining = false;
        Debug.LogError($"Failed to join Steam lobby {lobbyId}.");
        OnLobbyFailed?.Invoke();
    }

    // Opens the Steam overlay's invite dialog for the current lobby. Needs
    // the overlay, so it does nothing when run from the Unity Editor.
    public void InviteFriends()
    {
        if (CurrentLobby.HasValue) SteamFriends.OpenGameInviteOverlay(CurrentLobby.Value.Id);
    }

    // A friend's invite accepted from the Steam overlay/friends list while
    // the game is running. Ignored mid-match so it can't yank a player out
    // of a game in progress.
    private void HandleJoinRequested(Lobby lobby, SteamId inviter)
    {
        if (SceneManager.GetActiveScene().name == "GameScene")
        {
            Debug.Log($"Ignoring lobby invite from {inviter} during a match.");
            return;
        }
        JoinInvitedLobby(lobby.Id.Value);
    }

    // An invite accepted while the game is closed launches it with
    // "+connect_lobby <id>" on the command line.
    private void TryJoinFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "+connect_lobby");
        if (index < 0 || index + 1 >= args.Length || !ulong.TryParse(args[index + 1], out var lobbyId)) return;
        JoinInvitedLobby(lobbyId);
    }

    private void JoinInvitedLobby(ulong lobbyId)
    {
        LeaveLobby();
        JoinLobby(lobbyId);
        if (SceneManager.GetActiveScene().name != "LobbyScene") SceneManager.LoadScene("LobbyScene");
    }

    public void LeaveLobby()
    {
        if (!CurrentLobby.HasValue) return;
        CurrentLobby.Value.Leave();
        CurrentLobby = null;
    }

    private void HandleLobbyEntered(Lobby lobby)
    {
        IsJoining = false;
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
