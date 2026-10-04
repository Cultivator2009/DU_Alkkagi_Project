using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the MainGame_UI prefab: the turn pill and notices, one
// PlayerHudPanel per player (two to four), the kill feed and the buttons
// beside the board, and it opens the game-over screen (ResultScreen) with
// the result. Layout and styling live in the prefab (built by Tools >
// Alkkagi UI > 2. Build HUD); this only pushes match state into it, all of
// it from the TurnController's events - the same on the host, a guest and
// a local game.
public class MainGameUIController : MonoBehaviour
{
    public GameObject turnPill;
    public UIPulse turnPulse;   // a punch as the turn changes
    public TMP_Text turnText;
    public SideMark turnStone;
    public PlayerHudPanel[] playerPanels; // index-aligned with GameManager.Sides (0 = black), four
    public float hudMargin = 28;
    [Range(0.3f, 1f)] public float compactScale = 0.58f; // the side panels when three or four play
    public float compactGap = 16;
    public Color turnTextColor = Color.black;
    public Color clockWarningColor = Color.red;
    public float clockWarningSeconds = 5f;
    public GameObject notice;   // "Black ran out of time" and the like, briefly, in the turn pill's place
    public TMP_Text noticeText;
    public UIPulse noticePulse;
    public float noticeSeconds = 2.5f;
    public GameObject statusChip; // under the turn pill: the crumbling edge's stage, the round of a round limit
    public TMP_Text statusChipText;
    public KillFeed killFeed;
    public ControlsHint controlsHint;
    [Range(0.3f, 1f)] public float minHintScale = 0.75f; // any smaller and the key legend is too small to read, so it hides

    public ResultScreen result;
    public Button resetViewButton; // shown once the view is panned away
    // In the key legend's place: to the screen running the physics once a
    // shot may be fast-forwarded (GamePace), to a guest while the host has.
    public Button fastForwardButton;
    public LocalizedText fastForwardLabel;

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
    private readonly Dictionary<int, MatchEndReason> outSides = new Dictionary<int, MatchEndReason>(); // out while the match went on
    private float matchSeconds; // real seconds of turns so far, pauses left out
    private int? winnerPlayerId; // set once the result is in; -1 = draw
    private MatchEndReason resultReason;
    private float resultAt = -1; // real time the result card comes up (ShowResult)
    private const float ResultBeat = 0.6f; // real seconds after a match won on the board
    private bool turnsStarted;
    private bool anyTurnAnnounced;
    private float noticeUntil;
    private bool hintFits = true; // the key legend is big enough to read (Arrange)
    private int lastTickSecond = -1; // the countdown second last ticked
    private float arrangedWidth = -1; // the canvas width Arrange last laid out for
    private RatingTracker rating; // the bridge's, once it has one

    private void Awake()
    {
        resetViewButton.onClick.AddListener(() =>
        {
            if (CameraRig.Instance != null) CameraRig.Instance.ResetView();
        });
        fastForwardButton.onClick.AddListener(() => GamePace.SetFastForward(!GamePace.FastForwarding));
        for (var i = 0; i < playerPanels.Length; i++)
        {
            var playerId = i;
            playerPanels[i].skipButton.onClick.AddListener(() => OnClickSkip(playerId));
        }
    }

    private void OnEnable()
    {
        result.Hide();
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
        if (rating != null) rating.OnFinished -= Render;
        rating = null;
        turnController = null;
        networkBridge = null;
    }

