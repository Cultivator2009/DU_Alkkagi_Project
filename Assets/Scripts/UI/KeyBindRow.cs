using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One rebindable action in the Settings card; SettingsPanel drives it.
public class KeyBindRow : MonoBehaviour
{
    public GameAction action;
    public Button button;
    public TMP_Text keyText;
}
