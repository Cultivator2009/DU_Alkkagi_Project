using UnityEngine;

public class GamePieceManager : MonoBehaviour
{

    public char pieceID;
    public int playerIndex;
    public bool isDestroyed;
    public float radius = 0.1f; // bounding circle on the board, set by BoardSetup when spawned

    private void Start() {
        
    }
}