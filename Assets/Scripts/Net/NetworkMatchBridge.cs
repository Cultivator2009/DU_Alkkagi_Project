using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bridges the local TurnController/ruleset core onto a host-authoritative
// network match of two to four players. Only the host runs the match; each
// guest's TurnController is a mirror of it (TurnController.ApplyRemote),
// so the HUD and the rating hear the same events on every screen. A guest
// renders the host's snapshots and forwards its own turn's flick. Who plays
// which side is the host's MatchRoster, sent with LoadGameScene: the host
// is player 0.
//
// Messages go through a scope of NetSession: only from this match's
// players, each only the way its route goes (NetFilters).
//
// Absent entirely for local play - NetworkBootstrap adds it when a Steam
// lobby is active.
public class NetworkMatchBridge : MonoBehaviour
{
    // How long past zero a guest's turn runs on the host's clock (see
    // TurnController.RemoteGraceSeconds): a round trip on a slow relay.
    private const float GuestFlickGraceSeconds = 0.5f;

    // A player quit, dropped or timed out. Before the result the host
    // forfeits their side; a guest whose host left can't go on. Afterwards it
    // only changes who can rematch.
    public event Action<int> OnPlayerLeft;
    public event Action<int> OnPlayerReturnedToLobby;
    public event Action OnRematchStateChanged;

    public bool IsHost => isHost;
    public int LocalPlayerId => localPlayerId;
    // On a guest: the host has the shot playing out fast-forwarded (GamePace).
    public bool HostFastForwarding { get; private set; }
    public int PlayerCount => roster.Count;
    public bool LocalWantsRematch => wantsRematch.Contains(localId);
    // The other players still here, and how many of them asked for a rematch.
    public int OthersPresent => roster.SteamIds.Count(id => id != localId && !gone.Contains(id));
    public int OthersWantingRematch => roster.SteamIds.Count(id => id != localId && !gone.Contains(id) && Wants(id));
    public bool OpponentGone => OthersPresent == 0;
    public bool HostGone => gone.Contains(hostId);
    // Whether the lobby's board (as picked: Random rolls among those that
    // fit) suits the players still here, for a rematch (MatchSettings.BoardFits).
    public bool RematchFits
    {
        get
        {
            var picked = lobby != null && lobby.CurrentLobby.HasValue ? lobby.ReadLobbySettings() : MatchSettings.Picked;
            var players = OthersPresent + 1;
            if (picked.Teams && players != MatchRoster.MaxPlayers) return false; // two teams of two, or no teams match
            return picked.BoardType == BoardType.Random || MatchSettings.BoardFits(picked.BoardType, players);
        }
    }
    public bool PlayerGone(int playerId) => playerId >= 0 && playerId < roster.Count && gone.Contains(roster.SteamIds[playerId]);
    public ulong SteamIdOf(int playerId) => playerId >= 0 && playerId < roster.Count ? roster.SteamIds[playerId] : 0;
    // This player's rating in a rated match, or null.
    public RatingTracker Rating { get; private set; }

    // A seat's Steam name (a number while Steam isn't there to ask).
    public string PlayerName(int playerId)
    {
        if (playerId < 0 || playerId >= roster.Count) return string.Empty;
        if (roster.IsBot(playerId)) return Loc.Get("bot." + roster.Who(playerId));
        return SteamClient.IsValid ? new Friend(roster.SteamIds[playerId]).Name : Loc.Get("player.number", playerId + 1);
    }

    public bool IsBot(int playerId) => roster.IsBot(playerId);

    // A bot always wants the rematch.
    private bool Wants(ulong id) => MatchRoster.IsBotId(id) || wantsRematch.Contains(id);

    private NetSession session;
    private NetScope scope;
    private SteamLobbyManager lobby;
    private GameManager gameManager;
    private MatchRoster roster;

    private bool isHost;
    private ulong localId;
    private ulong hostId;
    private int localPlayerId;
    private const int SnapshotPieces = 30;
    private int hostTick;  // the host's physics steps, stamped on its snapshots
    private readonly Dictionary<char, PieceTransform> lastSent = new Dictionary<char, PieceTransform>();
    private bool matchResolved;
    private bool hostInitialized;
    private bool matchBegun;
    private readonly HashSet<ulong> readyGuests = new HashSet<ulong>(); // loaded and spawned
    private readonly HashSet<ulong> gone = new HashSet<ulong>();        // left, or back in the lobby
    private readonly HashSet<ulong> wantsRematch = new HashSet<ulong>();

