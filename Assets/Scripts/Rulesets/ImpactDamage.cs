using System.Collections.Generic;
using UnityEngine;

// A battle of health: the knocks this piece takes while a shot (or the
// edge giving way) plays out - from another piece, or a wall - cost health
// by how hard they are (GameManager.DamageFor, the contact's impulse). The
// piece the shot was flicked with takes none. Both pieces of a knock hear
// it, so each takes its own share. On the physics authority only: a guest's
// pieces are kinematic and never collide, and the host's word comes over.
[RequireComponent(typeof(Rigidbody))]
public class ImpactDamage : MonoBehaviour
{
    private Rigidbody body;
    private GamePieceManager manager;
    // A chess piece is several colliders: one knock can arrive as several
    // collider pairs at once. Once per other body (or wall) at a time.
    private readonly Dictionary<Object, float> lastKnock = new Dictionary<Object, float>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        manager = GetComponent<GamePieceManager>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        var gameManager = GameManager.manager;
        var turns = gameManager != null ? gameManager.TurnController : null;
        if (body.isKinematic || turns == null || manager.isDestroyed || turns.State != GameManager.GameState.ProcessingTurn) return;
        if (turns.ShotPieceId == manager.pieceID) return;
        var other = (Object)collision.rigidbody;
        if (other == null)
        {
            if (collision.collider.name != BoardVariant.WallName) return; // the board itself, or a hinge
            other = collision.collider;
        }
        if (lastKnock.TryGetValue(other, out var at) && Time.time - at < 0.1f) return;
        var damage = gameManager.DamageFor(collision.impulse.magnitude);
        if (damage <= 0) return;
        lastKnock[other] = Time.time;
        gameManager.Damage(manager, damage, collision.GetContact(0).point);
    }
}
