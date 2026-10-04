using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The records card on the main menu (built by MenuBuilder), a page at a
// time: this player's own record on this machine (PlayerRecords) and the
// achievements. The rankings card (LeaderboardPanel) opens over it.
public class RecordsPanel : MonoBehaviour
{
    public SegmentedToggle tabs; // record, achievements
    public GameObject[] pages;
    // In order: matches, record, against the AI, online, knocked off, most
    // in one shot, streak, time played.
    public TMP_Text[] statValues;
    public AchievementTile[] tiles; // in AchievementId order
    public TMP_Text earnedText;
    public Button rankingButton;
    public Button closeButton;
    public LeaderboardPanel leaderboard;

    private int page;

    private void Awake()
    {
        tabs.OnSelected += index =>
        {
            page = index;
            Render();
        };
        rankingButton.onClick.AddListener(leaderboard.Open);
        closeButton.onClick.AddListener(() => gameObject.SetActive(false));
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Render;
        PlayerRecords.OnChanged += Render;
        Render();
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Render;
        PlayerRecords.OnChanged -= Render;
    }

    private void Render()
    {
        tabs.Show(page);
        for (var i = 0; i < pages.Length; i++) pages[i].SetActive(i == page);
        var r = PlayerRecords.Current;
        var minutes = Mathf.FloorToInt(r.seconds / 60);
        var values = new[]
        {
            Loc.Get("records.matches", r.played),
            Loc.Get("records.wld", r.won, r.lost, r.drawn),
            Loc.Get("records.wl", r.aiWon, r.aiLost),
            Loc.Get("records.wl", r.onlineWon, r.onlineLost),
            Loc.Get("records.count", r.knockedOff),
            Loc.Get("records.count", r.bestShot),
            Loc.Get("records.streakValue", r.bestStreak, r.streak),
            Loc.Get("records.hours", minutes / 60, minutes % 60),
        };
        for (var i = 0; i < statValues.Length; i++) statValues[i].text = values[i];
        for (var i = 0; i < tiles.Length; i++) tiles[i].Show(Achievements.All[i], r);
        earnedText.text = Loc.Get("records.earned", Achievements.All.Count(def => r.Has(def.Id)), Achievements.All.Length);
    }
}
