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
            pieceLookup[piece.GetComponent<GamePieceManager>().pieceID] = piece;
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
        transport.Broadcast(NetMessage.WriteTurnResult(player.ID, false, -1, removed));
        ownerAtTurnStart = SnapshotOwners();
    }

    private void HandleHostMatchEnded(PlayersManager winner)
    {
        var removed = ComputeRemovedSinceTurnStart();
        transport.Broadcast(NetMessage.WriteTurnResult(-1, true, winner.ID, removed));
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
        if (!isHost || gameManager.TurnController == null) return;
        if (gameManager.gameState != GameManager.GameState.ProcessingTurn) return;

        snapshotTickCounter++;
        if (snapshotTickCounter < SnapshotIntervalFixedFrames) return;
        snapshotTickCounter = 0;
        BroadcastSnapshot();
    }

    private void BroadcastSnapshot()
    {
        var transforms = gameManager.gamePieceScripts
            .Select(piece => new PieceTransform
            {
                PieceId = piece.GetComponent<GamePieceManager>().pieceID,
                Position = piece.transform.position,
                Rotation = piece.transform.rotation
            })
            .ToList();
        transport.Broadcast(NetMessage.WritePieceSnapshot(transforms), reliable: false);
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
            rb.isKinematic = true;
            piece.isAuthority = false;
            piece.OnFlickRequested += force => transport.Send(hostId, NetMessage.WriteFlickCommand(pieceId, force));
        }

        guestSelector.LocalPlayerId = localPlayerId;
    }

    private void Update()
    {
        if (isHost || pieceLookup == null || guestSelector == null || !guestSelector.LocalPlayerId.HasValue) return;

        var picked = guestSelector.TrySelect(guestKnownCurrentPlayerId);
        if (picked != null) picked.isDragging = true;

        InterpolateSnapshots();
    }

    private void InterpolateSnapshots()
    {
        foreach (var kv in pieceLookup)
        {
            if (!snapshotTargetPosition.TryGetValue(kv.Key, out var targetPos)) continue;
            var t = kv.Value.transform;
            t.position = Vector3.Lerp(t.position, targetPos, 15f * Time.deltaTime);
            t.rotation = Quaternion.Slerp(t.rotation, snapshotTargetRotation[kv.Key], 15f * Time.deltaTime);
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
        var (nextPlayerId, matchOver, winnerPlayerId, removed) = NetMessage.ReadTurnResult(data);

        foreach (var entry in removed)
        {
            if (!pieceLookup.TryGetValue(entry.PieceId, out var piece)) continue;
            pieceLookup.Remove(entry.PieceId);
            gameManager.gamePieceScripts.Remove(piece);
            var scorer = gameManager.playersList.Find(p => p.ID == entry.ScoredForPlayerId);
            scorer?.AddScore(1);
            Destroy(piece.gameObject);
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
                    if (pieceLookup.TryGetValue(pieceId, out var piece))
                    {
                        var owner = piece.GetComponent<GamePieceManager>().playerIndex;
                        if (gameManager.TurnController.CurrentPlayerID == owner) piece.ApplyFlick(force);
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
