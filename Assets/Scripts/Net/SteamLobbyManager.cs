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
    private const string HostIdKey = "hostId"; // a search can't ask who owns a lobby; the browser drops blocked hosts by this
    private const string VisibilityKey = "vis";
    private const string VisibilityPref = "lobby.visibility";
    private const string RatingKey = "rating"; // member data: each member's own rating, for the seats and the roster
    private const string BotsKey = "bots";     // the host's bots, their levels in order ("2,3", Opponent)
    private const string TeamPrefix = "team."; // a seat's team: "team.<steam id>", or a bot's "team.bot<index>"

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
        SteamMatchmaking.OnLobbyMemberDataChanged += HandleMemberDataChanged;
        SteamFriends.OnGameLobbyJoinRequested += HandleJoinRequested;
        PlayerRating.OnChanged += ShareRating;
        BlockList.OnChanged += EnforceBlocks;
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
        SteamMatchmaking.OnLobbyMemberDataChanged -= HandleMemberDataChanged;
        SteamFriends.OnGameLobbyJoinRequested -= HandleJoinRequested;
        PlayerRating.OnChanged -= ShareRating;
        BlockList.OnChanged -= EnforceBlocks;
    }

    // Leave explicitly on quit so the other player is told right away;
    // otherwise Steam only notices once the connection times out.
    private void OnApplicationQuit()
    {
        LeaveLobby();
    }

    // mode: what the lobby plays for; left out, the host's last pick. A
    // ranked lobby has Ranked's rules, nothing of the host's.
    public async void CreateLobby(LobbyVisibility visibility, MatchMode? mode = null)
    {
        // The host's last-used rules are where the lobby starts, seats
        // included, within what the mode allows.
        var settings = mode == MatchMode.Ranked ? MatchSettings.ForRanked() : MatchSettings.LoadPrefs();
        if (mode.HasValue) settings.Set(MatchSettingId.Mode, (int)mode.Value);
        if (settings.RankedMode && mode != MatchMode.Ranked) settings.Set(MatchSettingId.Mode, (int)MatchMode.Normal);
        settings.ApplyMode();
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
        lobby.SetData(ProtocolKey, NetProtocol.Version.ToString());
        lobby.SetData(StateKey, Open);
        lobby.SetData(HostNameKey, SteamClient.Name);
        lobby.SetData(HostIdKey, SteamClient.SteamId.Value.ToString());
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

    // Public lobbies of this game, on this protocol, waiting for a player,
    // of one mode or any. Steam sorts them nearest first.
    public async Task<Lobby[]> FindOpenLobbies(MatchMode? mode = null)
    {
        var query = SteamMatchmaking.LobbyList
            .WithKeyValue(GameKey, GameId)
            .WithKeyValue(ProtocolKey, NetProtocol.Version.ToString())
            .WithKeyValue(StateKey, Open)
            .WithSlotsAvailable(1)
            .FilterDistanceWorldwide()
            .WithMaxResults(20);
        if (mode.HasValue) query = query.WithKeyValue(RulePrefix + MatchSettings.Defs[(int)MatchSettingId.Mode].Key, ((int)mode.Value).ToString());
        var lobbies = await query.RequestAsync();
        return (lobbies ?? Array.Empty<Lobby>()).Where(l => !(ulong.TryParse(l.GetData(HostIdKey), out var host) && BlockList.IsBlocked(host))).ToArray();
    }

    // The nearest open public lobby of the mode (Normal, or Ranked for the
    // ranked quick match), or failing that a new one to wait in. A lobby
    // can fill between the search and the join, so each is tried in turn.
    public async void QuickMatch(MatchMode mode)
    {
        IsSearching = true;
        var lobbies = await FindOpenLobbies(mode);
        IsSearching = false;
        IsJoining = true;
        foreach (var lobby in lobbies)
        {
            if ((await SteamMatchmaking.JoinLobbyAsync(lobby.Id)).HasValue) return; // HandleLobbyEntered finishes the join
        }
        IsJoining = false;
        CreateLobby(LobbyVisibility.Public, mode);
    }

    public static string HostName(Lobby lobby) => lobby.GetData(HostNameKey);

    // The rules a lobby advertises, e.g. for a lobby browser row.
    public static MatchSettings RulesOf(Lobby lobby) => MatchSettings.FromPairs(key => lobby.GetData(RulePrefix + key));

    // Steam lobbies can't eject anyone, so the host asks the guest's game to
    // leave (Msg.Kick), and asks again if they come back.
    public void Kick(ulong memberId)
    {
        if (!IsHost) return;
        kicked.Add(memberId);
        NetSession.Current?.Send(memberId, new Msg.Kick());
    }

    // Match rules live in lobby data: only the owner may write them, every
    // member reads the same values. What a match actually uses is the copy
    // the host sends with LoadGameScene, so a late data update can't split
    // the two sides.
    public void SetLobbySettings(MatchSettings settings)
    {
        if (!IsHost) return;
        WriteSettings(CurrentLobby.Value, settings);
        // Fewer seats: the bots go first, the players never.
        var bots = Bots.ToList();
        var room = Mathf.Max(0, settings.Seats - CurrentLobby.Value.MemberCount);
        if (bots.Count > room) WriteBots(bots.Take(room).ToList(), settings);
        else FitMembers(settings, bots.Count);
    }

    // Seats is how many may be in the lobby, bots included; never fewer
    // players than are in it.
    private void FitMembers(MatchSettings settings, int bots)
    {
        var lobby = CurrentLobby.Value;
        lobby.MaxMembers = Mathf.Max(settings.Seats - bots, lobby.MemberCount);
    }

    // ---- The host's bots and the teams ----

    // One seat as the host would start the match: a member or a bot.
    public sealed class LobbySeat
    {
        public ulong Id;       // the member's Steam id, or MatchRoster.BotId
        public string Name;    // the member's (a bot's is LobbySceneUI's to give)
        public Opponent Who;   // Human for a member
        public int BotIndex = -1;
        public int Team;
        public bool IsBot => BotIndex >= 0;
    }

    public static List<Opponent> BotsOf(Lobby lobby) =>
        (lobby.GetData(BotsKey) ?? string.Empty).Split(',').Where(v => int.TryParse(v, out _)).Select(v => (Opponent)int.Parse(v)).Where(o => o != Opponent.Human).ToList();

    public IReadOnlyList<Opponent> Bots => CurrentLobby.HasValue ? BotsOf(CurrentLobby.Value) : new List<Opponent>();

    // A bot into the first open seat (the host's AI plays it).
    public void AddBot(Opponent level)
    {
        if (!IsHost) return;
        var settings = ReadLobbySettings();
        var bots = Bots.ToList();
        if (CurrentLobby.Value.MemberCount + bots.Count >= settings.Seats) return;
        bots.Add(level);
        WriteBots(bots, settings);
    }

    public void RemoveBot(int index)
    {
        if (!IsHost) return;
        var lobby = CurrentLobby.Value;
        var seats = SeatsOf(lobby);
        var bots = Bots.ToList();
        if (index < 0 || index >= bots.Count) return;
        bots.RemoveAt(index);
        // The later bots move up a place: their teams with them.
        var botSeats = seats.Where(seat => seat.IsBot && seat.BotIndex != index).ToList();
        for (var i = 0; i < botSeats.Count; i++) lobby.SetData(TeamPrefix + "bot" + i, botSeats[i].Team.ToString());
        lobby.DeleteData(TeamPrefix + "bot" + botSeats.Count); // the place left open: a new bot there starts afresh
        WriteBots(bots, ReadLobbySettings());
    }

    public void SetBotLevel(int index, Opponent level)
    {
        if (!IsHost) return;
        var bots = Bots.ToList();
        if (index < 0 || index >= bots.Count || level == Opponent.Human) return;
        bots[index] = level;
        WriteBots(bots, ReadLobbySettings());
    }

    private void WriteBots(List<Opponent> bots, MatchSettings settings)
    {
        CurrentLobby.Value.SetData(BotsKey, string.Join(",", bots.Select(b => (int)b)));
        FitMembers(settings, bots.Count);
    }

    public void SetTeam(LobbySeat seat, int team)
    {
        if (IsHost) CurrentLobby.Value.SetData(TeamKey(seat), team.ToString());
    }

    private static string TeamKey(LobbySeat seat) => TeamPrefix + (seat.IsBot ? "bot" + seat.BotIndex : seat.Id.ToString());

    // The seats as the lobby lists them: the members (SeatOrder), then the
    // bots. A seat no one has put on a team is on the one its place gives.
    public static List<LobbySeat> SeatsOf(Lobby lobby)
    {
        var seats = SeatOrder(lobby).Select(m => new LobbySeat { Id = m.Id.Value, Name = m.Name, Who = Opponent.Human }).ToList();
        var bots = BotsOf(lobby);
        for (var i = 0; i < bots.Count; i++) seats.Add(new LobbySeat { Id = MatchRoster.BotId(i), Who = bots[i], BotIndex = i });
        seats = seats.Take(MatchRoster.MaxPlayers).ToList();
        for (var i = 0; i < seats.Count; i++) seats[i].Team = int.TryParse(lobby.GetData(TeamKey(seats[i])), out var team) ? team : i % 2;
        return seats;
    }

    // Two teams of two: the host's and one other.
    public static bool TeamsReady(List<LobbySeat> seats) => seats.Count == 4 && seats.Count(seat => seat.Team == seats[0].Team) == 2;

    // The seats in the order the match puts them: as listed, or with teams
    // the host's team in seats 0 and 2 and the other in 1 and 3, so the
    // teammates sit across from each other and the turns go team to team.
    public static List<LobbySeat> MatchOrder(List<LobbySeat> seats, bool teams)
    {
        if (!teams || !TeamsReady(seats)) return seats;
        var hostTeam = seats[0].Team;
        var mate = seats.Skip(1).First(seat => seat.Team == hostTeam);
        var others = seats.Where(seat => seat.Team != hostTeam).ToList();
        return new List<LobbySeat> { seats[0], others[0], mate, others[1] };
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

    public MatchRoster BuildRoster() => RosterOf(MatchOrder(SeatsOf(CurrentLobby.Value), ReadLobbySettings().Teams).Select(seat => seat.Id));

    // These players (and bots), in this order, with the ratings they show in
    // the lobby; a bot as strong as the lobby has it now.
    public MatchRoster RosterOf(IEnumerable<ulong> steamIds)
    {
        var ids = steamIds.ToList();
        var bots = Bots;
        Opponent Who(ulong id) => !MatchRoster.IsBotId(id) ? Opponent.Human : (int)id - 1 < bots.Count ? bots[(int)id - 1] : Opponent.AINormal;
        return new MatchRoster(ids, ids.Select(id => MatchRoster.IsBotId(id) ? Elo.Start : RatingOf(id) ?? Elo.Start), ids.Select(Who));
    }

    // A member's rating as their own game shares it; null until it has.
    public int? RatingOf(ulong steamId)
    {
        if (!CurrentLobby.HasValue) return null;
        return int.TryParse(CurrentLobby.Value.GetMemberData(new Friend(steamId), RatingKey), out var rating) ? rating : (int?)null;
    }

    private void ShareRating()
    {
        if (CurrentLobby.HasValue) CurrentLobby.Value.SetMemberData(RatingKey, PlayerRating.Current.Rating.ToString());
    }

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

    private void HandleMemberDataChanged(Lobby lobby, Friend member) => HandleLobbyDataChanged(lobby);

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

    // No one this player blocked stays in a lobby with them: the host sends
    // them away; a guest leaves.
    private void EnforceBlocks()
    {
        if (!CurrentLobby.HasValue) return;
        var lobby = CurrentLobby.Value;
        var blocked = lobby.Members.Where(m => m.Id.Value != SteamClient.SteamId.Value && BlockList.IsBlocked(m.Id.Value)).ToList();
        if (blocked.Count == 0) return;
        if (IsHost)
        {
            foreach (var member in blocked) Kick(member.Id.Value);
            return;
        }
        LeaveLobby();
        OnLobbyFailed?.Invoke("lobby.status.blocked");
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
        if (!lobby.IsOwnedBy(SteamClient.SteamId) && version != NetProtocol.Version.ToString())
        {
            Debug.LogWarning($"Leaving lobby {lobby.Id}: protocol '{version}', this build speaks {NetProtocol.Version}.");
            lobby.Leave();
            OnLobbyFailed?.Invoke("lobby.status.version");
            return;
        }
        CurrentLobby = lobby;
        // Someone this player blocked is in it: not a lobby to be in.
        if (!IsHost && lobby.Members.Any(m => BlockList.IsBlocked(m.Id.Value)))
        {
            LeaveLobby();
            OnLobbyFailed?.Invoke("lobby.status.blocked");
            return;
        }
        ShareRating();
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
        if (IsHost && kicked.Contains(friend.Id.Value)) NetSession.Current?.Send(friend.Id.Value, new Msg.Kick());
        OnMemberJoined?.Invoke(friend);
        EnforceBlocks();
    }

    private void HandleMemberLeft(Lobby lobby, Friend friend)
    {
        // When the host leaves, Steam hands the lobby to whoever is left: the
        // browser should show the new host's name.
        if (IsHost)
        {
            lobby.SetData(HostNameKey, SteamClient.Name);
            lobby.SetData(HostIdKey, SteamClient.SteamId.Value.ToString());
        }
        OnMemberLeft?.Invoke(friend);
    }
}