    private IEnumerator WaitForMatchThenSubscribe()
    {
        GameManager gameManager;
        while ((gameManager = GameManager.manager) == null || gameManager.TurnController == null) yield return null;

        turnController = gameManager.TurnController;
        Subscribe(turnController);

        // Only present on a networked match (see NetworkBootstrap). A guest's
        // TurnController mirrors the host's, so its events are the match's
        // there too; the bridge only adds who left and who wants a rematch.
        networkBridge = FindAnyObjectByType<NetworkMatchBridge>();
        if (networkBridge != null) SubscribeNetwork(networkBridge);

        var online = networkBridge != null;
        // The series runs for as long as the same players keep rematching:
        // the lobby and its roster online, the session since the main menu
        // locally.
        var lobbyId = SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.CurrentLobby.HasValue ? SteamLobbyManager.Instance.CurrentLobby.Value.Id.Value : 0;
        MatchSeries.Begin(online ? $"lobby:{lobbyId}:{string.Join(",", MatchRoster.Current?.SteamIds ?? new ulong[0])}"
            : $"local:{string.Join(",", Enumerable.Range(0, PlayerCount).Select(i => LocalOpponent.Of(i)))}");

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
        // The bridge rates the match once it's set up: its result shows
        // on the scoreboard as it comes in.
        if (rating == null && networkBridge != null && networkBridge.Rating != null)
        {
            rating = networkBridge.Rating;
            rating.OnFinished += Render;
        }
        ShowFastForward();
        var gameManager = GameManager.manager;
        if (!Mathf.Approximately(CanvasRect.rect.width, arrangedWidth)) Arrange(PlayerCount); // the window changed shape

        // Turns begin after the guest has loaded and any placement is done;
        // the match clock starts there too.
        if (!turnsStarted && IsTurnState(gameManager.gameState))
        {
            turnsStarted = true;
            // The start signal, and who opens. The opening turn itself may
            // have begun before this screen subscribed (a local game without
            // placement), so it's announced here unless it already was.
            GameAudio.PlayInterface(GameAudio.Bank.start);
            ShowNotice(Loc.Get("hud.start", ColorName(currentPlayerId)));
            if (!anyTurnAnnounced) AnnounceTurn(chime: false);
            Render();
        }
        if (resultAt >= 0 && Time.realtimeSinceStartup >= resultAt) ShowResultCard();
        if (noticeUntil > 0 && Time.time >= noticeUntil)
        {
            noticeUntil = 0;
            notice.SetActive(false);
            Render(); // the pill comes back
        }
        if (turnsStarted && !winnerPlayerId.HasValue)
        {
            matchSeconds += GamePace.ClockDelta;
            RenderTurn();
        }
    }

    public bool IsOnline => networkBridge != null;
    public TurnController Turns => turnController;
    public NetworkMatchBridge Bridge => networkBridge;
    public bool OpponentReturnedToLobby { get; private set; }
    private static int PlayerCount => GameManager.manager.Sides.Count;
    private static bool VersusAI => GameManager.manager.VersusAI;
    // The side this screen plays, when it plays just one: online, or alone against the AI.
    public int? OwnSide => networkBridge != null ? networkBridge.LocalPlayerId : GameManager.manager.SoleHuman >= 0 ? GameManager.manager.SoleHuman : (int?)null;
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

