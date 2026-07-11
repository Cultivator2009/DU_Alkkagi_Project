using TMPro;
using UnityEngine;

// Attach this to the MainGame_UI prefab root and wire the fields below in the
// Inspector. It only touches text/active-state — visual layout and styling
// are intentionally left to be built around these hooks.
public class MainGameUIController : MonoBehaviour
{
    public TMP_Text turnText;
    public TMP_Text[] scoreTexts; // index-aligned with GameManager.playersList
    public GameObject winPanel;
    public TMP_Text winText;

    private void OnEnable()
    {
        var gameManager = GameManager.manager;
        if (gameManager == null || gameManager.TurnController == null) return;
        Subscribe(gameManager.TurnController);
    }

    private void OnDisable()
    {
        var gameManager = GameManager.manager;
        if (gameManager == null || gameManager.TurnController == null) return;
        Unsubscribe(gameManager.TurnController);
    }

    private void Subscribe(TurnController turnController)
    {
        turnController.OnTurnStarted += HandleTurnStarted;
        turnController.OnTurnEnded += HandleTurnEnded;
        turnController.OnMatchEnded += HandleMatchEnded;
    }

    private void Unsubscribe(TurnController turnController)
    {
        turnController.OnTurnStarted -= HandleTurnStarted;
        turnController.OnTurnEnded -= HandleTurnEnded;
        turnController.OnMatchEnded -= HandleMatchEnded;
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
        if (winPanel != null) winPanel.SetActive(true);
        if (winText != null) winText.text = $"Player {winner.ID + 1} Wins!";
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
}
