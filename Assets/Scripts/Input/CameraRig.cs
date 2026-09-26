using System.Linq;
using Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;

// The board camera during a match. The main view looks straight down; the
// camera-view key (held) swaps to the free-look orbit. Pan (the middle button,
// held) drags the main view across the board: the point grabbed stays under
// the cursor, and the middle of the view stays on the inner part of the
// board, so most of the board is always in sight. The free look orbits that
// same middle, so after a pan it turns about what's in the centre of the
// screen. The wheel brings the main view a little closer or takes it
// further back: back out, a full pull is a shorter reach of the mouse, so a
// piece near the screen's edge can still be pulled all the way without the
// cursor stopping at the edge. Reset view (its key, or the HUD button)
// glides back from both. While the camera moves - a pan, or the free look -
// the board takes no clicks, and a pull in progress is dropped.
public class CameraRig : MonoBehaviour
{
    public static CameraRig Instance { get; private set; }
    public static bool Busy => Instance != null && (Instance.panning || Instance.freeLook);

    public float reach = 0.5f;         // how far the middle of the view may go, as a share of the way from the board's centre to its edge
    public float resetSharpness = 12f; // how fast Reset view glides back (1/s)
    public float minZoom = 0.94f;      // the main view's distance to the board, as a share of home: closest (any closer and the board runs under the HUD)
    public float maxZoom = 1.35f;      // and furthest
    public float zoomStep = 0.05f;     // per wheel notch
    public float zoomSharpness = 14f;  // how fast the view follows the wheel (1/s)

    private Transform mainView;  // the main virtual camera
    private float mainFieldOfView;
    private Transform pivot;     // what the free look orbits
    private Vector3 mainHome;
    private Vector3 pivotHome;
    private Rect limits;         // where the pivot may go (x, z)
    private float boardHeight;
    private Vector3 offset;      // from home, level
    private float homeDistance;  // from the main view to the board, along the view
    private float zoom = 1f;
    private float zoomTarget = 1f;
    private Vector3 grabbed;     // the board point held under the cursor
    private bool panning;
    private bool freeLook;
    private bool resetting;

    public bool IsMoved => offset.sqrMagnitude > 1e-6f || !Mathf.Approximately(zoomTarget, 1f);

    // vcams: the scene's tagged virtual cameras; the main view is the plain
    // one with the highest priority, the free look's target is its pivot.
    public void Init(GameObject[] vcams, Bounds surface)
    {
        Instance = this;
        var main = vcams.Select(v => v.GetComponent<CinemachineVirtualCamera>()).Where(v => v != null).OrderByDescending(v => v.Priority).First();
        mainView = main.transform;
        mainFieldOfView = main.m_Lens.FieldOfView;
        pivot = vcams.Select(v => v.GetComponent<CinemachineFreeLook>()).First(v => v != null).Follow;
        mainHome = mainView.position;
        pivotHome = pivot.position;
        var center = surface.center;
        var half = surface.extents * reach;
        limits = Rect.MinMaxRect(center.x - half.x, center.z - half.z, center.x + half.x, center.z + half.z);
        boardHeight = surface.max.y;
        homeDistance = (boardHeight - mainHome.y) / Mathf.Min(-0.01f, mainView.forward.y);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ResetView()
    {
        panning = false;
        resetting = offset.sqrMagnitude > 1e-6f;
        zoomTarget = 1f;
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
        var look = KeyBindings.Held(GameAction.CameraView);
        if (look != freeLook)
        {
            freeLook = look;
            mainView.gameObject.SetActive(!look);
            if (look) DropPulls();
        }

        if (!panning && !freeLook && Time.timeScale > 0 && KeyBindings.Down(GameAction.PanView) && !PointerOverUI() && BoardPoint(out grabbed))
        {
            panning = true;
            resetting = false;
            DropPulls();
        }
        else if (panning && (freeLook || !KeyBindings.Held(GameAction.PanView)))
            panning = false;
        if (panning && BoardPoint(out var under))
        {
            MoveTo(offset + grabbed - under);
            BoardPoint(out grabbed); // the same point, unless the edge stopped the view
        }

        // Not over the HUD (a menu), in the free look, or paused.
        var scroll = Input.mouseScrollDelta.y;
        if (scroll != 0 && !freeLook && Time.timeScale > 0 && !PointerOverUI())
            zoomTarget = Mathf.Clamp(zoomTarget - scroll * zoomStep, minZoom, maxZoom);
        if (zoom != zoomTarget)
        {
            zoom = Mathf.Lerp(zoom, zoomTarget, 1 - Mathf.Exp(-zoomSharpness * Time.unscaledDeltaTime));
            if (Mathf.Abs(zoom - zoomTarget) < 1e-4f) zoom = zoomTarget;
            MoveTo(offset);
        }

        if (KeyBindings.Down(GameAction.ResetView)) ResetView();
        if (resetting)
        {
            MoveTo(Vector3.Lerp(offset, Vector3.zero, 1 - Mathf.Exp(-resetSharpness * Time.unscaledDeltaTime)));
            if (offset.sqrMagnitude < 1e-6f)
            {
                MoveTo(Vector3.zero);
                resetting = false;
            }
        }
    }

    private void MoveTo(Vector3 to)
    {
        var at = pivotHome + new Vector3(to.x, 0, to.z);
        at.x = Mathf.Clamp(at.x, limits.xMin, limits.xMax);
        at.z = Mathf.Clamp(at.z, limits.yMin, limits.yMax);
        offset = at - pivotHome;
        mainView.position = mainHome + offset - mainView.forward * (homeDistance * (zoom - 1));
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
