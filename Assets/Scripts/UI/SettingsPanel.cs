using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Settings card (built by Tools > Alkkagi UI > 3. Build main menu):
// language (its own LanguageToggle), the two volumes, and key bindings.
// Everything saves as it changes. A key row, once clicked, takes the next
// key pressed; Escape backs out.
public class SettingsPanel : MonoBehaviour
{
    public Slider masterSlider;
    public TMP_Text masterValue;
    public Slider interfaceSlider;
    public TMP_Text interfaceValue;
    public KeyBindRow[] keyRows;
    public Button resetButton;

    private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private KeyBindRow capturing;

    private void Awake()
    {
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
        masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        interfaceSlider.SetValueWithoutNotify(GameSettings.InterfaceVolume);
        masterValue.text = Percent(GameSettings.MasterVolume);
        interfaceValue.text = Percent(GameSettings.InterfaceVolume);
        foreach (var row in keyRows)
            row.keyText.text = row == capturing ? Loc.Get("bind.press") : KeyBindings.DisplayName(KeyBindings.Get(row.action));
    }

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100)}%";
}
