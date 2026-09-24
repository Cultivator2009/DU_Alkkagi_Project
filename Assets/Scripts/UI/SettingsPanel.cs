using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Settings card (built by Tools > Alkkagi UI > 3. Build main menu):
// language (its own LanguageToggle), janggi letters, the two volumes, and
// key bindings.
// Everything saves as it changes. A key row, once clicked, takes the next
// key pressed; Escape backs out.
public class SettingsPanel : MonoBehaviour
{
    public SegmentedToggle janggiLetters; // 0 Hangul, 1 Hanja
    public Slider masterSlider;
    public TMP_Text masterValue;
    public Slider interfaceSlider;
    public TMP_Text interfaceValue;
    public KeyBindRow[] keyRows;
    public Button resetButton;

    private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private KeyBindRow capturing;

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
        masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        interfaceSlider.SetValueWithoutNotify(GameSettings.InterfaceVolume);
        masterValue.text = Percent(GameSettings.MasterVolume);
        interfaceValue.text = Percent(GameSettings.InterfaceVolume);
        foreach (var row in keyRows)
            row.keyText.text = row == capturing ? Loc.Get("bind.press") : KeyBindings.DisplayName(KeyBindings.Get(row.action));
    }

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100)}%";
}
