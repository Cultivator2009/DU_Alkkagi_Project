using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Settings card (built by Tools > Alkkagi UI > 3. Build main menu), a
// page at a time behind tabs: general (language - its own LanguageToggle -,
// janggi letters, the blocked players, the version and the credits), display (fullscreen or a window and
// its size, graphics quality, vertical sync or a frame cap, screen shake,
// colourblind mode), sound (four volumes) and key bindings.
// Everything saves as it changes. A key row, once clicked, takes the next
// key pressed; Escape backs out.
public class SettingsPanel : MonoBehaviour
{
    public SegmentedToggle tabs;          // general, display, sound, controls
    public GameObject[] pages;            // in the tabs' order
    public SegmentedToggle janggiLetters; // 0 Hangul, 1 Hanja
    public SegmentedToggle windowMode;    // 0 fullscreen, 1 windowed
    public SegmentedToggle screenShake;   // 0 on, 1 off
    public SegmentedToggle quality;       // DisplaySettings.QualityLevels order
    public SegmentedToggle vsync;         // 0 on, 1 off
    public TMP_Text frameCapText;
    public Button frameCapLowerButton;
    public Button frameCapHigherButton;
    public CanvasGroup frameCapRow;       // dimmed while vertical sync is on
    public SegmentedToggle colorAssist;   // 0 on, 1 off
    public TMP_Text windowSizeText;
    public Button windowSmallerButton;
    public Button windowLargerButton;
    public CanvasGroup windowSizeRow;     // dimmed in fullscreen, which has no size to pick
    [Range(0f, 1f)] public float fixedAlpha = 0.4f;
    public Slider masterSlider;
    public TMP_Text masterValue;
    public Slider interfaceSlider;
    public TMP_Text interfaceValue;
    public Slider musicSlider;
    public TMP_Text musicValue;
    public Slider effectsSlider;
    public TMP_Text effectsValue;
    public KeyBindRow[] keyRows;
    public Button resetButton;
    public TMP_Text blockedText;      // how many players this one has blocked
    public Button unblockAllButton;
    public TMP_Text versionText;
    public Button creditsButton;
    public GameObject creditsPanel;   // over the card
    public TMP_Text creditsText;
    public Button creditsCloseButton;

    private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private KeyBindRow capturing;
    private static int page; // the page last shown, while the game runs
    // What the display rows show. A new mode or size lands at the end of
    // the frame, and the window can also be resized or switched from
    // outside, so Update re-renders whenever the screen differs from this.
    private (Vector2Int size, bool fullscreen) shownScreen;

    // The frame this card last used Esc (to cancel a rebind), so whatever
    // else listens for Esc on that frame can leave it be.
    public int EscapeUsedFrame { get; private set; } = -1;

    private void Awake()
    {
        tabs.OnSelected += index =>
        {
            page = index;
            capturing = null;
            Render();
        };
        janggiLetters.OnSelected += index =>
        {
            GameSettings.JanggiHanja = index == 1;
            Render();
        };
        windowMode.OnSelected += index => DisplaySettings.SetFullscreen(index == 0);
        screenShake.OnSelected += index =>
        {
            GameSettings.ScreenShake = index == 0;
            Render();
        };
        quality.OnSelected += index => SetGraphics(() => GameSettings.Quality = index);
        vsync.OnSelected += index => SetGraphics(() => GameSettings.VSync = index == 0);
        frameCapLowerButton.onClick.AddListener(() => SetGraphics(() => GameSettings.FrameCap--));
        frameCapHigherButton.onClick.AddListener(() => SetGraphics(() => GameSettings.FrameCap++));
        colorAssist.OnSelected += index =>
        {
            GameSettings.ColorAssist = index == 0;
            Render();
        };
        windowSmallerButton.onClick.AddListener(() => StepWindow(-1));
        windowLargerButton.onClick.AddListener(() => StepWindow(1));
        masterSlider.onValueChanged.AddListener(value =>
        {
            GameSettings.MasterVolume = value;
            Render();
        });
        interfaceSlider.onValueChanged.AddListener(value =>
        {
            GameSettings.InterfaceVolume = value;
            Render();
        });
        musicSlider.onValueChanged.AddListener(value =>
        {
            GameSettings.MusicVolume = value;
            Render();
        });
        effectsSlider.onValueChanged.AddListener(value =>
        {
            GameSettings.EffectsVolume = value;
            Render();
        });
        foreach (var row in keyRows)
        {
            var target = row;
            row.button.onClick.AddListener(() =>
            {
                capturing = target;
                Render();
            });
        }
        unblockAllButton.onClick.AddListener(BlockList.Clear);
        creditsButton.onClick.AddListener(() => creditsPanel.SetActive(true));
        creditsCloseButton.onClick.AddListener(() => creditsPanel.SetActive(false));
        resetButton.onClick.AddListener(() =>
        {
            capturing = null;
            GameSettings.ResetVolumes();
            KeyBindings.ResetAll();
            Render();
        });
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Render;
        KeyBindings.OnChanged += Render;
        BlockList.OnChanged += Render;
        Render();
    }

    private void OnDisable()
    {
        capturing = null;
        creditsPanel.SetActive(false);
        Loc.OnLanguageChanged -= Render;
        KeyBindings.OnChanged -= Render;
        BlockList.OnChanged -= Render;
    }

