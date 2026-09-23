using System.Collections.Generic;
using UnityEngine;

// GameScene's board. Spawns each side's stones from the inactive templates
// (the count is a match rule, so stones are no longer hand-placed in the
// scene), holds the preset layouts and the placement zones, and shows the
// placement phase while it runs.
public class BoardSetup : MonoBehaviour
{
    public const int MaxStones = 12;

    public GamePieceDragAndReleaseForce blackTemplate;
    public GamePieceDragAndReleaseForce whiteTemplate;

    // Children named "1".."12", each holding that many points: black's preset
    // layout for that stone count, editable in the scene. White uses the same
    // layout turned 180 degrees about the board center, so both sides get the
    // same shape from where they sit. Tools > Alkkagi > Reset spawn layouts
    // regenerates the defaults.
    public Transform layouts;

    // Black's placement zone on the board, in (x, z); white's is its mirror.
    // Keeps stones inside the rim and away from the center line.
    public Rect blackZone = new Rect(-1.35f, -1.35f, 2.7f, 1.05f);
    public float minSpacing = 0.22f; // center to center: stone diameter 0.2 plus a little air
    public Color zoneColor = new Color(0.18f, 0.14f, 0.10f, 0.10f);
    public Color activeZoneColor = new Color(0.70f, 0.19f, 0.16f, 0.22f);

    private readonly Dictionary<Rigidbody, CollisionDetectionMode> frozenBodies = new Dictionary<Rigidbody, CollisionDetectionMode>();
    private SpriteRenderer[] zoneMarkers;

    // ---- Spawning ----

    public List<GamePieceDragAndReleaseForce> Spawn(MatchSettings settings)
    {
        var parent = new GameObject("Pieces").transform;
        var pieces = new List<GamePieceDragAndReleaseForce>();
        for (var player = 0; player < 2; player++)
        {
            var count = Mathf.Clamp(settings.StonesFor(player), 1, MaxStones);
            var template = player == 0 ? blackTemplate : whiteTemplate;
            for (var i = 0; i < count; i++)
            {
                var piece = Instantiate(template, PresetPosition(player, i, count), template.transform.rotation, parent);
                piece.name = $"{(player == 0 ? "Black" : "White")} {i + 1}";
                var manager = piece.GetComponent<GamePieceManager>();
                manager.playerIndex = player;
                manager.pieceID = PieceId(player, i);
                piece.gameObject.SetActive(true);
                pieces.Add(piece);
            }
        }
        return pieces;
    }

    // Unique per stone and the same on both machines: every network message
    // addresses stones by it.
    public static char PieceId(int player, int index) => (char)((player == 0 ? 'A' : 'a') + index);

    public Vector3 PresetPosition(int player, int index, int count)
    {
        var point = LayoutPoint(index, count);
        if (player == 1) point = -point;
        return OnBoard(player, point);
    }

    private Vector2 LayoutPoint(int index, int count)
    {
        var layout = layouts != null ? layouts.Find(count.ToString()) : null;
        if (layout != null && index < layout.childCount)
        {
            var p = layout.GetChild(index).position;
            return new Vector2(p.x, p.z);
        }
        return DefaultLayout(count)[index];
    }

    // Black's side (negative z): up to 7 stones in one row, more in two
    // staggered rows. 6 reproduces the original hand-placed opening.
    public static Vector2[] DefaultLayout(int count)
    {
        const float spacing = 0.4f;
        var rows = count <= 7 ? new[] { (count, -1.0f) } : new[] { ((count + 1) / 2, -0.8f), (count / 2, -1.2f) };
        var points = new List<Vector2>();
        foreach (var (n, z) in rows)
            for (var i = 0; i < n; i++)
                points.Add(new Vector2((i - (n - 1) / 2f) * spacing, z));
        return points.ToArray();
    }

    private Vector3 OnBoard(int player, Vector2 point)
    {
        var template = player == 0 ? blackTemplate : whiteTemplate;
        return new Vector3(point.x, template.transform.position.y, point.y);
    }

    // ---- Zones ----

    public Rect Zone(int player)
    {
        return player == 0 ? blackZone : new Rect(-blackZone.xMax, -blackZone.yMax, blackZone.width, blackZone.height);
    }

    public bool InZone(int player, Vector3 position) => Zone(player).Contains(new Vector2(position.x, position.z));

    public Vector3 ClampToZone(int player, Vector3 position)
    {
        var zone = Zone(player);
        return OnBoard(player, new Vector2(Mathf.Clamp(position.x, zone.xMin, zone.xMax), Mathf.Clamp(position.z, zone.yMin, zone.yMax)));
    }

