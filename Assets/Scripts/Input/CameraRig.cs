using System.Linq;
using Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;

// The board camera during a match. The main view looks straight down. Pan
// (the middle button, held) drags it across the board: the point grabbed
// stays under the cursor, and the middle of the view stays on the inner
// part of the board, so most of the board is always in sight; the
// movement keys (WASD, the arrows) slide it too. The wheel brings it a
// little closer or takes it further back: back out, a full pull is a
// shorter reach of the mouse, so a piece near the screen's edge can still
// be pulled all the way without the cursor stopping at the edge. The
// camera-view key swaps to a free camera, flown round the room - the
// mouse turns it, WASD moves it, Q and E down and up, Shift faster - and
// back to the main view as it's pressed again (or the game is paused).
// Reset view (its key, or the HUD button) glides the main view back.
// A board too big to frame whole (the landscape) is framed in part, and
// panned to its edges; the view glides to each side's pieces as its turn
// comes, and follows a shot while it runs (unless the player moved it).
// While the camera moves - a pan, or the free camera - the board takes no
// clicks, and a pull in progress is dropped.
public class CameraRig : MonoBehaviour
{
    public static CameraRig Instance { get; private set; }
    public static bool Busy => Instance != null && (Instance.panning || Instance.flying);

    public float reach = 0.5f;         // how far the middle of the view may go, as a share of the way from the board's centre to its edge
    public float resetSharpness = 12f; // how fast Reset view glides back (1/s)
    public float minZoom = 0.94f;      // the main view's distance to the board, as a share of home: closest (any closer and the board runs under the HUD)
    public float maxZoom = 1.35f;      // and furthest
    public float zoomStep = 0.05f;     // per wheel notch
    public float zoomSharpness = 14f;  // how fast the view follows the wheel (1/s)
    // The main view's with standing pieces (chess), narrower and from further
    // back so the board is framed the same: seen from straight above at the
    // scene's 60 degrees, a king by the edge leaned a whole square out over it.
    public float tallFieldOfView = 35f;
    // Under the room's rafters (RoomBuilder): the view stays under them,
    // zoomed all the way out too, a wider lens making up the rest.
    public float ceiling = 12.5f;
    public Vector2 roomHalf = new Vector2(22f, 16f); // the free camera stays inside the walls
    public float keyPan = 0.8f;        // the movement keys' pan, in view heights a second
    public float flySpeed = 3f;        // the free camera, units a second (Shift: three times)
    public float lookSpeed = 0.12f;    // degrees per pixel the mouse moves

    private Transform mainView;  // the main virtual camera
    private float mainFieldOfView;
    private CinemachineVirtualCamera main;
    private CinemachineVirtualCamera fly; // the free camera, over the main view while on
    private Transform pivot;     // the middle of the main view, on the board
    private Vector3 mainHome;
    private Vector3 builtHome;   // the main view as the scene has it, before Frame
    private Quaternion builtRotation;
    private float builtFieldOfView;
    private Bounds surface;
    private bool tallPieces;
    private float facing;        // the seat it's seen from (Face)
    private Vector3 pivotHome;
    private Rect limits;         // where the pivot may go (x, z)
    private float boardHeight;
    private Vector3 offset;      // from home, level
    private float homeDistance;  // from the main view to the board, along the view
    private float zoom = 1f;
    private float zoomTarget = 1f;
    private Vector3 grabbed;     // the board point held under the cursor
    private bool panning;
    private bool flying;
    private float yaw, pitch;    // the free camera's
    private bool gliding;        // toward glideTo: Reset view, or (on a big board) a turn's pieces or a shot
    private Vector3 glideTo;
    private float glideSharpness;
    private Vector3 home;        // where Reset view goes: the middle, or on a big board this turn's pieces
    private bool big;            // too big to frame whole (BigView)
    private int focusedTurn = -1;
    private bool movedThisTurn;  // by the player: a big board's view stops following