    private Dictionary<char, GamePieceDragAndReleaseForce> pieceLookup;
    private HashSet<char> inPlayAtLastResult; // host: the pieces the guests last heard of
    private PieceSelector guestSelector;
    private bool awaitingTurnResult;
    private bool sentFastForward; // on the host: what the guests were last told
    // A guest plays the host's poses back a few steps behind the newest
    // heard (PlaybackDelay), between two real poses - never guessed ahead -
    // so a knock shows where it happened. Its clock runs at the rate the
    // steps come in (the host's pace, fast-forward and hit-stops included),
    // pulled toward that delay.
    private struct Pose
    {
        public int Tick;
        public Vector3 Position;
        public Quaternion Rotation;
    }
    private readonly Dictionary<char, List<Pose>> tracks = new Dictionary<char, List<Pose>>();
    private int newestTick = -1;
    private float newestHeardAt;
    private float playTick;
    private float tickRate = GamePace.Speed / 0.02f; // steps a real second, as heard
    private const float PlaybackDelay = 4f;  // steps: covers a late packet or two
    private const float CatchUp = 3f;        // 1/s, the clock toward that delay
    private const float SnapBehind = 20f;    // steps off: jumps there (a new shot, after a wait)
    private const int StillGap = 3;          // steps unheard of a piece: it lay still meanwhile

    private IEnumerable<ulong> Guests => roster.SteamIds.Skip(1).Where(id => !MatchRoster.IsBotId(id)); // the bots are the host's
    private TurnController Controller => gameManager.TurnController;
    private static ItemSystem Items => ItemSystem.Instance; // the item mode's, or null

    private void Start()
    {
        session = NetSession.Current;
        lobby = SteamLobbyManager.Instance;
        gameManager = GameManager.manager;

        if (session == null || lobby == null || !lobby.CurrentLobby.HasValue)
        {
            enabled = false; // no active Steam lobby - this is a local match, stay dormant
            return;
        }

        localId = session.LocalId;
        roster = MatchRoster.Current ?? lobby.BuildRoster();
        hostId = roster.SteamIds[0];
        isHost = hostId == localId;
        localPlayerId = Mathf.Max(0, roster.PlayerOf(localId));
        gameManager.SkipLocalTurnProcessing = !isHost;
        OpenScope();
        session.Transport.OnPeerDisconnected += PlayerDeparted;
        lobby.OnMemberLeft += HandleMemberLeft;

        StartCoroutine(WaitForTurnControllerThenInit());
    }

    private void OpenScope()
    {
        scope = session.Scope("match")
            .Use(NetFilters.From(id => roster.PlayerOf(id) >= 0))
            .Use(NetFilters.NotBlocked())
            .Use(NetFilters.Authority(() => hostId, () => isHost))
            .On<Msg.RematchRequest>(HandleRematchRequest)
            .On<Msg.ReturnToLobby>(HandleReturnToLobby)
            // The host's
            .On<Msg.ClientReady>(HandleClientReady)
            .On<Msg.Flick>(HandleFlick)
            .On<Msg.Pass>(HandlePass)
            .On<Msg.PlaceRequest>(HandlePlaceRequest)
            .On<Msg.PlacementReady>(HandlePlacementReady)
            .On<Msg.Concede>((sender, _) => Controller.Concede(roster.PlayerOf(sender)))
            .On<Msg.UseItem>(HandleUseItem)
            .On<Msg.DiscardItem>(HandleDiscardItem)
            // The guests'
            .On<Msg.StartMatch>((_, m) => ApplyGuestRole(m.PlayerId, m.Owners))
            .On<Msg.PlacementState>(HandleGuestPlacementState)
            .On<Msg.PieceSnapshot>(HandleGuestSnapshot)
            .On<Msg.BoardSound>((_, m) => StartCoroutine(PlayLater(m.Sound)))
            .On<Msg.TurnResult>(HandleGuestTurnResult)
            .On<Msg.MatchStateUpdate>((_, m) => Controller.ApplyRemote(m.State, Array.Empty<KillEvent>()))
            .On<Msg.FastForward>((_, m) => HostFastForwarding = m.On)
            .On<Msg.Damage>(HandleGuestDamage)
            .On<Msg.ItemUpdate>(HandleGuestItemUpdate)
            .On<Msg.LoadGameScene>(HandleGuestLoadGameScene);
    }

    private void OnDestroy()
    {
        scope?.Dispose();
        if (session != null) session.Transport.OnPeerDisconnected -= PlayerDeparted;
        if (lobby != null) lobby.OnMemberLeft -= HandleMemberLeft;
        BoardSounds.OnEmitted -= ForwardBoardSound;
        GameManager.Damaged -= ForwardDamage;
        if (Items != null) Items.OnUpdate -= BroadcastItems;
    }

    // ---- Players leaving, rematch, back to lobby ----

    private void HandleMemberLeft(Friend friend)
    {
        PlayerDeparted(friend.Id.Value);
    }

