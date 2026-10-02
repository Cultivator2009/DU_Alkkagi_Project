using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Screen-space aiming feedback for the piece being dragged: a line round it
// (PieceOutline) that deepens with power, an arrow in the shot direction,
// the power percentage, a faint line back to the pull point, and - when the
// match allows it (MatchSettings.AimGuide) - a dotted guide up to the first
// stone or board edge the shot would reach, the stone outlined too. Only
// ever shows for this machine's own drag. Before a drag, a line round the
// piece under the cursor says it can be picked up.
public class AimIndicator : MonoBehaviour
{
    public RectTransform arrow;         // pivot at its base, pointing up
    public RectTransform arrowShaft;
    public RectTransform powerLabel;
    public TMP_Text powerText;
    public RectTransform pullLine;      // pivot at its base, pointing up
    public RectTransform pullMark;
    public RectTransform guideRoot;
    public GameObject dotTemplate;
    public float arrowGap = 14f;        // between the piece's outline and the arrow
    // Short, so the arrow says which way rather than drawing a line to the
    // target: a long one was as good as the aim guide on a match without it.
    public float arrowMaxLength = 56f;
    public float labelGap = 46f;
    public float dotSpacing = 16f;

    private readonly List<RectTransform> dots = new List<RectTransform>();
    private RectTransform area;
    private Canvas canvas;
    private Camera worldCamera;
    private MainGameUIController game;
    private PieceOutline outline;
    private bool guideEnabled;

    private void OnEnable()
    {
        area = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        game = GetComponentInParent<MainGameUIController>();
        worldCamera = Camera.main;
        if (worldCamera != null) outline = PieceOutline.For(worldCamera);
        guideEnabled = MatchSettings.Current.AimGuide; // fixed for the match
        SetVisible(false);
    }

    private void OnDisable()
    {
        if (outline == null) return;
        foreach (PieceOutline.Mark mark in System.Enum.GetValues(typeof(PieceOutline.Mark))) outline.Set(mark, null);
    }

    private void LateUpdate()
    {
        var gameManager = GameManager.manager;
        var piece = gameManager == null ? null : gameManager.gamePieceScripts.FirstOrDefault(p => p != null && p.isDragging);
        if (piece == null || worldCamera == null)
        {
            SetVisible(false);
            if (outline != null)
            {
                outline.Set(PieceOutline.Mark.Aim, null);
                outline.Set(PieceOutline.Mark.Target, null);
            }
            ShowHover();
            return;
        }
        SetVisible(true);
        outline.Set(PieceOutline.Mark.Hover, null);
        outline.Set(PieceOutline.Mark.Aim, piece);

        var origin = piece.AimOrigin;
        var stoneRadius = piece.Manager.radius;
        var center = ToLocal(origin);
        var power = piece.AimPower;
        outline.AimPower = power;

        // No further than a full-power pull: past it the line would only
        // grow into a longer ruler.
        var pullPoint = ToLocal(piece.DragPoint);
        var fullPull = piece.fullPullScreen * Screen.height / canvas.scaleFactor;
        pullPoint = center + Vector2.ClampMagnitude(pullPoint - center, fullPull);
        Point(pullLine, center, pullPoint);
        pullMark.anchoredPosition = pullPoint;

        var hasDirection = piece.AimDirection != Vector3.zero && power > 0f;
        var direction = hasDirection ? (ToLocal(origin + piece.AimDirection) - center).normalized : Vector2.up;
        // From just outside the piece as it lies, whichever way it points.
        var arrowBase = center + direction * (Reach(piece, center, direction) + arrowGap);
        arrow.gameObject.SetActive(hasDirection);
        arrow.anchoredPosition = arrowBase;
        arrow.localRotation = Quaternion.Euler(0, 0, Angle(direction));
        arrowShaft.sizeDelta = new Vector2(arrowShaft.sizeDelta.x, arrowMaxLength * power);

        // Beside the arrow rather than on it, whichever way it points.
        var side = new Vector2(direction.y, -direction.x);
        powerLabel.anchoredPosition = center + side * (Reach(piece, center, side) + labelGap);
        powerText.text = $"{Mathf.RoundToInt(power * 100)}%";

        if (guideEnabled && hasDirection) DrawGuide(piece, gameManager, origin, stoneRadius, arrowBase);
        else HideGuide();
    }

