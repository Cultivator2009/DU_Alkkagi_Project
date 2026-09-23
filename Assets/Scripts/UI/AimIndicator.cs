using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen-space aiming feedback for the piece being dragged: a ring around
// the stone that fills with power, an arrow in the shot direction, the
// power percentage, a faint line back to the pull point, and - when the
// match allows it (MatchSettings.AimGuide) - a dotted guide up to the first
// stone or board edge the shot would reach. Only ever shows for this
// machine's own drag.
public class AimIndicator : MonoBehaviour
{
    public RectTransform ring;
    public Image ringFill;              // Filled / Radial360
    public RectTransform arrow;         // pivot at its base, pointing up
    public RectTransform arrowShaft;
    public RectTransform powerLabel;
    public TMP_Text powerText;
    public RectTransform pullLine;      // pivot at its base, pointing up
    public RectTransform pullMark;
    public RectTransform guideRoot;
    public GameObject dotTemplate;
    public RectTransform targetMark;
    public float ringPadding = 14f;
    public float arrowMaxLength = 150f;
    public float labelGap = 46f;
    public float dotSpacing = 16f;

    private readonly List<RectTransform> dots = new List<RectTransform>();
    private RectTransform area;
    private Canvas canvas;
    private Camera worldCamera;
    private bool guideEnabled;

    private void OnEnable()
    {
        area = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        worldCamera = Camera.main;
        guideEnabled = MatchSettings.Current.AimGuide; // fixed for the match
        SetVisible(false);
    }

    private void LateUpdate()
    {
        var gameManager = GameManager.manager;
        var piece = gameManager == null ? null : gameManager.gamePieceScripts.FirstOrDefault(p => p != null && p.isDragging);
        if (piece == null || worldCamera == null)
        {
            SetVisible(false);
            return;
        }
        SetVisible(true);

        var origin = piece.transform.position;
        var stoneRadius = piece.GetComponent<GamePieceManager>().radius;
        var center = ToLocal(origin);
        var ringRadius = (ToLocal(origin + Vector3.right * stoneRadius) - center).magnitude + ringPadding;
        var power = piece.AimPower;

        ring.anchoredPosition = center;
        ring.sizeDelta = Vector2.one * ringRadius * 2f;
        ringFill.fillAmount = power;

        var pullPoint = ToLocal(piece.DragPoint);
        Point(pullLine, center, pullPoint);
        pullMark.anchoredPosition = pullPoint;

        var hasDirection = piece.AimDirection != Vector3.zero && power > 0f;
        var direction = hasDirection ? (ToLocal(origin + piece.AimDirection) - center).normalized : Vector2.up;
        arrow.gameObject.SetActive(hasDirection);
        arrow.anchoredPosition = center + direction * ringRadius;
        arrow.localRotation = Quaternion.Euler(0, 0, Angle(direction));
        arrowShaft.sizeDelta = new Vector2(arrowShaft.sizeDelta.x, arrowMaxLength * power);

        // Beside the arrow rather than on it, whichever way it points.
        var side = new Vector2(direction.y, -direction.x);
        powerLabel.anchoredPosition = center + side * (ringRadius + labelGap);
        powerText.text = $"{Mathf.RoundToInt(power * 100)}%";

        if (guideEnabled && hasDirection) DrawGuide(piece, gameManager, origin, stoneRadius, center + direction * ringRadius);
        else HideGuide();
    }

    // First contact along the shot, on the board plane: the nearest piece
    // whose center comes within the two pieces' radii of the shot line, else
    // where the moving piece would cross the board edge.
    private void DrawGuide(GamePieceDragAndReleaseForce piece, GameManager gameManager, Vector3 origin, float radius, Vector2 from)
    {
        var dir = piece.AimDirection;
        var best = DistanceToBoardEdge(gameManager.Board.SurfaceBounds, origin, dir);
        Transform target = null;
        var targetRadius = 0f;
        foreach (var other in gameManager.gamePieceScripts)
        {
            if (other == null || other == piece) continue;
            var to = other.transform.position - origin;
            to.y = 0;
            var otherRadius = other.GetComponent<GamePieceManager>().radius;
            var reach = radius + otherRadius; // pieces come in sizes
            var along = Vector3.Dot(to, dir);
            if (along <= 0) continue;
            var offLine = to.sqrMagnitude - along * along;
            if (offLine > reach * reach) continue;
            var contact = along - Mathf.Sqrt(reach * reach - offLine);
            if (contact >= best) continue;
            best = contact;
            target = other.transform;
            targetRadius = otherRadius;
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

        targetMark.gameObject.SetActive(target != null);
        if (target != null)
        {
            var targetCenter = ToLocal(target.position);
            var screenRadius = (ToLocal(target.position + Vector3.right * targetRadius) - targetCenter).magnitude;
            targetMark.anchoredPosition = targetCenter;
            targetMark.sizeDelta = Vector2.one * (screenRadius * 2f + 16f);
        }
    }

    private void HideGuide()
    {
        foreach (var dot in dots) dot.gameObject.SetActive(false);
        targetMark.gameObject.SetActive(false);
    }

    private static float DistanceToBoardEdge(Bounds board, Vector3 origin, Vector3 dir)
    {
        var t = float.MaxValue;
        if (dir.x > 1e-5f) t = Mathf.Min(t, (board.max.x - origin.x) / dir.x);
        if (dir.x < -1e-5f) t = Mathf.Min(t, (board.min.x - origin.x) / dir.x);
        if (dir.z > 1e-5f) t = Mathf.Min(t, (board.max.z - origin.z) / dir.z);
        if (dir.z < -1e-5f) t = Mathf.Min(t, (board.min.z - origin.z) / dir.z);
        return t == float.MaxValue ? 0f : t;
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
