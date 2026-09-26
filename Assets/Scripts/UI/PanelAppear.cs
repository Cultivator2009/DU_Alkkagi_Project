using UnityEngine;

// A modal opening: its dimmed overlay fades in and its card grows into
// place, with a paper swish (the Alkkagi UI builders put it on every
// overlay). Unscaled time - the pause menu opens with the game stopped.
[RequireComponent(typeof(CanvasGroup))]
public class PanelAppear : MonoBehaviour
{
    public RectTransform card;
    public float seconds = 0.2f;
    public float fromScale = 0.92f;
    public bool swish = true;

    private CanvasGroup group;
    private float openedAt;
    private bool done;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        openedAt = Time.unscaledTime;
        done = false;
        Apply(0);
        if (swish) GameAudio.PlayInterface(GameAudio.Bank.open, 0.6f);
    }

    private void Update()
    {
        if (done) return;
        var u = Mathf.Clamp01((Time.unscaledTime - openedAt) / seconds);
        Apply(u);
        done = u >= 1; // the end state always gets applied, however long the frame
    }

    private void Apply(float u)
    {
        group.alpha = 1 - (1 - u) * (1 - u);
        if (card == null) return;
        var scale = Mathf.LerpUnclamped(fromScale, 1f, BackOut(u));
        card.localScale = new Vector3(scale, scale, 1);
    }

    // Overshoots a touch and settles.
    private static float BackOut(float u)
    {
        const float overshoot = 1.6f;
        var t = u - 1;
        return 1 + t * t * ((overshoot + 1) * t + overshoot);
    }
}
