using UnityEngine;

// Every sound the game plays, in one asset (Assets/Resources/SoundBank,
// built by Tools > Alkkagi > Build sounds). The clips are synthesized; a
// recorded replacement only needs to keep the file name.
[CreateAssetMenu(menuName = "Alkkagi/Sound bank")]
public class SoundBank : ScriptableObject
{
    [Header("Board")]
    public AudioClip stoneHit;  // go stones knocking together
    public AudioClip woodHit;   // janggi pieces knocking together
    public AudioClip hingeHit;  // a piece meeting a board hinge
    public AudioClip flick;
    public AudioClip fall;      // a piece going over the edge
    public AudioClip place;     // a stone set down while placing

    [Header("Interface")]
    public AudioClip click;
    public AudioClip tick;      // the last seconds of a turn timer
    public AudioClip turn;      // a new turn
    public AudioClip kill;      // a shot that knocked out an opponent's piece
    public AudioClip win;
    public AudioClip lose;
    public AudioClip draw;
    public AudioClip start;     // the match's first turn: a bak, the court clapper
    public AudioClip notch;     // each tenth of a pull's power
    public AudioClip cancel;    // an aim let go of
    public AudioClip stamp;     // the result's seal landing
    public AudioClip open;      // a card opening
}
