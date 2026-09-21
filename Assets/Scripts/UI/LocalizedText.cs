using TMPro;
using UnityEngine;

// Static label text: looks its key up in Loc and re-renders on a language
// switch. Text with runtime values (turn, scores, names) is set by its
// screen's controller instead, which listens to Loc.OnLanguageChanged too.
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    public string key;

    private TMP_Text text;

    private void Awake()
    {
        text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Refresh;
    }

    private void Refresh()
    {
        if (!string.IsNullOrEmpty(key)) text.text = Loc.Get(key);
    }
}
