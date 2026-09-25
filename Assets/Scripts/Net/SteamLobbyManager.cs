using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum LobbyVisibility : byte
{
    Public,      // listed in the lobby browser and quick match, and friends see it
    FriendsOnly, // friends can join from Steam; anyone with the code
    Private      // the code or an invite only
}

// Wraps Steam's Lobby (matchmaking) API. This is the only "server" involved
// in a match - Valve's own lobby/relay infrastructure - the match itself
// stays host/guest P2P via SteamTransport.
public class SteamLobbyManager : MonoBehaviour
{
    private const string RulePrefix = "rule."; // lobby data keys of the match rules
    // Lobby data every lobby of ours carries. The Spacewar test app (480) is
    // shared by every Steamworks developer, so searches filter on GameKey;
    // ProtocolKey keeps players on different builds apart.
    private const string GameKey = "game";
    private const string GameId = "du-alkkagi";
    private const string ProtocolKey = "proto";
    private const string StateKey = "state"; // Open while in the lobby, Playing during a match
    private const string Open = "open";
    private const string Playing = "playing";
    private const string HostNameKey = "host";
    private const string VisibilityKey = "vis";
    private const string VisibilityPref = "lobby.visibility";

    public static SteamLobbyManager Instance { get; private set; }

    public Lobby? CurrentLobby { get; private set; }
    public bool IsHost => CurrentLobby.HasValue && CurrentLobby.Value.Owner.Id.Value == SteamClient.SteamId.Value;
    public bool IsJoining { get; private set; }
    public bool IsSearching { get; private set; } // quick match looking for a lobby

    public event Action<Lobby> OnLobbyReady;
    public event Action<Friend> OnMemberJoined;
    public event Action<Friend> OnMemberLeft;
    public event Action<string> OnLobbyFailed; // the Loc key of the status to show
    public event Action OnLobbyDataChanged;

    // Players this host has removed from its current lobby: sent away again
    // if they rejoin.
    private readonly HashSet<ulong> kicked = new HashSet<ulong>();

    // What the lobbies this player creates start as.
    public static LobbyVisibility PreferredVisibility
    {
        get => (LobbyVisibility)PlayerPrefs.GetInt(VisibilityPref, (int)LobbyVisibility.FriendsOnly);
        set => PlayerPrefs.SetInt(VisibilityPref, (int)value);
    }

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

    public async void CreateLobby(LobbyVisibility visibility)
    {
        // The host's last-used rules are where the lobby starts, seats included.
        var settings = MatchSettings.LoadPrefs();
        var result = await SteamMatchmaking.CreateLobbyAsync(settings.Seats);
        if (!result.HasValue)
        {
            Debug.LogError("Failed to create Steam lobby.");
            OnLobbyFailed?.Invoke("lobby.status.failed");
            return;
        }
        var lobby = result.Value;
        kicked.Clear();
        lobby.SetJoinable(true);
        lobby.SetData(GameKey, GameId);
        lobby.SetData(ProtocolKey, NetMessage.ProtocolVersion.ToString());
        lobby.SetData(StateKey, Open);
        lobby.SetData(HostNameKey, SteamClient.Name);
        ApplyVisibility(lobby, visibility);
        WriteSettings(lobby, settings);
    }

    // Steam has no getter for a lobby's type, so it's mirrored in lobby data.
    public LobbyVisibility Visibility =>
        CurrentLobby.HasValue && byte.TryParse(CurrentLobby.Value.GetData(VisibilityKey), out var value) ? (LobbyVisibility)value : LobbyVisibility.FriendsOnly;

    public void SetVisibility(LobbyVisibility visibility)
    {
        if (!IsHost) return;
        ApplyVisibility(CurrentLobby.Value, visibility);
        PreferredVisibility = visibility;
    }

    private static void ApplyVisibility(Lobby lobby, LobbyVisibility visibility)
    {
        switch (visibility)
        {
            case LobbyVisibility.Public: lobby.SetPublic(); break;
            case LobbyVisibility.FriendsOnly: lobby.SetFriendsOnly(); break;
            default: lobby.SetPrivate(); break;
        }
        lobby.SetData(VisibilityKey, ((byte)visibility).ToString());
    }

    // During a match the lobby takes no one new and drops out of searches;
    // back in the lobby it opens again.
    public void SetMatchInProgress(bool playing)
    {
        if (!IsHost) return;
        CurrentLobby.Value.SetJoinable(!playing);
        CurrentLobby.Value.SetData(StateKey, playing ? Playing : Open);
    }