    private void Update()
    {
        if (shownScreen != (DisplaySettings.Current, DisplaySettings.Fullscreen)) Render();
        if (capturing == null || !Input.anyKeyDown) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            EscapeUsedFrame = Time.frameCount;
            capturing = null;
            Render();
            return;
        }
        foreach (var key in AllKeys)
        {
            if (!KeyBindings.IsBindable(key) || !Input.GetKeyDown(key)) continue;
            var row = capturing;
            capturing = null;
            KeyBindings.Set(row.action, key); // re-renders through OnChanged
            return;
        }
    }

    private void Render()
    {
        tabs.Show(page);
        for (var i = 0; i < pages.Length; i++) pages[i].SetActive(i == page);
        janggiLetters.Show(GameSettings.JanggiHanja ? 1 : 0);
        screenShake.Show(GameSettings.ScreenShake ? 0 : 1);
        colorAssist.Show(GameSettings.ColorAssist ? 0 : 1);
        RenderGraphics();
        RenderDisplay();
        masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        interfaceSlider.SetValueWithoutNotify(GameSettings.InterfaceVolume);
        masterValue.text = Percent(GameSettings.MasterVolume);
        interfaceValue.text = Percent(GameSettings.InterfaceVolume);
        musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        effectsSlider.SetValueWithoutNotify(GameSettings.EffectsVolume);
        musicValue.text = Percent(GameSettings.MusicVolume);
        effectsValue.text = Percent(GameSettings.EffectsVolume);
        foreach (var row in keyRows)
            row.keyText.text = row == capturing ? Loc.Get("bind.press") : KeyBindings.DisplayName(KeyBindings.Get(row.action));
        blockedText.text = Loc.Get("settings.blocked", BlockList.Count);
        versionText.text = Loc.Get("settings.version", Application.version);
        creditsText.text = Credits();
        unblockAllButton.interactable = BlockList.Count > 0;
        var group = unblockAllButton.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = BlockList.Count > 0 ? 1f : fixedAlpha;
    }

    private void RenderDisplay()
    {
        var size = DisplaySettings.Current;
        var fullscreen = DisplaySettings.Fullscreen;
        shownScreen = (size, fullscreen);
        windowMode.Show(fullscreen ? 0 : 1);
        windowSizeText.text = Loc.Get("settings.sizeValue", size.x, size.y);
        windowSizeRow.alpha = fullscreen ? fixedAlpha : 1f;
        SetArrow(windowSmallerButton, !fullscreen && DisplaySettings.StepFrom(size, -1).HasValue);
        SetArrow(windowLargerButton, !fullscreen && DisplaySettings.StepFrom(size, 1).HasValue);
    }

    private void SetGraphics(System.Action change)
    {
        change();
        DisplaySettings.ApplyGraphics();
        Render();
    }

    private void RenderGraphics()
    {
        quality.Show(GameSettings.Quality);
        vsync.Show(GameSettings.VSync ? 0 : 1);
        var cap = DisplaySettings.FrameCaps[GameSettings.FrameCap];
        frameCapText.text = cap > 0 ? Loc.Get("settings.fps", cap) : Loc.Get("settings.fpsNone");
        var free = !GameSettings.VSync;
        frameCapRow.alpha = free ? 1f : fixedAlpha;
        SetStep(frameCapLowerButton, free && GameSettings.FrameCap > 0);
        SetStep(frameCapHigherButton, free && GameSettings.FrameCap < DisplaySettings.FrameCaps.Length - 1);
    }

    private static void SetStep(Button arrow, bool available)
    {
        arrow.interactable = available;
        arrow.transform.GetChild(0).GetComponent<Graphic>().canvasRenderer.SetAlpha(available ? 1f : 0.25f);
    }

    private static void StepWindow(int step)
    {
        var size = DisplaySettings.StepFrom(DisplaySettings.Current, step);
        if (size.HasValue) DisplaySettings.SetWindowSize(size.Value);
    }

    // Like a match rule's stepper: faded at either end of the list, hidden
    // when there's nothing to step (fullscreen).
    private static void SetArrow(Button arrow, bool available)
    {
        arrow.gameObject.SetActive(!DisplaySettings.Fullscreen);
        arrow.interactable = available;
        arrow.transform.GetChild(0).GetComponent<Graphic>().canvasRenderer.SetAlpha(available ? 1f : 0.25f);
    }

    // The names stay as their owners write them; the full licenses ship in
    // StreamingAssets/Licenses.
    private static string Credits() => string.Join("\n", new[]
    {
        $"<size=130%><b><color=#2E241A>{Loc.Get("menu.title")}</color></b></size>",
        Loc.Get("credits.madeBy", Application.companyName),
        "",
        $"<b><color=#2E241A>{Loc.Get("credits.fonts")}</color></b>",
        "Pretendard - Kil Hyung-jin · SIL Open Font License 1.1",
        "Noto Serif KR - Adobe · SIL Open Font License 1.1",
        "Liberation Sans - Red Hat · SIL Open Font License 1.1",
        "",
        $"<b><color=#2E241A>{Loc.Get("credits.libraries")}</color></b>",
        "Facepunch.Steamworks - Facepunch Studios · MIT License",
        "Unity Logs Viewer - aliessmael",
        "Steamworks SDK - Valve Corporation",
        "Made with Unity",
        "",
        $"<size=80%>{Loc.Get("credits.licenses")}</size>",
    });

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100)}%";
}
