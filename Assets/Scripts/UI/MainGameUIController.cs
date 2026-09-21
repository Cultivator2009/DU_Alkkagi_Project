using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the MainGame_UI prefab: the turn pill, one PlayerHudPanel per
// player and the win modal. Layout and styling live in the prefab (built by
// Tools > Alkkagi UI > 2. Build HUD); this only pushes match state into it.
public class MainGameUIController : MonoBehaviour
{
    public GameObject turnPill;
    public TMP_Text turnText;
    public Image turnStone;
    public PlayerHudPanel[] playerPanels; // index-aligned with GameManager.playersList (0 = black)
    public GameObject winPanel;
    public TMP_Text winText;
    public TMP_Text winDetailText;
    public Button rematchButton;
    public Button mainMenuButton;
    public Color blackStoneColor = new Color(0.08f, 0.08f, 0.08f);
    public Color whiteStoneColor = new Color(0.97f, 0.97f, 0.97f);

    // Item-mode placeholder: reserves a slot for a future item bar without
    // building any real item logic yet (Phase 1's ITurnAction is still a
    // stub). Wire itemBarRoot to an empty layout container in the Inspector;
    // ShowAvailableItems/OnItemButtonClicked are the seam an item ruleset
    // will use once real items exist.
    public Transform itemBarRoot;
    public GameObject itemButtonTemplate; // simple Button+TMP_Text prefab, kept inactive as a template
    public event Action<string> OnItemButtonClicked;

    private TurnController turnController;
    private NetworkMatchBridge networkBridge;
    private int currentPlayerId;
    private int? winnerPlayerId;

    private void Awake()
    {
        rematchButton.onClick.AddListener(Rematch);
        mainMenuButton.onClick.AddListener(ReturnToMainMenu);
    }

    private void OnEnable()
    {
        winPanel.SetActive(false);
        Loc.OnLanguageChanged += Render;
        StartCoroutine(WaitForMatchThenSubscribe());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Loc.OnLanguageChanged -= Render;
        if (turnController != null) Unsubscribe(turnController);
        if (networkBridge != null) UnsubscribeNetwork(networkBridge);
        turnController = null;
        networkBridge = null;
    }

    private IEnumerator WaitForMatchThenSubscribe()
    {
        GameManager gameManager;
        while ((gameManager = GameManager.manager) == null || gameManager.TurnController == null) yield return null;

        turnController = gameManager.TurnController;
        Subscribe(turnController);

        // Only present on a networked match (see NetworkBootstrap) - a guest's
        // TurnController never ticks, so its own events never fire past the
        // initial StartMatch and the bridge's events carry turn/score updates
        // instead.
        networkBridge = FindObjectOfType<NetworkMatchBridge>();
        if (networkBridge != null) SubscribeNetwork(networkBridge);

        // Rematching an online game needs a protocol round-trip that doesn't
        // exist yet, so it's offered for local matches only.
        rematchButton.gameObject.SetActive(!IsOnlineMatch());

        for (var i = 0; i < playerPanels.Length; i++) playerPanels[i].Build(CountPieces(i));

        // StartMatch already fired OnTurnStarted inside GamePreparation, before
        // this coroutine could subscribe - read the opening turn directly.
        currentPlayerId = turnController.CurrentPlayerID;
        Render();
    }

    private void Subscribe(TurnController controller)
    {
        controller.OnTurnStarted += HandleTurnStarted;
        controller.OnTurnEnded += HandleTurnEnded;
        controller.OnMatchEnded += HandleMatchEnded;
    }

    private void Unsubscribe(TurnController controller)
    {
        controller.OnTurnStarted -= HandleTurnStarted;
        controller.OnTurnEnded -= HandleTurnEnded;
        controller.OnMatchEnded -= HandleMatchEnded;
    }

    private void SubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged += HandleGuestTurnChanged;
        bridge.OnGuestMatchEnded += HandleGuestMatchEnded;
    }

    private void UnsubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged -= HandleGuestTurnChanged;
        bridge.OnGuestMatchEnded -= HandleGuestMatchEnded;
    }

    private void HandleTurnStarted(PlayersManager player)
    {
        currentPlayerId = player.ID;
        Render();
    }

    private void HandleTurnEnded(PlayersManager player)
    {
        Render();
    }

    private void HandleMatchEnded(PlayersManager winner)
    {
        ShowWinPanel(winner.ID);
    }

    private void HandleGuestTurnChanged(int playerId)
    {
        currentPlayerId = playerId;
        Render();
    }

    private void HandleGuestMatchEnded(int winnerPlayerId)
    {
        ShowWinPanel(winnerPlayerId);
    }

    private void ShowWinPanel(int winnerId)
    {
        winnerPlayerId = winnerId;
        winPanel.SetActive(true);
        Render();
    }

    private void Render()
    {
        var gameManager = GameManager.manager;
        if (turnController == null || gameManager == null) return;

        turnPill.SetActive(!winnerPlayerId.HasValue);
        turnText.text = Loc.Get("hud.turn", ColorName(currentPlayerId));
        turnStone.color = currentPlayerId == 0 ? blackStoneColor : whiteStoneColor;

        for (var i = 0; i < playerPanels.Length && i < gameManager.playersList.Count; i++)
        {
            var isTurn = !winnerPlayerId.HasValue && i == currentPlayerId;
            playerPanels[i].Render(ColorName(i), Loc.Get("player.number", i + 1), CountPieces(i), gameManager.playersList[i].score, isTurn);
        }

        if (winnerPlayerId.HasValue)
        {
            var winner = winnerPlayerId.Value;
            winText.text = Loc.Get("win.title", ColorName(winner));
            winDetailText.text = Loc.Get("win.detail", Loc.Get("player.number", winner + 1), CountPieces(winner));
        }
    }

    private static string ColorName(int playerId) => Loc.Get(playerId == 0 ? "player.black" : "player.white");

    // Counted from the live pieces rather than PlayersManager.totalPieceCnt:
    // a network guest only receives removals (NetworkMatchBridge drops them
    // from gamePieceScripts), its totalPieceCnt never decrements.
    private static int CountPieces(int playerId)
    {
        return GameManager.manager.gamePieceScripts.Count(piece => piece != null && piece.GetComponent<GamePieceManager>().playerIndex == playerId);
    }

    private static bool IsOnlineMatch()
    {
        return SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.CurrentLobby.HasValue;
    }

    private void Rematch()
    {
        GameManager.manager.EndMatch();
        SceneManager.LoadScene("GameScene");
    }

    private void ReturnToMainMenu()
    {
        // Leaving the lobby matters: NetworkBootstrap treats any GameScene load
        // with a live lobby as an online match, local games included.
        if (IsOnlineMatch()) SteamLobbyManager.Instance.LeaveLobby();
        GameManager.manager.EndMatch();
        SceneManager.LoadScene("MainMenuScene");
    }

    // ---- Item bar placeholder ----

    public void ShowAvailableItems(IReadOnlyList<string> itemIds)
    {
        if (itemBarRoot == null || itemButtonTemplate == null) return;

        for (var i = itemBarRoot.childCount - 1; i >= 0; i--)
        {
            var child = itemBarRoot.GetChild(i).gameObject;
            if (child != itemButtonTemplate) Destroy(child);
        }

        foreach (var itemId in itemIds)
        {
            var button = Instantiate(itemButtonTemplate, itemBarRoot);
            button.SetActive(true);
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = itemId;
            var clickTarget = button.GetComponent<Button>();
            if (clickTarget != null) clickTarget.onClick.AddListener(() => OnItemButtonClicked?.Invoke(itemId));
        }
    }
}
