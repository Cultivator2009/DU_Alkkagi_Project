using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach this to the MainGame_UI prefab root and wire the fields below in the
// Inspector. It only touches text/active-state — visual layout and styling
// are intentionally left to be built around these hooks.
public class MainGameUIController : MonoBehaviour
{
    public TMP_Text turnText;
    public TMP_Text[] scoreTexts; // index-aligned with GameManager.playersList
    public GameObject winPanel;
    public TMP_Text winText;

    // Item-mode placeholder: reserves a slot for a future item bar without
    // building any real item logic yet (Phase 1's ITurnAction is still a
    // stub). Wire itemBarRoot to an empty layout container in the Inspector;
    // ShowAvailableItems/OnItemButtonClicked are the seam an item ruleset
    // will use once real items exist.
    public Transform itemBarRoot;
    public GameObject itemButtonTemplate; // simple Button+TMP_Text prefab, kept inactive as a template
    public event Action<string> OnItemButtonClicked;

    private TurnController turnController;
    private NetworkMatchBridge networkBridge;

    private void OnEnable()
    {
        StartCoroutine(WaitForMatchThenSubscribe());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (turnController != null) Unsubscribe(turnController);
        if (networkBridge != null) UnsubscribeNetwork(networkBridge);
        turnController = null;
        networkBridge = null;
    }

    private IEnumerator WaitForMatchThenSubscribe()
    {
        GameManager gameManager;
        while ((gameManager = GameManager.manager) == null || gameManager.TurnController == null) yield return null;

        turnController = gameManager.TurnController;
        Subscribe(turnController);

        // Only present on a networked match (see NetworkBootstrap) - a guest's
        // TurnController never ticks, so its own events never fire past the
        // initial StartMatch and the bridge's events carry turn/score updates
        // instead.
        networkBridge = FindObjectOfType<NetworkMatchBridge>();
        if (networkBridge != null) SubscribeNetwork(networkBridge);
    }

    private void Subscribe(TurnController controller)
    {
        controller.OnTurnStarted += HandleTurnStarted;
        controller.OnTurnEnded += HandleTurnEnded;
        controller.OnMatchEnded += HandleMatchEnded;
    }

    private void Unsubscribe(TurnController controller)
    {
        controller.OnTurnStarted -= HandleTurnStarted;
        controller.OnTurnEnded -= HandleTurnEnded;
        controller.OnMatchEnded -= HandleMatchEnded;
    }

    private void SubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged += HandleGuestTurnChanged;
        bridge.OnGuestMatchEnded += HandleGuestMatchEnded;
    }

    private void UnsubscribeNetwork(NetworkMatchBridge bridge)
    {
        bridge.OnGuestTurnChanged -= HandleGuestTurnChanged;
        bridge.OnGuestMatchEnded -= HandleGuestMatchEnded;
    }

    private void HandleTurnStarted(PlayersManager player)
    {
        if (turnText != null) turnText.text = $"Player {player.ID + 1}'s Turn";
    }

    private void HandleTurnEnded(PlayersManager player)
    {
        RefreshScores();
    }

    private void HandleMatchEnded(PlayersManager winner)
    {
        RefreshScores();
        ShowWinPanel(winner.ID);
    }

    private void HandleGuestTurnChanged(int playerId)
    {
        RefreshScores();
        if (turnText != null) turnText.text = $"Player {playerId + 1}'s Turn";
    }

    private void HandleGuestMatchEnded(int winnerPlayerId)
    {
        RefreshScores();
        ShowWinPanel(winnerPlayerId);
    }

    private void ShowWinPanel(int winnerPlayerId)
    {
        if (winPanel != null) winPanel.SetActive(true);
        if (winText != null) winText.text = $"Player {winnerPlayerId + 1} Wins!";
    }

    private void RefreshScores()
    {
        var gameManager = GameManager.manager;
        if (gameManager == null || scoreTexts == null) return;

        for (var i = 0; i < scoreTexts.Length && i < gameManager.playersList.Count; i++)
        {
            if (scoreTexts[i] == null) continue;
            scoreTexts[i].text = gameManager.playersList[i].score.ToString();
        }
    }

    // ---- Item bar placeholder ----

    public void ShowAvailableItems(IReadOnlyList<string> itemIds)
    {
        if (itemBarRoot == null || itemButtonTemplate == null) return;

        for (var i = itemBarRoot.childCount - 1; i >= 0; i--)
        {
            var child = itemBarRoot.GetChild(i).gameObject;
            if (child != itemButtonTemplate) Destroy(child);
        }

        foreach (var itemId in itemIds)
        {
            var button = Instantiate(itemButtonTemplate, itemBarRoot);
            button.SetActive(true);
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = itemId;
            var clickTarget = button.GetComponent<Button>();
            if (clickTarget != null) clickTarget.onClick.AddListener(() => OnItemButtonClicked?.Invoke(itemId));
        }
    }
}
