using System;
using System.Collections;
using UnityEngine;

public enum BoardSound : byte
{
    Hit,   // two pieces knocking together
    Hinge, // a piece meeting a board hinge
    Flick,
    Fall,  // a piece going over the edge
    Topple, // a standing piece (chess) knocking down onto the board, a gonggi stone landing
    Shatter, // a piece breaking as it falls past the edge (PieceShatter)
    Wall    // a piece against the barrier (a battle of health's walls)
}

public struct BoardSoundEvent
{
    public BoardSound Kind;
    public float Volume; // 0..1
    public float Pan;    // -1 left .. 1 right
    public int Owner;    // whose piece flicked, -1 for the rest
    public Vector3 Position;
    public char Piece;   // which piece breaks (Shatter), '\0' for the rest
}

// The board's sounds for one match. The physics authority (a local game, or
// the network host) hears its own collisions through each piece's
// PieceSounds. A guest's pieces are kinematic and never collide, so
// NetworkMatchBridge forwards the host's sounds, and the guest plays each a
// moment late to line up with its eased view of the board. A knock's burst,
// a fall's ink splash and a flick's ring (HitEffects) go off with the sound.
// Each sound is the pieces' own where there is one (SoundBank.Get with
// PieceSet.Sound): a go stone on the wall isn't janggi wood on it.
public class BoardSounds : MonoBehaviour
{
    public static BoardSounds Instance { get; private set; }
    public static event Action<BoardSoundEvent> OnEmitted; // on the authority, for the network host to forward

    public float fullHitSpeed = 8.5f;        // m/s for a full-volume knock (about a full-power hit)
    public float guestDelaySeconds = 0.06f;  // about how far a guest's view trails the host's snapshots
    public int maxSoundsPerStep = 4;         // a break into a cluster stays a clatter, not a roar

    public float damageGapSeconds = 0.05f;   // one knock's damage sound at a time (a chain of them lands at once)

    private string material;
    private int soundsThisStep;
    private float lastDamageAt = -1;

    public void Init(PieceSet pieces)
    {
        Instance = this;
        material = pieces.Sound;
        GameManager.Damaged += PlayDamage;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GameManager.Damaged -= PlayDamage;
    }

    // A battle of health's knock, with its number (host and guests alike):
    // the harder, the heavier.
    private void PlayDamage(char pieceId, int amount, Vector3 at)
    {
        if (Time.unscaledTime - lastDamageAt < damageGapSeconds) return;
        lastDamageAt = Time.unscaledTime;
        var strength = Mathf.Clamp01(amount / (float)HealthRuleset.MaxDamage);
        GameAudio.PlayInterface(GameAudio.Bank.damage, 0.35f + 0.65f * strength, UnityEngine.Random.Range(0.97f, 1.03f), strength);
    }

    private void FixedUpdate()
    {
        soundsThisStep = 0;
    }

    public static float Pan(Vector3 position) => Mathf.Clamp(position.x / 1.5f * 0.6f, -1f, 1f);

    // speed: how hard it happened, in m/s. A piece breaking always goes
    // off (and over to a guest), however many knocks share its step.
    public void Emit(BoardSound kind, float speed, Vector3 at, int owner = -1, char piece = '\0')
    {
        if (kind != BoardSound.Shatter && soundsThisStep >= maxSoundsPerStep) return;
        soundsThisStep++;
        var sound = new BoardSoundEvent { Kind = kind, Volume = Loudness(kind, speed), Pan = Pan(at), Owner = owner, Position = at, Piece = piece };
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
        Play(new BoardSoundEvent { Kind = BoardSound.Flick, Volume = 0.35f + 0.65f * power, Pan = Pan(at), Owner = -1, Position = at });
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
            case BoardSound.Shatter: return 0.7f;
            case BoardSound.Flick: return Mathf.Clamp01(0.35f + speed / 17.7f);
            case BoardSound.Topple: return 0.1f + 0.6f * Mathf.Pow(Mathf.Clamp01(speed / 3f), 0.6f);
            default: return 0.15f + 0.85f * Mathf.Pow(Mathf.Clamp01(speed / fullHitSpeed), 0.6f);
        }
    }

    private void Play(BoardSoundEvent sound)
    {
        var effects = HitEffects.Instance;
        if (effects != null)
        {
            if (sound.Kind == BoardSound.Hit || sound.Kind == BoardSound.Wall) effects.Play(sound.Position, sound.Volume);
            else if (sound.Kind == BoardSound.Fall) effects.PlayFall(sound.Position);
            else if (sound.Kind == BoardSound.Flick) effects.PlayFlick(sound.Position, sound.Volume);
        }
        if (sound.Kind == BoardSound.Shatter) Break(sound.Piece);
        var key = sound.Kind switch
        {
            BoardSound.Hit => "hit",
            BoardSound.Wall => "wall",
            BoardSound.Topple => "land",
            BoardSound.Shatter => "shatter",
            BoardSound.Hinge => "hinge",
            BoardSound.Flick => "flick",
            _ => "fall",
        };
        var set = GameAudio.Bank.Get(key, material);
        // How hard, for the take (a knock's volume runs from 0.15 for the
        // lightest); one without takes by strength rings a touch higher
        // instead. A small random spread on top.
        var knock = sound.Kind == BoardSound.Hit || sound.Kind == BoardSound.Wall || sound.Kind == BoardSound.Topple;
        var strength = knock ? Mathf.InverseLerp(0.15f, 1f, sound.Volume) : 0.5f;
        var pitch = UnityEngine.Random.Range(0.95f, 1.05f) + (knock && set != null && !set.Tiered ? 0.08f * sound.Volume : 0f);
        GameAudio.PlayBoard(set, sound.Volume, pitch, sound.Pan, strength);
    }

    // The piece breaks and goes from view (once). It still falls on,
    // unseen, to be counted out (DeathTrigger); a guest's goes when the
    // host's turn result says so.
    public static void Break(char id)
    {
        var gameManager = GameManager.manager;
        if (gameManager == null) return;
        var piece = gameManager.gamePieceScripts.Find(p => p != null && p.Manager.pieceID == id);
        if (piece == null || !piece.gameObject.activeInHierarchy) return;
        var renderers = piece.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0 || !renderers[0].enabled) return;
        PieceShatter.Break(piece);
        foreach (var renderer in renderers) renderer.enabled = false;
    }
}