    // A hard knock's shake (Shake): the most, in board units, and how long it
    // takes to settle, in real seconds.
    public float shakeAmplitude = 0.03f;
    public float shakeSeconds = 0.3f;
    public float shakeFrequency = 28f;
    private float shake;         // 1 at the knock, down to 0
    private Vector3 shaken;      // the view's offset by it, this frame

    public bool IsMoved => (offset - home).sqrMagnitude > 1e-6f || !Mathf.Approximately(zoomTarget, 1f);
    // What the home view frames: the board, or the middle of a big one.
    public Bounds Framed { get; private set; }

    // strength 0..1 (the hardest knock). Not while the player has it off
    // (GameSettings.ScreenShake). Real time: a slowed match shakes as fast.
    public static void Shake(float strength)
    {
        if (Instance == null || !GameSettings.ScreenShake) return;
        Instance.shake = Mathf.Max(Instance.shake, Mathf.Clamp01(strength));
    }

    // vcams: the scene's tagged virtual cameras; the main view is the plain
    // one with the highest priority, the free look's target is its pivot
    // (the orbit itself is put away: the free camera has its place).
    public void Init(GameObject[] vcams, Bounds surface, bool tallPieces = false)
    {
        Instance = this;
        main = vcams.Select(v => v.GetComponent<CinemachineVirtualCamera>()).Where(v => v != null).OrderByDescending(v => v.Priority).First();
        mainView = main.transform;
        mainFieldOfView = main.m_Lens.FieldOfView;
        var orbit = vcams.Select(v => v.GetComponent<CinemachineFreeLook>()).First(v => v != null);
        pivot = orbit.Follow;
        orbit.gameObject.SetActive(false);
        fly = new GameObject("FreeCamera").AddComponent<CinemachineVirtualCamera>();
        fly.transform.SetParent(transform, false);
        fly.Priority = main.Priority + 1;
        fly.gameObject.SetActive(false);
        builtHome = mainView.position;
        builtRotation = mainView.rotation;
        builtFieldOfView = mainFieldOfView;
        pivotHome = pivot.position;
        this.surface = surface;
        this.tallPieces = tallPieces;
        Frame();
    }

    // Seen from a side's edge (BoardSetup.SeatAngle): that side's pieces at
    // the bottom of the screen, as the first side's are from the scene's
    // own view. Online, each screen from its own side.
    public void Face(float seatAngle)
    {
        if (Mathf.Approximately(facing, seatAngle)) return;
        facing = seatAngle;
        Frame();
        MoveTo(offset);
    }