    private void PlayerDeparted(ulong steamId)
    {
        var playerId = roster.PlayerOf(steamId);
        if (playerId < 0 || steamId == localId || !gone.Add(steamId)) return;
        wantsRematch.Remove(steamId);
        LeftSeries(playerId);

        if (isHost)
        {
            // Their side forfeits; with two players that's the match. Before
            // the match has begun, TryBeginMatch does it once it does.
            if (matchBegun && !matchResolved) Forfeit(playerId);
            TryBeginMatch(); // a guest that left while loading holds nothing up
            TryStartRematch();
        }
        else if (steamId == hostId && !matchResolved)
        {
            // The host runs the match: nothing more can happen on this board.
            // With two, a win by forfeit; with more, it's just over.
            matchResolved = true;
            if (roster.Count > 2) Controller?.EndWithoutHost(-1, MatchEndReason.HostLeft);
            else Controller?.EndWithoutHost(localPlayerId, MatchEndReason.OpponentLeft);
            gameManager.gameState = GameManager.GameState.MatchOver;
        }
        OnPlayerLeft?.Invoke(playerId);
        OnRematchStateChanged?.Invoke();
    }

    public void RequestRematch()
    {
        if (OpponentGone || LocalWantsRematch) return;
        wantsRematch.Add(localId);
        session.Broadcast(new Msg.RematchRequest());
        OnRematchStateChanged?.Invoke();
        TryStartRematch();
    }

    public void ReturnToLobby()
    {
        session.Broadcast(new Msg.ReturnToLobby());
        gameManager.EndMatch();
        SceneManager.LoadScene("LobbyScene");
    }

    private void HandleRematchRequest(ulong sender, Msg.RematchRequest _)
    {
        wantsRematch.Add(sender);
        OnRematchStateChanged?.Invoke();
        TryStartRematch();
    }

    private void HandleReturnToLobby(ulong sender, Msg.ReturnToLobby _)
    {
        gone.Add(sender);
        wantsRematch.Remove(sender);
        LeftSeries(roster.PlayerOf(sender));
        OnPlayerReturnedToLobby?.Invoke(roster.PlayerOf(sender));
        OnRematchStateChanged?.Invoke();
        TryStartRematch();
    }

    // The host owns scene flow: once everyone still here has asked, it
    // restarts the match with them, the same way the lobby's Start button
    // does.
    private void TryStartRematch()
    {
        if (!isHost || !LocalWantsRematch) return;
        var present = roster.SteamIds.Where(id => !gone.Contains(id)).ToList();
        if (present.Count < 2 || !present.All(Wants) || !RematchFits) return;
        if (Series != null)
        {
            // Both ready for the next game: no need to wait out the clock.
            if (!Series.Over) StartNextGame();
            return;
        }
        MatchRoster.Current = lobby.RosterOf(present); // their ratings after this match
        MatchSettings.Current = MatchSettings.Picked.Resolve(present.Count); // Random rolls again
        session.Broadcast(new Msg.LoadGameScene { Settings = MatchSettings.Current, Roster = MatchRoster.Current });
        gameManager.EndMatch();
        SceneManager.LoadScene("GameScene");
    }

    private IEnumerator WaitForTurnControllerThenInit()
    {
        while (Controller == null || gameManager.gamePieceScripts.Count == 0) yield return null;

        BuildPieceLookup();

        // A ranked game: counted for the series before anything else hears
        // the result. From its first game on, leaving is held as a loss.
        if (Series != null)
        {
            if (Series.Game == 1) Series.Hold(SeriesRatings(), localPlayerId);
            Controller.OnMatchEnded += SeriesGameEnded;
        }
        if (isHost) InitHost();
        else InitGuest();
        // Rated match by match (a series rates itself): a rated mode played by
        // its rules, everyone the roster lists in it. It counts from here:
        // leaving is held as a loss.
        if (MatchSettings.Current.Rated && Series == null && roster.Count == gameManager.Sides.Count) Rating = new RatingTracker(Controller, roster.Ratings, localPlayerId);
    }

    // ---- Ranked (RankedSeries) ----

    public const float NextGameSeconds = 8f;
    public RankedSeries Series => MatchSettings.Current.RankedMode ? RankedSeries.Current : null;
    // When the host starts the next game (real time), while there is one to come.
    public float NextGameAt { get; private set; } = -1;

    // The roster's ratings, this player's own from their record (as RatingTracker).
    private int[] SeriesRatings()
    {
        var ratings = roster.Ratings.ToArray();
        ratings[localPlayerId] = PlayerRating.Current.Rating;
        return ratings;
    }

    // Every screen counts the game the same: won, lost or drawn - or, won
    // because the other left, the whole series. The host then starts the
    // next game a moment later.
    private void SeriesGameEnded(int winner, MatchEndReason reason)
    {
        var series = Series;
        if (reason == MatchEndReason.OpponentLeft && winner >= 0) series.Forfeit(1 - winner);
        else series.Record(winner);
        if (series.Over)
        {
            series.Apply(SeriesRatings(), localPlayerId);
            NextGameAt = -1;
            return;
        }
        NextGameAt = Time.realtimeSinceStartup + NextGameSeconds;
        if (isHost) StartCoroutine(StartNextGameSoon());
    }

