using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The rankings card on the main menu (built by MenuBuilder): the top of the
// Steam leaderboard, this player's neighbours on it, or their Steam friends,
// a page of rows at a time, over a line with this player's own standing.
public class LeaderboardPanel : MonoBehaviour
{
    public SegmentedToggle scopeToggle; // RankBoard.Scope order
    public LeaderboardRow[] rows;
    public TMP_Text summaryText;
    public TMP_Text statusText; // loading, empty, no Steam
    public Button closeButton;

    private RankBoard.Scope scope;
    private int request; // the latest fetch: one that lands after a newer one started is dropped
    private int? ownPlace;

    private void Awake()
    {
        closeButton.onClick.AddListener(() => gameObject.SetActive(false));
        scopeToggle.OnSelected += index => Show((RankBoard.Scope)index);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        ownPlace = null;
        Show(scope);
    }

    public static string RecordText(PlayerRating.Record record) => record.Draws > 0
        ? Loc.Get("rank.recordDraws", record.Wins, record.Losses, record.Draws)
        : Loc.Get("rank.record", record.Wins, record.Losses);

    private async void Show(RankBoard.Scope next)
    {
        scope = next;
        scopeToggle.Show((int)scope);
        var id = ++request;
        foreach (var row in rows) row.gameObject.SetActive(false);
        RenderSummary();
        if (!RankBoard.Available)
        {
            SetStatus("rank.offline");
            return;
        }
        SetStatus("rank.loading");

        if (!ownPlace.HasValue)
        {
            var (_, own) = await RankBoard.FetchOwn(PlayerRating.Owner);
            if (this == null || id != request) return;
            if (own.HasValue) ownPlace = own.Value.Place;
            RenderSummary();
        }
        var entries = await RankBoard.Fetch(scope, rows.Length);
        if (this == null || id != request) return; // closed, or another tab picked meanwhile
        if (entries == null)
        {
            SetStatus("rank.failed");
            return;
        }
        for (var i = 0; i < rows.Length && i < entries.Length; i++) rows[i].Show(entries[i], entries[i].SteamId == PlayerRating.Owner);
        SetStatus(entries.Length == 0 ? "rank.empty" : null);
    }

    private void RenderSummary()
    {
        var record = PlayerRating.Current;
        var text = Loc.Get("rank.summary", record.Rating, record.Games > 0 ? RecordText(record) : Loc.Get("rank.noGames"));
        if (ownPlace.HasValue && record.Games > 0) text += Loc.Get("rank.summaryPlace", ownPlace.Value);
        summaryText.text = text;
    }

    private void SetStatus(string key)
    {
        statusText.gameObject.SetActive(key != null);
        if (key != null) statusText.text = Loc.Get(key);
    }
}
