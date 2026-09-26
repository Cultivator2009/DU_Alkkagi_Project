using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Settings card (built by Tools > Alkkagi UI > 3. Build main menu):
// language (its own LanguageToggle), janggi letters, fullscreen or a window
// and its size, the two volumes, and key bindings.
// Everything saves as it changes. A key row, once clicked, takes the next
// key pressed; Escape backs out.
public class SettingsPanel : MonoBehaviour
{
    public SegmentedToggle janggiLetters; // 0 Hangul, 1 Hanja
    public SegmentedToggle windowMode;    // 0 fullscreen, 1 windowed
    public TMP_Text windowSizeText;
    public Button windowSmallerButton;
    public Button windowLargerButton;
    public CanvasGroup windowSizeRow;     // dimmed in fullscreen, which has no size to pick
    [Range(0f, 1f)] public float fixedAlpha = 0.4f;
    public Slider masterSlider;
    public TMP_Text masterValue;
    public Slider interfaceSlider;
    public TMP_Text interfaceValue;
    public KeyBindRow[] keyRows;
    public Button resetButton;

    private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private KeyBindRow capturing;
    // What the display rows show. A new mode or size lands at the end of
    // the frame, and the window can also be resized or switched from
    // outside, so Update re-renders whenever the screen differs from this.
    private (Vector2Int size, bool fullscreen) shownScreen;

    // The frame this card last used Esc (to cancel a rebind), so whatever
    // else listens for Esc on that frame can leave it be.
    public int EscapeUsedFrame { get; private set; } = -1;

    private void Awake()
    {
        janggiLetters.OnSelected += index =>
        {
            GameSettings.JanggiHanja = index == 1;
            Render();
        };
        windowMode.OnSelected += index => DisplaySettings.SetFullscreen(index == 0);
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
        foreach (var row in keyRows)
        {
            var target = row;
            row.button.onClick.AddListener(() =>
            {
                capturing = target;
                Render();
            });
        }
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
        Render();
    }

    private void OnDisable()
    {
        capturing = null;
        Loc.OnLanguageChanged -= Render;
        KeyBindings.OnChanged -= Render;
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
        janggiLetters.Show(GameSettings.JanggiHanja ? 1 : 0);
        RenderDisplay();
        masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        interfaceSlider.SetValueWithoutNotify(GameSettings.InterfaceVolume);
        masterValue.text = Percent(GameSettings.MasterVolume);
        interfaceValue.text = Percent(GameSettings.InterfaceVolume);
        foreach (var row in keyRows)
            row.keyText.text = row == capturing ? Loc.Get("bind.press") : KeyBindings.DisplayName(KeyBindings.Get(row.action));
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

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100)}%";
}
