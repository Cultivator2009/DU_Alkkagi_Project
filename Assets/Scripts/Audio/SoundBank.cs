using System.Collections.Generic;
using UnityEngine;

// Every sound the game plays, in one asset (Assets/Resources/SoundBank),
// filled from the files in Assets/Audio by their names (SoundLibrary, on
// its own whenever one changes): "hit_go_hard_2.wav" is the second
// full-power take of go stones knocking. The files are synthesized (Tools >
// Alkkagi > Build sounds); a recording only needs the right name.
[CreateAssetMenu(menuName = "Alkkagi/Sound bank")]
public class SoundBank : ScriptableObject
{
    public List<SoundSet> sets = new List<SoundSet>();

    private Dictionary<string, SoundSet> byKey;

    // Null when there's no such sound.
    public SoundSet Get(string key)
    {
        if (byKey == null)
        {
            byKey = new Dictionary<string, SoundSet>();
            foreach (var set in sets) byKey[set.key] = set;
        }
        return byKey.TryGetValue(key, out var found) ? found : null;
    }

    // The pieces' own (key_material, PieceSet.Sound), or else the shared one.
    public SoundSet Get(string key, string material) => (material != null ? Get(key + "_" + material) : null) ?? Get(key);

    public void ClearCache() => byKey = null;

    private void OnEnable() => byKey = null;

    // Board, each also by the pieces' material (Get(key, material)): hit
    // (two knocking), wall (the barrier), land (a piece coming down on the
    // board), shatter, hinge, flick, fall, place.

    // Interface
    public SoundSet click => Get("click");
    public SoundSet tick => Get("tick");          // the last seconds of a turn timer
    public SoundSet turn => Get("turn");          // a new turn
    public SoundSet kill => Get("kill");          // a shot that knocked out an opponent's piece
    public SoundSet win => Get("win");
    public SoundSet lose => Get("lose");
    public SoundSet draw => Get("draw");
    public SoundSet start => Get("start");        // the match's first turn: a bak, the court clapper
    public SoundSet notch => Get("notch");        // each tenth of a pull's power
    public SoundSet cancel => Get("cancel");      // an aim let go of
    public SoundSet stamp => Get("stamp");        // the result's seal landing
    public SoundSet open => Get("open");          // a card opening
    public SoundSet damage => Get("damage");      // a battle of health's knock, with its number (by strength)
    public SoundSet zoneWarn => Get("zone_warn"); // the edge will give way when this turn ends
    public SoundSet crumble => Get("crumble");    // the edge giving way
    public SoundSet countdown => Get("countdown"); // the last seconds before a ranked series' next game
}
