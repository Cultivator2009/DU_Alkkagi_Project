using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The game-over screen (Tools > Alkkagi UI > 2. Build HUD): the result and
// why, a scoreboard column per side, the rating row for a rated match, the
// running series, and what's next - a rematch (everyone still here asks
// for it online), the lobby, the main menu. MainGameUIController opens it
// with the result; it renders from the match as the HUD sees it.
public class ResultScreen : MonoBehaviour
{
    public TMP_Text stampText;
    public TMP_Text resultTitleText;
    public TMP_Text resultReasonText;
    public RectTransform[] scoreColumns; // one per side, header and cells
    public LocalizedText remainingLabel; // "Pieces left", or in a battle of health "Health left"
    public TMP_Text[] remainingCells; // scoreboard cells, by player id
    public TMP_Text[] killCells;
    public TMP_Text[] nongaeCells;
    public TMP_Text[] suicideCells;
    public TMP_Text[] teamKillCells;
    public TMP_Text[] shotsCells;
    public GameObject ratingLabel; // the scoreboard's rating row, shown for a rated match
    public TMP_Text[] ratingCells;
    public float scoreRowPitch = 46; // without the rating row, the scoreboard and the modal close up by this
    public Color ratingUpColor = Color.green;
    public Color ratingDownColor = Color.red;
    public Color ratingSameColor = Color.gray;
    public TMP_Text matchTimeText;
    public TMP_Text seriesText;
    public TMP_Text statusText;
    public Button rematchButton;
    public TMP_Text rematchLabel;
    public Button lobbyButton;
    public Button mainMenuButton;
    public Button reportButton;   // online: block or report a player of the match
    public ReportPanel reportPanel;

    private MainGameUIController hud;
    private int winner;
    private MatchEndReason reason;
    private int seconds;
    private bool ratingRowShown = true; // as built
    private float nextRender; // a ranked series: its countdown to the next game, and its end as it comes in
    private int countedDown;  // the last of the countdown's final seconds heard
    private const int CountdownSeconds = 3;

    public bool IsShown => gameObject.activeSelf;

    private void Update()
    {
        var series = hud != null && hud.Bridge != null ? hud.Bridge.Series : null;
        if (series == null) return;
        CountDown(hud.Bridge.NextGameAt);
        if (Time.unscaledTime < nextRender) return;
        nextRender = Time.unscaledTime + 0.25f;
        Render();
    }

    // The next game's last seconds, one knock each.
    private void CountDown(float nextGameAt)
    {
        var left = nextGameAt < 0 ? 0 : Mathf.CeilToInt(nextGameAt - Time.realtimeSinceStartup);
        if (left < 1 || left > CountdownSeconds)
        {
            countedDown = 0;
            return;
        }
        if (left == countedDown) return;
        countedDown = left;
        GameAudio.PlayInterface(GameAudio.Bank.countdown, 0.8f, left == 1 ? 1.12f : 1f);
    }

    private void Awake()
    {
        rematchButton.onClick.AddListener(OnClickRematch);
        lobbyButton.onClick.AddListener(() => hud.Bridge.ReturnToLobby());
        mainMenuButton.onClick.AddListener(() => hud.ReturnToMainMenu());
        reportButton.onClick.AddListener(() => reportPanel.Open(hud.Bridge));
    }

    // Three or four sides spread the scoreboard's columns.
    public void Arrange(int players)
    {
        for (var i = 0; i < scoreColumns.Length; i++) scoreColumns[i].gameObject.SetActive(i < players);
        if (players <= 2) return;
        var columns = players == 3 ? new[] { 330f, 450f, 570f } : new[] { 290f, 390f, 490f, 590f };
        for (var i = 0; i < players; i++) scoreColumns[i].anchoredPosition = new Vector2(columns[i], 0);
    }

    // matchSeconds: from the first turn to the result, in real seconds.
    public void Show(MainGameUIController owner, int winnerId, MatchEndReason why, float matchSeconds)
    {
        hud = owner;
        winner = winnerId;
        reason = why;
        seconds = Mathf.FloorToInt(matchSeconds);
        lobbyButton.gameObject.SetActive(hud.IsOnline);
        reportButton.gameObject.SetActive(hud.IsOnline);
        reportPanel.gameObject.SetActive(false);
        gameObject.SetActive(true);
        Render();
    }

    public void Hide() => gameObject.SetActive(false);

