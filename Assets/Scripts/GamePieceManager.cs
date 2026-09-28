using UnityEngine;

public class GamePieceManager : MonoBehaviour
{

    public char pieceID;
    public int playerIndex;
    public bool isDestroyed;
    public float radius = 0.1f; // bounding circle on the board, set by BoardSetup when spawned
    public int value = 2;       // what losing it costs in a battle of health, set by BoardSetup (BoardSetup.Value)

    private void Start() {
        
    }
}