    // Leaving between games (or the scene) is leaving the series.
    private void LeftSeries(int playerId)
    {
        var series = Series;
        if (series == null || series.Over || playerId < 0) return;
        series.Forfeit(playerId);
        series.Apply(SeriesRatings(), localPlayerId);
        NextGameAt = -1;
    }

    private IEnumerator StartNextGameSoon()
    {
        while (Time.realtimeSinceStartup < NextGameAt) yield return null;
        if (Series != null && !Series.Over && !OpponentGone) StartNextGame();
    }

    // The same two, a new board and pieces rolled, the loser of the last
    // game first (RankedSeries.Next).
    private void StartNextGame()
    {
        NextGameAt = -1;
        MatchRoster.Current = lobby.RosterOf(roster.SteamIds);
        RankedSeries.Current = Series.Next();
        MatchSettings.Current = MatchSettings.Picked.Resolve(roster.Count);
        session.Broadcast(new Msg.LoadGameScene { Settings = MatchSettings.Current, Roster = MatchRoster.Current, Series = RankedSeries.Current });
        gameManager.EndMatch();
        SceneManager.LoadScene("GameScene");
    }

    private void BuildPieceLookup()
    {
        pieceLookup = new Dictionary<char, GamePieceDragAndReleaseForce>();
        foreach (var piece in gameManager.gamePieceScripts)
        {
            var id = piece.Manager.pieceID;
            // Every message addresses pieces by this id - a duplicate silently
            // merges two pieces into one lookup entry and desyncs the match.
            if (pieceLookup.TryGetValue(id, out var existing))
                Debug.LogError($"Duplicate pieceID '{id}' on {existing.name} and {piece.name} - set unique ids in GameScene.");
            pieceLookup[id] = piece;
        }
    }

    // ---- Host ----

    private void InitHost()
    {
        var controller = Controller;
        controller.PieceSelector.LocalPlayerId = localPlayerId;
        // The host's AI plays its bots' sides, through the same flicks as a guest's.
        for (var seat = 0; seat < roster.Count; seat++)
        {
            if (!roster.IsBot(seat)) continue;
            var bot = new GameObject("Bot " + seat).AddComponent<AIOpponent>();
            bot.playerId = seat;
            bot.level = LocalOpponent.Level(roster.Who(seat));
        }
        controller.HostPlayerId = localPlayerId;
        controller.RemoteGraceSeconds = GuestFlickGraceSeconds;
        controller.OnTurnStarted += _ => BroadcastTurnResult();
        controller.OnMatchEnded += HandleHostMatchEnded;
        controller.OnPlayerOut += HandleHostPlayerOut;
        // The edge giving way: the shot before it and the crumble go out now,
        // so the guests see the board go when it goes.
        controller.OnZoneChanged += () =>
        {
            if (controller.Collapsing) BroadcastTurnResult();
        };
        BoardSounds.OnEmitted += ForwardBoardSound;
        GameManager.Damaged += ForwardDamage;
        if (Items != null)
        {
            Items.OnUpdate += BroadcastItems;
            // An item's motion over: the side to move's turn goes on, on every screen.
            controller.OnMotionEnded += BroadcastTurnResult;
        }
        inPlayAtLastResult = new HashSet<char>(pieceLookup.Keys);
        hostInitialized = true;
        TryBeginMatch();
    }

    // Every guest has loaded the scene and spawned its stones (or left): only
    // now does the match (placement or the first turn) start, so neither the
    // host's first shot nor its placement clock runs ahead of a guest still
    // loading. Guests that left on the way forfeit straight away.
    private void TryBeginMatch()
    {
        if (!hostInitialized || matchBegun || !Guests.All(id => readyGuests.Contains(id) || gone.Contains(id))) return;
        matchBegun = true;
        gameManager.BeginMatch(hotSeat: false);
        foreach (var id in Guests.Where(gone.Contains).ToList()) Forfeit(roster.PlayerOf(id));
        if (gameManager.Placement == null || matchResolved) return;
        gameManager.Placement.OnChanged += SendPlacementState;
        SendPlacementState();
    }

    private void Forfeit(int playerId)
    {
        Controller.Forfeit(playerId, MatchEndReason.OpponentLeft);
        // Mid-placement GameManager isn't following the controller yet.
        if (Controller.State == GameManager.GameState.MatchOver) gameManager.gameState = GameManager.GameState.MatchOver;
    }

    // Each guest its own view: hidden placement leaves out the other sides'
    // stones until it's over.
    private void SendPlacementState()
    {
        foreach (var id in Guests.Where(id => !gone.Contains(id))) SendPlacementStateTo(id);
    }

    private void SendPlacementStateTo(ulong guestId)
    {
        session.Send(guestId, new Msg.PlacementState { State = gameManager.Placement.SnapshotFor(roster.PlayerOf(guestId)) });
    }

