using System;
using UnityEngine;
// https://docs.unity3d.com/ScriptReference/Camera.ScreenToWorldPoint.html

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(LineRenderer))]
public class GamePieceDragAndReleaseForce : MonoBehaviour
{
    // A full-power flick. 50 until friction became real (improved patch
    // friction, half what PhysX's default gave): 50 / sqrt(2) sends every
    // piece as far as before, a little slower.
    public float maxForce = 35.36f;
    // The pull that reads as 100% power, as a share of the screen's height:
    // the same hand movement whatever the zoom, board or camera distance.
    // (It was a board unit, a third of the screen at the home view - too
    // far to reach from a piece near the screen's edge without zooming out.)
    public float fullPullScreen = 0.18f;
    // Holding FineAim, the pull follows the mouse this much slower.
    public float fineAimFactor = 0.25f;
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
    private int powerNotch; // tenths of the pull's power reached so far, for the ratchet tick
    private bool pulling;   // isDragging as of the last frame: a new pull starts at the cursor
    private Vector2 pullScreen; // where the pull is held, on the screen: the cursor, slowed by FineAim
    private Vector2 lastMouse;

    private Rigidbody rb;
    private GamePieceManager manager;
    private LineRenderer lr;
    private Camera mainCam;
    private Vector3 mousePosInput;
    private Vector3 startPos;
    private Vector3 endPos;
    private Vector3 force;
    private Plane plane;
    private Ray ray;
    public bool isSelected = false;
    public int PickRank { get; private set; } // among the pieces under the last click, 0 = nearest
    public bool isDragging = false;
    public bool isOnfire = false;
    public bool isCancelled = false;

    public bool isGamePieceMoving = false;
    public bool IsSettled => !isGamePieceMoving;

    // Read by AimIndicator while this piece is being dragged.
    public float AimPower { get; private set; }         // 0..1
    public Vector3 AimDirection { get; private set; }   // shot direction on the board plane (unit, or zero)
    public Vector3 DragPoint => endPos;                 // where the pull is held, on the board plane

    // Its other components, looked up once: they're asked for every frame.
    public GamePieceManager Manager => manager != null ? manager : manager = GetComponent<GamePieceManager>();
    public Rigidbody Body => rb != null ? rb : rb = GetComponent<Rigidbody>();

    // Where the pull is measured from and the aim drawn round: the centre of
    // mass (where the flick acts), at the pull's level. Over the foot of
    // anything standing; partway along a chess piece lying down, whose foot
    // is off to one end.
    public Vector3 AimOrigin
    {
        get
        {
            var centre = Body.worldCenterOfMass;
            return new Vector3(centre.x, transform.position.y, centre.z);
        }
    }

    // Off the board until the shot is over, when it comes back (a battle of
    // health, HealthRuleset). Warped: put back since the host last told the
    // guests where it is, so they jump it there (NetworkMatchBridge).
    public bool IsParked { get; private set; }
    public bool Warped { get; set; }

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
            // Get the end position of the drag in the world space
            startPos = AimOrigin;

            // The held point moves with the mouse, slower while FineAim is
            // held. Off the window the pull holds where it was.
            var mouse = (Vector2)mousePosInput;
            if (!pulling) pullScreen = lastMouse = mouse;
            if (Pointer.OnScreen) pullScreen += (mouse - lastMouse) * (KeyBindings.Held(GameAction.FineAim) ? fineAimFactor : 1f);
            pullScreen = new Vector2(Mathf.Clamp(pullScreen.x, 0, Screen.width - 1), Mathf.Clamp(pullScreen.y, 0, Screen.height - 1));
            lastMouse = mouse;
            ray = mainCam.ScreenPointToRay(pullScreen);
            // https://docs.unity3d.com/ScriptReference/Physics.Raycast.html
            if (plane.Raycast(ray, out float dist)) endPos = ray.GetPoint(dist);

            // Slingshot: the shot goes opposite the pull, as hard as the pull
            // is long on the screen.
            var pull = startPos - endPos;
            pull.y = 0;
            var pullLength = ((Vector2)mainCam.WorldToScreenPoint(startPos) - pullScreen).magnitude;
            AimPower = Mathf.Clamp01(pullLength / (fullPullScreen * Screen.height));
            AimDirection = pull.sqrMagnitude > 1e-6f ? pull.normalized : Vector3.zero;

            // A soft ratchet: a tick at each tenth of power pulled, higher as it grows.
            var notch = Mathf.FloorToInt(AimPower * 10);
            if (notch > powerNotch) GameAudio.PlayInterface(GameAudio.Bank.notch, 0.3f, 0.85f + 0.06f * notch);
            powerNotch = notch;
        }
        else powerNotch = 0;
        pulling = isDragging;
        // https://docs.unity3d.com/ScriptReference/Input.GetMouseButtonDown.html
        if (isDragging && KeyBindings.Down(GameAction.CancelAim)) Cancel();
        // Let go: Unity's OnMouseUp would go to the piece the click landed
        // on, which needn't be the one picked (OnMouseDown).
        if (isDragging && Input.GetMouseButtonUp(0)) isOnfire = true;

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
        if (isDragging) GameAudio.PlayInterface(GameAudio.Bank.cancel, 0.5f);
        isSelected = false;
        isDragging = false;
        isCancelled = true;
        AimPower = 0;
    }

    public void Park()
    {
        isDragging = false;
        isSelected = false;
        AimPower = 0;
        lowVelocityFrameCount = 0;
        isGamePieceMoving = false; // it counts as stopped while it waits
        IsParked = true;
        gameObject.SetActive(false);
    }

    public void Unpark(Vector3 position, Quaternion rotation)
    {
        gameObject.SetActive(true);
        transform.SetPositionAndRotation(position, rotation);
        Body.position = position;
        Body.rotation = rotation;
        if (!Body.isKinematic)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }
        IsParked = false;
        Warped = true;
    }

    // Only ever called on the authoritative simulation (local single-player,
    // or the network host applying its own input or a guest's FlickCommand).
    public void ApplyFlick(Vector3 flickForce)
    {
        if (flickForce.magnitude > maxForce) flickForce = flickForce.normalized * maxForce;
        var launch = flickForce / referenceMass * Mathf.Pow(referenceMass / Body.mass, massExponent);
        Body.AddForce(launch, ForceMode.VelocityChange);
        if (BoardSounds.Instance != null) BoardSounds.Instance.Emit(BoardSound.Flick, launch.magnitude, Body.position, Manager.playerIndex);

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

    // Unity sends this to whichever piece is first under the click. Every
    // piece under it is marked, nearest first, and PieceSelector takes the
    // nearest one that may be moved: a piece of your own under someone
    // else's can still be picked up.
    private void OnMouseDown()
    {
        // Physics picking ignores the UI: a click on a menu or button over
        // the board would otherwise also pick up the piece under it.
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        if (CameraRig.Busy) return; // no aiming while the view moves
        var under = PiecePicker.UnderCursor(mainCam);
        for (var i = 0; i < under.Count; i++)
        {
            under[i].isSelected = true;
            under[i].PickRank = i;
        }
    }
}

