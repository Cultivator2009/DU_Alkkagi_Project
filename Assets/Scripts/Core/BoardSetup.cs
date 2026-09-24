using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// GameScene's board. Turns on the board the match rules pick, spawns each
// side's pieces - go stones from the black/white templates, or janggi
// pieces from the janggi template - holds the preset layouts and placement
// zones, and shows the placement phase while it runs.
public class BoardSetup : MonoBehaviour
{
    public const int MaxStones = 12;
    public const float StoneRadius = 0.1f; // a go stone; zones are drawn for this size

    // A janggi piece size, as on a real set: the general, the four major
    // pieces, then the guards and soldiers.
    [Serializable]
    public struct JanggiKind
    {
        public string choLabel; // Loc keys of the letter each side's piece carries
        public string hanLabel;
        public float width;     // across the flats
        public float height;
    }

    public GamePieceDragAndReleaseForce blackTemplate;
    public GamePieceDragAndReleaseForce whiteTemplate;
    public GamePieceDragAndReleaseForce janggiTemplate;
    public BoardVariant[] boards;

    // Children named "1".."12", each holding that many points: black's preset
    // layout for that stone count, editable in the scene. White uses the same
    // layout turned 180 degrees about the board center, so both sides get the
    // same shape from where they sit. Tools > Alkkagi > Reset spawn layouts
    // regenerates the defaults.
    public Transform layouts;

