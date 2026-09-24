using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A capsule split into segments, one of them selected (ink highlight). The
// owner listens to OnSelected, applies the choice, and calls Show with what
// is now in effect.
public class SegmentedToggle : MonoBehaviour
{
    public Button[] buttons;
    public Graphic[] highlights;
    public TMP_Text[] labels;
    public Color selectedTextColor = Color.white;
    public Color idleTextColor = Color.black;

    public event Action<int> OnSelected;

    private void Awake()
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            buttons[i].onClick.AddListener(() => OnSelected?.Invoke(index));
        }
    }

    public void Show(int selected, bool interactable = true)
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            highlights[i].enabled = i == selected;
            labels[i].color = i == selected ? selectedTextColor : idleTextColor;
            buttons[i].interactable = interactable;
        }
    }
}