    // The guests' pieces never collide, so they hear the host's. Not a
    // guest's own flicks: it played those when it let go.
    private void ForwardBoardSound(BoardSoundEvent sound)
    {
        foreach (var id in Guests.Where(id => !gone.Contains(id)))
        {
            if (sound.Kind == BoardSound.Flick && sound.Owner == roster.PlayerOf(id)) continue;
            session.Send(id, new Msg.BoardSound { Sound = sound });
        }
    }

    // A battle of health's knocks, as they land: the guests show them (the
    // turn's result has the health that counts).
    private void ForwardDamage(char pieceId, int amount, Vector3 at)
    {
        if (!pieceLookup.TryGetValue(pieceId, out var piece)) return;
        var manager = piece != null ? piece.Manager : null;
        var health = gameManager.Ruleset is HealthRuleset rules && rules.Rule == HealthRule.Side
            ? gameManager.Sides[manager != null ? manager.playerIndex : 0].Health
            : manager != null ? manager.health : 0;
        session.Broadcast(new Msg.Damage { PieceId = pieceId, Amount = amount, Health = health, Position = at });
    }

    private void HandleGuestDamage(ulong _, Msg.Damage message)
    {
        if (!pieceLookup.TryGetValue(message.PieceId, out var piece) || piece == null) return;
        var manager = piece.Manager;
        if (MatchSettings.Current.HealthRule == HealthRule.Side) gameManager.Sides[manager.playerIndex].Health = message.Health;
        else
        {
            var side = gameManager.Sides[manager.playerIndex];
            side.Health = Mathf.Max(0, side.Health - (manager.health - message.Health));
            manager.health = message.Health;
            if (message.Health <= 0) BoardSounds.Break(message.PieceId); // the host's own word may be lost on the way
        }
        GameManager.ShowDamage(message.PieceId, message.Amount, message.Position);
    }

    // The match, the last shot's kill feed, what went for good since the
    // guests last heard, and where everything is.
    private Msg.TurnResult CurrentResult()
    {
        var inPlay = new HashSet<char>(gameManager.gamePieceScripts.Select(p => p.Manager.pieceID));
        var result = new Msg.TurnResult
        {
            State = Controller.Snapshot(),
            Kills = Controller.LastTurnEnd == TurnEnd.Shot || Controller.LastTurnEnd == TurnEnd.Collapse ? Controller.Kills.LastShot.ToList() : new List<KillEvent>(),
            Removed = inPlayAtLastResult.Where(id => !inPlay.Contains(id)).ToList(),
            Pieces = CurrentTransforms(),
            Items = Items?.State,
        };
        inPlayAtLastResult = inPlay;
        return result;
    }

    private void BroadcastTurnResult() => session.Broadcast(CurrentResult());

    private void HandleHostMatchEnded(int winner, MatchEndReason reason)
    {
        matchResolved = true;
        BroadcastTurnResult();
    }

    // A side conceding or leaving mid-turn; a knockout goes with the turn's
    // result, after the shot's kill feed.
    private void HandleHostPlayerOut(int playerId, MatchEndReason reason)
    {
        if (reason != MatchEndReason.Knockout) session.Broadcast(new Msg.MatchStateUpdate { State = Controller.Snapshot() });
    }

    // A resync: the match as it is, told again to one guest.
    private void SendCurrentTurnTo(ulong targetId)
    {
        session.Send(targetId, new Msg.TurnResult
        {
            State = Controller.Snapshot(),
            Pieces = CurrentTransforms(),
            Removed = pieceLookup.Keys.Where(id => pieceLookup[id] == null || !pieceLookup[id].gameObject.activeSelf).ToList(),
            Items = Items?.State,
        });
    }

    // ---- Items (ItemSystem) ----

    // A guest's item: the host decides. Turned down, the guest is told the
    // items as they are, so it stops waiting.
    private void HandleUseItem(ulong sender, Msg.UseItem message)
    {
        if (Items != null && !Items.TryUse(roster.PlayerOf(sender), message.Slot, message.Target)) Items.Resend();
    }

    private void HandleDiscardItem(ulong sender, Msg.DiscardItem message)
    {
        if (Items != null && !Items.Discard(roster.PlayerOf(sender), message.Index)) Items.Resend();
    }

    private void BroadcastItems(IReadOnlyList<ItemEvent> events, IReadOnlyList<char> moved, IReadOnlyList<char> revived)
    {
        var shown = new HashSet<char>(moved.Concat(revived));
        session.Broadcast(new Msg.ItemUpdate
        {
            State = Items.State,
            Events = events.ToList(),
            Pieces = CurrentTransforms().Where(t => shown.Contains(t.PieceId)).ToList(),
            Revived = revived.ToList(),
            Sides = Controller.Snapshot().Sides,
        });
    }