    // Two sides keep their built corners. Three or four: the panels shrink
    // and each goes next to its side's edge - south and west up the left
    // column from the bottom, north and east down the right one from the top
    // - and the skip button (only ever this screen's own) sits above the Menu
    // button. The scoreboard spreads its columns.
    //
    // The board fills the screen height, so on narrower screens (16:10 and
    // below) the room beside it shrinks, and whatever lives there - panels,
    // skip buttons, kill feed, placement card, Menu and Reset view - shrinks
    // to fit it. Update reruns this when the window changes shape.
    private void Arrange(int players)
    {
        arrangedWidth = CanvasRect.rect.width;
        var room = SideRoom() - hudMargin;
        float Fit(RectTransform rect, float most = 1) => Mathf.Min(most, room / rect.rect.width);
        void Scale(RectTransform rect, float scale) => rect.localScale = new Vector3(scale, scale, 1);

        for (var i = 0; i < playerPanels.Length; i++) playerPanels[i].gameObject.SetActive(i < players);
        result.Arrange(players);

        var feed = (RectTransform)killFeed.transform;
        Scale(feed, Fit(feed));
        var placement = GetComponentInChildren<PlacementHud>(true);
        if (placement != null) Scale((RectTransform)placement.panel.transform, Fit((RectTransform)placement.panel.transform));
        var lesson = GetComponentInChildren<TutorialCard>(true);
        if (lesson != null) Scale((RectTransform)lesson.panel.transform, Fit((RectTransform)lesson.panel.transform));
        // Menu in the corner, Reset view to its left.
        var menu = (RectTransform)GetComponent<PauseMenu>().openButton.transform;
        var reset = (RectTransform)resetViewButton.transform;
        var buttons = Mathf.Min(1, room / (menu.rect.width + ButtonGap + reset.rect.width));
        Scale(menu, buttons);
        Scale(reset, buttons);
        reset.anchoredPosition = menu.anchoredPosition - new Vector2((menu.rect.width + ButtonGap) * buttons, 0);
        // The key legend above them - with three or four sides above the
        // skip button too, which moves there (below). It keeps a margin from
        // the board as well as from the screen's edge.
        var hint = (RectTransform)controlsHint.transform;
        var hintScale = Mathf.Min(1, (room - hudMargin) / hint.rect.width);
        Scale(hint, hintScale);
        var hintY = hudMargin + (menu.rect.height + ButtonGap) * buttons;
        if (players > 2)
        {
            var skip = (RectTransform)playerPanels[0].skipButton.transform;
            hintY += (skip.rect.height + compactGap) * Fit(skip);
        }
        hint.anchoredPosition = new Vector2(-hudMargin, hintY);
        hintFits = hintScale >= minHintScale;
        controlsHint.gameObject.SetActive(hintFits && !fastForwardButton.gameObject.activeSelf);
        var fast = (RectTransform)fastForwardButton.transform;
        Scale(fast, buttons);
        fast.anchoredPosition = new Vector2(-hudMargin, hintY);

        var panelSize = ((RectTransform)playerPanels[0].transform).rect.size;
        if (players <= 2)
        {
            var scale = Mathf.Min(1, room / panelSize.x);
            for (var i = 0; i < players; i++)
            {
                var rect = (RectTransform)playerPanels[i].transform;
                Scale(rect, scale);
                // Just outside the panel, toward the middle of the column.
                var skip = (RectTransform)playerPanels[i].skipButton.transform;
                Scale(skip, Fit(skip));
                var toward = rect.anchorMin.y < 0.5f ? 1 : -1;
                skip.anchoredPosition = rect.anchoredPosition + new Vector2(0, toward * (panelSize.y * scale + ButtonGap));
            }
            return;
        }

        var compact = Mathf.Min(compactScale, room / panelSize.x);
        var pitch = panelSize.y * compact + compactGap;
        // Left column: the sides whose edge is on the left (and the south),
        // up from the bottom; right column: the rest, down from the top.
        var board = GameManager.manager.Board;
        var directions = Enumerable.Range(0, players).Select(i => board.SeatDirection(i)).ToArray();
        bool Left(int i) => directions[i].x < -0.1f || (Mathf.Abs(directions[i].x) <= 0.1f && directions[i].y < 0);
        for (var i = 0; i < players; i++)
        {
            var left = Left(i);
            var slot = Enumerable.Range(0, players).Count(j => Left(j) == left && (left ? directions[j].y < directions[i].y : directions[j].y > directions[i].y));
            var rect = (RectTransform)playerPanels[i].transform;
            Scale(rect, compact);
            rect.anchorMin = rect.anchorMax = rect.pivot = left ? Vector2.zero : Vector2.one;
            rect.anchoredPosition = left ? new Vector2(hudMargin, hudMargin + slot * pitch) : new Vector2(-hudMargin, -hudMargin - slot * pitch);

            var skip = (RectTransform)playerPanels[i].skipButton.transform;
            Scale(skip, Fit(skip));
            skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(1, 0);
            skip.anchoredPosition = new Vector2(-hudMargin, hudMargin + (menu.rect.height + compactGap) * buttons);
        }
    }

