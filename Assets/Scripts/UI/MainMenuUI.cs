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
    public Button settingsButton;
    public Button rankingButton;
    public Button quitButton;
    public GameObject settingsPanel;
    public Button settingsCloseButton;
    // Local match setup: the same rules an online host sets in the lobby.
    public GameObject setupPanel;
    public MatchSettingsPanel setupRules;
    public SegmentedToggle opponentToggle; // Opponent order
    public Button setupStartButton;
    public Button setupCancelButton;
    public GameObject steamUserRow;
    public TMP_Text steamUserText; // name and rating
    public LeaderboardPanel leaderboard;

    private void Awake()
    {
        NetworkServices.EnsureCreated();

        localButton.onClick.AddListener(() =>
        {
            setupRules.Show(MatchSettings.LoadPrefs(), true);
            opponentToggle.Show((int)LocalOpponent.Current);
            setupPanel.SetActive(true);
        });
        opponentToggle.OnSelected += index =>
        {
            LocalOpponent.Current = (Opponent)index;
            opponentToggle.Show(index);
        };
        setupStartButton.onClick.AddListener(StartLocalMatch);
        setupCancelButton.onClick.AddListener(() => setupPanel.SetActive(false));
        onlineButton.onClick.AddListener(() => SceneManager.LoadScene("LobbyScene"));
        settingsButton.onClick.AddListener(() => settingsPanel.SetActive(true));
        rankingButton.onClick.AddListener(leaderboard.Open);
        settingsCloseButton.onClick.AddListener(() => settingsPanel.SetActive(false));
        quitButton.onClick.AddListener(Quit);
        settingsPanel.SetActive(false);
        setupPanel.SetActive(false);
    }

    private void StartLocalMatch()
    {
        MatchSettings.Picked = setupRules.Settings;
        MatchSettings.Picked.SavePrefs();
        MatchSettings.Current = MatchSettings.Picked.Resolve();
        MatchRoster.Current = null; // two at this screen
        MatchSeries.Reset(); // a fresh local session; rematches from the game-over screen keep counting
        SceneManager.LoadScene("GameScene");
    }

    private void Start()
    {
        PlayerRating.SettlePending(); // a rated match left before its result
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
