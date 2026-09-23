using System;
using UnityEngine;
// https://docs.unity3d.com/ScriptReference/Camera.ScreenToWorldPoint.html

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(LineRenderer))]
public class GamePieceDragAndReleaseForce : MonoBehaviour
{
    public float maxForce = 50f;
    // Pull-back distance (world units) that reads as 100% power. 1.0 keeps
    // the previous feel exactly: the scene used forceMultiplier 50 x
    // distance, capped at maxForce 50.
    public float maxDragDistance = 1f;
    // Releasing below this is treated as a cancel rather than a wasted turn.
    public float minShotPower = 0.03f;
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
    private Plane plane;
    private Ray ray;
    public bool isSelected = false;
    public bool isDragging = false;
    public bool isOnfire = false;
    public bool isCancelled = false;

    public bool isGamePieceMoving = false;
    public bool IsSettled => !isGamePieceMoving;

    // Read by AimIndicator while this piece is being dragged.
    public float AimPower { get; private set; }         // 0..1
    public Vector3 AimDirection { get; private set; }   // shot direction on the board plane (unit, or zero)
    public Vector3 DragPoint => endPos;                 // where the pull is held, on the board plane

    // True on a local single-player piece and on the network host (who always
    // simulates physics). False on a network guest, whose flicks are only
    // requests sent to the host instead of being applied locally.
    public bool isAuthority = true;
    public event Action<Vector3> OnFlickRequested;


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

            // Slingshot: the shot goes opposite the pull.
            var pull = startPos - endPos;
            pull.y = 0;
            AimPower = Mathf.Clamp01(pull.magnitude / maxDragDistance);
            AimDirection = pull.sqrMagnitude > 1e-6f ? pull.normalized : Vector3.zero;
        }
        // https://docs.unity3d.com/ScriptReference/Input.GetMouseButtonDown.html
        if (isDragging && KeyBindings.Down(GameAction.CancelAim)) Cancel();

        // Debug lines
        // Debug.Log(mainCam.transform.position.y);
    }
    private void FixedUpdate()
    {
        if (isOnfire)
        {
            isOnfire = false;
            if (AimPower < minShotPower)
            {
                Cancel();
            }
            else
            {
                force = AimDirection * (AimPower * maxForce);
                if (isAuthority) ApplyFlick(force);
                else OnFlickRequested?.Invoke(force);
                isSelected = false;
                isDragging = false;
            }
            AimPower = 0;
        }
        UpdateSettleState();
    }

    // Back to choosing a piece; TurnController sees isCancelled and waits
    // for input again.
    private void Cancel()
    {
        isSelected = false;
        isDragging = false;
        isCancelled = true;
        AimPower = 0;
    }

    // Only ever called on the authoritative simulation (local single-player,
    // or the network host applying its own input or a guest's FlickCommand).
    public void ApplyFlick(Vector3 flickForce)
    {
        if (flickForce.magnitude > maxForce) flickForce = flickForce.normalized * maxForce;
        rb.AddForce(flickForce, ForceMode.Impulse);

        // The impulse only shows up in linearVelocity after the next physics
        // step, so a resting piece would still read as settled for a frame
        // and TurnController could end the turn before anything moved. Count
        // it as moving until it has genuinely come to rest again.
        lowVelocityFrameCount = 0;
        isGamePieceMoving = true;
    }

    private void UpdateSettleState()
    {
        bool isSlow = rb.linearVelocity.sqrMagnitude < settleVelocityThreshold * settleVelocityThreshold &&
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

