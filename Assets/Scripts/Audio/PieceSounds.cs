using System.Collections.Generic;
using UnityEngine;

// One piece's knocks, hinge hits and fall, heard on the physics authority
// and passed to BoardSounds. On a network guest the pieces are kinematic:
// nothing collides and this stays quiet (the host's sounds come over).
[RequireComponent(typeof(Rigidbody))]
public class PieceSounds : MonoBehaviour
{
    public float minHitSpeed = 0.3f;
    public float fallHeight = -0.1f; // below the board surface: it went over the edge

    private Rigidbody body;
    private bool fallen;
    // A go stone is eight colliders, so one knock can arrive as several
    // collider pairs at once: one sound per other piece (or hinge) at a time.
    private readonly Dictionary<Collider, float> lastHit = new Dictionary<Collider, float>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
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
        else return; // the board itself

        if (lastHit.TryGetValue(key, out var at) && Time.time - at < 0.1f) return;
        lastHit[key] = Time.time;
        BoardSounds.Instance.Emit(kind, speed, collision.GetContact(0).point);
    }

    private void FixedUpdate()
    {
        if (fallen || body.isKinematic || body.position.y > fallHeight || BoardSounds.Instance == null) return;
        fallen = true;
        BoardSounds.Instance.Emit(BoardSound.Fall, 0f, body.position);
    }
}