    private const float ButtonGap = 16; // between the Menu and Reset view buttons, and a panel and its skip button
    private RectTransform CanvasRect => (RectTransform)transform;

    // Canvas units between the screen's side and the board's, in the home view.
    private float SideRoom()
    {
        var canvas = CanvasRect.rect;
        var board = GameManager.manager.Board;
        if (CameraRig.Instance == null || board == null) return canvas.width;
        return canvas.width / 2 - CameraRig.Instance.HomeHalfWidth(board.SurfaceBounds) * canvas.height;
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

    // The crumbling edge's stage once it's come (or announced), and the
    // round when there's a round limit.
    private void RenderStatusChip(bool playing)
    {
        var parts = new List<string>();
        var zone = turnController.Zone;
        if (zone.Warned) parts.Add(Loc.Get("hud.zoneChipWarn"));
        else if (zone.Stage > 0) parts.Add(Loc.Get("hud.zoneChip", zone.Stage));
        if (turnController.RoundLimit > 0) parts.Add(Loc.Get("hud.roundChip", turnController.Round, turnController.RoundLimit));
        statusChip.SetActive(playing && parts.Count > 0);
        if (parts.Count > 0) statusChipText.text = string.Join(" · ", parts);
    }

    private float? TurnTimeRemaining()
    {
        if (turnController.TurnSeconds <= 0 || !turnController.IsAwaitingShot) return null;
        return turnController.TurnTimeRemaining;
    }

    // The fast-forward button, in the key legend's place (the keys are no
    // use while the pieces roll): the screen that runs the physics can turn
    // it on and off once the shot has run a while; a guest only sees that
    // the host has.
    private void ShowFastForward()
    {
        var authority = networkBridge == null || networkBridge.IsHost;
        var show = authority ? turnController.MayFastForward && !winnerPlayerId.HasValue : networkBridge.HostFastForwarding;
        fastForwardButton.gameObject.SetActive(show);
        controlsHint.gameObject.SetActive(hintFits && !show);
        if (!show) return;
        fastForwardButton.interactable = authority;
        var key = !authority ? "hud.hostFastForward" : GamePace.FastForwarding ? "hud.normalSpeed" : "hud.fastForward";
        if (fastForwardLabel.key == key) return;
        fastForwardLabel.key = key;
        fastForwardLabel.GetComponent<TMP_Text>().text = Loc.Get(key);
    }

    // This screen may pick up playerId's pieces right now: that side is to
    // move, it's this screen's to play, and it's waiting for a flick. The
    // aim's hover ring asks.
    public bool MayPickUp(int playerId)
    {
        if (turnController == null || winnerPlayerId.HasValue || playerId != currentPlayerId) return false;
        if (networkBridge != null && !networkBridge.IsHost) return playerId == networkBridge.LocalPlayerId && networkBridge.GuestCanPass;
        if (turnController.State != GameManager.GameState.WaitingForInput) return false;
        return networkBridge != null ? playerId == networkBridge.LocalPlayerId : !GameManager.manager.IsAI(playerId);
    }

    private bool CanSkip(int playerId)
    {
        if (!turnController.MayPass) return false; // the edge is coming in
        if (networkBridge == null) return turnController.IsAwaitingShot && !GameManager.manager.IsAI(playerId); // hot seat: whoever's turn it is
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
        controller.OnSidesChanged += Render;
        controller.OnTurnPassed += ShowPassNotice;
        controller.OnMatchEnded += ShowResult;
        controller.OnPlayerOut += HandlePlayerOut;
        controller.OnZoneChanged += HandleZoneChanged;
        controller.Kills.OnShotResolved += HandleKills;
    }

    private void Unsubscribe(TurnController controller)
    {
        controller.OnTurnStarted -= HandleTurnStarted;
        controller.OnSidesChanged -= Render;
        controller.OnTurnPassed -= ShowPassNotice;
        controller.OnMatchEnded -= ShowResult;
        controller.OnPlayerOut -= HandlePlayerOut;
        controller.OnZoneChanged -= HandleZoneChanged;
        controller.Kills.OnShotResolved -= HandleKills;
    }

    private void SubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnPlayerLeft += HandlePlayerLeft;
        bridge.OnPlayerReturnedToLobby += HandlePlayerReturnedToLobby;
        bridge.OnRematchStateChanged += Render;
    }