    // Public lobbies of this game, on this protocol, waiting for a player.
    // Steam sorts them nearest first.
    public async Task<Lobby[]> FindOpenLobbies()
    {
        var lobbies = await SteamMatchmaking.LobbyList
            .WithKeyValue(GameKey, GameId)
            .WithKeyValue(ProtocolKey, NetMessage.ProtocolVersion.ToString())
            .WithKeyValue(StateKey, Open)
            .WithSlotsAvailable(1)
            .FilterDistanceWorldwide()
            .WithMaxResults(20)
            .RequestAsync();
        return lobbies ?? Array.Empty<Lobby>();
    }

    // The nearest open public lobby, or failing that a new public one to
    // wait in. A lobby can fill between the search and the join, so each is
    // tried in turn.
    public async void QuickMatch()
    {
        IsSearching = true;
        var lobbies = await FindOpenLobbies();
        IsSearching = false;
        IsJoining = true;
        foreach (var lobby in lobbies)
        {
            if ((await SteamMatchmaking.JoinLobbyAsync(lobby.Id)).HasValue) return; // HandleLobbyEntered finishes the join
        }
        IsJoining = false;
        CreateLobby(LobbyVisibility.Public);
    }

    public static string HostName(Lobby lobby) => lobby.GetData(HostNameKey);

    // The rules a lobby advertises, e.g. for a lobby browser row.
    public static MatchSettings RulesOf(Lobby lobby) => MatchSettings.FromPairs(key => lobby.GetData(RulePrefix + key));

    // Steam lobbies can't eject anyone, so the host asks the guest's game to
    // leave (NetMessage.Kick), and asks again if they come back.
    public void Kick(ulong memberId)
    {
        if (!IsHost) return;
        kicked.Add(memberId);
        SteamTransport.Instance?.Send(memberId, NetMessage.WriteKick());
    }

    // Match rules live in lobby data: only the owner may write them, every
    // member reads the same values. What a match actually uses is the copy
    // the host sends with LoadGameScene, so a late data update can't split
    // the two sides.
    public void SetLobbySettings(MatchSettings settings)
    {
        if (!IsHost) return;
        WriteSettings(CurrentLobby.Value, settings);
        // Seats is how many may be in the lobby; never fewer than are in it.
        var lobby = CurrentLobby.Value;
        lobby.MaxMembers = Mathf.Max(settings.Seats, lobby.MemberCount);
    }

    // The lobby's seats in order: the owner first (player 0), then the
    // others as Steam lists them, which is the same on every member's
    // machine. The host's roster for a match is this order.
    public static List<Friend> SeatOrder(Lobby lobby)
    {
        var members = lobby.Members.ToList();
        var seats = members.Where(m => m.Id.Value == lobby.Owner.Id.Value).ToList();
        seats.AddRange(members.Where(m => m.Id.Value != lobby.Owner.Id.Value));
        return seats.Take(MatchRoster.MaxPlayers).ToList();
    }

    public MatchRoster BuildRoster() => new MatchRoster(SeatOrder(CurrentLobby.Value).Select(m => m.Id.Value));

    public MatchSettings ReadLobbySettings()
    {
        return CurrentLobby.HasValue ? RulesOf(CurrentLobby.Value) : new MatchSettings();
    }

    private static void WriteSettings(Lobby lobby, MatchSettings settings)
    {
        foreach (var pair in settings.ToPairs()) lobby.SetData(RulePrefix + pair.Key, pair.Value);
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
        OnLobbyFailed?.Invoke("lobby.status.failed");
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
        // A code or an invite can lead to a lobby from another build (or
        // another game on the shared test app): its messages wouldn't read.
        var version = lobby.GetData(ProtocolKey);
        if (!lobby.IsOwnedBy(SteamClient.SteamId) && version != NetMessage.ProtocolVersion.ToString())
        {
            Debug.LogWarning($"Leaving lobby {lobby.Id}: protocol '{version}', this build speaks {NetMessage.ProtocolVersion}.");
            lobby.Leave();
            OnLobbyFailed?.Invoke("lobby.status.version");
            return;
        }
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
        if (IsHost && kicked.Contains(friend.Id.Value)) SteamTransport.Instance?.Send(friend.Id.Value, NetMessage.WriteKick());
        OnMemberJoined?.Invoke(friend);
    }

    private void HandleMemberLeft(Lobby lobby, Friend friend)
    {
        // When the host leaves, Steam hands the lobby to whoever is left: the
        // browser should show the new host's name.
        if (IsHost) lobby.SetData(HostNameKey, SteamClient.Name);
        OnMemberLeft?.Invoke(friend);
    }
}
