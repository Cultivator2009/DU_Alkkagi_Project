using UnityEngine;
using UnityEngine.EventSystems;

// Mouse input and board view for the placement phase: click inside your
// zone to put down your next stone, drag a placed one to move it. Online it
// acts for this machine's player and routes through NetworkMatchBridge (the
// host owns the rules); in a local hot-seat game it acts for whoever's
// turn it is to place.
public class PlacementController : MonoBehaviour
{
    private Camera mainCamera;
    private NetworkMatchBridge bridge;
    private GamePieceDragAndReleaseForce dragging;
    private Plane boardPlane;

    // The player this screen places for right now, or -1.
    public int Actor
    {
        get
        {
            var gameManager = GameManager.manager;
            var phase = gameManager != null ? gameManager.Placement : null;
            if (phase == null) return -1;
            if (Bridge != null) return Bridge.LocalPlayerId;
            if (gameManager.VersusAI) return 1 - gameManager.AIPlayerId; // the AI places its own
            return phase.Placer;
        }
    }

    private NetworkMatchBridge Bridge
    {
        get
        {
            if (bridge == null) bridge = FindObjectOfType<NetworkMatchBridge>();
            return bridge != null && bridge.enabled ? bridge : null;
        }
    }

    public void Ready()
    {
        var phase = GameManager.manager.Placement;
        if (phase == null) return;
        if (Bridge != null && !Bridge.IsHost) Bridge.RequestPlacementReady();
        else phase.TryReady(Actor);
    }

    private void Update()
    {
        var gameManager = GameManager.manager;
        var phase = gameManager != null ? gameManager.Placement : null;
        if (phase == null || gameManager.gameState != GameManager.GameState.Placement)
        {
            dragging = null;
            return;
        }

        var player = Actor;
        if (player < 0 || !phase.CanAct(player))
        {
            dragging = null;
            return;
        }
        if (mainCamera == null) mainCamera = Camera.main;
        var board = gameManager.Board;
        if (!TryGetBoardPoint(board, player, out var point)) return;

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI() && !CameraRig.Busy)
        {
            var hit = StoneUnderCursor();
            if (hit != null)
            {
                var manager = hit.GetComponent<GamePieceManager>();
                if (manager.playerIndex == player && phase.CanMoveStones) dragging = hit;
            }
            else
            {
                var next = phase.NextUnplaced(player);
                if (next != null && board.InZone(player, point, next.radius)) Place(phase, player, next.pieceID, point);
            }
        }

        if (dragging == null) return;
        // A pan or the free look drops the stone where it is.
        if (Input.GetMouseButton(0) && !CameraRig.Busy)
        {
            dragging.transform.position = board.ClampToZone(player, point, dragging.GetComponent<GamePieceManager>().radius);
            return;
        }
        // Released: keep it there if the spot is clear, otherwise the next
        // view refresh puts it back where it was.
        var id = dragging.GetComponent<GamePieceManager>().pieceID;
        var position = dragging.transform.position;
        dragging = null;
        Place(phase, player, id, position);
    }

    private void LateUpdate()
    {
        var gameManager = GameManager.manager;
        var phase = gameManager != null ? gameManager.Placement : null;
        if (phase == null || phase.Done || gameManager.gameState != GameManager.GameState.Placement) return;

        // Hidden placement shows this screen only its own side's stones.
        var viewer = Actor;
        foreach (var piece in gameManager.gamePieceScripts)
        {
            if (piece == dragging) continue;
            var id = piece.GetComponent<GamePieceManager>().pieceID;
            var visible = phase.IsVisibleTo(id, viewer);
            if (piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
            if (visible && phase.TryGetPosition(id, out var position)) piece.transform.position = position;
        }

        gameManager.Board.ShowZones(viewer == 0 && phase.CanAct(0), viewer == 1 && phase.CanAct(1));
    }

    private void Place(PlacementPhase phase, int player, char id, Vector3 position)
    {
        if (Bridge != null && !Bridge.IsHost) Bridge.RequestPlace(id, position);
        else phase.TryPlace(player, id, position);
    }

    private bool TryGetBoardPoint(BoardSetup board, int player, out Vector3 point)
    {
        point = default;
        if (mainCamera == null) return false;
        boardPlane.SetNormalAndPosition(Vector3.up, new Vector3(0, board.PieceHeight, 0));
        var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (!boardPlane.Raycast(ray, out var distance)) return false;
        point = ray.GetPoint(distance);
        return true;
    }

    private GamePieceDragAndReleaseForce StoneUnderCursor()
    {
        var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out var hit, 100f) ? hit.collider.GetComponentInParent<GamePieceDragAndReleaseForce>() : null;
    }

    private static bool IsPointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}
