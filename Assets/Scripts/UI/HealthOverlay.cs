using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A battle of health on the board: the damage of each knock rising from
// where it landed, and with each piece's own health (A) a bar over every
// piece. Built by Tools > Alkkagi UI > 2. Build HUD; quiet in a classic match.
public class HealthOverlay : MonoBehaviour
{
    public RectTransform barTemplate; // a background, its "Fill" child stretched from the left
    public TMP_Text numberTemplate;
    public float barGap = 16f;        // above the piece's top, on the screen
    public float numberRise = 70f;
    public float numberSeconds = 0.9f;
    public Color barFull = new Color(0.36f, 0.66f, 0.30f);
    public Color barLow = new Color(0.78f, 0.20f, 0.14f);

    private RectTransform area;
    private Canvas canvas;
    private Camera worldCamera;
    private bool perPiece;
    private int fullHealth;
    private readonly Dictionary<GamePieceDragAndReleaseForce, (RectTransform bar, RectTransform fill, Image fillImage)> bars = new Dictionary<GamePieceDragAndReleaseForce, (RectTransform, RectTransform, Image)>();
    private readonly List<(TMP_Text text, Vector3 at, float age)> numbers = new List<(TMP_Text, Vector3, float)>();

    private void OnEnable()
    {
        area = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        worldCamera = Camera.main;
        var settings = MatchSettings.Current;
        perPiece = settings.Variant == GameVariant.Health && settings.HealthRule == HealthRule.PerPiece;
        fullHealth = settings.PieceHealth;
        barTemplate.gameObject.SetActive(false);
        numberTemplate.gameObject.SetActive(false);
        GameManager.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        GameManager.Damaged -= OnDamaged;
    }

    private void OnDamaged(char pieceId, int amount, Vector3 at)
    {
        var text = Instantiate(numberTemplate, area);
        text.text = "-" + amount;
        // Harder knocks come up bigger.
        text.fontSize = numberTemplate.fontSize * Mathf.Lerp(0.8f, 1.5f, amount / (float)HealthRuleset.MaxDamage);
        text.gameObject.SetActive(true);
        numbers.Add((text, at, 0f));
    }

    // Game time, as the board's other effects.
    private void LateUpdate()
    {
        if (worldCamera == null) worldCamera = Camera.main;
        var gameManager = GameManager.manager;
        if (worldCamera == null || gameManager == null) return;

        if (perPiece) PlaceBars(gameManager.gamePieceScripts);

        for (var i = numbers.Count - 1; i >= 0; i--)
        {
            var (text, at, age) = numbers[i];
            age += Time.deltaTime;
            if (age >= numberSeconds || text == null)
            {
                if (text != null) Destroy(text.gameObject);
                numbers.RemoveAt(i);
                continue;
            }
            var t = age / numberSeconds;
            text.rectTransform.anchoredPosition = ToLocal(at) + Vector2.up * (numberRise * (1 - (1 - t) * (1 - t)));
            text.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            numbers[i] = (text, at, age);
        }
    }

    // A bar over each piece still in sight, its fill its health left.
    private void PlaceBars(List<GamePieceDragAndReleaseForce> pieces)
    {
        foreach (var gone in bars.Keys.Where(p => p == null || !pieces.Contains(p)).ToList())
        {
            if (bars[gone].bar != null) Destroy(bars[gone].bar.gameObject);
            bars.Remove(gone);
        }
        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            if (!bars.TryGetValue(piece, out var bar))
            {
                var made = Instantiate(barTemplate, area);
                var fill = (RectTransform)made.Find("Fill");
                bar = (made, fill, fill.GetComponent<Image>());
                bars[piece] = bar;
            }
            var look = piece.GetComponent<Renderer>();
            var shown = look != null && look.enabled && piece.Manager.health > 0;
            bar.bar.gameObject.SetActive(shown);
            if (!shown) continue;
            var bounds = look.bounds;
            bar.bar.anchoredPosition = ToLocal(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)) + Vector2.up * barGap;
            var left = Mathf.Clamp01(piece.Manager.health / (float)Mathf.Max(1, fullHealth));
            bar.fill.anchorMax = new Vector2(left, 1);
            bar.fillImage.color = Color.Lerp(barLow, barFull, left);
        }
    }

    private Vector2 ToLocal(Vector3 world)
    {
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, worldCamera.WorldToScreenPoint(world), uiCamera, out var local);
        return local;
    }
}
