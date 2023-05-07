using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class StopDetection : MonoBehaviour
{
    private Rigidbody rb;

    public bool isGamePieceMoving = false;
    private void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnCollisionStay(Collision collision)
    {
        if (rb.velocity.magnitude > 0.1f)
        {
            // Code to run when RigidBody moving
            isGamePieceMoving = true;
        }
        else if (rb.velocity.magnitude < 0.1f){
            isGamePieceMoving = false;
        }
    }
}