    // The host's items: pieces it brought back come back here too, pieces
    // it moved jump there (no easing: they went in an instant), and the
    // sides as an item left them.
    private void HandleGuestItemUpdate(ulong _, Msg.ItemUpdate message)
    {
        if (Items == null) return;
        Items.RestoreRemote(message.Revived);
        foreach (var t in message.Pieces)
        {
            if (!pieceLookup.TryGetValue(t.PieceId, out var piece) || piece == null) continue;
            piece.Body.position = t.Position;
            piece.Body.rotation = t.Rotation;
            piece.transform.SetPositionAndRotation(t.Position, t.Rotation);
            piece.Manager.health = t.Health;
            tracks[t.PieceId] = new List<Pose> { new Pose { Tick = newestTick, Position = t.Position, Rotation = t.Rotation } };
        }
        Controller.ApplyRemoteSides(message.Sides);
        Items.ApplyRemote(message.State, message.Events);
    }

    // The physics pose, not transform: with interpolation on, transform is
    // the render pose, which lags or overshoots the simulated one. A piece
    // put back since the last word jumps there on the guests.
    private List<PieceTransform> CurrentTransforms()
    {
        var transforms = new List<PieceTransform>(gameManager.gamePieceScripts.Count);
        foreach (var piece in gameManager.gamePieceScripts)
        {
            if (piece == null) continue;
            var body = piece.Body;
            transforms.Add(new PieceTransform { PieceId = piece.Manager.pieceID, Position = body.position, Rotation = body.rotation, Health = piece.Manager.health });
        }
        return transforms;
    }

    private void HandleClientReady(ulong sender, Msg.ClientReady _)
    {
        var owners = gameManager.gamePieceScripts.Select(p => new PieceOwnerEntry { PieceId = p.Manager.pieceID, PlayerId = p.Manager.playerIndex }).ToList();
        session.Send(sender, new Msg.StartMatch { PlayerId = roster.PlayerOf(sender), Owners = owners });
        readyGuests.Add(sender);
        TryBeginMatch();
    }

    private void HandleFlick(ulong sender, Msg.Flick message)
    {
        // Unity-null once it's gone: a lagging guest board can still name a
        // piece that's already out. Only the sender's own pieces.
        pieceLookup.TryGetValue(message.PieceId, out var piece);
        if (piece != null && (piece.Manager.playerIndex != roster.PlayerOf(sender) || !piece.gameObject.activeSelf)) piece = null; // an item mode's piece kept off the board
        var controller = Controller;
        // Rejected while idle means the guest disagrees about the board or
        // whose turn it is - resend the current state so its view and input
        // line up again. (Mid-turn rejections need nothing: the turn's own
        // TurnResult will arrive.)
        if (!controller.TryApplyExternalFlick(piece, message.Force) && controller.State == GameManager.GameState.WaitingForInput) SendCurrentTurnTo(sender);
    }

    private void HandlePass(ulong sender, Msg.Pass _)
    {
        if (!Controller.TryPassTurn(roster.PlayerOf(sender)) && Controller.State == GameManager.GameState.WaitingForInput) SendCurrentTurnTo(sender);
    }

    private void HandlePlaceRequest(ulong sender, Msg.PlaceRequest message)
    {
        var phase = gameManager.Placement;
        // Rejected: resend, so the guest's optimistic stone goes back.
        if (phase != null && !phase.TryPlace(roster.PlayerOf(sender), message.PieceId, new Vector3(message.X, 0, message.Z))) SendPlacementStateTo(sender);
    }

    private void HandlePlacementReady(ulong sender, Msg.PlacementReady _)
    {
        if (gameManager.Placement != null && !gameManager.Placement.TryReady(roster.PlayerOf(sender))) SendPlacementStateTo(sender);
    }

    private void FixedUpdate()
    {
        if (isHost) HostFixedUpdate();
    }

    // Every step of a shot, the pieces that moved since they were last sent.
    private void HostFixedUpdate()
    {
        hostTick++;
        if (Controller == null) return;
        if (gameManager.gameState != GameManager.GameState.ProcessingTurn) return;

        var moved = CurrentTransforms().Where(t => !lastSent.TryGetValue(t.PieceId, out var sent) || (sent.Position - t.Position).sqrMagnitude > 1e-8f
                                                   || Quaternion.Angle(sent.Rotation, t.Rotation) > 0.1f || sent.Health != t.Health).ToList();
        if (moved.Count == 0) return;
        foreach (var t in moved) lastSent[t.PieceId] = t;
        // Under an unreliable packet's 1200 bytes (about 32 a piece).
        for (var i = 0; i < moved.Count; i += SnapshotPieces)
            session.Broadcast(new Msg.PieceSnapshot { Tick = hostTick, Pieces = moved.GetRange(i, Mathf.Min(SnapshotPieces, moved.Count - i)) });
    }

    // ---- Guest ----

