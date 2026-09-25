using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bridges the local, mode-agnostic TurnController/ClassicRuleset core (built
// for hot-seat play) onto a host-authoritative network match of two to four
// players. Only the host ever runs TurnController; each guest is a thin
// client that renders host snapshots and forwards its own turn's flick as a
// FlickCommand. Who plays which side is the host's MatchRoster, sent with
// LoadGameScene: the host is player 0.
//
// Absent entirely for local single-player/hot-seat play - GamePreparation
// only needs this component when a Steam lobby is active.
public class NetworkMatchBridge : MonoBehaviour
{
    private const int SnapshotIntervalFixedFrames = 3;
    // How long past zero a guest's turn runs on the host's clock (see
    // TurnController.RemoteGraceSeconds): a round trip on a slow relay.
    private const float GuestFlickGraceSeconds = 0.5f;

    // On a guest, TurnController.Tick() never runs (see GameManager.
    // SkipLocalTurnProcessing), so TurnController's own events never fire
    // past the initial StartMatch. UI that needs to reflect turn/score/
    // match-over state on both host and guest should use these instead.
    public event Action<int> OnGuestTurnChanged;
    public event Action<int, MatchEndReason> OnGuestMatchEnded;
    // Guest-side stand-in for TurnController.OnTurnEnded: shooterId just shot.
    public event Action<int> OnGuestTurnEnded;
    // Guest-side stand-in for TurnController.OnTurnPassed.
    public event Action<int, TurnEnd> OnGuestTurnPassed;
    // Guest-side stand-in for TurnController.OnPlayerOut.
    public event Action<int, MatchEndReason> OnPlayerOut;

    // A player quit, dropped or timed out. Before the result the host
    // forfeits their side; a guest whose host left can't go on. Afterwards it
    // only changes who can rematch.
    public event Action<int> OnPlayerLeft;
    public event Action<int> OnPlayerReturnedToLobby;
    public event Action OnRematchStateChanged;

    public bool IsHost => isHost;
    public int LocalPlayerId => localPlayerId;
    public int PlayerCount => roster.Count;
    public bool LocalWantsRematch => wantsRematch.Contains(localId);
    // The other players still here, and how many of them asked for a rematch.
    public int OthersPresent => roster.SteamIds.Count(id => id != localId && !gone.Contains(id));
    public int OthersWantingRematch => roster.SteamIds.Count(id => id != localId && !gone.Contains(id) && wantsRematch.Contains(id));
    public bool OpponentGone => OthersPresent == 0;
    public bool HostGone => gone.Contains(hostId);
    public bool PlayerGone(int playerId) => playerId >= 0 && playerId < roster.Count && gone.Contains(roster.SteamIds[playerId]);

    // A seat's Steam name.
    public string PlayerName(int playerId) => playerId >= 0 && playerId < roster.Count ? new Friend(roster.SteamIds[playerId]).Name : string.Empty;

    private ISessionTransport transport;
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
    private Dictionary<char, int> ownerAtTurnStart;
    private PieceSelector guestSelector;
    private int guestKnownCurrentPlayerId;
    private bool awaitingTurnResult;
    private bool guestTurnsStarted;
    private float guestClockRemaining;
    private bool guestClockRunning;
    private readonly Dictionary<char, Vector3> snapshotTargetPosition = new Dictionary<char, Vector3>();
    private readonly Dictionary<char, Quaternion> snapshotTargetRotation = new Dictionary<char, Quaternion>();

    private IEnumerable<ulong> Guests => roster.SteamIds.Skip(1);

    private void Start()
    {
        transport = SteamTransport.Instance;
        lobby = SteamLobbyManager.Instance;
        gameManager = GameManager.manager;

        if (transport == null || lobby == null || !lobby.CurrentLobby.HasValue)
        {
            enabled = false; // no active Steam lobby - this is a local match, stay dormant
            return;
        }

        localId = transport.LocalId;
        roster = MatchRoster.Current ?? lobby.BuildRoster();
        hostId = roster.SteamIds[0];
        isHost = hostId == localId;
        localPlayerId = Mathf.Max(0, roster.PlayerOf(localId));
        gameManager.SkipLocalTurnProcessing = !isHost;
        transport.OnMessageReceived += HandleMessage;
        transport.OnPeerDisconnected += PlayerDeparted;
        lobby.OnMemberLeft += HandleMemberLeft;

        StartCoroutine(WaitForTurnControllerThenInit());
    }

    private void OnDestroy()
    {
        if (transport != null)
        {
            transport.OnMessageReceived -= HandleMessage;
            transport.OnPeerDisconnected -= PlayerDeparted;
        }
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
            matchResolved = true;
            gameManager.TurnController?.Abort();
            gameManager.gameState = GameManager.GameState.MatchOver;
        }
        OnPlayerLeft?.Invoke(playerId);
        OnRematchStateChanged?.Invoke();
    }

    public void RequestRematch()
    {
        if (OpponentGone || LocalWantsRematch) return;
        wantsRematch.Add(localId);
        transport.Broadcast(NetMessage.WriteRematchRequest());
        OnRematchStateChanged?.Invoke();
        TryStartRematch();
    }

    public void ReturnToLobby()
    {
        transport.Broadcast(NetMessage.WriteReturnToLobby());
        gameManager.EndMatch();
        SceneManager.LoadScene("LobbyScene");
    }

    // The host owns scene flow: once everyone still here has asked, it
    // restarts the match with them, the same way the lobby's Start button
    // does.
    private void TryStartRematch()
    {
        if (!isHost || !LocalWantsRematch) return;
        var present = roster.SteamIds.Where(id => !gone.Contains(id)).ToList();
        if (present.Count < 2 || !present.All(wantsRematch.Contains)) return;
        MatchRoster.Current = lobby.RosterOf(present); // their ratings after this match
        MatchSettings.Current = MatchSettings.Picked.Resolve(); // Random rolls again
        transport.Broadcast(NetMessage.WriteLoadGameScene(MatchSettings.Current, MatchRoster.Current));
        gameManager.EndMatch();
        SceneManager.LoadScene("GameScene");
    }

    private IEnumerator WaitForTurnControllerThenInit()
    {
        if (isHost)
        {
            while (gameManager.TurnController == null) yield return null;
        }
        else
        {
            while (gameManager.gamePieceScripts == null || gameManager.gamePieceScripts.Count == 0) yield return null;
        }

        BuildPieceLookup();

        if (isHost) InitHost();
        else InitGuest();
    }

    private void BuildPieceLookup()
    {
        pieceLookup = new Dictionary<char, GamePieceDragAndReleaseForce>();
        foreach (var piece in gameManager.gamePieceScripts)
        {
            var id = piece.GetComponent<GamePieceManager>().pieceID;
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
        var controller = gameManager.TurnController;
        controller.PieceSelector.LocalPlayerId = localPlayerId;
        controller.HostPlayerId = localPlayerId;
        controller.RemoteGraceSeconds = GuestFlickGraceSeconds;
        controller.OnTurnStarted += HandleHostTurnStarted;
        controller.OnMatchEnded += HandleHostMatchEnded;
        controller.OnPlayerOut += HandleHostPlayerOut;
        BoardSounds.OnEmitted += ForwardBoardSound;
        ownerAtTurnStart = SnapshotOwners();
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
        var controller = gameManager.TurnController;
        controller.Forfeit(playerId, MatchEndReason.OpponentLeft);
        // Mid-placement GameManager isn't following the controller yet.
        if (controller.State == GameManager.GameState.MatchOver) gameManager.gameState = GameManager.GameState.MatchOver;
    }

    // Each guest its own view: hidden placement leaves out the other sides'
    // stones until it's over.
    private void SendPlacementState()
    {
        foreach (var id in Guests.Where(id => !gone.Contains(id))) SendPlacementStateTo(id);
    }

    private void SendPlacementStateTo(ulong guestId)
    {
        transport.Send(guestId, NetMessage.WritePlacementState(gameManager.Placement.SnapshotFor(roster.PlayerOf(guestId))));
    }

    // The guests' pieces never collide, so they hear the host's. Not a
    // guest's own flicks: it played those when it let go.
    private void ForwardBoardSound(BoardSoundEvent sound)
    {
        foreach (var id in Guests.Where(id => !gone.Contains(id)))
        {
            if (sound.Kind == BoardSound.Flick && sound.Owner == roster.PlayerOf(id)) continue;
            transport.Send(id, NetMessage.WriteBoardSound(sound), reliable: false);
        }
    }

    private Dictionary<char, int> SnapshotOwners()
    {
        var dict = new Dictionary<char, int>();
        foreach (var piece in gameManager.gamePieceScripts)
        {
            var pm = piece.GetComponent<GamePieceManager>();
            dict[pm.pieceID] = pm.playerIndex;
        }
        return dict;
    }

    // Every piece gone since the turn started, and who it counts for: the
    // side whose turn it was knocked it out.
    private List<RemovedPieceEntry> ComputeRemovedSinceTurnStart()
    {
        var currentIds = new HashSet<char>(gameManager.gamePieceScripts.Select(p => p.GetComponent<GamePieceManager>().pieceID));
        var shooter = gameManager.TurnController.LastPlayerId;
        var removed = new List<RemovedPieceEntry>();
        foreach (var kv in ownerAtTurnStart)
        {
            if (currentIds.Contains(kv.Key)) continue;
            removed.Add(new RemovedPieceEntry { PieceId = kv.Key, ScoredForPlayerId = ClassicRuleset.ScorerFor(kv.Value, shooter, gameManager.playersList) });
        }
        return removed;
    }

    private void HandleHostTurnStarted(PlayersManager player)
    {
        var removed = ComputeRemovedSinceTurnStart();
        var turnEnd = gameManager.TurnController.LastTurnEnd;
        var kills = turnEnd == TurnEnd.Shot ? gameManager.TurnController.Kills.LastShot : new List<KillEvent>();
        transport.Broadcast(NetMessage.WriteTurnResult(player.ID, turnEnd, false, -1, MatchEndReason.Knockout, removed, kills, CurrentTransforms()));
        ownerAtTurnStart = SnapshotOwners();
    }

    private void HandleHostMatchEnded(PlayersManager winner, MatchEndReason reason)
    {
        matchResolved = true;
        var removed = ComputeRemovedSinceTurnStart();
        var winnerId = winner != null ? winner.ID : -1; // -1: draw
        transport.Broadcast(NetMessage.WriteTurnResult(-1, TurnEnd.Shot, true, winnerId, reason, removed, gameManager.TurnController.Kills.LastShot, CurrentTransforms()));
    }

    private void HandleHostPlayerOut(int playerId, MatchEndReason reason)
    {
        transport.Broadcast(NetMessage.WritePlayerOut(playerId, reason));
    }

    private void SendCurrentTurnTo(ulong targetId)
    {
        var controller = gameManager.TurnController;
        transport.Send(targetId, NetMessage.WriteTurnResult(controller.CurrentPlayerID, TurnEnd.None, false, -1, MatchEndReason.Knockout, new List<RemovedPieceEntry>(), new List<KillEvent>(), CurrentTransforms()));
    }

    // The physics pose, not transform: with interpolation on, transform is
    // the render pose, which lags or overshoots the simulated one.
    private List<PieceTransform> CurrentTransforms()
    {
        return gameManager.gamePieceScripts
            .Where(piece => piece != null)
            .Select(piece =>
            {
                var rb = piece.GetComponent<Rigidbody>();
                return new PieceTransform { PieceId = piece.GetComponent<GamePieceManager>().pieceID, Position = rb.position, Rotation = rb.rotation };
            })
            .ToList();
    }

    private void SendStartMatchTo(ulong targetId)
    {
        var owners = SnapshotOwners().Select(kv => new PieceOwnerEntry { PieceId = kv.Key, PlayerId = kv.Value }).ToList();
        transport.Send(targetId, NetMessage.WriteStartMatch(roster.PlayerOf(targetId), owners));
    }

    private void FixedUpdate()
    {
        if (isHost) HostFixedUpdate();
        else GuestFixedUpdate();
    }

    private void HostFixedUpdate()
    {
        if (gameManager.TurnController == null) return;
        if (gameManager.gameState != GameManager.GameState.ProcessingTurn) return;

        snapshotTickCounter++;
        if (snapshotTickCounter < SnapshotIntervalFixedFrames) return;
        snapshotTickCounter = 0;
        transport.Broadcast(NetMessage.WritePieceSnapshot(CurrentTransforms()), reliable: false);
    }

    // ---- Guest ----

    private void InitGuest()
    {
        guestSelector = new PieceSelector(gameManager.gamePieceScripts) { LocalPlayerId = null };
        transport.Send(hostId, NetMessage.WriteClientReady());
    }

    private void ApplyGuestRole(int assignedPlayerId, List<PieceOwnerEntry> hostOwners)
    {
        localPlayerId = assignedPlayerId;
        guestKnownCurrentPlayerId = gameManager.playersList[0].ID; // host always starts

        // Every side spawns from the host's settings, so the stone sets must
        // match exactly; if they don't, the builds disagree about the rules.
        if (hostOwners.Count != pieceLookup.Count || hostOwners.Any(o => !pieceLookup.ContainsKey(o.PieceId)))
            Debug.LogError($"Host has {hostOwners.Count} stones, this guest spawned {pieceLookup.Count} - are both on the same commit?");

        foreach (var kv in pieceLookup)
        {
            var pieceId = kv.Key;
            var piece = kv.Value;
            var rb = piece.GetComponent<Rigidbody>();
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
                guestClockRunning = false;
                transport.Send(hostId, NetMessage.WriteFlickCommand(pieceId, force));
            };
        }

        guestSelector.LocalPlayerId = localPlayerId;
    }

    private void Update()
    {
        if (isHost || pieceLookup == null || guestSelector == null || !guestSelector.LocalPlayerId.HasValue) return;
        if (guestClockRunning) guestClockRemaining = Mathf.Max(0, guestClockRemaining - GamePace.ClockDelta);

        // Turns only; not while placing stones or after the result.
        if (gameManager.gameState != GameManager.GameState.WaitingForInput) return;
        // One flick per turn: after sending it, wait for the host's verdict
        // (the TurnResult that ends the turn) before accepting another.
        if (awaitingTurnResult) return;
        var picked = guestSelector.TrySelect(guestKnownCurrentPlayerId);
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
            if (kv.Value == null || !snapshotTargetPosition.TryGetValue(kv.Key, out var targetPos)) continue;
            var rb = kv.Value.GetComponent<Rigidbody>();
            rb.MovePosition(Vector3.Lerp(rb.position, targetPos, t));
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, snapshotTargetRotation[kv.Key], t));
        }
    }

    private void HandleGuestSnapshot(byte[] data)
    {
        guestClockRunning = false; // a shot is in flight
        foreach (var t in NetMessage.ReadPieceSnapshot(data))
        {
            snapshotTargetPosition[t.PieceId] = t.Position;
            snapshotTargetRotation[t.PieceId] = t.Rotation;
        }
    }

    private void HandleGuestTurnResult(byte[] data)
    {
        var (nextPlayerId, turnEnd, matchOver, winnerPlayerId, reason, removed, kills, finalTransforms) = NetMessage.ReadTurnResult(data);
        awaitingTurnResult = false;
        var finishedPlayerId = guestKnownCurrentPlayerId;
        if (turnEnd == TurnEnd.Shot) OnGuestTurnEnded?.Invoke(finishedPlayerId);
        else if (turnEnd != TurnEnd.None) OnGuestTurnPassed?.Invoke(finishedPlayerId, turnEnd);
        gameManager.TurnController.Kills.Record(kills);

        foreach (var entry in removed)
        {
            if (!pieceLookup.TryGetValue(entry.PieceId, out var piece)) continue;
            pieceLookup.Remove(entry.PieceId);
            gameManager.gamePieceScripts.Remove(piece);
            var scorer = gameManager.playersList.Find(p => p.ID == entry.ScoredForPlayerId);
            scorer?.AddScore(1);
            Destroy(piece.gameObject);
        }

        // The host's settled board is the truth; GuestFixedUpdate eases every
        // piece onto it whatever the snapshots did or didn't deliver.
        foreach (var t in finalTransforms)
        {
            snapshotTargetPosition[t.PieceId] = t.Position;
            snapshotTargetRotation[t.PieceId] = t.Rotation;
        }

        if (matchOver)
        {
            matchResolved = true;
            guestClockRunning = false;
            gameManager.gameState = GameManager.GameState.MatchOver;
            OnGuestMatchEnded?.Invoke(winnerPlayerId, reason);
            return;
        }

        // A new turn restarts the countdown; a resync (None, after the opening
        // turn) leaves it running.
        if (turnEnd != TurnEnd.None || !guestTurnsStarted)
        {
            guestTurnsStarted = true;
            guestClockRemaining = MatchSettings.Current.TurnSeconds;
            guestClockRunning = true;
        }
        gameManager.gameState = GameManager.GameState.WaitingForInput;
        guestKnownCurrentPlayerId = nextPlayerId;
        OnGuestTurnChanged?.Invoke(nextPlayerId);
    }

    // The turn clock as this guest last heard it; null while nothing counts
    // down (no timer, a shot in flight, or the match is over).
    public float? GuestTurnTimeRemaining => !isHost && guestClockRunning && MatchSettings.Current.TurnSeconds > 0 ? guestClockRemaining : (float?)null;

    public bool GuestCanPass => !isHost && !awaitingTurnResult && guestKnownCurrentPlayerId == localPlayerId
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
        guestClockRunning = false;
        transport.Send(hostId, NetMessage.WritePassCommand());
    }

    // Placement on a guest shows at once (the mirror applies the same rules);
    // the host's next PlacementState confirms it or puts it back.
    public void RequestPlace(char pieceId, Vector3 position)
    {
        if (isHost || gameManager.Placement == null) return;
        if (!gameManager.Placement.TryPlace(localPlayerId, pieceId, position)) return;
        transport.Send(hostId, NetMessage.WritePlaceRequest(pieceId, position));
    }

    // The host decides; its PlayerOut (or, with two, the result) follows.
    public void RequestConcede()
    {
        if (!isHost && !matchResolved) transport.Send(hostId, NetMessage.WriteConcede());
    }

    public void RequestPlacementReady()
    {
        if (!isHost) transport.Send(hostId, NetMessage.WritePlacementReady());
    }

    private void HandleGuestPlacementState(byte[] data)
    {
        if (gameManager.Placement == null) gameManager.BeginGuestPlacement();
        gameManager.Placement.Apply(NetMessage.ReadPlacementState(data));
    }

    // ---- Shared message dispatch ----

    private void HandleMessage(ulong senderId, byte[] data)
    {
        var senderPlayer = roster.PlayerOf(senderId);
        if (senderPlayer < 0) return; // not in this match
        var type = NetMessage.PeekType(data);

        switch (type)
        {
            case NetMessageType.RematchRequest:
                wantsRematch.Add(senderId);
                OnRematchStateChanged?.Invoke();
                TryStartRematch();
                return;
            case NetMessageType.ReturnToLobby:
                gone.Add(senderId);
                wantsRematch.Remove(senderId);
                OnPlayerReturnedToLobby?.Invoke(senderPlayer);
                OnRematchStateChanged?.Invoke();
                TryStartRematch();
                return;
        }

        if (isHost)
        {
            switch (type)
            {
                case NetMessageType.ClientReady:
                    SendStartMatchTo(senderId);
                    readyGuests.Add(senderId);
                    TryBeginMatch();
                    break;
                case NetMessageType.FlickCommand:
                    var (pieceId, force) = NetMessage.ReadFlickCommand(data);
                    // Unity-null once DeathTrigger destroyed it: a lagging guest
                    // board can still name a piece that's already gone. Only
                    // the sender's own pieces.
                    pieceLookup.TryGetValue(pieceId, out var piece);
                    if (piece != null && piece.GetComponent<GamePieceManager>().playerIndex != senderPlayer) piece = null;
                    var controller = gameManager.TurnController;
                    if (!controller.TryApplyExternalFlick(piece, force) && controller.State == GameManager.GameState.WaitingForInput)
                    {
                        // Rejected while idle means the guest disagrees about the
                        // board or whose turn it is - resend the current state so
                        // its view and input line up again. (Mid-turn rejections
                        // need nothing: the turn's own TurnResult will arrive.)
                        SendCurrentTurnTo(senderId);
                    }
                    break;
                case NetMessageType.PassCommand:
                    if (!gameManager.TurnController.TryPassTurn(senderPlayer) && gameManager.TurnController.State == GameManager.GameState.WaitingForInput)
                        SendCurrentTurnTo(senderId);
                    break;
                case NetMessageType.PlaceRequest:
                    var (placeId, x, z) = NetMessage.ReadPlaceRequest(data);
                    var phase = gameManager.Placement;
                    // Rejected: resend, so the guest's optimistic stone goes back.
                    if (phase != null && !phase.TryPlace(senderPlayer, placeId, new Vector3(x, 0, z))) SendPlacementStateTo(senderId);
                    break;
                case NetMessageType.Concede:
                    gameManager.TurnController.Concede(senderPlayer);
                    break;
                case NetMessageType.PlacementReady:
                    if (gameManager.Placement != null && !gameManager.Placement.TryReady(senderPlayer)) SendPlacementStateTo(senderId);
                    break;
            }
            return;
        }

        if (senderId != hostId) return; // the rest comes from the host only
        switch (type)
        {
            case NetMessageType.StartMatch:
                var (assignedPlayerId, owners) = NetMessage.ReadStartMatch(data);
                ApplyGuestRole(assignedPlayerId, owners);
                break;
            case NetMessageType.PlacementState:
                HandleGuestPlacementState(data);
                break;
            case NetMessageType.PieceSnapshot:
                HandleGuestSnapshot(data);
                break;
            case NetMessageType.BoardSound:
                if (BoardSounds.Instance != null) BoardSounds.Instance.PlayRemote(NetMessage.ReadBoardSound(data));
                break;
            case NetMessageType.TurnResult:
                HandleGuestTurnResult(data);
                break;
            case NetMessageType.PlayerOut:
                var (outPlayer, outReason) = NetMessage.ReadPlayerOut(data);
                OnPlayerOut?.Invoke(outPlayer, outReason);
                break;
            case NetMessageType.LoadGameScene:
                // A rematch, or the host starting a new match from the lobby
                // while this guest was still on the game-over screen.
                var (settings, nextRoster) = NetMessage.ReadLoadGameScene(data);
                if (nextRoster.PlayerOf(localId) < 0) break; // left out: this player went back to the lobby
                MatchSettings.Current = settings;
                MatchRoster.Current = nextRoster;
                gameManager.EndMatch();
                SceneManager.LoadScene("GameScene");
                break;
        }
    }
}
