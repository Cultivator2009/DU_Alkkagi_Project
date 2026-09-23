using UnityEngine;

// On the janggi board's hinge colliders. A piece crossing a hinge should hop
// a little and carry on; left alone, the physics treats the hinge's curve
// like a ski jump and a hard shot leaves the board. So while a piece touches
// the hinge its upward speed is capped: a hop of well under a piece's height.
// Only the physics authority simulates, so kinematic guest pieces are left be.
public class HingeBump : MonoBehaviour
{
    public float maxHopSpeed = 0.4f;

    private void OnCollisionEnter(Collision collision) => Cap(collision.rigidbody);
    private void OnCollisionStay(Collision collision) => Cap(collision.rigidbody);
    private void OnCollisionExit(Collision collision) => Cap(collision.rigidbody);

    private void Cap(Rigidbody body)
    {
        if (body == null || body.isKinematic) return;
        var velocity = body.linearVelocity;
        if (velocity.y > maxHopSpeed) body.linearVelocity = new Vector3(velocity.x, maxHopSpeed, velocity.z);
    }
}