    private void InitGuest()
    {
        Controller.BecomeMirror();
        if (Items != null)
        {
            Items.BecomeMirror();
            Items.OnRequestUse += (slot, target) => session.Send(hostId, new Msg.UseItem { Slot = slot, Target = target });
            Items.OnRequestDiscard += index => session.Send(hostId, new Msg.DiscardItem { Index = index });
            Items.OnPieceRestored += piece =>
            {
                pieceLookup[piece.Manager.pieceID] = piece;
                tracks.Remove(piece.Manager.pieceID); // back where it's put, not eased there from where it went
            };
        }
        guestSelector = new PieceSelector(gameManager.gamePieceScripts) { LocalPlayerId = null };
        session.Send(hostId, new Msg.ClientReady());
    }

    private void ApplyGuestRole(int assignedPlayerId, List<PieceOwnerEntry> hostOwners)
    {
        localPlayerId = assignedPlayerId;

        // Every side spawns from the host's settings, so the stone sets must
        // match exactly; if they don't, the builds disagree about the rules.
        if (hostOwners.Count != pieceLookup.Count || hostOwners.Any(o => !pieceLookup.ContainsKey(o.PieceId)))
            Debug.LogError($"Host has {hostOwners.Count} stones, this guest spawned {pieceLookup.Count} - are both on the same commit?");

        foreach (var kv in pieceLookup)
        {
            var pieceId = kv.Key;
            var piece = kv.Value;
            var rb = piece.Body;
            // Kinematic bodies don't support ContinuousDynamic; set the mode
            // first so the switch doesn't warn. Posed every frame (Play), not
            // interpolated between physics steps.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            piece.isAuthority = false;
            piece.OnFlickRequested += force =>
            {
                awaitingTurnResult = true;
                Controller.MirrorShotUnderway();
                session.Send(hostId, new Msg.Flick { PieceId = pieceId, Force = force });
            };
        }

        guestSelector.LocalPlayerId = localPlayerId;
    }

    private void Update()
    {
        // The host's fast-forward, as it's turned on or off (the guests
        // follow its snapshots anyway; this is so they know why).
        if (isHost && hostInitialized && GamePace.FastForwarding != sentFastForward)
        {
            sentFastForward = GamePace.FastForwarding;
            session.Broadcast(new Msg.FastForward { On = sentFastForward });
        }
        if (!isHost && pieceLookup != null) Play();
        if (isHost || pieceLookup == null || guestSelector == null || !guestSelector.LocalPlayerId.HasValue) return;

        // Turns only; not while placing stones or after the result.
        if (gameManager.gameState != GameManager.GameState.WaitingForInput) return;
        // One flick per turn: after sending it, wait for the host's verdict
        // (the TurnResult that ends the turn) before accepting another.
        if (awaitingTurnResult || !Controller.Ready) return;
        var picked = guestSelector.TrySelect(Controller.CurrentPlayerID);
        if (picked != null) picked.isDragging = true;
    }

    // The guest's clock a frame on, and every piece where the host had it
    // then.
    private void Play()
    {
        var dt = Time.unscaledDeltaTime;
        var target = newestTick - PlaybackDelay;
        if (Mathf.Abs(target - playTick) > SnapBehind) playTick = target;
        else playTick += dt * tickRate + (target - playTick) * (1 - Mathf.Exp(-CatchUp * dt));
        foreach (var kv in pieceLookup)
        {
            var piece = kv.Value;
            if (piece == null || !piece.gameObject.activeSelf || !tracks.TryGetValue(kv.Key, out var track) || track.Count == 0) continue;
            // Behind the clock, only the pose just before it is still needed.
            while (track.Count > 2 && track[1].Tick <= playTick) track.RemoveAt(0);
            var from = track[0];
            var to = track.Count > 1 ? track[1] : from;
            var t = to.Tick > from.Tick ? Mathf.Clamp01((playTick - from.Tick) / (to.Tick - from.Tick)) : 1;
            var position = Vector3.Lerp(from.Position, to.Position, t);
            var rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
            var body = piece.Body;
            body.position = position;
            body.rotation = rotation;
            piece.transform.SetPositionAndRotation(position, rotation);
        }
    }

    // A piece's pose at a host step. One unheard of for a while lay still
    // till just before it (only what moves is sent).
    private void Track(PieceTransform pose, int tick)
    {
        if (pieceLookup.TryGetValue(pose.PieceId, out var piece) && piece != null) piece.Manager.health = pose.Health;
        if (!tracks.TryGetValue(pose.PieceId, out var track)) tracks[pose.PieceId] = track = new List<Pose>();
        if (track.Count > 0 && tick <= track[track.Count - 1].Tick) return; // late, overtaken
        if (track.Count == 0 && piece != null) track.Add(new Pose { Tick = tick - 1, Position = piece.Body.position, Rotation = piece.Body.rotation });
        else if (track.Count > 0 && tick - track[track.Count - 1].Tick > StillGap)
        {
            var still = track[track.Count - 1];
            still.Tick = tick - 1;
            track.Add(still);
        }
        track.Add(new Pose { Tick = tick, Position = pose.Position, Rotation = pose.Rotation });
    }