    // How far the piece reaches on screen from center along direction: the
    // furthest corner of its bounds.
    private float Reach(GamePieceDragAndReleaseForce piece, Vector2 center, Vector2 direction)
    {
        var bounds = piece.GetComponent<Renderer>().bounds;
        var reach = 0f;
        for (var i = 0; i < 8; i++)
        {
            var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            reach = Mathf.Max(reach, Vector2.Dot(ToLocal(corner) - center, direction));
        }
        return reach;
    }

    // Not during placement, which marks its own (PlacementController).
    private void ShowHover()
    {
        if (outline == null || (GameManager.manager != null && GameManager.manager.gameState == GameManager.GameState.Placement)) return;
        outline.Set(PieceOutline.Mark.Hover, game != null ? PieceUnderCursor() : null);
    }

    // The piece a click here would pick up (as PieceSelector takes it): the
    // nearest under the cursor that this screen may move. Not while the
    // view moves, the game is paused, or the cursor is on the HUD.
    private GamePieceDragAndReleaseForce PieceUnderCursor()
    {
        if (CameraRig.Busy || Time.timeScale <= 0 || !Pointer.OnScreen) return null;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return null;
        return PiecePicker.UnderCursor(worldCamera).FirstOrDefault(p => game.MayPickUp(p.Manager.playerIndex));
    }

    // First contact along the shot, on the board plane: the nearest piece
    // whose center comes within the two pieces' radii of the shot line, else
    // where the moving piece would cross the board edge.
    private void DrawGuide(GamePieceDragAndReleaseForce piece, GameManager gameManager, Vector3 origin, float radius, Vector2 from)
    {
        var dir = piece.AimDirection;
        var best = gameManager.Board.Playable.Exit(new Vector2(origin.x, origin.z), new Vector2(dir.x, dir.z));
        GamePieceDragAndReleaseForce target = null;
        foreach (var other in gameManager.gamePieceScripts)
        {
            if (other == null || other == piece) continue;
            var to = other.AimOrigin - origin;
            to.y = 0;
            var otherRadius = other.Manager.radius;
            var reach = radius + otherRadius; // pieces come in sizes
            var along = Vector3.Dot(to, dir);
            if (along <= 0) continue;
            var offLine = to.sqrMagnitude - along * along;
            if (offLine > reach * reach) continue;
            var contact = along - Mathf.Sqrt(reach * reach - offLine);
            if (contact >= best) continue;
            best = contact;
            target = other;
        }

        var to2D = ToLocal(origin + dir * Mathf.Max(best, 0f));
        var length = Vector2.Distance(from, to2D);
        var step = (to2D - from).normalized;
        var count = Mathf.Max(0, Mathf.FloorToInt(length / dotSpacing));
        for (var i = 0; i < count; i++)
        {
            if (i >= dots.Count)
            {
                var dot = Instantiate(dotTemplate, guideRoot).GetComponent<RectTransform>();
                dots.Add(dot);
            }
            dots[i].gameObject.SetActive(true);
            dots[i].anchoredPosition = from + step * (dotSpacing * (i + 0.5f));
        }
        for (var i = count; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
        outline.Set(PieceOutline.Mark.Target, target);
    }

    private void HideGuide()
    {
        foreach (var dot in dots) dot.gameObject.SetActive(false);
        if (outline != null) outline.Set(PieceOutline.Mark.Target, null);
    }

    private Vector2 ToLocal(Vector3 world)
    {
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, worldCamera.WorldToScreenPoint(world), uiCamera, out var local);
        return local;
    }

    // Stretches an up-pointing, bottom-pivoted rect from a to b.
    private static void Point(RectTransform line, Vector2 a, Vector2 b)
    {
        line.anchoredPosition = a;
        line.sizeDelta = new Vector2(line.sizeDelta.x, Vector2.Distance(a, b));
        line.localRotation = Quaternion.Euler(0, 0, Angle(b - a));
    }

    private static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f;

    private void SetVisible(bool visible)
    {
        for (var i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(visible);
        if (visible) return;
        HideGuide();
    }
}
