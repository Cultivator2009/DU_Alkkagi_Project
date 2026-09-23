using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the MainGame_UI prefab: the turn pill, one PlayerHudPanel per
// player and the game-over screen. Layout and styling live in the prefab
// (built by Tools > Alkkagi UI > 2. Build HUD); this only pushes match state
// into it.
public class MainGameUIController : MonoBehaviour
{
    public GameObject turnPill;
    public TMP_Text turnText;
    public Image turnStone;
    public PlayerHudPanel[] playerPanels; // index-aligned with GameManager.playersList (0 = black)
    public Color blackStoneColor = new Color(0.08f, 0.08f, 0.08f);
    public Color whiteStoneColor = new Color(0.97f, 0.97f, 0.97f);
    public Color turnTextColor = Color.black;
    public Color clockWarningColor = Color.red;
    public float clockWarningSeconds = 5f;
    public GameObject notice;   // "Black ran out of time" and the like, briefly
    public TMP_Text noticeText;
    public float noticeSeconds = 2.5f;

    [Header("Game over")]
    public GameObject gameOverPanel;
    public TMP_Text stampText;
    public TMP_Text resultTitleText;
    public TMP_Text resultReasonText;
    public TMP_Text[] remainingCells; // scoreboard columns, by player id
    public TMP_Text[] capturedCells;
    public TMP_Text[] shotsCells;
    public TMP_Text matchTimeText;
    public TMP_Text seriesText;
    public TMP_Text statusText;
    public Button rematchButton;
    public TMP_Text rematchLabel;
    public Button lobbyButton;
    public Button mainMenuButton;

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
    private readonly int[] shots = new int[2];
    private float matchStartTime;
    private float matchEndTime;
    private int? winnerPlayerId; // set once the result is in; -1 = draw
    private MatchEndReason endReason;
    private bool opponentReturnedToLobby;
    private bool turnsStarted;
    private float noticeUntil;

    private void Awake()
    {
        rematchButton.onClick.AddListener(OnClickRematch);
        lobbyButton.onClick.AddListener(() => networkBridge.ReturnToLobby());
        mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        for (var i = 0; i < playerPanels.Length; i++)
        {
            var playerId = i;
            playerPanels[i].skipButton.onClick.AddListener(() => OnClickSkip(playerId));
        }
    }

    private void OnEnable()
    {
        gameOverPanel.SetActive(false);
        notice.SetActive(false);
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

        var online = networkBridge != null;
        lobbyButton.gameObject.SetActive(online);
        // The series runs for as long as the same two players keep rematching:
        // the lobby online, the session since the main menu locally.
        MatchSeries.Begin(online ? $"lobby:{SteamLobbyManager.Instance.CurrentLobby.Value.Id.Value}" : "local");

        for (var i = 0; i < playerPanels.Length; i++) playerPanels[i].Build(CountPieces(i));

        // A local game without placement has already fired its opening
        // OnTurnStarted by now - read the opening turn directly.
        currentPlayerId = turnController.CurrentPlayerID;
        Render();
    }

    private void Update()
    {
        if (turnController == null) return;
        var gameManager = GameManager.manager;

        // Turns begin after the guest has loaded and any placement is done;
        // the match clock starts there too.
        if (!turnsStarted && IsTurnState(gameManager.gameState))
        {
            turnsStarted = true;
            matchStartTime = Time.time;
            Render();
        }
        if (noticeUntil > 0 && Time.time >= noticeUntil)
        {
            noticeUntil = 0;
            notice.SetActive(false);
        }
        if (turnsStarted && !winnerPlayerId.HasValue) RenderTurn();
    }

    private static bool IsTurnState(GameManager.GameState state)
    {
        return state == GameManager.GameState.WaitingForInput || state == GameManager.GameState.WaitingForEndTurn
            || state == GameManager.GameState.ProcessingTurn || state == GameManager.GameState.TurnChanging;
    }

    // The turn pill with its countdown, and the skip button for the side
    // whose turn it is when this screen plays that side. Every frame, since
    // the clock moves.
    private void RenderTurn()
    {
        var remaining = TurnTimeRemaining();
        turnText.text = remaining.HasValue
            ? Loc.Get("hud.turnTimed", ColorName(currentPlayerId), Mathf.CeilToInt(remaining.Value))
            : Loc.Get("hud.turn", ColorName(currentPlayerId));
        turnText.color = remaining.HasValue && remaining.Value <= clockWarningSeconds ? clockWarningColor : turnTextColor;

        for (var i = 0; i < playerPanels.Length; i++)
            playerPanels[i].skipButton.gameObject.SetActive(i == currentPlayerId && CanSkip(i));
    }

