using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the MainGame_UI prefab: the turn pill, one PlayerHudPanel per
// player (two to four) and the game-over screen. Layout and styling live in the prefab
// (built by Tools > Alkkagi UI > 2. Build HUD); this only pushes match state
// into it.
public class MainGameUIController : MonoBehaviour
{
    public GameObject turnPill;
    public TMP_Text turnText;
    public SideMark turnStone;
    public PlayerHudPanel[] playerPanels; // index-aligned with GameManager.playersList (0 = black), four
    public float hudMargin = 28;
    [Range(0.3f, 1f)] public float compactScale = 0.58f; // the side panels when three or four play
    public float compactGap = 16;
    public Color turnTextColor = Color.black;
    public Color clockWarningColor = Color.red;
    public float clockWarningSeconds = 5f;
    public GameObject notice;   // "Black ran out of time" and the like, briefly
    public TMP_Text noticeText;
    public float noticeSeconds = 2.5f;
    public KillFeed killFeed;

    [Header("Game over")]
    public GameObject gameOverPanel;
    public TMP_Text stampText;
    public TMP_Text resultTitleText;
    public TMP_Text resultReasonText;
    public RectTransform[] scoreColumns; // one per side, header and cells
    public TMP_Text[] remainingCells; // scoreboard cells, by player id
    public TMP_Text[] killCells;
    public TMP_Text[] nongaeCells;
    public TMP_Text[] suicideCells;
    public TMP_Text[] teamKillCells;
    public TMP_Text[] shotsCells;
    public TMP_Text matchTimeText;
    public TMP_Text seriesText;
    public TMP_Text statusText;
    public Button rematchButton;
    public TMP_Text rematchLabel;
    public Button lobbyButton;
    public Button mainMenuButton;
    public Button resetViewButton; // shown once the view is panned away

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
    private readonly int[] shots = new int[MatchRoster.MaxPlayers];
    private readonly Dictionary<int, MatchEndReason> outSides = new Dictionary<int, MatchEndReason>(); // out while the match went on
    private int lastShooterId = -1;
    private float matchStartTime;
    private float matchEndTime;
    private int? winnerPlayerId; // set once the result is in; -1 = draw
    private MatchEndReason endReason;
    private bool opponentReturnedToLobby;
    private bool turnsStarted;
    private float noticeUntil;
    private int lastTickSecond = -1; // the countdown second last ticked

    private void Awake()
    {
        rematchButton.onClick.AddListener(OnClickRematch);
        lobbyButton.onClick.AddListener(() => networkBridge.ReturnToLobby());
        mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        resetViewButton.onClick.AddListener(() =>
        {
            if (CameraRig.Instance != null) CameraRig.Instance.ResetView();
        });
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
        // The series runs for as long as the same players keep rematching:
        // the lobby and its roster online, the session since the main menu
        // locally.
        MatchSeries.Begin(online ? $"lobby:{SteamLobbyManager.Instance.CurrentLobby.Value.Id.Value}:{string.Join(",", MatchRoster.Current?.SteamIds ?? new ulong[0])}"
            : VersusAI ? $"ai:{LocalOpponent.Current}" : "local");

        Arrange(PlayerCount);
        for (var i = 0; i < PlayerCount; i++) playerPanels[i].Build(CountPieces(i));
        // Every icon and side name: black/white, or Cho/Han with janggi pieces.
        SideMark.ShowAll(this, MatchSettings.Current.PieceType);

        // A local game without placement has already fired its opening
        // OnTurnStarted by now - read the opening turn directly.
        currentPlayerId = turnController.CurrentPlayerID;
        Render();
    }

