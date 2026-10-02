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
    private const int SnapshotIntervalFixedFrames = 3;
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
    public int OthersWantingRematch => roster.SteamIds.Count(id => id != localId && !gone.Contains(id) && wantsRematch.Contains(id));
    public bool OpponentGone => OthersPresent == 0;
    public bool HostGone => gone.Contains(hostId);
    // Whether the lobby's board (as picked: Random rolls among those that
    // fit) suits the players still here, for a rematch (MatchSettings.BoardFits).
    public bool RematchFits
    {
        get
        {
            var picked = lobby != null && lobby.CurrentLobby.HasValue ? lobby.ReadLobbySettings() : MatchSettings.Picked;
            return picked.BoardType == BoardType.Random || MatchSettings.BoardFits(picked.BoardType, OthersPresent + 1);
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
        return SteamClient.IsValid ? new Friend(roster.SteamIds[playerId]).Name : Loc.Get("player.number", playerId + 1);
    }

    private NetSession session;
    private NetScope scope;
    private SteamLobbyManager lobby;
    private GameManager gameManager;
    private MatchRoster roster;

    private bool isHost;
    private ulong localId;
    private ulong hostId;
    private int localPlayerId;
    private int snapshotTickCounter;
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
    private readonly Dictionary<char, Vector3> snapshotTargetPosition = new Dictionary<char, Vector3>();
    private readonly Dictionary<char, Quaternion> snapshotTargetRotation = new Dictionary<char, Quaternion>();

    private IEnumerable<ulong> Guests => roster.SteamIds.Skip(1);
    private TurnController Controller => gameManager.TurnController;

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
            // The guests'
            .On<Msg.StartMatch>((_, m) => ApplyGuestRole(m.PlayerId, m.Owners))
            .On<Msg.PlacementState>(HandleGuestPlacementState)
            .On<Msg.PieceSnapshot>(HandleGuestSnapshot)
            .On<Msg.BoardSound>((_, m) =>
            {
                if (BoardSounds.Instance != null) BoardSounds.Instance.PlayRemote(m.Sound);
            })
            .On<Msg.TurnResult>(HandleGuestTurnResult)
            .On<Msg.MatchStateUpdate>((_, m) => Controller.ApplyRemote(m.State, Array.Empty<KillEvent>()))
            .On<Msg.FastForward>((_, m) => HostFastForwarding = m.On)
            .On<Msg.LoadGameScene>(HandleGuestLoadGameScene);
    }

    private void OnDestroy()
    {
        scope?.Dispose();
        if (session != null) session.Transport.OnPeerDisconnected -= PlayerDeparted;
        if (lobby != null) lobby.OnMemberLeft -= HandleMemberLeft;
        BoardSounds.OnEmitted -= ForwardBoardSound;
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
        if (present.Count < 2 || !present.All(wantsRematch.Contains) || !RematchFits) return;
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

        if (isHost) InitHost();
        else InitGuest();
        // Rated: a ranked mode played by its rules, everyone the roster lists
        // in it. It counts from here: leaving is held as a loss.
        if (MatchSettings.Current.Rated && roster.Count == gameManager.Sides.Count) Rating = new RatingTracker(Controller, roster.Ratings, localPlayerId);
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
            Removed = pieceLookup.Keys.Where(id => pieceLookup[id] == null).ToList(),
        });
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
            var flags = (byte)((piece.IsParked ? PieceTransform.Parked : 0) | (piece.Warped ? PieceTransform.Warped : 0));
            piece.Warped = false;
            transforms.Add(new PieceTransform { PieceId = piece.Manager.pieceID, Position = body.position, Rotation = body.rotation, Flags = flags });
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
        if (piece != null && (piece.IsParked || piece.Manager.playerIndex != roster.PlayerOf(sender))) piece = null;
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
        else GuestFixedUpdate();
    }

    private void HostFixedUpdate()
    {
        if (Controller == null) return;
        if (gameManager.gameState != GameManager.GameState.ProcessingTurn) return;

        snapshotTickCounter++;
        if (snapshotTickCounter < SnapshotIntervalFixedFrames) return;
        snapshotTickCounter = 0;
        session.Broadcast(new Msg.PieceSnapshot { Pieces = CurrentTransforms() });
    }

    // ---- Guest ----

    private void InitGuest()
    {
        Controller.BecomeMirror();
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
            // first so the switch doesn't warn. Interpolate keeps the
            // MovePosition-driven motion smooth between physics steps.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
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
        if (isHost || pieceLookup == null || guestSelector == null || !guestSelector.LocalPlayerId.HasValue) return;

        // Turns only; not while placing stones or after the result.
        if (gameManager.gameState != GameManager.GameState.WaitingForInput) return;
        // One flick per turn: after sending it, wait for the host's verdict
        // (the TurnResult that ends the turn) before accepting another.
        if (awaitingTurnResult) return;
        var picked = guestSelector.TrySelect(Controller.CurrentPlayerID);
        if (picked != null) picked.isDragging = true;
    }

    // Drives the kinematic guest pieces toward the host's latest poses through
    // the Rigidbody. Writing transform directly (as before) fights the
    // Rigidbody's own interpolation, which re-applies the physics pose every
    // frame.
    private void GuestFixedUpdate()
    {
        if (pieceLookup == null) return;
        var t = 15f * Time.fixedDeltaTime;
        foreach (var kv in pieceLookup)
        {
            if (kv.Value == null || !kv.Value.gameObject.activeSelf || !snapshotTargetPosition.TryGetValue(kv.Key, out var targetPos)) continue;
            var rb = kv.Value.Body;
            rb.MovePosition(Vector3.Lerp(rb.position, targetPos, t));
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, snapshotTargetRotation[kv.Key], t));
        }
    }

    private void HandleGuestSnapshot(ulong _, Msg.PieceSnapshot message)
    {
        Controller.MirrorShotUnderway(); // a shot is in flight
        ApplyTransforms(message.Pieces);
    }

    // Parked pieces hide; one put back jumps there.
    private void ApplyTransforms(List<PieceTransform> transforms)
    {
        foreach (var t in transforms)
        {
            snapshotTargetPosition[t.PieceId] = t.Position;
            snapshotTargetRotation[t.PieceId] = t.Rotation;
            if (!pieceLookup.TryGetValue(t.PieceId, out var piece) || piece == null) continue;
            var shown = !t.Is(PieceTransform.Parked);
            if (piece.gameObject.activeSelf != shown) piece.gameObject.SetActive(shown);
            if (!shown || !t.Is(PieceTransform.Warped)) continue;
            piece.transform.SetPositionAndRotation(t.Position, t.Rotation);
            piece.Body.position = t.Position;
            piece.Body.rotation = t.Rotation;
        }
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
            Destroy(piece.gameObject);
        }
        // The host's settled board is the truth; GuestFixedUpdate eases every
        // piece onto it whatever the snapshots did or didn't deliver.
        ApplyTransforms(message.Pieces);

        var controller = Controller;
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
