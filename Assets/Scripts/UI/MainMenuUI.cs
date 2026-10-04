using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the MainMenu_UI prefab (built by Tools > Alkkagi UI > 3. Build
// main menu). Replaces the old ID/password login screen: Steam already
// identifies the player, so the menu shows their persona name instead.
public class MainMenuUI : MonoBehaviour
{
    public Button localButton;
    public Button onlineButton;
    public Button practiceButton;   // the practice match (Tutorial)
    public GameObject offerPanel;   // the first start's offer of it
    public Button offerStartButton;
    public Button offerLaterButton;
    public Button settingsButton;
    public Button recordsButton;    // the records card, the rankings a button further
    public Button quitButton;
    public GameObject settingsPanel;
    public Button settingsCloseButton;
    // Local match setup: the same rules an online host sets in the lobby.
    public GameObject setupPanel;
    public MatchSettingsPanel setupRules;
    // Who plays each seat (Opponent order), as many rows shown as the match
    // has players; the card closes up by the rest.
    public RectTransform setupCard;
    public GameObject[] seatRows;
    public TMP_Text[] seatLabels;
    public SegmentedToggle[] seatToggles;
    public float seatRowPitch = 72f;
    public Button setupStartButton;
    public Button setupCancelButton;
    public GameObject steamUserRow;
    public TMP_Text steamUserText; // name and rating
    public RecordsPanel records;
    public LeaderboardPanel leaderboard;

    private void Awake()
    {
        NetworkServices.EnsureCreated();

        cardHeight = setupCard.sizeDelta.y;
        rulesTop = ((RectTransform)setupRules.transform).anchoredPosition;
        localButton.onClick.AddListener(() =>
        {
            var rules = MatchSettings.LoadPrefs();
            setupRules.Show(rules, true);
            RenderSeats(rules);
            setupPanel.SetActive(true);
        });
        setupRules.OnChanged += RenderSeats;
        for (var i = 0; i < seatToggles.Length; i++)
        {
            var seat = i;
            seatToggles[i].OnSelected += index =>
            {
                LocalOpponent.Set(seat, (Opponent)index);
                seatToggles[seat].Show(index);
            };
        }
        setupStartButton.onClick.AddListener(StartLocalMatch);
        setupCancelButton.onClick.AddListener(() => setupPanel.SetActive(false));
        onlineButton.onClick.AddListener(() => SceneManager.LoadScene("LobbyScene"));
        practiceButton.onClick.AddListener(StartPractice);
        offerStartButton.onClick.AddListener(StartPractice);
        offerLaterButton.onClick.AddListener(() =>
        {
            Tutorial.Offered = true;
            offerPanel.SetActive(false);
        });
        settingsButton.onClick.AddListener(() => settingsPanel.SetActive(true));
        recordsButton.onClick.AddListener(() => records.gameObject.SetActive(true));
        settingsCloseButton.onClick.AddListener(() => settingsPanel.SetActive(false));
        quitButton.onClick.AddListener(Quit);
        settingsPanel.SetActive(false);
        setupPanel.SetActive(false);
        offerPanel.SetActive(false);
    }

    private static void StartPractice()
    {
        MatchSeries.Reset();
        Tutorial.Begin();
    }

    private float cardHeight;
    private Vector2 rulesTop;

    // A row per seat the rules give, named as the pieces name the sides.
    private void RenderSeats(MatchSettings rules)
    {
        for (var i = 0; i < seatRows.Length; i++)
        {
            seatRows[i].SetActive(i < rules.Seats);
            seatLabels[i].text = SideStyle.Name(i, rules.PieceType);
            seatToggles[i].Show((int)LocalOpponent.Of(i));
        }
        var hidden = seatRows.Length - rules.Seats;
        setupCard.sizeDelta = new Vector2(setupCard.sizeDelta.x, cardHeight - hidden * seatRowPitch);
        ((RectTransform)setupRules.transform).anchoredPosition = rulesTop + Vector2.up * (hidden * seatRowPitch);
    }

    private void StartLocalMatch()
    {
        MatchSettings.Picked = setupRules.Settings;
        MatchSettings.Picked.SavePrefs();
        MatchSettings.Current = MatchSettings.Picked.Resolve();
        MatchRoster.Current = null; // the seats at this screen (LocalOpponent)
        MatchSeries.Reset(); // a fresh local session; rematches from the game-over screen keep counting
        SceneManager.LoadScene("GameScene");
    }

    private void Start()
    {
        PlayerRating.SettlePending(); // a rated match left before its result
        Achievements.Sync(PlayerRecords.Current.Earned); // any earned while Steam wasn't running
        // The first start: practice first?
        offerPanel.SetActive(!Tutorial.Done && !Tutorial.Offered);
        PlayerRating.OnChanged += RenderSteamUser;
        Loc.OnLanguageChanged += RenderSteamUser;
        RenderSteamUser();
    }

    private void OnDestroy()
    {
        PlayerRating.OnChanged -= RenderSteamUser;
        Loc.OnLanguageChanged -= RenderSteamUser;
    }

    private void RenderSteamUser()
    {
        var steamReady = SteamTransport.Instance != null && SteamTransport.Instance.IsReady;
        steamUserRow.SetActive(steamReady);
        if (steamReady) steamUserText.text = Loc.Get("menu.steamUser", Steamworks.SteamClient.Name, PlayerRating.Current.Rating);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