    private void Update()
    {
        resetViewButton.gameObject.SetActive(CameraRig.Instance != null && CameraRig.Instance.IsMoved);
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

    public bool IsOnline => networkBridge != null;
    private static int PlayerCount => GameManager.manager.playersList.Count;
    private static bool VersusAI => GameManager.manager.VersusAI;
    // The side this screen plays, when it plays just one: online, or against the AI.
    private int? OwnSide => networkBridge != null ? networkBridge.LocalPlayerId : VersusAI ? 1 - GameManager.manager.AIPlayerId : (int?)null;
    // From the match's start (placement included) until its result.
    public bool MatchInProgress => turnController != null && !winnerPlayerId.HasValue;
    public bool CanConcede => turnsStarted && !winnerPlayerId.HasValue && !(OwnSide.HasValue && outSides.ContainsKey(OwnSide.Value));
    // Whose concession the menu offers when it isn't obvious: in a hot-seat
    // game, the side to move. Null when this screen plays one side.
    public int? ConcedingSide => OwnSide.HasValue ? (int?)null : currentPlayerId;

    // The in-game menu's Concede. Online the host decides, as for everything.
    public void Concede()
    {
        if (!CanConcede) return;
        if (networkBridge == null) turnController.Concede(OwnSide ?? currentPlayerId);
        else if (networkBridge.IsHost) turnController.Concede(networkBridge.LocalPlayerId);
        else networkBridge.RequestConcede();
    }

    // Two sides keep the built layout. Three or four: the panels shrink and
    // each goes next to its side's edge - south and west up the left column
    // from the bottom, north and east down the right one from the top - and
    // the skip button (only ever this screen's own) sits above the Menu
    // button. The scoreboard spreads its columns.
    private void Arrange(int players)
    {
        for (var i = 0; i < playerPanels.Length; i++) playerPanels[i].gameObject.SetActive(i < players);
        for (var i = 0; i < scoreColumns.Length; i++) scoreColumns[i].gameObject.SetActive(i < players);
        if (players <= 2) return;

        var pitch = ((RectTransform)playerPanels[0].transform).rect.height * compactScale + compactGap;
        var rightColumn = 0;
        for (var i = 0; i < players; i++)
        {
            var seat = BoardSetup.Seat(i, players);
            var left = seat == 0 || seat == 3;
            var slot = left ? (seat == 0 ? 0 : 1) : rightColumn++;
            var rect = (RectTransform)playerPanels[i].transform;
            rect.localScale = Vector3.one * compactScale;
            rect.anchorMin = rect.anchorMax = rect.pivot = left ? Vector2.zero : Vector2.one;
            rect.anchoredPosition = left ? new Vector2(hudMargin, hudMargin + slot * pitch) : new Vector2(-hudMargin, -hudMargin - slot * pitch);

            var skip = (RectTransform)playerPanels[i].skipButton.transform;
            skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(1, 0);
            skip.anchoredPosition = new Vector2(-hudMargin, hudMargin + 64 + compactGap); // 64: the Menu button
        }
        var columns = players == 3 ? new[] { 330f, 450f, 570f } : new[] { 290f, 390f, 490f, 590f };
        for (var i = 0; i < players; i++) scoreColumns[i].anchoredPosition = new Vector2(columns[i], 0);
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
        // The last seconds tick, on both screens.
        var second = remaining.HasValue ? Mathf.CeilToInt(remaining.Value) : -1;
        if (second > 0 && second <= clockWarningSeconds && second != lastTickSecond) GameAudio.PlayInterface(GameAudio.Bank.tick);
        lastTickSecond = second;

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
        if (networkBridge == null) return turnController.IsAwaitingShot && playerId != GameManager.manager.AIPlayerId; // hot seat: whoever's turn it is
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
        controller.OnPlayerOut += HandlePlayerOut;
        controller.Kills.OnShotResolved += HandleKills;
    }

    private void Unsubscribe(TurnController controller)
    {
        controller.OnTurnStarted -= HandleTurnStarted;
        controller.OnTurnEnded -= HandleTurnEnded;
        controller.OnTurnPassed -= HandleTurnPassed;
        controller.OnMatchEnded -= HandleMatchEnded;
        controller.OnPlayerOut -= HandlePlayerOut;
        controller.Kills.OnShotResolved -= HandleKills;
    }

    private void SubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged += HandleGuestTurnChanged;
        bridge.OnGuestTurnEnded += HandleGuestTurnEnded;
        bridge.OnGuestTurnPassed += ShowPassNotice;
        bridge.OnGuestMatchEnded += ShowResult;
        bridge.OnPlayerOut += HandlePlayerOut;
        bridge.OnPlayerLeft += HandlePlayerLeft;
        bridge.OnPlayerReturnedToLobby += HandlePlayerReturnedToLobby;
        bridge.OnRematchStateChanged += Render;
    }

