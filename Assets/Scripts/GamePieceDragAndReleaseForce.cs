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
    // A flick is the impulse a go stone of referenceMass gets. Other masses
    // leave at speed / (mass / referenceMass)^massExponent: 1 would give every
    // piece the same momentum (the janggi general left so slowly it hardly
    // moved what it hit), 0 the same speed. Between, like a real finger: a
    // heavy piece goes a little slower but hits harder.
    public float referenceMass = 3f;
    [Range(0f, 1f)] public float massExponent = 0.25f;
    // Upward speed limit (m/s). The flick itself is level, but a piece that
    // leans - on another piece, or over a board hinge - is pushed up by
    // whatever it rests on, and a hard shot launched it up to 2 units high.
    // Capped, the hop stays under a piece's height.
    public float maxRiseSpeed = 0.4f;

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
                else
                {
                    if (BoardSounds.Instance != null) BoardSounds.Instance.PlayLocalFlick(transform.position, AimPower);
                    OnFlickRequested?.Invoke(force);
                }
                isSelected = false;
                isDragging = false;
            }
            AimPower = 0;
        }
        LimitRise();
        UpdateSettleState();
    }

    // Kinematic pieces (network guest, placement) are moved, not simulated.
    private void LimitRise()
    {
        if (rb.isKinematic) return;
        var velocity = rb.linearVelocity;
        if (velocity.y > maxRiseSpeed) rb.linearVelocity = new Vector3(velocity.x, maxRiseSpeed, velocity.z);
    }

    // Back to choosing a piece; TurnController sees isCancelled and waits
    // for input again.
    public void Cancel()
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
        var launch = flickForce / referenceMass * Mathf.Pow(referenceMass / rb.mass, massExponent);
        rb.AddForce(launch, ForceMode.VelocityChange);
        if (BoardSounds.Instance != null) BoardSounds.Instance.Emit(BoardSound.Flick, launch.magnitude, rb.position, GetComponent<GamePieceManager>().playerIndex);

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
        // Physics picking ignores the UI: a click on a menu or button over
        // the board would otherwise also pick up the piece under it.
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        if (Cursor.lockState == CursorLockMode.Locked) return; // looking around: the cursor is hidden
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

