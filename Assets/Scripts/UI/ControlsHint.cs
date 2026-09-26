using TMPro;
using UnityEngine;

// The key legend in the HUD's right column (built by Tools > Alkkagi UI >
// 2. Build HUD): how to flick, then the rebindable keys as this machine has
// them, then the menu. MainGameUIController places and scales it with the
// column, and hides it where the column is too narrow to read it.
public class ControlsHint : MonoBehaviour
{
    public GameAction[] actions;  // the rebindable lines, in order
    public TMP_Text[] keyTexts;   // their keys, by the same index

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Render;
        KeyBindings.OnChanged += Render;
        Render();
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Render;
        KeyBindings.OnChanged -= Render;
    }

    private void Render()
    {
        for (var i = 0; i < actions.Length; i++) keyTexts[i].text = KeyBindings.DisplayName(KeyBindings.Get(actions[i]));
    }
}