    private float? TurnTimeRemaining()
    {
        if (networkBridge != null && !networkBridge.IsHost) return networkBridge.GuestTurnTimeRemaining;
        if (turnController.TurnSeconds <= 0 || !turnController.IsAwaitingShot) return null;
        return turnController.TurnTimeRemaining;
    }

    private bool CanSkip(int playerId)
    {
        if (networkBridge == null) return turnController.IsAwaitingShot; // hot seat: whoever's turn it is
        if (playerId != networkBridge.LocalPlayerId) return false;
        return networkBridge.IsHost ? turnController.IsAwaitingShot : networkBridge.GuestCanPass;
    }

    private void OnClickSkip(int playerId)
    {
        if (networkBridge != null && !networkBridge.IsHost) networkBridge.RequestPass();
        else turnController.TryPassTurn(playerId);
    }

    private void Subscribe(TurnController controller)
    {
        controller.OnTurnStarted += HandleTurnStarted;
        controller.OnTurnEnded += HandleTurnEnded;
        controller.OnTurnPassed += HandleTurnPassed;
        controller.OnMatchEnded += HandleMatchEnded;
    }

    private void Unsubscribe(TurnController controller)
    {
        controller.OnTurnStarted -= HandleTurnStarted;
        controller.OnTurnEnded -= HandleTurnEnded;
        controller.OnTurnPassed -= HandleTurnPassed;
        controller.OnMatchEnded -= HandleMatchEnded;
    }