    private void UnsubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged -= HandleGuestTurnChanged;
        bridge.OnGuestTurnEnded -= HandleGuestTurnEnded;
        bridge.OnGuestTurnPassed -= ShowPassNotice;
        bridge.OnGuestMatchEnded -= ShowResult;
        bridge.OnPlayerOut -= HandlePlayerOut;
        bridge.OnPlayerLeft -= HandlePlayerLeft;
        bridge.OnPlayerReturnedToLobby -= HandlePlayerReturnedToLobby;
        bridge.OnRematchStateChanged -= Render;
    }

    private void HandleTurnStarted(PlayersManager player)
    {
        currentPlayerId = player.ID;
        PlayTurnChime();
        Render();
    }

    // Only for this screen's own turns (online, against the AI); in a
    // hot-seat game every turn is somebody's here.
    private void PlayTurnChime()
    {
        if (!OwnSide.HasValue || currentPlayerId == OwnSide.Value) GameAudio.PlayInterface(GameAudio.Bank.turn, 0.7f);
    }

    // Host and local only; a guest counts through HandleGuestTurnEnded.
    private void HandleTurnEnded(PlayersManager player)
    {
        shots[player.ID]++;
        lastShooterId = player.ID;
        Render();
    }

    // A guest's KillLog records the host's events, so this fires there too.
    private void HandleKills(IReadOnlyList<KillEvent> events)
    {
        int? localPlayer = networkBridge != null ? networkBridge.LocalPlayerId : (int?)null;
        killFeed.Add(events, GameManager.manager.Board, MatchSettings.Current.PieceType, localPlayer);
        if (events.Any(e => e.Kind == KillKind.Kill || e.Kind == KillKind.Nongae)) GameAudio.PlayInterface(GameAudio.Bank.kill, 0.8f);
    }

    private void HandleTurnPassed(PlayersManager player, TurnEnd why)
    {
        ShowPassNotice(player.ID, why);
    }

    private void ShowPassNotice(int playerId, TurnEnd why)
    {
        ShowNotice(Loc.Get(why == TurnEnd.Timeout ? "hud.timeout" : "hud.skipped", ColorName(playerId)));
    }

    private void ShowNotice(string text)
    {
        noticeText.text = text;
        notice.SetActive(true);
        noticeUntil = Time.time + noticeSeconds;
    }

    // Three or four sides: knocked out, conceded or gone while the match
    // goes on. Its panel stays, dimmed, with why.
    private void HandlePlayerOut(int playerId, MatchEndReason reason)
    {
        outSides[playerId] = reason;
        ShowNotice(Loc.Get("hud.outNotice." + reason, ColorName(playerId)));
        Render();
    }

    private void HandleMatchEnded(PlayersManager winner, MatchEndReason reason)
    {
        ShowResult(winner != null ? winner.ID : -1, reason);
    }

    private void HandleGuestTurnChanged(int playerId)
    {
        currentPlayerId = playerId;
        PlayTurnChime();
        Render();
    }

    private void HandleGuestTurnEnded(int shooterId)
    {
        shots[shooterId]++;
        lastShooterId = shooterId;
    }

    // The host rules on anyone else leaving (their side forfeits, and its
    // result or PlayerOut follows). A guest can't go on without the host:
    // with two that's a win by forfeit, with more the match is just over.
    private void HandlePlayerLeft(int playerId)
    {
        if (!winnerPlayerId.HasValue && !networkBridge.IsHost && playerId == 0)
        {
            if (PlayerCount > 2) ShowResult(-1, MatchEndReason.HostLeft);
            else ShowResult(networkBridge.LocalPlayerId, MatchEndReason.OpponentLeft);
        }
        else Render();
    }

    private void HandlePlayerReturnedToLobby(int playerId)
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
        if (reason != MatchEndReason.HostLeft) MatchSeries.Record(winnerId);
        var bank = GameAudio.Bank;
        var lost = OwnSide.HasValue && winnerId >= 0 && winnerId != OwnSide.Value;
        GameAudio.PlayInterface(winnerId < 0 ? bank.draw : lost ? bank.lose : bank.win);
        gameOverPanel.SetActive(true);
        Render();
    }

    private void Render()
    {
        var gameManager = GameManager.manager;
        if (turnController == null || gameManager == null) return;

        var playing = turnsStarted && !winnerPlayerId.HasValue;
        turnPill.SetActive(playing);
        turnStone.playerId = currentPlayerId;
        turnStone.Show(MatchSettings.Current.PieceType);
        if (playing) RenderTurn();
        else
            foreach (var panel in playerPanels) panel.skipButton.gameObject.SetActive(false);

        for (var i = 0; i < playerPanels.Length && i < gameManager.playersList.Count; i++)
        {
            var isTurn = playing && i == currentPlayerId;
            var outStatus = outSides.TryGetValue(i, out var why) ? Loc.Get("hud.out." + why) : null;
            playerPanels[i].Render(ColorName(i), PlayerLabel(i), CountPieces(i), gameManager.playersList[i].score, isTurn, outStatus);
        }

        if (winnerPlayerId.HasValue) RenderGameOver(gameManager);
    }

    private void RenderGameOver(GameManager gameManager)
    {
        var winner = winnerPlayerId.Value;
        var loser = 1 - winner; // two sides
        var online = networkBridge != null;
        var multi = PlayerCount > 2;

        if (endReason == MatchEndReason.HostLeft)
        {
            stampText.text = Loc.Get("result.stampOver");
            resultTitleText.text = Loc.Get("result.over");
        }
        else if (winner < 0)
        {
            stampText.text = Loc.Get("result.stampDraw");
            resultTitleText.text = Loc.Get("result.draw");
        }
        else if (OwnSide.HasValue)
        {
            // Online and against the AI the result reads from this player's side.
            var won = winner == OwnSide.Value;
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
            MatchEndReason.HostLeft => Loc.Get("reason.hostLeft"),
            MatchEndReason.BothOut when winner < 0 => Loc.Get("reason.bothOutDraw"),
            MatchEndReason.BothOut when MatchSettings.Current.BothOutRule == BothOutRule.ShooterWins => Loc.Get("reason.bothOutWin", ColorName(winner)),
            MatchEndReason.BothOut => Loc.Get("reason.bothOut", ColorName(lastShooterId >= 0 ? lastShooterId : loser)),
            MatchEndReason.Knockout when multi => Loc.Get("reason.lastStanding", ColorName(winner)),
            _ when multi => Loc.Get("reason.othersGone"),
            MatchEndReason.OpponentLeft => Loc.Get("reason.opponentLeft"),
            MatchEndReason.Surrender => Loc.Get("reason.surrender", ColorName(loser)),
            _ => Loc.Get("reason.knockout", ColorName(loser)),
        };

        var kills = turnController.Kills;
        for (var i = 0; i < PlayerCount; i++)
        {
            remainingCells[i].text = CountPieces(i).ToString();
            killCells[i].text = kills.Kills(i).ToString();
            nongaeCells[i].text = kills.Nongae(i).ToString();
            suicideCells[i].text = kills.Suicides(i).ToString();
            teamKillCells[i].text = kills.TeamKills(i).ToString();
            shotsCells[i].text = shots[i].ToString();
        }
        // Time.time runs at the game's pace (and stands still while paused).
        var seconds = Mathf.FloorToInt((matchEndTime - matchStartTime) / GamePace.Speed);
        matchTimeText.text = Loc.Get("stats.time", $"{seconds / 60}:{seconds % 60:00}");
        seriesText.text = (multi
                              ? Loc.Get("series.multi", string.Join(" · ", Enumerable.Range(0, PlayerCount).Select(i => Loc.Get("series.side", ColorName(i), MatchSeries.Wins(i)))))
                              : Loc.Get("series.score", ColorName(0), MatchSeries.Wins(0), MatchSeries.Wins(1), ColorName(1)))
                          + (MatchSeries.Draws > 0 ? Loc.Get("series.draws", MatchSeries.Draws) : string.Empty);

        if (!online)
        {
            rematchLabel.text = Loc.Get("win.rematch");
            statusText.text = string.Empty;
            SetInteractable(rematchButton, true);
            return;
        }

        // Everyone still here has to ask for the rematch; the host starts it.
        var wanting = networkBridge.OthersWantingRematch + (networkBridge.LocalWantsRematch ? 1 : 0);
        var status = string.Empty;
        if (networkBridge.HostGone) status = string.Empty; // the reason line says it
        else if (networkBridge.OpponentGone && endReason != MatchEndReason.OpponentLeft)
            status = multi ? Loc.Get("rematch.othersGone") : Loc.Get(opponentReturnedToLobby ? "rematch.opponentLobby" : "rematch.opponentLeft");
        else if (multi && wanting > 0)
            status = Loc.Get("rematch.count", wanting, networkBridge.OthersPresent + 1);
        else if (!multi && networkBridge.OthersWantingRematch > 0 && !networkBridge.LocalWantsRematch)
            status = Loc.Get("rematch.opponentWants");
        statusText.text = status;

        rematchLabel.text = Loc.Get(networkBridge.LocalWantsRematch ? "rematch.waiting"
            : networkBridge.OthersWantingRematch > 0 ? "rematch.accept" : "rematch.request");
        SetInteractable(rematchButton, !networkBridge.OpponentGone && !networkBridge.HostGone && !networkBridge.LocalWantsRematch);
    }

    private static string ColorName(int playerId) => SideStyle.Name(playerId);

    // Online, the player's Steam name; locally the AI's level or "Player N".
    private string PlayerLabel(int playerId)
    {
        var number = networkBridge != null ? networkBridge.PlayerName(playerId)
            : playerId == GameManager.manager.AIPlayerId ? LocalOpponent.Name : Loc.Get("player.number", playerId + 1);
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

    public void ReturnToMainMenu()
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
