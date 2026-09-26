using UnityEngine;

// A quick punch in scale: the turn pill as the turn changes, a panel's
// count as it drops, a notice as it shows. With slam on, it comes down
// instead - from big and faint onto its place, where it lands with a sound
// and settles (the result's seal). Unscaled time.
public class UIPulse : MonoBehaviour
{
    public float from = 1.15f;
    public float seconds = 0.25f;
    public float delay;
    public bool playOnEnable;
    public bool slam;
    public CanvasGroup fade;  // slam: faded in on the way down, if set
    public AudioClip landSound;
    public float landVolume = 1f;

    private float startedAt = -1;
    private Vector3 rest = Vector3.one;
    private bool landed;

    private void OnEnable()
    {
        if (playOnEnable) Play();
    }

    private void OnDisable()
    {
        if (startedAt >= 0) transform.localScale = rest;
        startedAt = -1;
    }

    public void Play()
    {
        if (startedAt < 0) rest = transform.localScale;
        startedAt = Time.unscaledTime + delay;
        landed = false;
        Update();
    }

    private void Update()
    {
        if (startedAt < 0) return;
        var u = (Time.unscaledTime - startedAt) / seconds;
        if (u >= 1)
        {
            Land();
            transform.localScale = rest;
            if (fade != null) fade.alpha = 1;
            startedAt = -1;
            return;
        }
        u = Mathf.Max(0, u);
        float scale;
        if (!slam) scale = Mathf.Lerp(from, 1f, 1 - (1 - u) * (1 - u) * (1 - u));
        else
        {
            // Accelerating down over the first three quarters, a little
            // under its size as it lands, then back up to it.
            const float hit = 0.75f;
            if (u < hit) scale = Mathf.Lerp(from, 0.93f, (u / hit) * (u / hit));
            else
            {
                Land();
                scale = Mathf.Lerp(0.93f, 1f, (u - hit) / (1 - hit));
            }
            if (fade != null) fade.alpha = Mathf.Clamp01(u / hit * 1.5f);
        }
        transform.localScale = rest * scale;
    }

    private void Land()
    {
        if (landed) return;
        landed = true;
        if (landSound != null) GameAudio.PlayInterface(landSound, landVolume);
    }
}
