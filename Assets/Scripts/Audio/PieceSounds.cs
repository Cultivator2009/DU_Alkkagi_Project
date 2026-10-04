using System.Collections.Generic;
using UnityEngine;

// One piece's knocks, hinge hits, fall, break and (a chess piece's) topples, heard
// on the physics authority and passed to BoardSounds. On a network guest the
// pieces are kinematic: nothing collides and this stays quiet (the host's
// sounds come over).
[RequireComponent(typeof(Rigidbody))]
public class PieceSounds : MonoBehaviour
{
    public float minHitSpeed = 0.2f;
    public bool knocksBoard;         // a chess piece or gonggi stone: coming down on the board is heard too
    public float minToppleSpeed = 0.5f;
    public float fallHeight = -0.1f; // below the board surface: it went over the edge
    public float shatterHeight = -0.2f; // a little further down, still beside the edge in view: it breaks (PieceShatter)

    private Rigidbody body;
    private char id;
    private bool fallen;
    private bool shattered;
    // A go stone is eight colliders, so one knock can arrive as several
    // collider pairs at once: one sound per other piece (or hinge) at a time.
    private readonly Dictionary<Collider, float> lastHit = new Dictionary<Collider, float>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        id = GetComponent<GamePieceManager>().pieceID;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (body.isKinematic || BoardSounds.Instance == null) return;
        // A hit that continuous collision caught inside one step (a hard
        // shot at a piece close by) comes with no relative velocity at all;
        // how fast the two part afterwards measures it just as well.
        var otherVelocity = collision.rigidbody != null ? collision.rigidbody.linearVelocity : Vector3.zero;
        var speed = Mathf.Max(collision.relativeVelocity.magnitude, (body.linearVelocity - otherVelocity).magnitude);
        if (speed < minHitSpeed) return;

        var other = collision.rigidbody != null ? collision.rigidbody.GetComponent<PieceSounds>() : null;
        BoardSound kind;
        Collider key;
        if (other != null)
        {
            if (other.GetEntityId() < GetEntityId()) return; // the pair's other half plays it
            kind = BoardSound.Hit;
            key = collision.rigidbody.GetComponentInChildren<Collider>();
        }
        else if (collision.collider.name == "Bump")
        {
            kind = BoardSound.Hinge;
            key = collision.collider;
        }
        else if (collision.collider.name == BoardVariant.WallName)
        {
            kind = BoardSound.Wall;
            key = collision.collider;
        }
        else if (knocksBoard)
        {
            // How hard it came down, not how fast it slides: a flicked piece
            // skipping along the board isn't falling over.
            speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal));
            if (speed < minToppleSpeed) return;
            kind = BoardSound.Topple;
            key = collision.collider;
        }
        else return; // the board itself, under a flat piece

        if (lastHit.TryGetValue(key, out var at) && Time.time - at < 0.1f) return;
        lastHit[key] = Time.time;
        BoardSounds.Instance.Emit(kind, speed, collision.GetContact(0).point);
    }

    private void FixedUpdate()
    {
        if (shattered || body.isKinematic || BoardSounds.Instance == null) return;
        var y = body.position.y;
        if (!fallen && y <= fallHeight)
        {
            fallen = true;
            BoardSounds.Instance.Emit(BoardSound.Fall, 0f, body.position);
            if (GameManager.manager != null) GameManager.manager.PieceFalling(GetComponent<GamePieceManager>());
        }
        if (y > shatterHeight) return;
        shattered = true;
        BoardSounds.Instance.Emit(BoardSound.Shatter, 0f, body.position, piece: id);
    }
}