    public void Render()
    {
        if (hud == null || !IsShown) return;
        var turns = hud.Turns;
        var bridge = hud.Bridge;
        var gameManager = GameManager.manager;
        var teams = gameManager.Teams;
        var sides = gameManager.Sides;
        var players = sides.Count;
        var loser = 1 - winner; // two sides
        var multi = players > 2;
        var health = sides.Count > 0 && sides[0].HasHealth;

        if (reason == MatchEndReason.HostLeft)
        {
            stampText.text = Loc.Get("result.stampOver");
            resultTitleText.text = Loc.Get("result.over");
        }
        else if (winner < 0)
        {
            stampText.text = Loc.Get("result.stampDraw");
            resultTitleText.text = Loc.Get("result.draw");
        }
        else if (hud.OwnSide.HasValue)
        {
            // Online and against the AI the result reads from this player's
            // side (and its team's).
            var won = gameManager.TeamOf(winner) == gameManager.TeamOf(hud.OwnSide.Value);
            stampText.text = Loc.Get(won ? "win.stamp" : "result.stampLose");
            resultTitleText.text = Loc.Get(won ? "result.win" : "result.lose");
        }
        else
        {
            stampText.text = Loc.Get("win.stamp");
            resultTitleText.text = teams ? Loc.Get("win.team", gameManager.TeamOf(winner) + 1) : Loc.Get("win.title", SideStyle.Name(winner));
        }

        // A ranked series that's over reads as the series.
        var series = bridge != null ? bridge.Series : null;
        if (series != null && series.Over && hud.OwnSide.HasValue)
        {
            var won = series.Winner == hud.OwnSide.Value;
            stampText.text = Loc.Get(series.Winner < 0 ? "result.stampDraw" : won ? "win.stamp" : "result.stampLose");
            resultTitleText.text = Loc.Get(series.Winner < 0 ? "ranked.draw" : won ? "ranked.win" : "ranked.lose");
        }

        resultReasonText.text = series != null && series.Over && series.End == SeriesEnd.Left ? Loc.Get("ranked.left") : reason switch
        {
            MatchEndReason.HostLeft => Loc.Get("reason.hostLeft"),
            MatchEndReason.RoundLimit when winner < 0 => Loc.Get("reason.roundLimitDraw"),
            MatchEndReason.RoundLimit => Loc.Get("reason.roundLimit", SideStyle.Name(winner)),
            MatchEndReason.BothOut when winner < 0 => Loc.Get("reason.bothOutDraw"),
            MatchEndReason.BothOut when MatchSettings.Current.BothOutRule == BothOutRule.ShooterWins => Loc.Get("reason.bothOutWin", SideStyle.Name(winner)),
            MatchEndReason.BothOut => Loc.Get("reason.bothOut", SideStyle.Name(turns.LastPlayerId >= 0 ? turns.LastPlayerId : loser)),
            MatchEndReason.Knockout when teams => Loc.Get("reason.teamStanding", gameManager.TeamOf(winner) + 1),
            MatchEndReason.Knockout when multi => Loc.Get("reason.lastStanding", SideStyle.Name(winner)),
            _ when multi => Loc.Get("reason.othersGone"),
            MatchEndReason.OpponentLeft => Loc.Get("reason.opponentLeft"),
            MatchEndReason.Surrender => Loc.Get("reason.surrender", SideStyle.Name(loser)),
            _ when health => Loc.Get("reason.healthOut", SideStyle.Name(loser)),
            _ => Loc.Get("reason.knockout", SideStyle.Name(loser)),
        };

        remainingLabel.Show(health ? "stats.health" : "hud.remaining");
        var kills = turns.Kills;
        var rating = bridge != null ? bridge.Rating : null;
        // A series rates itself, once it's over.
        var changes = series != null ? series.Changes : rating?.Changes;
        for (var i = 0; i < players; i++)
        {
            remainingCells[i].text = health ? sides[i].Health.ToString() : sides[i].Pieces.ToString();
            killCells[i].text = kills.Kills(i).ToString();
            nongaeCells[i].text = kills.Nongae(i).ToString();
            suicideCells[i].text = kills.Suicides(i).ToString();
            teamKillCells[i].text = kills.TeamKills(i).ToString();
            shotsCells[i].text = sides[i].Shots.ToString();
            ratingCells[i].gameObject.SetActive(changes != null);
            if (changes != null) ratingCells[i].text = RatingCell(series != null ? series.Ratings[i] : rating.Match.Rating(i), changes[i]);
        }
        ShowRatingRow(changes != null);
        matchTimeText.text = Loc.Get("stats.time", $"{seconds / 60}:{seconds % 60:00}");
        seriesText.text = series != null ? Loc.Get("ranked.series", series.Game, SideStyle.Name(0), series.Wins[0], series.Wins[1], SideStyle.Name(1)) : (multi
                              ? Loc.Get("series.multi", string.Join(" · ", Enumerable.Range(0, players).Select(i => Loc.Get("series.side", SideStyle.Name(i), MatchSeries.Wins(i)))))
                              : Loc.Get("series.score", SideStyle.Name(0), MatchSeries.Wins(0), MatchSeries.Wins(1), SideStyle.Name(1)))
                          + (MatchSeries.Draws > 0 ? Loc.Get("series.draws", MatchSeries.Draws) : string.Empty);

        if (bridge == null)
        {
            rematchLabel.text = Loc.Get("win.rematch");
            statusText.text = string.Empty;
            SetInteractable(rematchButton, true);
            return;
        }

        // A ranked series: the next game comes by itself (sooner if both
        // ask); none once it's over.
        if (series != null)
        {
            var left = Mathf.Max(0, Mathf.CeilToInt(bridge.NextGameAt - Time.realtimeSinceStartup));
            statusText.text = series.Over ? string.Empty : Loc.Get("ranked.next", left);
            rematchLabel.text = Loc.Get(series.Over ? "ranked.over" : bridge.LocalWantsRematch ? "rematch.waiting" : "ranked.nextButton");
            SetInteractable(rematchButton, !series.Over && !bridge.OpponentGone && !bridge.LocalWantsRematch);
            return;
        }

        // Everyone still here has to ask for the rematch; the host starts it.
        var wanting = bridge.OthersWantingRematch + (bridge.LocalWantsRematch ? 1 : 0);
        var status = string.Empty;
        if (bridge.HostGone) status = string.Empty; // the reason line says it
        else if (bridge.OpponentGone && reason != MatchEndReason.OpponentLeft)
            status = multi ? Loc.Get("rematch.othersGone") : Loc.Get(hud.OpponentReturnedToLobby ? "rematch.opponentLobby" : "rematch.opponentLeft");
        else if (!bridge.RematchFits)
            status = MatchSettings.Current.Teams && bridge.OthersPresent + 1 != MatchRoster.MaxPlayers ? Loc.Get("rematch.teams") : Loc.Get("rematch.boardPlayers", bridge.OthersPresent + 1);
        else if (multi && wanting > 0)
            status = Loc.Get("rematch.count", wanting, bridge.OthersPresent + 1);
        else if (!multi && bridge.OthersWantingRematch > 0 && !bridge.LocalWantsRematch)
            status = Loc.Get("rematch.opponentWants");
        statusText.text = status;

        rematchLabel.text = Loc.Get(bridge.LocalWantsRematch ? "rematch.waiting"
            : bridge.OthersWantingRematch > 0 ? "rematch.accept" : "rematch.request");
        SetInteractable(rematchButton, !bridge.OpponentGone && !bridge.HostGone && !bridge.LocalWantsRematch && bridge.RematchFits);
    }

