using UnityEngine;

public class DeathTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("GamePiece_GO")) return;

        var gameManager = GameManager.manager;
        // On a network guest, pieces are host-driven (kinematic + interpolated)
        // and removal/scoring arrives authoritatively via NetworkMatchBridge's
        // TurnResult - a local trigger fire here would double-count it.
        if (gameManager != null && gameManager.SkipLocalTurnProcessing) return;

        var pieceManager = other.GetComponent<GamePieceManager>();
        if (pieceManager.isDestroyed) return;
        pieceManager.isDestroyed = true;

        gameManager.Ruleset.OnPieceRemoved(pieceManager, gameManager.playersList);
        gameManager.RemovePiece(other.GetComponent<GamePieceDragAndReleaseForce>());

        Destroy(other.gameObject);
    }
}