    private void UnsubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnPlayerLeft -= HandlePlayerLeft;
        bridge.OnPlayerReturnedToLobby -= HandlePlayerReturnedToLobby;
        bridge.OnRematchStateChanged -= Render;
    }

    private void HandleTurnStarted(int playerId)
    {
        currentPlayerId = playerId;
        turnPulse.Play();
        AnnounceTurn();
        Render();
    }

    // The chime, and a ring out of each piece that may now be flicked. Only
    // for this screen's own turns (online, against the AI); in a hot-seat
    // game every turn is somebody's here.
    private void AnnounceTurn(bool chime = true)
    {
        anyTurnAnnounced = true;
        if (OwnSide.HasValue && currentPlayerId != OwnSide.Value) return;
        if (chime) GameAudio.PlayInterface(GameAudio.Bank.turn, 0.7f);
        if (HitEffects.Instance == null) return;
        var index = 0;
        foreach (var piece in GameManager.manager.gamePieceScripts)
        {
            if (piece == null) continue;
            var manager = piece.Manager;
            if (manager.playerIndex == currentPlayerId) HitEffects.Instance.PlayTurn(piece.transform.position, manager.radius, index++);
        }
    }

    // A guest's KillLog records the host's events, so this fires there too.
    private void HandleKills(IReadOnlyList<KillEvent> events)
    {
        int? localPlayer = networkBridge != null ? networkBridge.LocalPlayerId : (int?)null;
        killFeed.Add(events, GameManager.manager.Board, MatchSettings.Current.PieceType, localPlayer);
        // Two or more of the others' off in one shot: called out, the kill rung higher.
        var kills = events.Count(e => e.Kind == KillKind.Kill || e.Kind == KillKind.Nongae);
        if (kills > 0) GameAudio.PlayInterface(GameAudio.Bank.kill, 0.8f, 1f + 0.08f * Mathf.Min(kills - 1, 3));
        if (kills >= 2) ShowNotice(Loc.Get(kills == 2 ? "hud.double" : kills == 3 ? "hud.triple" : "hud.multi", ColorName(events.First(e => e.Kind == KillKind.Kill || e.Kind == KillKind.Nongae).ShooterId), kills));
    }

    private void ShowPassNotice(int playerId, TurnEnd why)
    {
        if (Tutorial.QuietPasses) return; // white sitting the lessons out
        ShowNotice(Loc.Get(why == TurnEnd.Timeout ? "hud.timeout" : "hud.skipped", ColorName(playerId)));
    }

    private void ShowNotice(string text)
    {
        noticeText.text = text;
        notice.SetActive(true);
        noticePulse.Play();
        turnPill.SetActive(false);
        noticeUntil = Time.time + noticeSeconds;
    }

    // The edge announced for this turn, or giving way now.
    private void HandleZoneChanged()
    {
        if (turnController.Zone.Warned)
        {
            ShowNotice(Loc.Get("hud.zoneWarn"));
            GameAudio.PlayInterface(GameAudio.Bank.zoneWarn, 0.8f);
        }
        else if (turnController.Collapsing)
        {
            ShowNotice(Loc.Get("hud.zoneCrumbled"));
            GameAudio.PlayBoard(GameAudio.Bank.crumble, 0.9f);
        }
        Render();
    }

    // Three or four sides: knocked out, conceded or gone while the match
    // goes on. Its panel stays, dimmed, with why.
    private void HandlePlayerOut(int playerId, MatchEndReason reason)
    {
        outSides[playerId] = reason;
        ShowNotice(Loc.Get("hud.outNotice." + reason, ColorName(playerId)));
        Render();
    }

    // Whoever left, the result follows from the controller (the host
    // forfeits their side; a guest's mirror ends without its host); here it
    // only changes who can rematch.
    private void HandlePlayerLeft(int playerId) => Render();

    private void HandlePlayerReturnedToLobby(int playerId)
    {
        OpponentReturnedToLobby = true;
        Render();
    }

    private void ShowResult(int winnerId, MatchEndReason reason)
    {
        if (winnerPlayerId.HasValue) return;
        winnerPlayerId = winnerId;
        resultReason = reason;
        if (reason != MatchEndReason.HostLeft) MatchSeries.Record(winnerId);
        // Won on the board, the last fall plays out (GamePace's slow motion)
        // and a beat passes before the result comes up.
        var onBoard = reason == MatchEndReason.Knockout || reason == MatchEndReason.BothOut;
        resultAt = Time.realtimeSinceStartup + (onBoard ? GamePace.HeldFor + ResultBeat : 0);
        Render();
    }

    private void ShowResultCard()
    {
        resultAt = -1;
        var bank = GameAudio.Bank;
        var gameManager = GameManager.manager;
        var winnerId = winnerPlayerId.Value;
        var lost = OwnSide.HasValue && winnerId >= 0 && gameManager.TeamOf(winnerId) != gameManager.TeamOf(OwnSide.Value);
        GameAudio.PlayInterface(winnerId < 0 ? bank.draw : lost ? bank.lose : bank.win);
        MusicPlayer.Stop(); // the match's music gives way to the result's sound
        result.Show(this, winnerId, resultReason, matchSeconds);
        Render();
    }

    private void Render()
    {
        var gameManager = GameManager.manager;
        if (turnController == null || gameManager == null) return;

        var playing = turnsStarted && !winnerPlayerId.HasValue;
        turnPill.SetActive(playing && noticeUntil <= 0);
        RenderStatusChip(playing);
        turnStone.playerId = currentPlayerId;
        turnStone.Show(MatchSettings.Current.PieceType);
        if (playing) RenderTurn();
        else
            foreach (var panel in playerPanels) panel.skipButton.gameObject.SetActive(false);

        for (var i = 0; i < playerPanels.Length && i < gameManager.Sides.Count; i++)
        {
            var isTurn = playing && i == currentPlayerId;
            var outStatus = outSides.TryGetValue(i, out var why) ? Loc.Get("hud.out." + why) : null;
            var side = gameManager.Sides[i];
            playerPanels[i].Render(ColorName(i), PlayerLabel(i), CountPieces(i), side.Score, isTurn, outStatus, side.Health, side.MaxHealth);
        }

        result.Render();
    }

    private static string ColorName(int playerId) => SideStyle.Name(playerId);

    // Online, the player's Steam name; locally the AI's level or "Player N".
    private string PlayerLabel(int playerId)
    {
        var number = networkBridge != null ? networkBridge.PlayerName(playerId)
            : GameManager.manager.IsAI(playerId) ? LocalOpponent.Name(playerId) : Loc.Get("player.number", playerId + 1);
        if (GameManager.manager.Teams) number = Loc.Get("team.tag", number, GameManager.manager.TeamOf(playerId) + 1);
        return MatchSeries.Played > 0 ? Loc.Get("series.panel", number, MatchSeries.Wins(playerId)) : number;
    }

    // The host's count on a guest too (MatchState).
    private static int CountPieces(int playerId) => GameManager.manager.Sides[playerId].Pieces;

    private static bool IsOnlineMatch()
    {
        return SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.CurrentLobby.HasValue;
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
