using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One player's side panel in the in-game HUD. Stone icons are spawned from
// an inactive template at match start, because the per-player piece count
// is a match rule rather than fixed in the prefab.
public class PlayerHudPanel : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text numberText;
    public TMP_Text remainingText;
    public UIPulse remainingPulse; // a punch when a stone goes
    public TMP_Text capturedText;
    public GameObject turnBadge;
    public CanvasGroup canvasGroup;
    public Transform stoneRow;
    public GameObject stoneTemplate; // children: "Fill", "Ring" (alive) and "Lost" (dashed ring)
    public Button skipButton;        // shown on this side's own turn, on the screen that plays it
    [Range(0f, 1f)] public float idleAlpha = 0.8f;
    [Range(0f, 1f)] public float outAlpha = 0.45f;
    public float minStoneSpacing = 4f;

    private readonly List<GameObject> stones = new List<GameObject>();
    private int shownRemaining = -1;

    public void Build(int stoneCount)
    {
        foreach (var stone in stones) Destroy(stone);
        stones.Clear();

        stoneTemplate.SetActive(false);
        // Up to 12 stones a side: shrink the icons (and the gaps) to fit the row.
        var layout = stoneRow.GetComponent<HorizontalLayoutGroup>();
        var width = ((RectTransform)stoneRow).rect.width;
        var fullSize = ((RectTransform)stoneTemplate.transform).sizeDelta.x;
        var spacing = layout.spacing;
        var size = fullSize;
        if (stoneCount * fullSize + (stoneCount - 1) * spacing > width)
        {
            spacing = minStoneSpacing;
            size = Mathf.Min(fullSize, (width - (stoneCount - 1) * spacing) / stoneCount);
        }
        layout.spacing = spacing;

        for (var i = 0; i < stoneCount; i++)
        {
            var stone = Instantiate(stoneTemplate, stoneRow);
            ((RectTransform)stone.transform).sizeDelta = new Vector2(size, size);
            stone.SetActive(true);
            stones.Add(stone);
        }
    }

    // outStatus: why this side is out of a match of three or four ("Out",
    // "Conceded", "Left"), or null while it plays.
    public void Render(string playerName, string playerNumber, int remaining, int captured, bool isTurn, string outStatus = null)
    {
        nameText.text = playerName;
        numberText.text = outStatus == null ? playerNumber : $"{playerNumber} · {outStatus}";
        remainingText.text = remaining.ToString();
        if (shownRemaining >= 0 && remaining < shownRemaining) remainingPulse.Play();
        shownRemaining = remaining;
        capturedText.text = Loc.Get("hud.captured", captured);
        turnBadge.SetActive(isTurn);
        canvasGroup.alpha = isTurn ? 1f : outStatus != null ? outAlpha : idleAlpha;

        for (var i = 0; i < stones.Count; i++)
        {
            var alive = i < remaining;
            var icon = stones[i].transform;
            icon.Find("Fill").gameObject.SetActive(alive);
            icon.Find("Ring").gameObject.SetActive(alive);
            icon.Find("Lost").gameObject.SetActive(!alive);
        }
    }
}
