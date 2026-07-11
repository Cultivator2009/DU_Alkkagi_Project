using UnityEngine;

public class DeathTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("GamePiece_GO")) return;

        var pieceManager = other.GetComponent<GamePieceManager>();
        if (pieceManager.isDestroyed) return;
        pieceManager.isDestroyed = true;

        var gameManager = GameManager.manager;
        gameManager.Ruleset.OnPieceRemoved(pieceManager, gameManager.playersList);
        gameManager.RemovePiece(other.GetComponent<GamePieceDragAndReleaseForce>());

        Destroy(other.gameObject);
    }
}