    public JanggiKind[] janggiKinds =
    {
        new JanggiKind { choLabel = "piece.cho", hanLabel = "piece.han", width = 0.28f, height = 0.09f },
        new JanggiKind { choLabel = "piece.cha", hanLabel = "piece.cha", width = 0.24f, height = 0.08f },
        new JanggiKind { choLabel = "piece.po", hanLabel = "piece.po", width = 0.24f, height = 0.08f },
        new JanggiKind { choLabel = "piece.ma", hanLabel = "piece.ma", width = 0.24f, height = 0.08f },
        new JanggiKind { choLabel = "piece.sang", hanLabel = "piece.sang", width = 0.24f, height = 0.08f },
        new JanggiKind { choLabel = "piece.sa", hanLabel = "piece.sa", width = 0.2f, height = 0.07f },
        new JanggiKind { choLabel = "piece.jol", hanLabel = "piece.byeong", width = 0.2f, height = 0.07f },
    };
    // Which kinds a side of N pieces gets: the first N of this list, handed
    // out back row first, each row from the middle outward, so the general
    // sits at the back in the middle as on a real board.
    public int[] janggiLineup = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6 };

    public float gap = 0.02f; // between two pieces placed side by side
    public Color zoneColor = new Color(0.18f, 0.14f, 0.10f, 0.10f);
    public Color activeZoneColor = new Color(0.70f, 0.19f, 0.16f, 0.22f);

    public BoardVariant Active { get; private set; }
    public Bounds SurfaceBounds => ActiveOrFirst.surface.bounds;
    // Where pieces rest while placed by hand (they drop onto the board when
    // the match starts).
    public float PieceHeight { get; private set; }

    private readonly Dictionary<Rigidbody, CollisionDetectionMode> frozenBodies = new Dictionary<Rigidbody, CollisionDetectionMode>();
    private readonly Dictionary<char, string> letterKeys = new Dictionary<char, string>(); // janggi pieces, by id
    private SpriteRenderer[] zoneMarkers;

    private BoardVariant ActiveOrFirst => Active != null ? Active : boards[0];

    // ---- Spawning ----

    public List<GamePieceDragAndReleaseForce> Spawn(MatchSettings settings)
    {
        UseBoard(settings.BoardType);
        var janggi = settings.PieceType == PieceType.JanggiPieces;
        PieceHeight = (janggi ? janggiTemplate : blackTemplate).transform.position.y;

        var parent = new GameObject("Pieces").transform;
        var pieces = new List<GamePieceDragAndReleaseForce>();
        for (var player = 0; player < 2; player++)
        {
            var count = Mathf.Clamp(settings.StonesFor(player), 1, MaxStones);
            var lineup = LineupRanks(count);
            for (var i = 0; i < count; i++)
            {
                var position = PresetPosition(player, i, count);
                var piece = janggi ? SpawnJanggiPiece(player, i, lineup[i], position, parent) : SpawnStone(player, i, position, parent);
                var manager = piece.GetComponent<GamePieceManager>();
                manager.playerIndex = player;
                manager.pieceID = PieceId(player, i);
                piece.gameObject.SetActive(true);
                pieces.Add(piece);
            }
        }
        return pieces;
    }

    // Unique per piece and the same on both machines: every network message
    // addresses pieces by it.
    public static char PieceId(int player, int index) => (char)((player == 0 ? 'A' : 'a') + index);

    // The Loc key of a janggi piece's letter, null for a go stone. Still
    // answers once the piece is gone (the kill feed asks then).
    public string LetterKey(char pieceId) => letterKeys.TryGetValue(pieceId, out var key) ? key : null;

    private void UseBoard(BoardType type)
    {
        Active = Array.Find(boards, b => b.type == type) ?? boards[0];
        foreach (var board in boards) board.gameObject.SetActive(board == Active);
    }

    private GamePieceDragAndReleaseForce SpawnStone(int player, int index, Vector3 position, Transform parent)
    {
        var template = player == 0 ? blackTemplate : whiteTemplate;
        var piece = Instantiate(template, position, template.transform.rotation, parent);
        piece.name = $"{(player == 0 ? "Black" : "White")} {index + 1}";
        piece.GetComponent<GamePieceManager>().radius = StoneRadius;
        return piece;
    }

    // Letters face their owner, so white's pieces are turned around. Mass
    // goes with volume against a go stone's: the general is the hardest to
    // move, as on a real board.
    private GamePieceDragAndReleaseForce SpawnJanggiPiece(int player, int index, int rank, Vector3 position, Transform parent)
    {
        var kind = janggiKinds[janggiLineup[Mathf.Min(rank, janggiLineup.Length - 1)]];
        var piece = Instantiate(janggiTemplate, position, Quaternion.Euler(0, player == 0 ? 0 : 180, 0), parent);
        var mesh = JanggiPieceMesh.Get(kind.width, kind.height);
        piece.GetComponent<MeshFilter>().sharedMesh = mesh;
        piece.GetComponent<MeshCollider>().sharedMesh = mesh;

        var label = piece.GetComponentInChildren<TMP_Text>(true);
        var letterKey = player == 0 ? kind.choLabel : kind.hanLabel;
        letterKeys[PieceId(player, index)] = letterKey;
        label.text = SideStyle.PieceLetter(letterKey);
        label.color = player == 0 ? SideStyle.Cho : SideStyle.Han;
        label.transform.localPosition = new Vector3(0, kind.height + 0.001f, 0);
        label.rectTransform.sizeDelta = Vector2.one * kind.width * 0.62f;

        var stoneBox = blackTemplate.GetComponent<BoxCollider>().size;
        var volume = 2 * (Mathf.Sqrt(2) - 1) * kind.width * kind.width * kind.height; // regular octagon area x height
        piece.GetComponent<Rigidbody>().mass = blackTemplate.GetComponent<Rigidbody>().mass * volume / (stoneBox.x * stoneBox.y * stoneBox.z);

        piece.GetComponent<GamePieceManager>().radius = JanggiPieceMesh.Circumradius(kind.width);
        piece.name = $"{(player == 0 ? "Cho" : "Han")} {index + 1} ({label.text})";
        return piece;
    }

    // For each layout slot, its place in the lineup: back row first (black's
    // layout, so further from the center line = smaller z), then from the
    // middle outward. Only layout positions go in, so both machines agree.
    private int[] LineupRanks(int count)
    {
        var order = Enumerable.Range(0, count)
            .OrderBy(i => LayoutPoint(i, count).y)
            .ThenBy(i => Mathf.Abs(LayoutPoint(i, count).x))
            .ToArray();
        var ranks = new int[count];
        for (var rank = 0; rank < count; rank++) ranks[order[rank]] = rank;
        return ranks;
    }

    public Vector3 PresetPosition(int player, int index, int count)
    {
        var point = LayoutPoint(index, count);
        if (player == 1) point = -point;
        return OnBoard(point);
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

    private Vector3 OnBoard(Vector2 point) => new Vector3(point.x, PieceHeight, point.y);

    // ---- Zones ----

    // Where the center of a piece of this radius may go: the board's zone,
    // pulled in for pieces bigger than a go stone.
    public Rect Zone(int player, float radius = StoneRadius)
    {
        var zone = ActiveOrFirst.blackZone;
        var inset = Mathf.Max(0, radius - StoneRadius);
        zone = Rect.MinMaxRect(zone.xMin + inset, zone.yMin + inset, zone.xMax - inset, zone.yMax - inset);
        return player == 0 ? zone : Rect.MinMaxRect(-zone.xMax, -zone.yMax, -zone.xMin, -zone.yMin);
    }

    // Edges count as inside: ClampToZone lands exactly on them, and
    // Rect.Contains would turn every stone dragged to the edge away.
    public bool InZone(int player, Vector3 position, float radius)
    {
        var zone = Zone(player, radius);
        return position.x >= zone.xMin && position.x <= zone.xMax && position.z >= zone.yMin && position.z <= zone.yMax;
    }

    public Vector3 ClampToZone(int player, Vector3 position, float radius)
    {
        var zone = Zone(player, radius);
        return OnBoard(new Vector2(Mathf.Clamp(position.x, zone.xMin, zone.xMax), Mathf.Clamp(position.z, zone.yMin, zone.yMax)));
    }

    public bool IsClear(Vector3 position, float radius, IEnumerable<(Vector3 position, float radius)> others)
    {
        foreach (var other in others)
        {
            var dx = other.position.x - position.x;
            var dz = other.position.z - position.z;
            var min = radius + other.radius + gap;
            if (dx * dx + dz * dz < min * min) return false;
        }
        return true;
    }

    // A random spot in the zone clear of every piece in `occupied`. Falls back
    // to scanning the zone on a grid, which with 12 pieces always finds room.
    public Vector3 RandomFreePosition(int player, float radius, List<(Vector3 position, float radius)> occupied, System.Random random)
    {
        var zone = Zone(player, radius);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var candidate = OnBoard(new Vector2(
                zone.xMin + (float)random.NextDouble() * zone.width,
                zone.yMin + (float)random.NextDouble() * zone.height));
            if (IsClear(candidate, radius, occupied)) return candidate;
        }
        var step = radius + gap;
        for (var z = zone.yMin; z <= zone.yMax; z += step)
        for (var x = zone.xMin; x <= zone.xMax; x += step)
        {
            var candidate = OnBoard(new Vector2(x, z));
            if (IsClear(candidate, radius, occupied)) return candidate;
        }
        return OnBoard(zone.center);
    }

    // ---- Placement phase view ----

    // Pieces sit still (kinematic) until the match starts, so dragging one
    // never shoves the others; unplaced pieces are hidden until
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

    // restorePhysics: false on a network guest, whose pieces stay kinematic
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
                // The body too, not just the transform: a piece that was hidden
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
        marker.transform.localScale = new Vector3(zone.width + 2 * StoneRadius, zone.height + 2 * StoneRadius, 1);
        return marker;
    }

    private void OnDrawGizmos()
    {
        if (boards == null || boards.Length == 0 || boards[0] == null) return;
        for (var player = 0; player < 2; player++)
        {
            var zone = Zone(player);
            Gizmos.color = player == 0 ? new Color(0.1f, 0.1f, 0.1f, 0.8f) : new Color(1f, 1f, 1f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(zone.center.x, 0.01f, zone.center.y), new Vector3(zone.width, 0, zone.height));
        }
    }
}
