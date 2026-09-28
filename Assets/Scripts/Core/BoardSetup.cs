using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// GameScene's board. Turns on the board the match rules pick, spawns each
// side's pieces - go stones from the black/white templates, janggi, chess
// or gonggi pieces from theirs - holds the preset layouts and placement
// zones, and shows the placement phase while it runs.
//
// Where the sides sit is the board's (BoardVariant.Seats): two face each
// other across it (south and north); three or four sit at its edges, going
// round clockwise from the south. Every side's layout and zone is the
// south side's turned to face its own edge, by its seat's angle.
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
    public GamePieceDragAndReleaseForce chessTemplate;
    public GamePieceDragAndReleaseForce gonggiTemplate;
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
    // The chess pieces a side of N gets, the first N of these.
    public ChessKind[] chessLineup =
    {
        ChessKind.King, ChessKind.Queen, ChessKind.Rook, ChessKind.Rook, ChessKind.Bishop, ChessKind.Bishop,
        ChessKind.Knight, ChessKind.Knight, ChessKind.Pawn, ChessKind.Pawn, ChessKind.Pawn, ChessKind.Pawn,
    };
    [Range(0.1f, 0.6f)] public float chessBalance = 0.3f; // a chess piece's centre of mass, up its height: weighted at the foot, as real ones are
    // A gonggi stone against a go stone: 6.5 g of plastic and steel shot to
    // a glass stone's 4.5 or so.
    public float gonggiWeight = 1.45f;
    // Its centre of mass, up its height: the shot lies in the bottom (a
    // plastic shell of even thickness alone would have it a third up).
    [Range(0.1f, 0.5f)] public float gonggiBalance = 0.22f;

    // The chess board's squares, 5.7 cm as at a tournament. Two sides on it
    // start on real squares: chess pieces where chess puts them (the first
    // twelve of chessLineup: e1, d1, a1, h1...), anything else from the
    // middle of the back rank outward. The far side is the mirror, so the
    // kings face each other up the e-file.
    public static float ChessSquare => 5.7f * ChessPieceMesh.UnitsPerCm;
    private static readonly Vector2Int[] ChessSquares =
    {
        new Vector2Int(4, 0), new Vector2Int(3, 0), new Vector2Int(0, 0), new Vector2Int(7, 0), new Vector2Int(2, 0), new Vector2Int(5, 0),
        new Vector2Int(1, 0), new Vector2Int(6, 0), new Vector2Int(4, 1), new Vector2Int(3, 1), new Vector2Int(2, 1), new Vector2Int(5, 1),
    };
    private static readonly Vector2Int[] MiddleOutSquares =
    {
        new Vector2Int(4, 0), new Vector2Int(3, 0), new Vector2Int(5, 0), new Vector2Int(2, 0), new Vector2Int(6, 0), new Vector2Int(1, 0),
        new Vector2Int(7, 0), new Vector2Int(0, 0), new Vector2Int(4, 1), new Vector2Int(3, 1), new Vector2Int(5, 1), new Vector2Int(2, 1),
    };

    public float gap = 0.02f; // between two pieces placed side by side
    public Color zoneColor = new Color(0.18f, 0.14f, 0.10f, 0.10f);
    public Color activeZoneColor = new Color(0.70f, 0.19f, 0.16f, 0.22f);

    public BoardVariant Active { get; private set; }
    public int Players { get; private set; } = 2;
    public Bounds SurfaceBounds => ActiveOrFirst.Bounds;
    // What's left of the board to play on (BoardZone crumbles it).
    public BoardShape Playable => ActiveOrFirst.Playable;
    // Where pieces rest while placed by hand (they drop onto the board when
    // the match starts).
    public float PieceHeight { get; private set; }

    private readonly Dictionary<Rigidbody, CollisionDetectionMode> frozenBodies = new Dictionary<Rigidbody, CollisionDetectionMode>();
    private readonly Dictionary<int, Material> stoneMaterials = new Dictionary<int, Material>(); // the third and fourth sides' colours
    private readonly Dictionary<int, Material> sideMaterials = new Dictionary<int, Material>(); // chess and gonggi pieces, every side's
    private readonly Dictionary<char, string> letterKeys = new Dictionary<char, string>(); // janggi and chess pieces, by id
    private readonly Dictionary<char, Quaternion> spawnRotations = new Dictionary<char, Quaternion>(); // how each piece first stood, facing its side
    private readonly System.Random respawnRandom = new System.Random();
    private PieceType pieceType;
    private SpriteRenderer[] zoneMarkers;

    private BoardVariant ActiveOrFirst => Active != null ? Active : boards[0];

    // ---- Spawning ----

    public List<GamePieceDragAndReleaseForce> Spawn(MatchSettings settings, int players)
    {
        UseBoard(settings.BoardType);
        Players = players;
        pieceType = settings.PieceType;
        var template = pieceType switch
        {
            PieceType.JanggiPieces => janggiTemplate,
            PieceType.ChessPieces => chessTemplate,
            PieceType.GonggiStones => gonggiTemplate,
            _ => blackTemplate,
        };
        PieceHeight = template.transform.position.y;

        var parent = new GameObject("Pieces").transform;
        var pieces = new List<GamePieceDragAndReleaseForce>();
        for (var player = 0; player < players; player++)
        {
            var count = Mathf.Clamp(settings.StonesFor(player), 1, MaxStones);
            // On chess squares the layout is already in lineup order.
            var lineup = OnChessSquares ? Enumerable.Range(0, count).ToArray() : LineupRanks(count, player);
            for (var i = 0; i < count; i++)
            {
                var position = PresetPosition(player, i, count);
                var piece = pieceType switch
                {
                    PieceType.JanggiPieces => SpawnJanggiPiece(player, i, lineup[i], position, parent),
                    PieceType.ChessPieces => SpawnChessPiece(player, i, lineup[i], position, parent),
                    PieceType.GonggiStones => SpawnGonggi(player, i, position, parent),
                    _ => SpawnStone(player, i, position, parent),
                };
                var manager = piece.GetComponent<GamePieceManager>();
                manager.playerIndex = player;
                manager.pieceID = PieceId(player, i);
                spawnRotations[manager.pieceID] = piece.transform.rotation;
                piece.gameObject.SetActive(true);
                pieces.Add(piece);
            }
        }
        return pieces;
    }

    // Unique per piece and the same on every machine: every network message
    // addresses pieces by it. Twelve a side at most.
    public static char PieceId(int player, int index) => (char)("AaMm"[player] + index);

    // Which edge a side sits at: degrees anticlockwise from the south,
    // seen from above (90 east, 180 north, 270 west on a square board).
    public float SeatAngle(int player)
    {
        var seats = ActiveOrFirst.Seats(Players);
        return seats[Mathf.Clamp(player, 0, seats.Length - 1)];
    }

    // Out from the board's centre towards a side's edge, in (x, z).
    public Vector2 SeatDirection(int player) => Turn(Vector2.down, SeatAngle(player));

    // How a piece of that side stands to face across the board from its edge.
    public Quaternion Facing(int player) => Quaternion.Euler(0, -SeatAngle(player), 0);

    // A south-side (x, z) point turned to face a seat's edge (angle), and back.
    public static Vector2 Turn(Vector2 point, float angle)
    {
        var a = angle * Mathf.Deg2Rad;
        var cos = Mathf.Cos(a);
        var sin = Mathf.Sin(a);
        var turned = new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
        // Exactly on the axes for the square board's quarter turns.
        return new Vector2(Mathf.Round(turned.x * 1e5f) / 1e5f, Mathf.Round(turned.y * 1e5f) / 1e5f);
    }

    private Vector2 ToSeat(int player, Vector2 world) => Turn(world, -SeatAngle(player));
    private Vector2 FromSeat(int player, Vector2 local) => Turn(local, SeatAngle(player));

    // The Loc key of a janggi piece's letter, null for a go stone. Still
    // answers once the piece is gone (the kill feed asks then).
    public string LetterKey(char pieceId) => letterKeys.TryGetValue(pieceId, out var key) ? key : null;

    private void UseBoard(BoardType type)
    {
        Active = Array.Find(boards, b => b.type == type) ?? boards[0];
        Active.Build(); // whole again, whatever the last match left of it
        foreach (var board in boards) board.gameObject.SetActive(board == Active);
    }

    // The third and fourth sides are white stones dyed.
    private GamePieceDragAndReleaseForce SpawnStone(int player, int index, Vector3 position, Transform parent)
    {
        var template = player == 0 ? blackTemplate : whiteTemplate;
        var piece = Instantiate(template, position, template.transform.rotation, parent);
        piece.name = $"{new[] { "Black", "White", "Blue", "Red" }[player]} {index + 1}";
        piece.GetComponent<MeshCollider>().sharedMesh = GoStoneCollider.Get();
        piece.GetComponent<GamePieceManager>().radius = StoneRadius;
        if (player >= 2)
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = StoneMaterial(player, renderer.sharedMaterial);
        return piece;
    }

    private Material StoneMaterial(int player, Material white)
    {
        if (!stoneMaterials.TryGetValue(player, out var material))
        {
            material = new Material(white) { color = SideStyle.StoneColor(player) };
            stoneMaterials[player] = material;
        }
        return material;
    }

    private void OnDestroy()
    {
        foreach (var material in stoneMaterials.Values) Destroy(material);
        foreach (var material in sideMaterials.Values) Destroy(material);
    }

    // Letters face their owner's edge. Mass goes with volume against a go
    // stone's: the general is the hardest to move, as on a real board.
    private GamePieceDragAndReleaseForce SpawnJanggiPiece(int player, int index, int rank, Vector3 position, Transform parent)
    {
        var kind = janggiKinds[janggiLineup[Mathf.Min(rank, janggiLineup.Length - 1)]];
        var piece = Instantiate(janggiTemplate, position, Facing(player), parent);
        var mesh = JanggiPieceMesh.Get(kind.width, kind.height);
        piece.GetComponent<MeshFilter>().sharedMesh = mesh;
        piece.GetComponent<MeshCollider>().sharedMesh = mesh;

        var label = piece.GetComponentInChildren<TMP_Text>(true);
        var letterKey = SideStyle.ChoLetters(player) ? kind.choLabel : kind.hanLabel;
        letterKeys[PieceId(player, index)] = letterKey;
        label.text = SideStyle.PieceLetter(letterKey);
        label.color = SideStyle.LetterColor(player, PieceType.JanggiPieces);
        label.transform.localPosition = new Vector3(0, kind.height + 0.001f, 0);
        label.rectTransform.sizeDelta = Vector2.one * kind.width * 0.62f;

        var stoneBox = blackTemplate.GetComponent<BoxCollider>().size;
        var volume = 2 * (Mathf.Sqrt(2) - 1) * kind.width * kind.width * kind.height; // regular octagon area x height
        piece.GetComponent<Rigidbody>().mass = blackTemplate.GetComponent<Rigidbody>().mass * volume / (stoneBox.x * stoneBox.y * stoneBox.z);

        piece.GetComponent<GamePieceManager>().radius = JanggiPieceMesh.Circumradius(kind.width);
        piece.name = $"{new[] { "Cho", "Han", "Blue", "Black" }[player]} {index + 1} ({label.text})";
        return piece;
    }

    // Standing pieces that topple and roll (ChessPieceMesh). Mass goes with
    // volume, the same wood as the janggi pieces, and sits low in the foot,
    // so a piece rocks before it tips.
    private GamePieceDragAndReleaseForce SpawnChessPiece(int player, int index, int rank, Vector3 position, Transform parent)
    {
        var kind = chessLineup[Mathf.Min(rank, chessLineup.Length - 1)];
        // Seat-facing, like the janggi letters: a knight looks across the board.
        var piece = Instantiate(chessTemplate, position, Facing(player), parent);
        piece.GetComponent<MeshFilter>().sharedMesh = ChessPieceMesh.Get(kind);
        // The template's collider takes the first part, and copies of it the rest.
        var parts = ChessPieceMesh.Colliders(kind);
        var collider = piece.GetComponent<MeshCollider>();
        collider.sharedMesh = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var part = piece.gameObject.AddComponent<MeshCollider>();
            part.convex = true;
            part.sharedMaterial = collider.sharedMaterial;
            part.sharedMesh = parts[i];
        }
        piece.GetComponent<MeshRenderer>().sharedMaterial = SideMaterial(player, piece.GetComponent<MeshRenderer>().sharedMaterial);
        letterKeys[PieceId(player, index)] = "chess." + kind;

        var stoneBox = blackTemplate.GetComponent<BoxCollider>().size;
        var body = piece.GetComponent<Rigidbody>();
        body.mass = blackTemplate.GetComponent<Rigidbody>().mass * ChessPieceMesh.Volume(kind) / (stoneBox.x * stoneBox.y * stoneBox.z);
        body.centerOfMass = new Vector3(0, ChessPieceMesh.Height(kind) * chessBalance, 0);

        piece.GetComponent<GamePieceManager>().radius = ChessPieceMesh.BaseRadius(kind);
        piece.name = $"{new[] { "White", "Black", "Red", "Blue" }[player]} {index + 1} ({kind})";
        return piece;
    }

    // Gonggi stones in the go stones' colours. The shot inside is the
    // template's doing (BoardBuilder): its knocks are dead - nothing
    // bounces off it, a piece that hits it square goes on with it - and a
    // spin soon dies, the shot dragging behind the shell. Here: the weight
    // of the shot, low down, so it rocks back onto its foot.
    private GamePieceDragAndReleaseForce SpawnGonggi(int player, int index, Vector3 position, Transform parent)
    {
        var piece = Instantiate(gonggiTemplate, position, Facing(player), parent);
        piece.GetComponent<MeshFilter>().sharedMesh = GonggiMesh.Get();
        piece.GetComponent<MeshCollider>().sharedMesh = GonggiMesh.Collider();
        piece.GetComponent<MeshRenderer>().sharedMaterial = SideMaterial(player, piece.GetComponent<MeshRenderer>().sharedMaterial);

        var body = piece.GetComponent<Rigidbody>();
        body.mass = blackTemplate.GetComponent<Rigidbody>().mass * gonggiWeight;
        body.centerOfMass = new Vector3(0, GonggiMesh.Height * gonggiBalance, 0);

        piece.GetComponent<GamePieceManager>().radius = GonggiMesh.Radius;
        piece.name = $"{new[] { "Black", "White", "Blue", "Red" }[player]} {index + 1} (gonggi)";
        return piece;
    }

    private Material SideMaterial(int player, Material template)
    {
        if (!sideMaterials.TryGetValue(player, out var material))
        {
            material = new Material(template) { color = SideStyle.Fill(player, pieceType) };
            sideMaterials[player] = material;
        }
        return material;
    }

    // Two sides on the chess board start on its squares.
    private bool OnChessSquares => ActiveOrFirst.type == BoardType.Chess && Players == 2;

    // A square's centre for the south side; the north side's is its mirror.
    private Vector2 ChessPoint(int index, int player)
    {
        var square = (pieceType == PieceType.ChessPieces ? ChessSquares : MiddleOutSquares)[Mathf.Min(index, ChessSquares.Length - 1)];
        var point = new Vector2((square.x - 3.5f) * ChessSquare, (square.y - 3.5f) * ChessSquare);
        return player == 0 ? point : new Vector2(point.x, -point.y);
    }

    // For each layout slot, its place in the lineup: back row first (the
    // south side's layout, so further from the center line = smaller z),
    // then from the middle outward. Only layout positions go in, so every
    // machine agrees.
    private int[] LineupRanks(int count, int player)
    {
        var order = Enumerable.Range(0, count)
            .OrderBy(i => SouthPoint(i, count, player).y)
            .ThenBy(i => Mathf.Abs(SouthPoint(i, count, player).x))
            .ToArray();
        var ranks = new int[count];
        for (var rank = 0; rank < count; rank++) ranks[order[rank]] = rank;
        return ranks;
    }

    public Vector3 PresetPosition(int player, int index, int count)
    {
        if (OnChessSquares) return OnBoard(ChessPoint(index, player));
        return OnBoard(FromSeat(player, SouthPoint(index, count, player)));
    }

    // The south side's layout: the scene's for two sides on the boards it's
    // drawn for, rows of the board's own otherwise, in as far from the
    // side's edge as the board says. The janggi board's hinges lie across
    // the middle, under the west and east sides' front rows: those leave the
    // fold clear.
    private Vector2 SouthPoint(int index, int count, int player)
    {
        var board = ActiveOrFirst;
        if (Players == 2 && board.sceneLayouts) return LayoutPoint(index, count);
        var edge = EdgeDistance(player);
        // East or west: facing along the fold.
        var acrossFold = board.type == BoardType.Janggi && Mathf.Abs(Mathf.Sin(SeatAngle(player) * Mathf.Deg2Rad)) > 0.7f;
        return MultiLayout(count, board.multiSpacing, -(edge - board.multiFront), -(edge - board.multiBack), acrossFold)[index];
    }

    // How far the board reaches from its centre towards a side's edge.
    public float EdgeDistance(int player) => ActiveOrFirst.Shape.Exit(Vector2.zero, SeatDirection(player));

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

    // A front row (frontZ) and, from six pieces, a second one behind it
    // (backZ). On the square board with three or four sides each row keeps
    // a third of a unit inside the diagonal, so the neighbours' corners stay
    // apart. clearMiddle keeps the front row off the centre line (the fold
    // of the janggi board): up to five go there in a single row further back.
    public static Vector2[] MultiLayout(int count, float spacing, float frontZ, float backZ, bool clearMiddle = false)
    {
        var back = count <= 5 ? 0 : Mathf.Max((count + 1) / 2, count - 5);
        var points = new List<Vector2>();
        void Row(int n, float z)
        {
            for (var i = 0; i < n; i++) points.Add(new Vector2((i - (n - 1) / 2f) * spacing, z));
        }
        if (back == 0)
        {
            Row(count, clearMiddle ? backZ : frontZ);
            return points.ToArray();
        }
        var front = count - back;
        if (clearMiddle)
        {
            // Out from the middle on alternate sides, a gap where the fold is.
            float[] slots = { 0.22f, -0.22f, 0.54f, -0.54f, 0.86f, -0.86f };
            for (var i = 0; i < front; i++) points.Add(new Vector2(slots[i], frontZ));
        }
        else Row(front, frontZ);
        Row(back, backZ);
        return points.ToArray();
    }

    private Vector3 OnBoard(Vector2 point) => new Vector3(point.x, PieceHeight, point.y);

    // ---- Zones ----

    // Where the center of a piece of this radius may go, in the side's own
    // frame (the south side's, turned by its seat's angle): the board's
    // zone, pulled in for pieces bigger than a go stone. With three or four
    // sides it's narrower and short of the middle, clear of the neighbours',
    // and measured in from the side's own edge.
    public Rect Zone(int player, float radius = StoneRadius)
    {
        var board = ActiveOrFirst;
        var zone = board.blackZone;
        if (Players > 2)
        {
            var edge = EdgeDistance(player);
            zone = Rect.MinMaxRect(board.multiZone.xMin, -(edge - board.multiZone.yMin), board.multiZone.xMax, -(edge - board.multiZone.yMax));
        }
        var inset = Mathf.Max(0, radius - StoneRadius);
        return Rect.MinMaxRect(zone.xMin + inset, zone.yMin + inset, zone.xMax - inset, zone.yMax - inset);
    }

    // Edges count as inside: ClampToZone lands exactly on them, and
    // Rect.Contains would turn every stone dragged to the edge away.
    public bool InZone(int player, Vector3 position, float radius)
    {
        var zone = Zone(player, radius);
        var local = ToSeat(player, new Vector2(position.x, position.z));
        return local.x >= zone.xMin - 1e-4f && local.x <= zone.xMax + 1e-4f && local.y >= zone.yMin - 1e-4f && local.y <= zone.yMax + 1e-4f;
    }

    public Vector3 ClampToZone(int player, Vector3 position, float radius)
    {
        var zone = Zone(player, radius);
        var local = ToSeat(player, new Vector2(position.x, position.z));
        return OnBoard(FromSeat(player, new Vector2(Mathf.Clamp(local.x, zone.xMin, zone.xMax), Mathf.Clamp(local.y, zone.yMin, zone.yMax))));
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

    // A random spot in the zone clear of every piece in `occupied`, on what's
    // left of the board. Falls back to scanning the zone on a grid, which
    // with 12 pieces always finds room - unless the board has crumbled away
    // under it, when the spot goes as near the side's edge as there's board.
    public Vector3 RandomFreePosition(int player, float radius, List<(Vector3 position, float radius)> occupied, System.Random random)
    {
        var zone = Zone(player, radius);
        var playable = Playable;
        bool Free(Vector2 local, out Vector3 world)
        {
            world = OnBoard(FromSeat(player, local));
            return playable.Contains(new Vector2(world.x, world.z), radius) && IsClear(world, radius, occupied);
        }
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var local = new Vector2(zone.xMin + (float)random.NextDouble() * zone.width, zone.yMin + (float)random.NextDouble() * zone.height);
            if (Free(local, out var world)) return world;
        }
        var step = radius + gap;
        for (var z = zone.yMin; z <= zone.yMax; z += step)
        for (var x = zone.xMin; x <= zone.xMax; x += step)
            if (Free(new Vector2(x, z), out var world)) return world;
        // In from the side's edge, and across, until there's board and room.
        for (var z = -EdgeDistance(player); z <= 0; z += step)
        for (var x = 0f; x <= zone.width; x += step)
        {
            if (Free(new Vector2(x, z), out var right)) return right;
            if (Free(new Vector2(-x, z), out var left)) return left;
        }
        return OnBoard(Vector2.zero);
    }

    // A piece coming back (a battle of health): somewhere free in its side's
    // zone, standing as it first did.
    public void Respawn(GamePieceDragAndReleaseForce piece, IEnumerable<GamePieceDragAndReleaseForce> pieces)
    {
        var manager = piece.Manager;
        var occupied = pieces.Where(p => p != null && p != piece && !p.IsParked)
            .Select(p => (p.transform.position, p.Manager.radius)).ToList();
        var position = RandomFreePosition(manager.playerIndex, manager.radius, occupied, respawnRandom);
        piece.Unpark(position, spawnRotations.TryGetValue(manager.pieceID, out var rotation) ? rotation : piece.transform.rotation);
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

    // active: whether a side's zone is the one this screen places in now.
    public void ShowZones(Func<int, bool> active)
    {
        if (zoneMarkers == null) zoneMarkers = Enumerable.Range(0, Players).Select(CreateZoneMarker).ToArray();
        for (var player = 0; player < zoneMarkers.Length; player++)
        {
            zoneMarkers[player].color = active(player) ? activeZoneColor : zoneColor;
            zoneMarkers[player].gameObject.SetActive(true);
        }
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
        var marker = new GameObject($"Zone{player}").AddComponent<SpriteRenderer>();
        marker.transform.SetParent(transform, false);
        marker.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        // Flat on the board, just above the surface; sprite height runs along z.
        var center = FromSeat(player, zone.center);
        marker.transform.SetPositionAndRotation(new Vector3(center.x, 0.003f, center.y), Facing(player) * Quaternion.Euler(90, 0, 0));
        // Pad by a stone radius so the tint covers the stones, not just their centers.
        marker.transform.localScale = new Vector3(zone.width + 2 * StoneRadius, zone.height + 2 * StoneRadius, 1);
        return marker;
    }

    private void OnDrawGizmos()
    {
        if (boards == null || boards.Length == 0 || boards[0] == null || boards[0].parts == null) return;
        for (var player = 0; player < 2; player++)
        {
            var zone = Zone(player);
            Gizmos.color = player == 0 ? new Color(0.1f, 0.1f, 0.1f, 0.8f) : new Color(1f, 1f, 1f, 0.8f);
            var corners = new[] { zone.min, new Vector2(zone.xMax, zone.yMin), zone.max, new Vector2(zone.xMin, zone.yMax) }.Select(c => FromSeat(player, c)).ToArray();
            for (var i = 0; i < 4; i++)
                Gizmos.DrawLine(new Vector3(corners[i].x, 0.01f, corners[i].y), new Vector3(corners[(i + 1) % 4].x, 0.01f, corners[(i + 1) % 4].y));
        }
    }
}
