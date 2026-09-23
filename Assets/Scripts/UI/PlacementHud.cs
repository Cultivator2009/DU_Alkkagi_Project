using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The placement-phase card in the HUD (Tools > Alkkagi UI > 2. Build HUD):
// whose go it is, what to do, both clocks, and Ready. Reads the phase every
// frame while it runs; hidden otherwise.
public class PlacementHud : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text titleText;
    public TMP_Text hintText;
    public TMP_Text[] clockTexts; // by player id
    public TMP_Text statusText;
    public Button readyButton;
    public Color clockColor = Color.black;
    public Color clockIdleColor = Color.gray;
    public Color clockWarningColor = Color.red;
    public float clockWarningSeconds = 10f;

    private PlacementController controller;

    private void Awake()
    {
        readyButton.onClick.AddListener(() => Controller.Ready());
    }

    private PlacementController Controller => controller != null ? controller : controller = FindObjectOfType<PlacementController>();

    private void Update()
    {
        var gameManager = GameManager.manager;
        var phase = gameManager != null ? gameManager.Placement : null;
        var show = phase != null && !phase.Done && gameManager.gameState == GameManager.GameState.Placement && Controller != null;
        if (panel.activeSelf != show)
        {
            panel.SetActive(show);
            if (show) SideMark.ShowAll(this, MatchSettings.Current.PieceType);
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

        for (var player = 0; player < clockTexts.Length; player++)
        {
            var seconds = Mathf.CeilToInt(phase.Clock(player));
            clockTexts[player].text = Loc.Get("placement.clock", ColorName(player), $"{seconds / 60}:{seconds % 60:00}");
            var running = phase.CanAct(player);
            clockTexts[player].color = !running ? clockIdleColor : seconds <= clockWarningSeconds ? clockWarningColor : clockColor;
        }

        // Both placing at once only happens online; there it's worth knowing
        // the other side is done.
        var bothAtOnce = phase.Placer == PlacementPhase.Everyone;
        statusText.text = bothAtOnce && actor >= 0 && phase.IsReady(1 - actor) ? Loc.Get("placement.opponentReady") : Loc.Get("placement.timeoutNote");

        // Taking turns ends by itself once the last stone is down.
        var showReady = phase.Style != PlacementStyle.Alternating && actor >= 0 && !phase.IsReady(actor);
        readyButton.gameObject.SetActive(showReady);
        if (showReady) SetInteractable(readyButton, acting && phase.Unplaced(actor) == 0);
    }

    private static string ColorName(int playerId) => SideStyle.Name(playerId);

    private static void SetInteractable(Button button, bool interactable)
    {
        button.interactable = interactable;
        var group = button.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = interactable ? 1f : 0.45f;
    }
}
