using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Bridges the local, mode-agnostic TurnController/ClassicRuleset core (built
// for hot-seat play) onto a host-authoritative network match. Only the host
// ever runs TurnController; the guest is a thin client that renders host
// snapshots and forwards its own turn's flick as a FlickCommand.
//
// Absent entirely for local single-player/hot-seat play - GamePreparation
// only needs this component when a Steam lobby is active.
public class NetworkMatchBridge : MonoBehaviour
{
    private const int SnapshotIntervalFixedFrames = 3;

    // On the guest, TurnController.Tick() never runs (see GameManager.
    // SkipLocalTurnProcessing), so TurnController's own events never fire
    // past the initial StartMatch. UI that needs to reflect turn/score/
    // match-over state on both host and guest should use these instead.
    public event Action<int> OnGuestTurnChanged;
    public event Action<int> OnGuestMatchEnded;

    private ISessionTransport transport;
    private SteamLobbyManager lobby;
    private GameManager gameManager;

    private bool isHost;
    private ulong hostId;
    private int localPlayerId;
    private int snapshotTickCounter;

    private Dictionary<char, GamePieceDragAndReleaseForce> pieceLookup;
    private Dictionary<char, int> ownerAtTurnStart;
    private PieceSelector guestSelector;
    private int guestKnownCurrentPlayerId;
    private bool awaitingTurnResult;
    private readonly Dictionary<char, Vector3> snapshotTargetPosition = new Dictionary<char, Vector3>();
    private readonly Dictionary<char, Quaternion> snapshotTargetRotation = new Dictionary<char, Quaternion>();

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

        isHost = lobby.IsHost;
        hostId = lobby.CurrentLobby.Value.Owner.Id.Value;
        gameManager.SkipLocalTurnProcessing = !isHost;
        transport.OnMessageReceived += HandleMessage;

        StartCoroutine(WaitForTurnControllerThenInit());
    }

    private void OnDestroy()
    {
        if (transport != null) transport.OnMessageReceived -= HandleMessage;
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
        localPlayerId = gameManager.playersList[0].ID;
        gameManager.TurnController.PieceSelector.LocalPlayerId = localPlayerId;
        gameManager.TurnController.OnTurnStarted += HandleHostTurnStarted;
        gameManager.TurnController.OnMatchEnded += HandleHostMatchEnded;
        ownerAtTurnStart = SnapshotOwners();
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

    private List<RemovedPieceEntry> ComputeRemovedSinceTurnStart()
    {
        var currentIds = new HashSet<char>(gameManager.gamePieceScripts.Select(p => p.GetComponent<GamePieceManager>().pieceID));
        var removed = new List<RemovedPieceEntry>();
        foreach (var kv in ownerAtTurnStart)
        {
            if (currentIds.Contains(kv.Key)) continue;
            var scorer = gameManager.playersList.Find(p => p.ID != kv.Value);
            removed.Add(new RemovedPieceEntry { PieceId = kv.Key, ScoredForPlayerId = scorer != null ? scorer.ID : -1 });
        }
        return removed;
    }

    private void HandleHostTurnStarted(PlayersManager player)
    {
        var removed = ComputeRemovedSinceTurnStart();
        transport.Broadcast(NetMessage.WriteTurnResult(player.ID, false, -1, removed, CurrentTransforms()));
        ownerAtTurnStart = SnapshotOwners();
    }

    private void HandleHostMatchEnded(PlayersManager winner)
    {
        var removed = ComputeRemovedSinceTurnStart();
        transport.Broadcast(NetMessage.WriteTurnResult(-1, true, winner.ID, removed, CurrentTransforms()));
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
        var guestPlayer = gameManager.playersList.Count > 1 ? gameManager.playersList[1] : null;
        var guestPlayerId = guestPlayer != null ? guestPlayer.ID : 1;
        var owners = SnapshotOwners().Select(kv => new PieceOwnerEntry { PieceId = kv.Key, PlayerId = kv.Value }).ToList();
        transport.Send(targetId, NetMessage.WriteStartMatch(guestPlayerId, owners));
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

    private void ApplyGuestRole(int assignedPlayerId)
    {
        localPlayerId = assignedPlayerId;
        guestKnownCurrentPlayerId = gameManager.playersList[0].ID; // host always starts

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
                transport.Send(hostId, NetMessage.WriteFlickCommand(pieceId, force));
            };
        }

        guestSelector.LocalPlayerId = localPlayerId;
    }

    private void Update()
    {
        if (isHost || pieceLookup == null || guestSelector == null || !guestSelector.LocalPlayerId.HasValue) return;

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
        foreach (var t in NetMessage.ReadPieceSnapshot(data))
        {
            snapshotTargetPosition[t.PieceId] = t.Position;
            snapshotTargetRotation[t.PieceId] = t.Rotation;
        }
    }

    private void HandleGuestTurnResult(byte[] data)
    {
        var (nextPlayerId, matchOver, winnerPlayerId, removed, finalTransforms) = NetMessage.ReadTurnResult(data);
        awaitingTurnResult = false;

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
            gameManager.gameState = GameManager.GameState.MatchOver;
            OnGuestMatchEnded?.Invoke(winnerPlayerId);
            return;
        }

        guestKnownCurrentPlayerId = nextPlayerId;
        OnGuestTurnChanged?.Invoke(nextPlayerId);
    }

    // ---- Shared message dispatch ----

    private void HandleMessage(ulong senderId, byte[] data)
    {
        var type = NetMessage.PeekType(data);

        if (isHost)
        {
            switch (type)
            {
                case NetMessageType.ClientReady:
                    SendStartMatchTo(senderId);
                    break;
                case NetMessageType.FlickCommand:
                    var (pieceId, force) = NetMessage.ReadFlickCommand(data);
                    // Unity-null once DeathTrigger destroyed it: a lagging guest
                    // board can still name a piece that's already gone.
                    pieceLookup.TryGetValue(pieceId, out var piece);
                    var controller = gameManager.TurnController;
                    if (!controller.TryApplyExternalFlick(piece, force) && controller.State == GameManager.GameState.WaitingForInput)
                    {
                        // Rejected while idle means the guest disagrees about the
                        // board or whose turn it is - resend the current state so
                        // its view and input line up again. (Mid-turn rejections
                        // need nothing: the turn's own TurnResult will arrive.)
                        transport.Send(senderId, NetMessage.WriteTurnResult(controller.CurrentPlayerID, false, -1, new List<RemovedPieceEntry>(), CurrentTransforms()));
                    }
                    break;
            }
            return;
        }

        switch (type)
        {
            case NetMessageType.StartMatch:
                var (assignedPlayerId, _) = NetMessage.ReadStartMatch(data);
                ApplyGuestRole(assignedPlayerId);
                break;
            case NetMessageType.PieceSnapshot:
                HandleGuestSnapshot(data);
                break;
            case NetMessageType.TurnResult:
                HandleGuestTurnResult(data);
                break;
        }
    }
}
