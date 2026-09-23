using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One rule in a MatchSettingsPanel: label, ◀ value ▶. The panel owns the
// value; the row only reports which way it was stepped.
public class MatchSettingRow : MonoBehaviour
{
    public MatchSettingId settingId;
    public TMP_Text valueText;
    public Button previousButton;
    public Button nextButton;
    public CanvasGroup canvasGroup;
    [Range(0f, 1f)] public float irrelevantAlpha = 0.4f;

    public event Action<MatchSettingId, int> OnStep;

    private void Awake()
    {
        previousButton.onClick.AddListener(() => OnStep?.Invoke(settingId, -1));
        nextButton.onClick.AddListener(() => OnStep?.Invoke(settingId, 1));
    }

    // editable = false shows the value only (a lobby guest); relevant = false
    // greys the row out when another rule makes it moot.
    public void Render(string value, bool canPrevious, bool canNext, bool editable, bool relevant)
    {
        valueText.text = value;
        SetArrow(previousButton, editable, canPrevious);
        SetArrow(nextButton, editable, canNext);
        canvasGroup.alpha = relevant ? 1f : irrelevantAlpha;
    }

    private static void SetArrow(Button arrow, bool editable, bool available)
    {
        arrow.gameObject.SetActive(editable);
        arrow.interactable = available;
        var graphic = arrow.transform.GetChild(0).GetComponent<Graphic>();
        if (graphic != null) graphic.canvasRenderer.SetAlpha(available ? 1f : 0.25f);
    }
}
