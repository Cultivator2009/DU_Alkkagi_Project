using TMPro;
using UnityEngine;
using UnityEngine.UI;

// From the online result screen: each other player of the match, to block
// (never matched again, BlockList) or report (Steam's own page for them,
// and blocked). Built by Tools > Alkkagi UI > 2. Build HUD.
public class ReportPanel : MonoBehaviour
{
    public TMP_Text[] names;       // a row per other player, three at most
    public Button[] blockButtons;
    public Button[] reportButtons;
    public GameObject[] blockedLabels;
    public GameObject[] rows;
    public Button closeButton;

    private NetworkMatchBridge bridge;
    private readonly int[] players = new int[3];
    private int count;

    private void Awake()
    {
        closeButton.onClick.AddListener(() => gameObject.SetActive(false));
        for (var i = 0; i < rows.Length; i++)
        {
            var row = i;
            blockButtons[i].onClick.AddListener(() => Act(row, report: false));
            reportButtons[i].onClick.AddListener(() => Act(row, report: true));
        }
    }

    private void OnEnable() => BlockList.OnChanged += Render;
    private void OnDisable() => BlockList.OnChanged -= Render;

    public void Open(NetworkMatchBridge matchBridge)
    {
        bridge = matchBridge;
        count = 0;
        for (var player = 0; player < bridge.PlayerCount && count < players.Length; player++)
            if (player != bridge.LocalPlayerId) players[count++] = player;
        gameObject.SetActive(true);
        Render();
    }

    private void Act(int row, bool report)
    {
        var player = players[row];
        var id = bridge.SteamIdOf(player);
        if (report) BlockList.Report(id, bridge.PlayerName(player));
        else BlockList.Block(id, bridge.PlayerName(player));
    }

    private void Render()
    {
        if (bridge == null) return;
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i].SetActive(i < count);
            if (i >= count) continue;
            var player = players[i];
            var blocked = BlockList.IsBlocked(bridge.SteamIdOf(player));
            names[i].text = $"{SideStyle.Name(player)} · {bridge.PlayerName(player)}";
            blockButtons[i].gameObject.SetActive(!blocked);
            blockedLabels[i].SetActive(blocked);
        }
    }
}