    private void SubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged += HandleGuestTurnChanged;
        bridge.OnGuestTurnEnded += HandleGuestTurnEnded;
        bridge.OnGuestTurnPassed += ShowPassNotice;
        bridge.OnGuestMatchEnded += ShowResult;
        bridge.OnOpponentLeft += HandleOpponentLeft;
        bridge.OnOpponentReturnedToLobby += HandleOpponentReturnedToLobby;
        bridge.OnRematchStateChanged += Render;
    }

    private void UnsubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged -= HandleGuestTurnChanged;
        bridge.OnGuestTurnEnded -= HandleGuestTurnEnded;
        bridge.OnGuestTurnPassed -= ShowPassNotice;
        bridge.OnGuestMatchEnded -= ShowResult;
        bridge.OnOpponentLeft -= HandleOpponentLeft;
        bridge.OnOpponentReturnedToLobby -= HandleOpponentReturnedToLobby;
        bridge.OnRematchStateChanged -= Render;
    }

    private void HandleTurnStarted(PlayersManager player)
    {
        currentPlayerId = player.ID;
        Render();
    }

    // Host and local only; a guest counts through HandleGuestTurnEnded.
    private void HandleTurnEnded(PlayersManager player)
    {
        shots[player.ID]++;
        Render();
    }

    private void HandleTurnPassed(PlayersManager player, TurnEnd why)
    {
        ShowPassNotice(player.ID, why);
    }

    private void ShowPassNotice(int playerId, TurnEnd why)
    {
        noticeText.text = Loc.Get(why == TurnEnd.Timeout ? "hud.timeout" : "hud.skipped", ColorName(playerId));
        notice.SetActive(true);
        noticeUntil = Time.time + noticeSeconds;
    }

    private void HandleMatchEnded(PlayersManager winner, MatchEndReason reason)
    {
        ShowResult(winner != null ? winner.ID : -1, reason);
    }

    private void HandleGuestTurnChanged(int playerId)
    {
        currentPlayerId = playerId;
        Render();
    }

    private void HandleGuestTurnEnded(int shooterId)
    {
        shots[shooterId]++;
    }

    private void HandleOpponentLeft()
    {
        // Mid-match that's a forfeit; after the result it only ends the rematch.
        if (!winnerPlayerId.HasValue) ShowResult(networkBridge.LocalPlayerId, MatchEndReason.OpponentLeft);
        else Render();
    }

    private void HandleOpponentReturnedToLobby()
    {
        opponentReturnedToLobby = true;
        Render();
    }

    private void ShowResult(int winnerId, MatchEndReason reason)
    {
        if (winnerPlayerId.HasValue) return;
        winnerPlayerId = winnerId;
        endReason = reason;
        matchEndTime = Time.time;
        MatchSeries.Record(winnerId);
        gameOverPanel.SetActive(true);
        Render();
    }

    private void Render()
    {
        var gameManager = GameManager.manager;
        if (turnController == null || gameManager == null) return;

        var playing = turnsStarted && !winnerPlayerId.HasValue;
        turnPill.SetActive(playing);
        turnStone.color = currentPlayerId == 0 ? blackStoneColor : whiteStoneColor;
        if (playing) RenderTurn();
        else
            foreach (var panel in playerPanels) panel.skipButton.gameObject.SetActive(false);

        for (var i = 0; i < playerPanels.Length && i < gameManager.playersList.Count; i++)
        {
            var isTurn = playing && i == currentPlayerId;
            playerPanels[i].Render(ColorName(i), PlayerLabel(i), CountPieces(i), gameManager.playersList[i].score, isTurn);
        }

        if (winnerPlayerId.HasValue) RenderGameOver(gameManager);
    }

    private void RenderGameOver(GameManager gameManager)
    {
        var winner = winnerPlayerId.Value;
        var loser = 1 - winner;
        var online = networkBridge != null;

        if (winner < 0)
        {
            stampText.text = Loc.Get("result.stampDraw");
            resultTitleText.text = Loc.Get("result.draw");
        }
        else if (online)
        {
            // Online the result reads from this player's side of the board.
            var won = winner == networkBridge.LocalPlayerId;
            stampText.text = Loc.Get(won ? "win.stamp" : "result.stampLose");
            resultTitleText.text = Loc.Get(won ? "result.win" : "result.lose");
        }
        else
        {
            stampText.text = Loc.Get("win.stamp");
            resultTitleText.text = Loc.Get("win.title", ColorName(winner));
        }

        resultReasonText.text = endReason switch
        {
            MatchEndReason.BothOut when winner < 0 => Loc.Get("reason.bothOutDraw"),
            MatchEndReason.BothOut when MatchSettings.Current.BothOutRule == BothOutRule.ShooterWins => Loc.Get("reason.bothOutWin", ColorName(winner)),
            MatchEndReason.BothOut => Loc.Get("reason.bothOut", ColorName(loser)),
            MatchEndReason.OpponentLeft => Loc.Get("reason.opponentLeft"),
            _ => Loc.Get("reason.knockout", ColorName(loser)),
        };

        for (var i = 0; i < 2 && i < gameManager.playersList.Count; i++)
        {
            remainingCells[i].text = CountPieces(i).ToString();
            capturedCells[i].text = gameManager.playersList[i].score.ToString();
            shotsCells[i].text = shots[i].ToString();
        }
        var seconds = Mathf.FloorToInt(matchEndTime - matchStartTime);
        matchTimeText.text = Loc.Get("stats.time", $"{seconds / 60}:{seconds % 60:00}");
        seriesText.text = Loc.Get("series.score", MatchSeries.Wins(0), MatchSeries.Wins(1))
                          + (MatchSeries.Draws > 0 ? Loc.Get("series.draws", MatchSeries.Draws) : string.Empty);

        if (!online)
        {
            rematchLabel.text = Loc.Get("win.rematch");
            statusText.text = string.Empty;
            SetInteractable(rematchButton, true);
            return;
        }

        string statusKey = null;
        if (networkBridge.OpponentGone && endReason != MatchEndReason.OpponentLeft)
            statusKey = opponentReturnedToLobby ? "rematch.opponentLobby" : "rematch.opponentLeft";
        else if (networkBridge.RemoteWantsRematch && !networkBridge.LocalWantsRematch)
            statusKey = "rematch.opponentWants";
        statusText.text = statusKey == null ? string.Empty : Loc.Get(statusKey);

        rematchLabel.text = Loc.Get(networkBridge.LocalWantsRematch ? "rematch.waiting"
            : networkBridge.RemoteWantsRematch ? "rematch.accept" : "rematch.request");
        SetInteractable(rematchButton, !networkBridge.OpponentGone && !networkBridge.LocalWantsRematch);
    }

    private static string ColorName(int playerId) => Loc.Get(playerId == 0 ? "player.black" : "player.white");

    private static string PlayerLabel(int playerId)
    {
        var number = Loc.Get("player.number", playerId + 1);
        return MatchSeries.Played > 0 ? Loc.Get("series.panel", number, MatchSeries.Wins(playerId)) : number;
    }

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

    // Dims the whole capsule via its CanvasGroup; Button's own tint only
    // reaches the fill.
    private static void SetInteractable(Button button, bool interactable)
    {
        button.interactable = interactable;
        var group = button.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = interactable ? 1f : 0.45f;
    }

    private void OnClickRematch()
    {
        if (networkBridge != null)
        {
            networkBridge.RequestRematch();
            return;
        }
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
