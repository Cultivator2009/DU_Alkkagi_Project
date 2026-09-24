using System;
using System.Collections;
using UnityEngine;

public enum BoardSound : byte
{
    Hit,   // two pieces knocking together
    Hinge, // a piece meeting a board hinge
    Flick,
    Fall   // a piece going over the edge
}

public struct BoardSoundEvent
{
    public BoardSound Kind;
    public float Volume; // 0..1
    public float Pan;    // -1 left .. 1 right
    public int Owner;    // whose piece flicked, -1 for the rest
}

// The board's sounds for one match. The physics authority (a local game, or
// the network host) hears its own collisions through each piece's
// PieceSounds. A guest's pieces are kinematic and never collide, so
// NetworkMatchBridge forwards the host's sounds, and the guest plays each a
// moment late to line up with its eased view of the board.
public class BoardSounds : MonoBehaviour
{
    public static BoardSounds Instance { get; private set; }
    public static event Action<BoardSoundEvent> OnEmitted; // on the authority, for the network host to forward

    public float fullHitSpeed = 12f;         // m/s for a full-volume knock
    public float guestDelaySeconds = 0.06f;  // about how far a guest's view trails the host's snapshots
    public int maxSoundsPerStep = 4;         // a break into a cluster stays a clatter, not a roar

    private AudioClip hit;
    private int soundsThisStep;

    public void Init(bool janggiPieces)
    {
        Instance = this;
        hit = janggiPieces ? GameAudio.Bank.woodHit : GameAudio.Bank.stoneHit;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void FixedUpdate()
    {
        soundsThisStep = 0;
    }

    public static float Pan(Vector3 position) => Mathf.Clamp(position.x / 1.5f * 0.6f, -1f, 1f);

    // speed: how hard it happened, in m/s.
    public void Emit(BoardSound kind, float speed, Vector3 at, int owner = -1)
    {
        if (soundsThisStep >= maxSoundsPerStep) return;
        soundsThisStep++;
        var sound = new BoardSoundEvent { Kind = kind, Volume = Loudness(kind, speed), Pan = Pan(at), Owner = owner };
        Play(sound);
        OnEmitted?.Invoke(sound);
    }

    public void PlayRemote(BoardSoundEvent sound)
    {
        StartCoroutine(PlayLater(sound));
    }

    // The flick on the screen that made it, at once: a guest's own shot only
    // starts moving when the host's snapshots come back.
    public void PlayLocalFlick(Vector3 at, float power)
    {
        Play(new BoardSoundEvent { Kind = BoardSound.Flick, Volume = 0.35f + 0.65f * power, Pan = Pan(at), Owner = -1 });
    }

    private IEnumerator PlayLater(BoardSoundEvent sound)
    {
        yield return new WaitForSeconds(guestDelaySeconds);
        Play(sound);
    }

    private float Loudness(BoardSound kind, float speed)
    {
        switch (kind)
        {
            case BoardSound.Fall: return 0.8f;
            case BoardSound.Flick: return Mathf.Clamp01(0.35f + speed / 25f);
            default: return 0.15f + 0.85f * Mathf.Pow(Mathf.Clamp01(speed / fullHitSpeed), 0.6f);
        }
    }

    private void Play(BoardSoundEvent sound)
    {
        var bank = GameAudio.Bank;
        var clip = sound.Kind switch
        {
            BoardSound.Hit => hit,
            BoardSound.Hinge => bank.hingeHit,
            BoardSound.Flick => bank.flick,
            _ => bank.fall,
        };
        // Harder knocks ring a touch higher; a small random spread keeps a
        // volley of hits from sounding like one sample repeated.
        var pitch = UnityEngine.Random.Range(0.95f, 1.05f) + (sound.Kind == BoardSound.Hit ? 0.08f * sound.Volume : 0f);
        GameAudio.PlayBoard(clip, sound.Volume, pitch, sound.Pan);
    }
}
