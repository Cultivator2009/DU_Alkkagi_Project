using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The placement-phase card in the HUD (Tools > Alkkagi UI > 2. Build HUD):
// whose go it is, what to do, every side's clock, and Ready. Reads the phase
// every frame while it runs; hidden otherwise.
public class PlacementHud : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text titleText;
    public TMP_Text hintText;
    public TMP_Text[] clockTexts; // by player id
    public RectTransform[] clockRows;
    public float rowSpacing = 44;
    public TMP_Text statusText;
    public Button readyButton;
    public Color clockColor = Color.black;
    public Color clockIdleColor = Color.gray;
    public Color clockWarningColor = Color.red;
    public float clockWarningSeconds = 10f;

    private PlacementController controller;
    private float statusY;    // as built, for two sides
    private float cardHeight;

    private void Awake()
    {
        readyButton.onClick.AddListener(() => Controller.Ready());
        statusY = statusText.rectTransform.anchoredPosition.y;
        cardHeight = ((RectTransform)panel.transform).sizeDelta.y;
    }

    private PlacementController Controller => controller != null ? controller : controller = FindObjectOfType<PlacementController>();

    private void Update()
    {
        var gameManager = GameManager.manager;
        var phase = gameManager != null ? gameManager.Placement : null;
        var show = phase != null && !phase.Done && gameManager.gameState == GameManager.GameState.Placement && Controller != null;
        var players = gameManager != null ? gameManager.playersList.Count : 2;
        if (panel.activeSelf != show)
        {
            panel.SetActive(show);
            if (show)
            {
                SideMark.ShowAll(this, MatchSettings.Current.PieceType);
                Arrange(players);
            }
        }
        if (!show) return;

        var actor = Controller.Actor;
        titleText.text = phase.Placer == PlacementPhase.Everyone ? Loc.Get("placement.title") : Loc.Get("placement.turn", ColorName(phase.Placer));

        var acting = actor >= 0 && phase.CanAct(actor);
        if (acting)
        {
            var left = phase.Unplaced(actor);
            var hint = left > 0 ? Loc.Get("placement.hint", left) : Loc.Get("placement.hintDone");
            if (phase.CanMoveStones) hint += "\n" + Loc.Get("placement.hintMove");
            hintText.text = hint;
        }
        else
        {
            hintText.text = Loc.Get(actor >= 0 && phase.IsReady(actor) ? "placement.readyDone" : "placement.waiting");
        }

        for (var player = 0; player < players; player++)
        {
            var seconds = Mathf.CeilToInt(phase.Clock(player));
            clockTexts[player].text = Loc.Get("placement.clock", ColorName(player), $"{seconds / 60}:{seconds % 60:00}");
            var running = phase.CanAct(player);
            clockTexts[player].color = !running ? clockIdleColor : seconds <= clockWarningSeconds ? clockWarningColor : clockColor;
        }

        // Everyone placing at once only happens online; there it's worth
        // knowing who else is done.
        var allAtOnce = phase.Placer == PlacementPhase.Everyone && actor >= 0;
        var othersReady = Enumerable.Range(0, players).Count(p => p != actor && phase.IsReady(p));
        statusText.text = !allAtOnce || othersReady == 0 ? Loc.Get("placement.timeoutNote")
            : players == 2 ? Loc.Get("placement.opponentReady")
            : Loc.Get("placement.othersReady", othersReady, players - 1);

        // Taking turns ends by itself once the last stone is down.
        var showReady = phase.Style != PlacementStyle.Alternating && actor >= 0 && !phase.IsReady(actor);
        readyButton.gameObject.SetActive(showReady);
        if (showReady) SetInteractable(readyButton, acting && phase.Unplaced(actor) == 0);
    }

    // A clock row per side playing; the status line and the card move down
    // for each one past two.
    private void Arrange(int players)
    {
        for (var i = 0; i < clockRows.Length; i++) clockRows[i].gameObject.SetActive(i < players);
        var extra = (players - 2) * rowSpacing;
        var status = statusText.rectTransform;
        status.anchoredPosition = new Vector2(status.anchoredPosition.x, statusY - extra);
        var card = (RectTransform)panel.transform;
        card.sizeDelta = new Vector2(card.sizeDelta.x, cardHeight + extra);
    }

    private static string ColorName(int playerId) => SideStyle.Name(playerId);

    private static void SetInteractable(Button button, bool interactable)
    {
        button.interactable = interactable;
        var group = button.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = interactable ? 1f : 0.45f;
    }
}
