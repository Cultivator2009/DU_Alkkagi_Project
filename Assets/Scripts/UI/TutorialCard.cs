using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The practice match's lesson on the HUD (Tutorial): which of how many,
// what to do, and Skip. Built hidden by the HUD builder.
public class TutorialCard : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text stepText;
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public Button skipButton;
    public UIPulse pulse; // a nudge as each lesson begins

    public event Action OnSkip;

    private void Awake()
    {
        skipButton.onClick.AddListener(() => OnSkip?.Invoke());
    }

    public void Show(int index, int count, string title, string body, bool canSkip)
    {
        panel.SetActive(true);
        stepText.text = Loc.Get("tutorial.step", index, count);
        titleText.text = title;
        bodyText.text = body;
        skipButton.gameObject.SetActive(canSkip);
        if (pulse != null) pulse.Play();
    }

    public void Hide() => panel.SetActive(false);
}
