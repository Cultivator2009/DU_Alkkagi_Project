using UnityEngine;

public class DeathTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("GamePiece_GO")) return;

        var gameManager = GameManager.manager;
        // On a network guest, pieces are host-driven (kinematic + interpolated)
        // and what goes out arrives with the host's TurnResult - a local
        // trigger fire here would count it twice.
        if (gameManager == null || gameManager.SkipLocalTurnProcessing) return;
        gameManager.PieceOut(other.GetComponent<GamePieceDragAndReleaseForce>());
    }
}
