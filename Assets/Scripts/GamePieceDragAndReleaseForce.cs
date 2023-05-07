using UnityEngine;
// https://docs.unity3d.com/ScriptReference/Camera.ScreenToWorldPoint.html

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(LineRenderer))]
public class GamePieceDragAndReleaseForce : MonoBehaviour
{
    public float forceMultiplier = 20f;
    public float maxForce = 50f;

    private Rigidbody rb;
    private LineRenderer lr;
    private Camera mainCam;
    private Vector3 startPos;
    private Vector3 endPos;
    private Vector3 force;
    private float lrDist;
    public bool isSelected = false;
    public bool isDragging = false;
    public bool isOnfire = false;

    public bool isGamePieceMoving = false;


    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        lr = GetComponent<LineRenderer>();
        mainCam = Camera.main;
        lr.enabled = false;
    }
    private void Update()
    {
        // https://docs.unity3d.com/ScriptReference/Input.GetMouseButtonDown.html
        if (isDragging && Input.GetMouseButtonDown(1))
        {
            isSelected = false;
            isDragging = false;
            lr.enabled = false;
        }
    }
    private void FixedUpdate()
    {
        if (isOnfire)
        {
            // Calculate the force vector
            force = (startPos - endPos) * forceMultiplier;
            force.y = 0f;
            if (force.magnitude > maxForce)force = force.normalized * maxForce;
            Debug.Log(force);
            rb.AddForce(force,ForceMode.Impulse);
            Debug.Log(force.magnitude);
            force=new Vector3(0,0,0);
            isOnfire=false;
            isSelected = false;
            // End dragging
            isDragging = false;
            // Disable the line renderer
            lr.enabled = false;
        }
        if (isDragging)
        {
            // Get the end position of the drag in the world space
            startPos = transform.position;
            endPos = mainCam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, mainCam.transform.position.y));
            // Debug.Log(endPos);

            // Update the positions of the line renderer
            lr.enabled = true;
            lrDist = Vector3.Distance(startPos, endPos);
            Debug.Log(lrDist);
            lr.SetPosition(0, startPos);
            lr.SetPosition(1, endPos);
        }
    }

    // private void FixedUpdate()
    // {
    //     if (isDragging)
    //     {
    //         // Calculate the force vector
    //         Vector3 force = (startPos - endPos) * forceMultiplier;
    //         force.y = 0f;

    //         // Limit the force magnitude
    //         if (force.magnitude > maxForce)
    //         {
    //             force = force.normalized * maxForce;
    //         }

    //         // Apply the force to the rigidbody
    //         rb.AddForce(force);
    //     }
    // }

    private void OnMouseDown()
    {
        isSelected = true;
    }

    private void OnMouseUp()
    {
        if (isDragging)
        {
            isOnfire = true;
        }
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