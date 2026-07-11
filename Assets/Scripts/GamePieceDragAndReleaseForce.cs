using UnityEngine;
// https://docs.unity3d.com/ScriptReference/Camera.ScreenToWorldPoint.html

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(LineRenderer))]
public class GamePieceDragAndReleaseForce : MonoBehaviour
{
    public float forceMultiplier = 20f;
    public float maxForce = 50f;
    public float settleVelocityThreshold = 0.05f;
    public int settleFrameThreshold = 5;

    private int lowVelocityFrameCount = 0;

    private Rigidbody rb;
    private LineRenderer lr;
    private Camera mainCam;
    private Vector3 mousePosInput;
    private Vector3 startPos;
    private Vector3 endPos;
    private Vector3 force;
    private float lrDist;
    private Plane plane;
    private Ray ray;
    public bool isSelected = false;
    public bool isDragging = false;
    public bool isOnfire = false;
    public bool isCancelled = false;

    public bool isGamePieceMoving = false;
    public bool IsSettled => !isGamePieceMoving;


    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        lr = GetComponent<LineRenderer>();
        mainCam = Camera.main;
        lr.enabled = false;
        // https://docs.unity3d.com/ScriptReference/Plane-ctor.html
        plane = new Plane(Vector3.up, 0);
    }
    private void Update()
    {
        if (isDragging)
        {
            // drag based on local rotation
            // drag with 0 degree TODO
            // plane.SetNormalAndPosition(transform.up,transform.position);
            plane.SetNormalAndPosition(Vector3.up,transform.position);
            mousePosInput = Input.mousePosition;
            ray = mainCam.ScreenPointToRay(mousePosInput);

            // Get the end position of the drag in the world space
            startPos = transform.position;

            // https://docs.unity3d.com/ScriptReference/Physics.Raycast.html
            if (plane.Raycast(ray, out float dist)) endPos = ray.GetPoint(dist);
            // endPos = mainCam.ScreenToWorldPoint(new Vector3(mousePosInput.x, mousePosInput.y, mainCam.transform.position.y));
            // endPos.y = transform.position.y;

            // Update the positions of the line renderer
            lr.enabled = true;
            lrDist = Vector3.Distance(startPos, endPos);
            // Debug.Log(lrDist);
            lr.SetPosition(0, startPos);
            lr.SetPosition(1, endPos);
        }
        // https://docs.unity3d.com/ScriptReference/Input.GetMouseButtonDown.html
        if (isDragging && Input.GetMouseButtonDown(1))
        {
            isSelected = false;
            isDragging = false;
            isCancelled = true;
            lr.enabled = false;
        }

        // Debug lines
        // Debug.Log(mainCam.transform.position.y);
    }
    private void FixedUpdate()
    {
        if (isOnfire)
        {
            // Calculate the force vector
            force = (startPos - endPos) * forceMultiplier;
            // force.y = 0;
            if (force.magnitude > maxForce)force = force.normalized * maxForce;
            rb.AddForce(force,ForceMode.Impulse);
            force=new Vector3(0,0,0);
            isOnfire=false;
            isSelected = false;
            // End dragging
            isDragging = false;
            // Disable the line renderer
            lr.enabled = false;
        }
        UpdateSettleState();
    }

    private void UpdateSettleState()
    {
        bool isSlow = rb.velocity.sqrMagnitude < settleVelocityThreshold * settleVelocityThreshold &&
                      rb.angularVelocity.sqrMagnitude < settleVelocityThreshold * settleVelocityThreshold;
        if (isSlow)
        {
            lowVelocityFrameCount++;
            if (lowVelocityFrameCount >= settleFrameThreshold) isGamePieceMoving = false;
        }
        else
        {
            lowVelocityFrameCount = 0;
            isGamePieceMoving = true;
        }
    }

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
}

