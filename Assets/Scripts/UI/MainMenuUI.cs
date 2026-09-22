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
    public Button quitButton;
    public GameObject settingsPanel;
    public Button settingsCloseButton;
    public BoolToggle aimGuideToggle; // local matches; online the lobby host decides
    public GameObject steamUserRow;
    public TMP_Text steamUserText;

    private void Awake()
    {
        NetworkServices.EnsureCreated();

        localButton.onClick.AddListener(() =>
        {
            MatchSeries.Reset(); // a fresh local session; rematches from the game-over screen keep counting
            SceneManager.LoadScene("GameScene");
        });
        onlineButton.onClick.AddListener(() => SceneManager.LoadScene("LobbyScene"));
        settingsButton.onClick.AddListener(() => settingsPanel.SetActive(true));
        settingsCloseButton.onClick.AddListener(() => settingsPanel.SetActive(false));
        quitButton.onClick.AddListener(Quit);
        aimGuideToggle.SetValue(MatchOptions.LocalAimGuide);
        aimGuideToggle.OnChanged += value => MatchOptions.LocalAimGuide = value;
        settingsPanel.SetActive(false);
    }

    private void Start()
    {
        var steamReady = SteamTransport.Instance != null && SteamTransport.Instance.IsReady;
        steamUserRow.SetActive(steamReady);
        if (steamReady) steamUserText.text = $"Steam · {Steamworks.SteamClient.Name}";
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
