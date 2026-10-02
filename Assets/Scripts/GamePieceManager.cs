using UnityEngine;

public class GamePieceManager : MonoBehaviour
{

    public char pieceID;
    public int playerIndex;
    public bool isDestroyed;
    public float radius = 0.1f; // bounding circle on the board, set by BoardSetup when spawned
    [System.NonSerialized] public Vector2[] footprint; // its outline from above as it was spawned (Footprint), for placing
    [System.NonSerialized] public int health; // a battle of health's, A: its own (HealthRuleset); the host's word on a guest

    private void Start() {
        
    }
}