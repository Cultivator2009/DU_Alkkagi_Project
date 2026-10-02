using UnityEngine;
using UnityEngine.EventSystems;

// Mouse input and board view for the placement phase: click inside your
// zone to put down your next stone, drag a placed one to move it. A see-
// through copy of the stone shows where a click would put it, green where
// it may go and red where it may not (out of the zone, or on another
// piece); a stone being dragged is tinted the same way. Online it
// acts for this machine's player and routes through NetworkMatchBridge (the
// host owns the rules); in a local hot-seat game it acts for whoever's
// turn it is to place.
public class PlacementController : MonoBehaviour
{
    private Camera mainCamera;
    private NetworkMatchBridge bridge;
    private GamePieceDragAndReleaseForce dragging;
    private Plane boardPlane;
    private GameObject ghost;
    private GamePieceManager ghostOf;
    private Material ghostMaterial;
    private static readonly Color GhostOk = new Color(0.30f, 0.78f, 0.38f, 0.5f);
    private static readonly Color GhostBlocked = new Color(0.86f, 0.22f, 0.16f, 0.5f);

    // The player this screen places for right now, or -1.
    public int Actor
    {
        get
        {
            var gameManager = GameManager.manager;
            var phase = gameManager != null ? gameManager.Placement : null;
            if (phase == null) return -1;
            if (Bridge != null) return Bridge.LocalPlayerId;
            if (gameManager.SoleHuman >= 0) return gameManager.SoleHuman; // the AI places its own
            return phase.Placer >= 0 && !gameManager.IsAI(phase.Placer) ? phase.Placer : -1;
        }
    }

    private NetworkMatchBridge Bridge
    {
        get
        {
            if (bridge == null) bridge = FindAnyObjectByType<NetworkMatchBridge>();
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
            HideGhost();
            return;
        }

        var player = Actor;
        if (mainCamera == null) mainCamera = Camera.main;
        if (player < 0 || !phase.CanAct(player))
        {
            dragging = null;
            HideGhost();
            PieceOutline.For(mainCamera).Set(PieceOutline.Mark.Hover, null);
            return;
        }
        // The stone being moved, or the one a press here would pick up.
        var under = dragging != null ? dragging : StoneUnderCursor();
        var movable = under != null && under.Manager.playerIndex == player && phase.CanMoveStones && (dragging != null || !IsPointerOverUI());
        PieceOutline.For(mainCamera).Set(PieceOutline.Mark.Hover, movable ? under : null);
        var board = gameManager.Board;
        if (!TryGetBoardPoint(board, player, out var point))
        {
            HideGhost();
            return;
        }

        var next = phase.NextUnplaced(player);
        if (dragging == null)
        {
            // Where a click would put the next stone, unless it would pick one up.
            var free = next != null && under == null && !IsPointerOverUI() && !CameraRig.Busy;
            if (free) ShowGhost(next, point, MayGo(phase, board, player, next, point));
            else HideGhost();
        }

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI() && !CameraRig.Busy)
        {
            var hit = StoneUnderCursor();
            if (hit != null)
            {
                var manager = hit.Manager;
                if (manager.playerIndex == player && phase.CanMoveStones) dragging = hit;
            }
            else if (next != null)
            {
                if (MayGo(phase, board, player, next, point)) Place(phase, player, next.pieceID, point);
                else GameAudio.PlayInterface(GameAudio.Bank.cancel, 0.4f);
            }
        }

        if (dragging == null) return;
        // A pan or the free look drops the stone where it is.
        if (Input.GetMouseButton(0) && !CameraRig.Busy)
        {
            var at = board.ClampToZone(player, point, dragging.Manager.radius);
            dragging.transform.position = at;
            ShowGhost(dragging.Manager, at, board.IsClear(dragging.Manager, at, phase.Occupied(dragging.Manager.pieceID)));
            return;
        }
        HideGhost();
        // Released: keep it there if the spot is clear, otherwise the next
        // view refresh puts it back where it was.
        var id = dragging.Manager.pieceID;
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
            var id = piece.Manager.pieceID;
            var visible = phase.IsVisibleTo(id, viewer);
            if (piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
            if (visible && phase.TryGetPosition(id, out var position)) piece.transform.position = position;
        }

        gameManager.Board.ShowZones(player => viewer == player && phase.CanAct(player));
    }

    private void Place(PlacementPhase phase, int player, char id, Vector3 position)
    {
        GameAudio.PlayBoard(GameAudio.Bank.place, 0.7f, Random.Range(0.94f, 1.06f), BoardSounds.Pan(position));
        if (Bridge != null && !Bridge.IsHost) Bridge.RequestPlace(id, position);
        else phase.TryPlace(player, id, position);
    }

    private static bool MayGo(PlacementPhase phase, BoardSetup board, int player, GamePieceManager piece, Vector3 at) =>
        board.InZone(player, at, piece.radius) && board.IsClear(piece, at, phase.Occupied(piece.pieceID));

    // A see-through copy of the piece's meshes, standing as it does, tinted
    // by whether it may go there. Built once per piece shown.
    private void ShowGhost(GamePieceManager piece, Vector3 at, bool ok)
    {
        if (ghostOf != piece)
        {
            if (ghost != null) Destroy(ghost);
            if (ghostMaterial == null) ghostMaterial = new Material(Shader.Find("Sprites/Default"));
            ghost = new GameObject("PlacementGhost");
            var root = piece.transform;
            foreach (var filter in piece.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var part = new GameObject(filter.name);
                part.transform.SetParent(ghost.transform, false);
                part.transform.localPosition = root.InverseTransformPoint(filter.transform.position);
                part.transform.localRotation = Quaternion.Inverse(root.rotation) * filter.transform.rotation;
                part.transform.localScale = filter.transform.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ghostMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // A touch bigger, so it tints a dragged stone rather than fighting it for depth.
            ghost.transform.localScale = Vector3.one * 1.03f;
            ghostOf = piece;
        }
        ghost.transform.SetPositionAndRotation(at, piece.transform.rotation);
        ghostMaterial.color = ok ? GhostOk : GhostBlocked;
        ghost.SetActive(true);
    }

    private void HideGhost()
    {
        if (ghost != null) ghost.SetActive(false);
    }

    private void OnDestroy()
    {
        if (ghost != null) Destroy(ghost);
        if (ghostMaterial != null) Destroy(ghostMaterial);
    }

    private bool TryGetBoardPoint(BoardSetup board, int player, out Vector3 point)
    {
        point = default;
        if (mainCamera == null || !Pointer.OnScreen) return false;
        boardPlane.SetNormalAndPosition(Vector3.up, new Vector3(0, board.PieceHeight, 0));
        var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (!boardPlane.Raycast(ray, out var distance)) return false;
        point = ray.GetPoint(distance);
        return true;
    }

    private GamePieceDragAndReleaseForce StoneUnderCursor()
    {
        if (!Pointer.OnScreen) return null;
        var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out var hit, 100f) ? hit.collider.GetComponentInParent<GamePieceDragAndReleaseForce>() : null;
    }

    private static bool IsPointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}
