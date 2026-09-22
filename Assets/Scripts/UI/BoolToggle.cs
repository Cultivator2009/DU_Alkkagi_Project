using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Two-segment On | Off switch for a match option. The owner of the value
// (settings screen, lobby) sets it with SetValue and listens to OnChanged.
public class BoolToggle : MonoBehaviour
{
    public Button onButton;
    public Button offButton;
    public Graphic onHighlight;
    public Graphic offHighlight;
    public TMP_Text onLabel;
    public TMP_Text offLabel;
    public Color selectedTextColor = Color.white;
    public Color idleTextColor = Color.black;

    public event Action<bool> OnChanged;
    public bool Value { get; private set; }

    private void Awake()
    {
        onButton.onClick.AddListener(() => Pick(true));
        offButton.onClick.AddListener(() => Pick(false));
    }

    public void SetValue(bool value)
    {
        Value = value;
        onHighlight.enabled = value;
        offHighlight.enabled = !value;
        onLabel.color = value ? selectedTextColor : idleTextColor;
        offLabel.color = value ? idleTextColor : selectedTextColor;
    }

    // Read-only for a lobby guest: the host owns the option.
    public void SetInteractable(bool interactable)
    {
        onButton.interactable = offButton.interactable = interactable;
        var group = GetComponent<CanvasGroup>();
        if (group != null) group.alpha = interactable ? 1f : 0.6f;
    }

    private void Pick(bool value)
    {
        if (value == Value) return;
        SetValue(value);
        OnChanged?.Invoke(value);
    }
}