    public bool IsClear(Vector3 position, IEnumerable<Vector3> others)
    {
        foreach (var other in others)
        {
            var dx = other.x - position.x;
            var dz = other.z - position.z;
            if (dx * dx + dz * dz < minSpacing * minSpacing) return false;
        }
        return true;
    }

    // A random spot in the zone clear of every stone in `occupied`. Falls back
    // to scanning the zone on a grid, which with 12 stones always finds room.
    public Vector3 RandomFreePosition(int player, List<Vector3> occupied, System.Random random)
    {
        var zone = Zone(player);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var candidate = OnBoard(player, new Vector2(
                zone.xMin + (float)random.NextDouble() * zone.width,
                zone.yMin + (float)random.NextDouble() * zone.height));
            if (IsClear(candidate, occupied)) return candidate;
        }
        for (var z = zone.yMin; z <= zone.yMax; z += minSpacing)
        for (var x = zone.xMin; x <= zone.xMax; x += minSpacing)
        {
            var candidate = OnBoard(player, new Vector2(x, z));
            if (IsClear(candidate, occupied)) return candidate;
        }
        return OnBoard(player, zone.center);
    }

    // ---- Placement phase view ----

    // Stones sit still (kinematic) until the match starts, so dragging one
    // never shoves the others; unplaced stones are hidden until
    // PlacementController shows them.
    public void BeginPlacement(IEnumerable<GamePieceDragAndReleaseForce> pieces)
    {
        foreach (var piece in pieces)
        {
            var rb = piece.GetComponent<Rigidbody>();
            if (!frozenBodies.ContainsKey(rb)) frozenBodies[rb] = rb.collisionDetectionMode;
            // Kinematic bodies don't support ContinuousDynamic: switch first.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            piece.gameObject.SetActive(false);
        }
    }

    public void ShowZones(bool blackActive, bool whiteActive)
    {
        if (zoneMarkers == null) zoneMarkers = new[] { CreateZoneMarker(0), CreateZoneMarker(1) };
        zoneMarkers[0].color = blackActive ? activeZoneColor : zoneColor;
        zoneMarkers[1].color = whiteActive ? activeZoneColor : zoneColor;
        foreach (var marker in zoneMarkers) marker.gameObject.SetActive(true);
    }

    // restorePhysics: false on a network guest, whose stones stay kinematic
    // and host-driven.
    public void EndPlacement(PlacementPhase phase, IEnumerable<GamePieceDragAndReleaseForce> pieces, bool restorePhysics)
    {
        foreach (var piece in pieces)
        {
            piece.gameObject.SetActive(true);
            var rb = piece.GetComponent<Rigidbody>();
            var id = piece.GetComponent<GamePieceManager>().pieceID;
            if (phase.TryGetPosition(id, out var position))
            {
                // The body too, not just the transform: a stone that was hidden
                // (or moved while kinematic) still has its spawn pose there,
                // and that's what the host sends and the guest eases from.
                piece.transform.position = position;
                rb.position = position;
            }
            // A click during placement leaves OnMouseDown's flag behind, and the
            // first turn would read it as a pick.
            piece.isSelected = false;

            if (restorePhysics && frozenBodies.TryGetValue(rb, out var mode))
            {
                rb.isKinematic = false;
                rb.collisionDetectionMode = mode;
            }
        }
        frozenBodies.Clear();
        if (zoneMarkers != null)
            foreach (var marker in zoneMarkers) marker.gameObject.SetActive(false);
    }

    private SpriteRenderer CreateZoneMarker(int player)
    {
        var zone = Zone(player);
        var marker = new GameObject(player == 0 ? "BlackZone" : "WhiteZone").AddComponent<SpriteRenderer>();
        marker.transform.SetParent(transform, false);
        marker.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        // Flat on the board, just above the surface; sprite height runs along z.
        marker.transform.SetPositionAndRotation(new Vector3(zone.center.x, 0.003f, zone.center.y), Quaternion.Euler(90, 0, 0));
        // Pad by a stone radius so the tint covers the stones, not just their centers.
        marker.transform.localScale = new Vector3(zone.width + 0.2f, zone.height + 0.2f, 1);
        return marker;
    }

    private void OnDrawGizmos()
    {
        for (var player = 0; player < 2; player++)
        {
            var zone = Zone(player);
            Gizmos.color = player == 0 ? new Color(0.1f, 0.1f, 0.1f, 0.8f) : new Color(1f, 1f, 1f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(zone.center.x, 0.01f, zone.center.y), new Vector3(zone.width, 0, zone.height));
        }
    }
}
