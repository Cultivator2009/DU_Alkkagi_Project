using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Two-segment 한 | EN switch. The selected segment gets the ink-filled
// highlight; Loc persists the choice and notifies every localized label.
public class LanguageToggle : MonoBehaviour
{
    public Button koreanButton;
    public Button englishButton;
    public Graphic koreanHighlight;
    public Graphic englishHighlight;
    public TMP_Text koreanLabel;
    public TMP_Text englishLabel;
    public Color selectedTextColor = Color.white;
    public Color idleTextColor = Color.black;

    private void Awake()
    {
        koreanButton.onClick.AddListener(() => Loc.Set(Language.Korean));
        englishButton.onClick.AddListener(() => Loc.Set(Language.English));
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Render;
        Render();
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Render;
    }

    private void Render()
    {
        var korean = Loc.Current == Language.Korean;
        koreanHighlight.enabled = korean;
        englishHighlight.enabled = !korean;
        koreanLabel.color = korean ? selectedTextColor : idleTextColor;
        englishLabel.color = korean ? idleTextColor : selectedTextColor;
    }
}
