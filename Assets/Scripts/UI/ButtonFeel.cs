using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A capsule button leans in under the cursor and gives when pressed (added
// by UIKit.CapsuleButton). It only scales while it's being touched, from
// whatever scale it had when the cursor came - the HUD scales some buttons
// to fit the room beside the board. Unscaled time, like the menus.
public class ButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.04f;
    public float pressScale = 0.95f;
    public float sharpness = 20f; // 1/s

    private Selectable selectable;
    private Vector3 rest;
    private float factor = 1f; // on top of rest
    private bool over;
    private bool down;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
    }

    private void OnDisable()
    {
        if (factor != 1f) transform.localScale = rest;
        factor = 1f;
        over = down = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (factor == 1f) rest = transform.localScale;
        over = true;
    }

    public void OnPointerExit(PointerEventData eventData) => over = false;
    public void OnPointerDown(PointerEventData eventData) => down = true;
    public void OnPointerUp(PointerEventData eventData) => down = false;

    private void Update()
    {
        if (factor == 1f && !over && !down) return; // at rest: leave the scale to whoever sets it
        var live = selectable == null || selectable.IsInteractable();
        var target = !live ? 1f : down ? pressScale : over ? hoverScale : 1f;
        factor = Mathf.Lerp(factor, target, 1 - Mathf.Exp(-sharpness * Time.unscaledDeltaTime));
        if (!over && !down && Mathf.Abs(factor - 1f) < 0.002f) factor = 1f;
        transform.localScale = rest * factor;
    }
}