    // The main view's home, turned to the side it faces and framing the
    // board (or a big board's middle) from as far back as it takes.
    private void Frame()
    {
        var turn = Quaternion.Euler(0, -facing, 0);
        var center = surface.center;
        var pivotLevel = new Vector3(pivotHome.x, 0, pivotHome.z);
        mainHome = pivotLevel + turn * (builtHome - pivotLevel);
        mainView.rotation = turn * builtRotation;
        mainFieldOfView = builtFieldOfView;
        main.m_Lens.FieldOfView = mainFieldOfView;
        // Across the screen and up it, as the view now lies.
        var sideways = Mathf.Abs(Mathf.Sin(facing * Mathf.Deg2Rad)) > 0.7f;
        big = Mathf.Max(surface.size.x, surface.size.z) > BigView * 1.2f;
        var framedSize = big ? new Vector3(Mathf.Min(surface.size.x, BigView), surface.size.y, Mathf.Min(surface.size.z, BigView)) : surface.size;
        Framed = new Bounds(center, framedSize);
        var framed = sideways ? new Vector3(framedSize.z, framedSize.y, framedSize.x) * 0.5f : framedSize * 0.5f;
        // A big board's view goes far enough that its edges come into sight.
        var half = big ? Vector3.Max(Vector3.zero, surface.extents - framedSize * 0.5f * 0.55f) : surface.extents * reach;
        limits = Rect.MinMaxRect(center.x - half.x, center.z - half.z, center.x + half.x, center.z + half.z);
        boardHeight = surface.max.y;
        // The scene frames a board FramedDepth deep; another is framed the
        // same, from further back or closer in, so it fills the same height.
        var distance = (boardHeight - mainHome.y) / Mathf.Min(-0.01f, mainView.forward.y);
        var fit = framed.z * 2 / FramedDepth;
        if (tallPieces)
        {
            fit *= Mathf.Tan(mainFieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(tallFieldOfView * 0.5f * Mathf.Deg2Rad);
            mainFieldOfView = tallFieldOfView;
            main.m_Lens.FieldOfView = tallFieldOfView;
        }
        homeDistance = distance * fit;
        // A narrow screen (5:4, 4:3): back far enough that the board leaves
        // the HUD its columns either side (HudRoom, in screen heights).
        var aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;
        var across = aspect / 2 - HudRoom;
        if (across > 0.1f) homeDistance = Mathf.Max(homeDistance, framed.x / (2 * Mathf.Tan(mainFieldOfView * 0.5f * Mathf.Deg2Rad) * across));
        var highest = (ceiling - boardHeight) / Mathf.Max(0.01f, -mainView.forward.y) / maxZoom;
        if (homeDistance > highest)
        {
            var tan = Mathf.Tan(mainFieldOfView * 0.5f * Mathf.Deg2Rad) * homeDistance / highest;
            mainFieldOfView = 2 * Mathf.Atan(tan) * Mathf.Rad2Deg;
            main.m_Lens.FieldOfView = mainFieldOfView;
            homeDistance = highest;
        }
        mainHome -= mainView.forward * (homeDistance - distance);
        mainView.position = mainHome;
    }

    private const float FramedDepth = 3f;
    private const float BigView = 4.6f;  // how deep a part of a big board the home view frames: zoomed right out, still not the whole
    private const float HudRoom = 0.25f; // a side panel's column, three or four sides (MainGameUIController.Arrange)

    private void OnDestroy()
    {
        if (flying) Cursor.lockState = CursorLockMode.None;
        if (Instance == this) Instance = null;
    }

    public void ResetView()
    {
        panning = false;
        Glide(home, resetSharpness);
        zoomTarget = 1f;
    }

    private void Glide(Vector3 to, float sharpness)
    {
        glideTo = Clamped(to);
        glideSharpness = sharpness;
        gliding = (offset - glideTo).sqrMagnitude > 1e-6f;
    }

    // How far the board reaches either side of the screen's middle in the
    // home view, in screen heights. The view keeps its vertical field of
    // view, so this is the same on every aspect ratio; the HUD fits its side
    // columns to what's left.
    public float HomeHalfWidth(Bounds board)
    {
        var tan = Mathf.Tan(mainFieldOfView * 0.5f * Mathf.Deg2Rad);
        var toView = Quaternion.Inverse(mainView.rotation);
        var widest = 0f;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? board.min.x : board.max.x, (i & 2) == 0 ? board.min.y : board.max.y, (i & 4) == 0 ? board.min.z : board.max.z);
            var local = toView * (corner - mainHome);
            widest = Mathf.Max(widest, Mathf.Abs(local.x) / (local.z * 2 * tan));
        }
        return widest;
    }

    private void Update()
    {
        if (Time.timeScale > 0 && KeyBindings.Down(GameAction.CameraView)) Fly(!flying);
        else if (flying && Time.timeScale <= 0) Fly(false);
        if (flying)
        {
            Steer();
            return;
        }

        if (!panning && Time.timeScale > 0 && KeyBindings.Down(GameAction.PanView) && !PointerOverUI() && BoardPoint(out grabbed))
        {
            panning = true;
            gliding = false;
            movedThisTurn = true;
            DropPulls();
        }
        else if (panning && !KeyBindings.Held(GameAction.PanView))
            panning = false;
        if (panning && BoardPoint(out var under))
        {
            MoveTo(offset + grabbed - under);
            BoardPoint(out grabbed); // the same point, unless the edge stopped the view
        }

        // The movement keys, not while a piece is pulled (its pull is read
        // off the board under the cursor).
        var keys = new Vector3(Axis(KeyCode.D, KeyCode.RightArrow, KeyCode.A, KeyCode.LeftArrow), 0, Axis(KeyCode.W, KeyCode.UpArrow, KeyCode.S, KeyCode.DownArrow));
        if (keys != Vector3.zero && !panning && !AimCursor.Held && Time.timeScale > 0)
        {
            gliding = false;
            movedThisTurn = true;
            var depth = homeDistance * zoom * Mathf.Tan(mainFieldOfView * 0.5f * Mathf.Deg2Rad) * 2;
            MoveTo(offset + Quaternion.Euler(0, -facing, 0) * keys.normalized * (keyPan * depth * Time.unscaledDeltaTime));
        }

        // Not over the HUD (a menu), or paused.
        var scroll = Input.mouseScrollDelta.y;
        if (scroll != 0 && Time.timeScale > 0 && !PointerOverUI())
            zoomTarget = Mathf.Clamp(zoomTarget - scroll * zoomStep, minZoom, maxZoom);
        if (zoom != zoomTarget)
        {
            zoom = Mathf.Lerp(zoom, zoomTarget, 1 - Mathf.Exp(-zoomSharpness * Time.unscaledDeltaTime));
            if (Mathf.Abs(zoom - zoomTarget) < 1e-4f) zoom = zoomTarget;
            MoveTo(offset);
        }

        if (KeyBindings.Down(GameAction.ResetView)) ResetView();
        if (big) Follow();
        if (gliding)
        {
            MoveTo(Vector3.Lerp(offset, glideTo, 1 - Mathf.Exp(-glideSharpness * Time.unscaledDeltaTime)));
            if ((offset - glideTo).sqrMagnitude < 1e-6f)
            {
                MoveTo(glideTo);
                gliding = false;
            }
        }
    }

    // A big board: to the pieces of the side whose turn has come, the
    // middle of them; through a shot, after the fastest piece.
    private void Follow()
    {
        var game = GameManager.manager;
        var turns = game != null ? game.TurnController : null;
        if (turns == null || !turns.HasStarted) return;
        if (turns.Turn != focusedTurn)
        {
            focusedTurn = turns.Turn;
            movedThisTurn = false;
            var side = turns.CurrentPlayerID;
            var mine = game.gamePieceScripts.Where(p => p != null && p.gameObject.activeInHierarchy && p.Manager.playerIndex == side && p.Body.position.y > -0.05f).ToList();
            if (mine.Count == 0) return;
            var middle = mine.Aggregate(Vector3.zero, (sum, p) => sum + p.Body.position) / mine.Count;
            home = Clamped(middle - pivotHome);
            Glide(home, 3f);
            return;
        }
        if (movedThisTurn || !turns.InMotion) return;
        GamePieceDragAndReleaseForce fastest = null;
        var top = 0.8f; // m/s: slower than this, nothing worth following
        foreach (var piece in game.gamePieceScripts)
        {
            if (piece == null || !piece.gameObject.activeInHierarchy) continue;
            var speed = piece.Body.linearVelocity.magnitude;
            if (speed <= top) continue;
            top = speed;
            fastest = piece;
        }
        if (fastest != null) Glide(fastest.Body.position - pivotHome, 2.5f);
    }

    // An offset from home as far as the view may go.
    private Vector3 Clamped(Vector3 to)
    {
        var at = pivotHome + new Vector3(to.x, 0, to.z);
        at.x = Mathf.Clamp(at.x, limits.xMin, limits.xMax);
        at.z = Mathf.Clamp(at.z, limits.yMin, limits.yMax);
        return at - pivotHome;
    }

    private static float Axis(KeyCode plus, KeyCode plus2, KeyCode minus, KeyCode minus2) =>
        (Input.GetKey(plus) || Input.GetKey(plus2) ? 1 : 0) - (Input.GetKey(minus) || Input.GetKey(minus2) ? 1 : 0);

    // Into the free camera from wherever the view is now, or back to the
    // main view (Cinemachine blends either way).
    private void Fly(bool on)
    {
        if (on == flying) return;
        flying = on;
        if (on)
        {
            DropPulls();
            panning = false;
            var seen = Camera.main != null ? Camera.main.transform : mainView;
            fly.transform.SetPositionAndRotation(seen.position, seen.rotation);
            var angles = seen.rotation.eulerAngles;
            yaw = angles.y;
            pitch = Mathf.DeltaAngle(0, angles.x);
            fly.m_Lens = main.m_Lens;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else Cursor.lockState = CursorLockMode.None;
        fly.gameObject.SetActive(on);
    }

    private void Steer()
    {
        Cursor.lockState = CursorLockMode.Locked; // a pull dropped as it began lets go of it
        var mouse = Input.mousePositionDelta;
        yaw += mouse.x * lookSpeed;
        pitch = Mathf.Clamp(pitch - mouse.y * lookSpeed, -80f, 89f);
        var turn = Quaternion.Euler(pitch, yaw, 0);
        var move = turn * new Vector3(Axis(KeyCode.D, KeyCode.RightArrow, KeyCode.A, KeyCode.LeftArrow), 0, Axis(KeyCode.W, KeyCode.UpArrow, KeyCode.S, KeyCode.DownArrow))
                   + Vector3.up * Axis(KeyCode.E, KeyCode.E, KeyCode.Q, KeyCode.Q);
        var fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 3f : 1f;
        var at = fly.transform.position + Vector3.ClampMagnitude(move, 1f) * (flySpeed * fast * Time.unscaledDeltaTime);
        at.x = Mathf.Clamp(at.x, -roomHalf.x, roomHalf.x);
        at.z = Mathf.Clamp(at.z, -roomHalf.y, roomHalf.y);
        at.y = Mathf.Clamp(at.y, boardHeight - 0.3f, ceiling);
        fly.transform.SetPositionAndRotation(at, turn);
    }

    // The shake on top of wherever the view is, easing out (the square of
    // what's left), on noise rather than a sine so it doesn't hum.
    private void LateUpdate()
    {
        if (shake <= 0 && shaken == Vector3.zero) return;
        shake = Mathf.Max(0, shake - Time.unscaledDeltaTime / shakeSeconds);
        var t = Time.unscaledTime * shakeFrequency;
        var amount = shakeAmplitude * shake * shake;
        var jitter = new Vector3(Mathf.PerlinNoise(t, 0.37f) - 0.5f, 0, Mathf.PerlinNoise(0.71f, t) - 0.5f) * (2 * amount);
        mainView.position += jitter - shaken;
        shaken = jitter;
    }

    private void MoveTo(Vector3 to)
    {
        offset = Clamped(to);
        var at = pivotHome + offset;
        mainView.position = mainHome + offset - mainView.forward * (homeDistance * (zoom - 1)) + shaken;
        pivot.position = at;
    }

    // Where the cursor meets the board as the main view sees it, from the
    // view's own pose: the camera itself may still be blending there.
    private bool BoardPoint(out Vector3 point)
    {
        point = default;
        var camera = Camera.main;
        if (camera == null) return false;
        var viewport = camera.ScreenToViewportPoint(Input.mousePosition);
        var tan = Mathf.Tan(mainFieldOfView * 0.5f * Mathf.Deg2Rad);
        var direction = mainView.rotation * new Vector3((viewport.x * 2 - 1) * tan * camera.aspect, (viewport.y * 2 - 1) * tan, 1);
        var ray = new Ray(mainView.position, direction);
        if (!new Plane(Vector3.up, new Vector3(0, boardHeight, 0)).Raycast(ray, out var distance)) return false;
        point = ray.GetPoint(distance);
        return true;
    }

    private static void DropPulls()
    {
        foreach (var piece in GameManager.manager.gamePieceScripts)
        {
            if (piece == null) continue;
            if (piece.isDragging) piece.Cancel();
            else piece.isSelected = false;
        }
    }

    private static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}