    private void Heard(int tick)
    {
        if (tick <= newestTick) return;
        var now = Time.unscaledTime;
        if (newestTick >= 0 && now > newestHeardAt + 1e-3f)
            tickRate = Mathf.Lerp(tickRate, Mathf.Clamp((tick - newestTick) / (now - newestHeardAt), 1f, 400f), 0.15f);
        newestTick = tick;
        newestHeardAt = now;
    }

    private void HandleGuestSnapshot(ulong _, Msg.PieceSnapshot message)
    {
        Controller.MirrorShotUnderway(); // a shot is in flight
        Heard(message.Tick);
        foreach (var t in message.Pieces) Track(t, message.Tick);
    }

    // The host's word on where pieces are, after what it already sent: played
    // after it.
    private void ApplyTransforms(List<PieceTransform> transforms)
    {
        foreach (var t in transforms)
            Track(t, tracks.TryGetValue(t.PieceId, out var track) && track.Count > 0 ? Mathf.Max(newestTick, track[track.Count - 1].Tick) + 1 : newestTick + 1);
    }

    // A knock heard as the pieces are seen to meet: as far behind as they're played.
    private IEnumerator PlayLater(BoardSoundEvent sound)
    {
        var behind = Mathf.Max(0, newestTick - playTick) / Mathf.Max(1f, tickRate);
        if (behind > 0.01f) yield return new WaitForSecondsRealtime(Mathf.Min(behind, 0.3f));
        if (BoardSounds.Instance != null) BoardSounds.Instance.PlayRemote(sound);
    }

    private void HandleGuestTurnResult(ulong _, Msg.TurnResult message)
    {
        awaitingTurnResult = false;
        HostFastForwarding = false; // the shot is over

        foreach (var id in message.Removed)
        {
            if (!pieceLookup.TryGetValue(id, out var piece)) continue;
            pieceLookup.Remove(id);
            if (piece == null) continue;
            gameManager.gamePieceScripts.Remove(piece);
            // The item mode keeps it, as the host does, to come back.
            if (Items != null) Items.BenchRemote(piece);
            else Destroy(piece.gameObject);
        }
        // The host's settled board is the truth; Play brings every piece
        // onto it whatever the snapshots did or didn't deliver.
        ApplyTransforms(message.Pieces);

        var controller = Controller;
        if (Items != null && message.Items != null) Items.ApplyRemote(message.Items, null);
        controller.ApplyRemote(message.State, message.Kills);
        if (controller.State == GameManager.GameState.MatchOver)
        {
            matchResolved = true;
            gameManager.gameState = GameManager.GameState.MatchOver;
            return;
        }
        // The edge giving way is no one's turn: no input until the next.
        gameManager.gameState = controller.Collapsing ? GameManager.GameState.ProcessingTurn : GameManager.GameState.WaitingForInput;
    }

    private void HandleGuestLoadGameScene(ulong _, Msg.LoadGameScene message)
    {
        // A rematch, or the host starting a new match from the lobby while
        // this guest was still on the game-over screen.
        if (message.Roster.PlayerOf(localId) < 0) return; // left out: this player went back to the lobby
        MatchSettings.Current = message.Settings;
        MatchRoster.Current = message.Roster;
        RankedSeries.Current = message.Series;
        gameManager.EndMatch();
        SceneManager.LoadScene("GameScene");
    }

    public bool GuestCanPass => !isHost && !awaitingTurnResult && Controller != null && Controller.CurrentPlayerID == localPlayerId
                                && gameManager.gameState == GameManager.GameState.WaitingForInput;

    // Skip-turn on a guest: the host decides, and its TurnResult ends the turn.
    public void RequestPass()
    {
        if (!GuestCanPass) return;
        foreach (var piece in pieceLookup.Values)
        {
            if (piece == null) continue;
            piece.isDragging = false;
            piece.isSelected = false;
        }
        awaitingTurnResult = true;
        Controller.MirrorShotUnderway();
        session.Send(hostId, new Msg.Pass());
    }

    // Placement on a guest shows at once (the mirror applies the same rules);
    // the host's next PlacementState confirms it or puts it back.
    public void RequestPlace(char pieceId, Vector3 position)
    {
        if (isHost || gameManager.Placement == null) return;
        if (!gameManager.Placement.TryPlace(localPlayerId, pieceId, position)) return;
        session.Send(hostId, new Msg.PlaceRequest { PieceId = pieceId, X = position.x, Z = position.z });
    }

    // The host decides; its MatchState (or, with two, the result) follows.
    public void RequestConcede()
    {
        if (!isHost && !matchResolved) session.Send(hostId, new Msg.Concede());
    }

    public void RequestPlacementReady()
    {
        if (!isHost) session.Send(hostId, new Msg.PlacementReady());
    }

    private void HandleGuestPlacementState(ulong _, Msg.PlacementState message)
    {
        if (gameManager.Placement == null) gameManager.BeginGuestPlacement();
        gameManager.Placement.Apply(message.State);
    }
}
