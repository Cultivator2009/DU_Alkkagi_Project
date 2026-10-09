using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One item on the HUD (built by Tools > Alkkagi UI > 2. Build HUD): a tile
// in its kind's colour with the item's name, empty when there's none.
// Pressable while this screen's side may use it; hovered, it says what it does.
public class ItemSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Button button;
    public Image fill;
    public TMP_Text nameText;
    public CanvasGroup group;
    public GameObject readyMark; // a line round it while it may be used
    public Color emptyColor = new Color(0.93f, 0.89f, 0.81f, 1f);

    public event Action<bool> OnHover;

    public byte Item { get; private set; } = SideItems.None;

    public void Show(byte item, bool usable)
    {
        Item = item;
        var has = ItemDefs.IsValid(item);
        var def = has ? ItemDefs.Of((ItemId)item) : default;
        fill.color = has ? ItemDefs.ColorOf(def.Category) : emptyColor;
        nameText.text = has ? def.Name : string.Empty;
        button.interactable = has && usable;
        group.alpha = has ? (usable ? 1f : 0.8f) : 0.55f;
        if (readyMark != null) readyMark.SetActive(has && usable);
    }

    public void OnPointerEnter(PointerEventData eventData) => OnHover?.Invoke(true);
    public void OnPointerExit(PointerEventData eventData) => OnHover?.Invoke(false);
}
