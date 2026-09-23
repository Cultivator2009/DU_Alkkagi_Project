using UnityEngine;

// One playable board in GameScene, under Board_GO (Tools > Alkkagi > Build
// boards and janggi pieces). BoardSetup turns on the one the match rules
// pick and turns the others off.
public class BoardVariant : MonoBehaviour
{
    public BoardType type;
    public Collider surface; // what the pieces sit on: its bounds are the board's edge
    // Black's placement zone in (x, z) for a go-stone-sized piece (radius
    // 0.1); larger pieces keep further in. White's zone is the mirror.
    public Rect blackZone = new Rect(-1.35f, -1.35f, 2.7f, 1.05f);
}
