using System.Collections.Generic;
using TMPro;
using UnityEngine;

// One player's side panel in the in-game HUD. Stone icons are spawned from
// an inactive template at match start, because the per-player piece count
// comes from the scene rather than being fixed in the prefab.
public class PlayerHudPanel : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text numberText;
    public TMP_Text remainingText;
    public TMP_Text capturedText;
    public GameObject turnBadge;
    public CanvasGroup canvasGroup;
    public Transform stoneRow;
    public GameObject stoneTemplate; // children: "Fill", "Ring" (alive) and "Lost" (dashed ring)
    [Range(0f, 1f)] public float idleAlpha = 0.8f;

    private readonly List<GameObject> stones = new List<GameObject>();

    public void Build(int stoneCount)
    {
        foreach (var stone in stones) Destroy(stone);
        stones.Clear();

        stoneTemplate.SetActive(false);
        for (var i = 0; i < stoneCount; i++)
        {
            var stone = Instantiate(stoneTemplate, stoneRow);
            stone.SetActive(true);
            stones.Add(stone);
        }
    }

    public void Render(string playerName, string playerNumber, int remaining, int captured, bool isTurn)
    {
        nameText.text = playerName;
        numberText.text = playerNumber;
        remainingText.text = remaining.ToString();
        capturedText.text = Loc.Get("hud.captured", captured);
        turnBadge.SetActive(isTurn);
        canvasGroup.alpha = isTurn ? 1f : idleAlpha;

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