    // The rating row is the scoreboard's last: without it the scoreboard,
    // the modal and the lines under the scoreboard close up by a row (the
    // buttons sit on the modal's bottom edge, so they follow).
    private void ShowRatingRow(bool shown)
    {
        ratingLabel.SetActive(shown);
        if (shown == ratingRowShown) return;
        ratingRowShown = shown;
        var step = new Vector2(0, shown ? scoreRowPitch : -scoreRowPitch);
        var scoreboard = (RectTransform)ratingLabel.transform.parent;
        scoreboard.sizeDelta += step;
        ((RectTransform)scoreboard.parent).sizeDelta += step;
        foreach (var line in new[] { matchTimeText, seriesText, statusText }) line.rectTransform.anchoredPosition -= step;
    }

    // The new rating, and smaller beside it the change.
    private string RatingCell(int rating, int change)
    {
        var color = change > 0 ? ratingUpColor : change < 0 ? ratingDownColor : ratingSameColor;
        return $"{rating + change}<size=62%><color=#{ColorUtility.ToHtmlStringRGB(color)}> {(change < 0 ? "-" : "+")}{Mathf.Abs(change)}</color></size>";
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
        if (hud.Bridge != null)
        {
            hud.Bridge.RequestRematch();
            return;
        }
        MatchSettings.Current = MatchSettings.Picked.Resolve(); // Random rolls again
        GameManager.manager.EndMatch();
        SceneManager.LoadScene("GameScene");
    }
}
