using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Every scene fades in from the menu colour as it loads, the first one at
// launch included, so a scene change doesn't just cut (and nothing of the
// first frame's layout shows). Made before the first scene loads and kept
// for the whole run; it never takes clicks.
public class SceneFade : MonoBehaviour
{
    public float seconds = 0.4f;

    private static readonly Color Cover = new Color32(0xCC, 0x86, 0x86, 0xFF); // the menu background

    private Image cover;
    private float loadedAt = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        var host = new GameObject("SceneFade");
        DontDestroyOnLoad(host);
        var canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var image = new GameObject("Cover", typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(host.transform, false);
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        image.raycastTarget = false;
        image.color = Cover;
        var fade = host.AddComponent<SceneFade>();
        fade.cover = image;
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (mode == LoadSceneMode.Single) fade.loadedAt = Time.unscaledTime;
        };
    }

    private void LateUpdate()
    {
        var u = (Time.unscaledTime - loadedAt) / seconds;
        cover.enabled = u < 1;
        if (u < 1) cover.color = new Color(Cover.r, Cover.g, Cover.b, 1 - u * u);
    }
}
