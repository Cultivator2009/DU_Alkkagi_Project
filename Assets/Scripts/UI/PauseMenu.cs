using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The in-game menu (Esc, or the HUD's Menu button): resume, this match's
// rules, settings, concede, back to the main menu. A local game stands still while it's open;
// an online one can't, and says so. Concede and Main menu take a second
// press to go through.
public class PauseMenu : MonoBehaviour
{
    public MainGameUIController game;
    public GameObject overlay;
    public Button openButton;
    public Button resumeButton;
    public Button rulesButton;
    public GameObject rulesPanel;   // over the menu: the match's rules (RulesSummary)
    public TMP_Text rulesText;
    public Button rulesCloseButton;
    public Button settingsButton;
    public Button concedeButton;
    public TMP_Text concedeLabel;
    public Button mainMenuButton;
    public TMP_Text mainMenuLabel;
    public TMP_Text caption;
    public SettingsPanel settings;
    public Button settingsCloseButton;
    public float confirmSeconds = 3f;

    private Button armed; // pressed once, waiting for the second press
    private float armedUntil;

    public bool IsOpen => overlay.activeSelf;

    private void Awake()
    {
        openButton.onClick.AddListener(Open);
        resumeButton.onClick.AddListener(Close);
        rulesButton.onClick.AddListener(ShowRules);
        rulesCloseButton.onClick.AddListener(() => rulesPanel.SetActive(false));
        settingsButton.onClick.AddListener(() => settings.gameObject.SetActive(true));
        settingsCloseButton.onClick.AddListener(() => settings.gameObject.SetActive(false));
        concedeButton.onClick.AddListener(() => Confirm(concedeButton, () =>
        {
            Close();
            game.Concede();
        }));
        mainMenuButton.onClick.AddListener(() => Confirm(mainMenuButton, () =>
        {
            Close();
            game.ReturnToMainMenu();
        }));
        overlay.SetActive(false);
        settings.gameObject.SetActive(false);
        rulesPanel.SetActive(false);
    }

    private void ShowRules()
    {
        var gameManager = GameManager.manager;
        rulesText.text = RulesSummary.Text(MatchSettings.Current, gameManager.Sides.Count, gameManager.Teams);
        rulesPanel.SetActive(true);
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Render;
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Render;
        Time.timeScale = 1f; // never leave the next scene frozen
    }

    // After SettingsPanel's Update, so an Esc it used to cancel a key rebind
    // doesn't also close the card.
    private void LateUpdate()
    {
        if (armed != null && Time.unscaledTime > armedUntil)
        {
            armed = null;
            Render();
        }
        openButton.gameObject.SetActive(game.MatchInProgress && !IsOpen);
        if (IsOpen && !game.MatchInProgress) Close(); // the result came in (e.g. the opponent left)
        if (!Input.GetKeyDown(KeyCode.Escape) || settings.EscapeUsedFrame == Time.frameCount) return;

        if (settings.gameObject.activeSelf) settings.gameObject.SetActive(false);
        else if (rulesPanel.activeSelf) rulesPanel.SetActive(false);
        else if (IsOpen) Close();
        else Open();
    }

    private void Open()
    {
        if (!game.MatchInProgress) return;
        // A pull in progress would fire when the mouse comes up over the menu.
        foreach (var piece in GameManager.manager.gamePieceScripts)
            if (piece != null && piece.isDragging) piece.Cancel();
        overlay.SetActive(true);
        if (!game.IsOnline) Time.timeScale = 0f;
        armed = null;
        Render();
    }

    private void Close()
    {
        overlay.SetActive(false);
        settings.gameObject.SetActive(false);
        rulesPanel.SetActive(false);
        Time.timeScale = GamePace.Current;
        armed = null;
    }

    private void Confirm(Button button, System.Action action)
    {
        if (armed == button)
        {
            armed = null;
            action();
            return;
        }
        armed = button;
        armedUntil = Time.unscaledTime + confirmSeconds;
        Render();
    }

    private void Render()
    {
        if (!IsOpen) return;
        var side = game.ConcedingSide;
        concedeLabel.text = armed == concedeButton ? Loc.Get("pause.confirm")
            : side.HasValue ? Loc.Get("pause.concedeSide", SideStyle.Name(side.Value)) : Loc.Get("pause.concede");
        mainMenuLabel.text = Loc.Get(armed == mainMenuButton ? "pause.confirm" : "pause.mainMenu");
        concedeButton.interactable = game.CanConcede;
        var group = concedeButton.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = game.CanConcede ? 1f : 0.45f;
        caption.text = Loc.Get(game.IsOnline ? "pause.captionOnline" : "pause.captionLocal");
    }
}
