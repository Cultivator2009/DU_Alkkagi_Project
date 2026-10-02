using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the board variants and the janggi, chess and gonggi piece
// templates into GameScene, wrapping the scene's original go board as a
// variant too. Safe to run again: it replaces what it made before.
//
// Boards are at their real sizes, at the scale where the go board's
// 45.45 cm length is three units (ChessPieceMesh.UnitsPerCm):
// - go: 42.42 x 45.45 cm (the Japanese standard; its lines are 22 mm apart
//   across and 23.7 along, which the original square texture, narrowed to
//   the board, comes to).
// - janggi: kept at the go board's size - sets vary, and a 4 cm general
//   sits right on it. A generated wood texture with the 9 x 10 grid and both
//   palaces, and two metal hinges across the fold - on a real folding board
//   they stand proud of the surface, and in alkkagi they're obstacles.
// - chess: 5.7 cm squares (a tournament board) with a 2.2 cm border, 50 cm.
// - hexagon (three sides): 54.5 cm corner to corner, a flat edge facing
//   each of the three sides; dimpled like a diamond-game board.
// - cross (four sides): arms 30 cm wide, 54.5 cm end to end, lined like a
//   go board.
// A board is its shape and materials (BoardVariant); the variant builds the
// solid board itself, here and again at run time. Pieces get their mesh,
// letter and mass per kind at spawn (BoardSetup), so the templates only
// carry the shared parts.
internal static class BoardBuilder
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const string AssetDir = "Assets/Materials/Janggi";
    private const string ChessDir = "Assets/Materials/Chess";
    private const string GonggiDir = "Assets/Materials/Gonggi";
    private const string BoardsDir = "Assets/Materials/Boards";
    private const float HexRadius = 1.8f;  // corner to centre
    private const float CrossHalf = 1.8f;  // centre to an arm's end
    private const float CrossArm = 1.0f;   // half an arm's width
    private const float GoWidth = 42.42f * ChessPieceMesh.UnitsPerCm;
    private const float Width = 2.8f;  // janggi: 9 files
    private const float Depth = 3.0f;  // 10 ranks, player to player
    private static readonly Rect BlackZone = new Rect(-1.25f, -1.35f, 2.5f, 1.05f); // go's too, now it's as narrow
    private static readonly float[] HingeX = { -0.8f, 0.8f };
    private const float ChessBorder = 2.2f * ChessPieceMesh.UnitsPerCm;
    private static float ChessSize => 8 * BoardSetup.ChessSquare + 2 * ChessBorder;
    private static readonly Rect ChessZone = new Rect(-1.45f, -1.5f, 2.9f, 1.1f);

    [MenuItem("Tools/Alkkagi/Build boards and piece templates")]
    private static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var setup = Object.FindAnyObjectByType<BoardSetup>();
        Directory.CreateDirectory(AssetDir);
        Directory.CreateDirectory(ChessDir);
        Directory.CreateDirectory(GonggiDir);
        Directory.CreateDirectory(BoardsDir);

        var go = WrapGoBoard(setup.transform);
        FitGoBoard(go);
        FitGoStones(setup);
        var janggi = BuildJanggiBoard(setup.transform, go);
        var chess = BuildChessBoard(setup.transform, go);
        var hexagon = BuildHexagonBoard(setup.transform, go);
        var cross = BuildCrossBoard(setup.transform, go);
        setup.boards = new[] { go, janggi, chess, hexagon, cross };
        setup.janggiTemplate = BuildPieceTemplate(setup);
        setup.chessTemplate = BuildChessTemplate(setup);
        setup.gonggiTemplate = BuildGonggiTemplate(setup);

        EditorUtility.SetDirty(setup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Alkkagi] Go, janggi, chess, hexagon and cross boards and the janggi, chess and gonggi piece templates built into GameScene.");
    }

    // The original board meshes move under a GoBoard variant, positions kept.
    private static BoardVariant WrapGoBoard(Transform boardRoot)
    {
        var existing = boardRoot.Find("GoBoard");
        if (existing != null) return existing.GetComponent<BoardVariant>();

        var root = new GameObject("GoBoard").transform;
        root.SetParent(boardRoot, false);
        var cube = boardRoot.Find("BoardCube");
        cube.SetParent(root, true);
        boardRoot.Find("Quad").SetParent(root, true);
        var variant = root.gameObject.AddComponent<BoardVariant>();
        variant.type = BoardType.Go;
        return variant;
    }

    // The original board was square; a real one is a little narrower than
    // it is long. Its cube and textured quad give way to the variant's own
    // board, their materials kept.
    private static void FitGoBoard(BoardVariant go)
    {
        var cube = go.transform.Find("BoardCube");
        var quad = go.transform.Find("Quad");
        if (cube != null)
        {
            go.sideMaterial = cube.GetComponent<MeshRenderer>().sharedMaterial;
            go.physics = cube.GetComponent<Collider>().sharedMaterial;
            Object.DestroyImmediate(cube.gameObject);
        }
        if (quad != null)
        {
            go.topMaterial = quad.GetComponent<MeshRenderer>().sharedMaterial;
            Object.DestroyImmediate(quad.gameObject);
        }
        Rectangle(go, GoWidth, Depth);
        go.blackZone = BlackZone;
        go.Build();
        EditorUtility.SetDirty(go);
    }

    // A rectangular board centred on the origin, its texture over all of it.
    private static void Rectangle(BoardVariant board, float width, float depth)
    {
        var rect = new Rect(-width / 2, -depth / 2, width, depth);
        board.parts = new[] { BoardPart.Rect(rect) };
        board.uvRect = rect;
    }

    // The go stones' eight boxes (a flat-topped disc) give way to one convex
    // collider of the stone's own shape (GoStoneCollider, set at spawn).
    // The big trigger box on the stone stays: it's what the death trigger
    // below the board catches, and the other pieces' masses are measured
    // against it.
    private static void FitGoStones(BoardSetup setup)
    {
        foreach (var stone in new[] { setup.blackTemplate, setup.whiteTemplate })
        {
            var boxes = stone.GetComponentsInChildren<BoxCollider>(true).Where(b => b.transform != stone.transform).ToArray();
            var physics = boxes.Select(b => b.sharedMaterial).FirstOrDefault(m => m != null);
            foreach (var box in boxes) Object.DestroyImmediate(box.gameObject);
            var collider = stone.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = stone.gameObject.AddComponent<MeshCollider>();
                collider.convex = true;
                collider.sharedMaterial = physics;
            }
            EditorUtility.SetDirty(stone.gameObject);
        }
    }

    private static BoardVariant BuildJanggiBoard(Transform boardRoot, BoardVariant go)
    {
        var old = boardRoot.Find("JanggiBoard");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("JanggiBoard").transform;
        root.SetParent(boardRoot, false);

        var boardMaterial = Material("JanggiBoard", Color.white, 0.2f, 0);
        boardMaterial.mainTexture = BoardTexture();

        var hinges = new GameObject("Hinges").transform;
        hinges.SetParent(root, false);
        var metal = Material("JanggiHinge", new Color(0.74f, 0.72f, 0.68f), 0.65f, 0.85f);
        foreach (var x in HingeX) BuildHinge(hinges, x, metal);

        var variant = root.gameObject.AddComponent<BoardVariant>();
        variant.type = BoardType.Janggi;
        variant.topMaterial = boardMaterial;
        variant.sideMaterial = Material("JanggiBoardSide", new Color(0.86f, 0.70f, 0.44f), 0.25f, 0);
        variant.physics = go.physics;
        Rectangle(variant, Width, Depth);
        variant.blackZone = BlackZone;
        variant.Build();
        root.gameObject.SetActive(false); // BoardSetup turns on the one in play
        return variant;
    }

    // A metal barrel along the fold with a flat leaf either side, standing
    // 0.010 proud (about an eighth of a piece's height).
    //
    // The collider is a separate, much wider capsule buried to the same
    // height: its gentle curve is what a piece meets, so a fast shot rides
    // up and over and a slow one is turned aside or stops on it. Against the
    // thin barrel itself a piece either stops dead (taller) or is flung
    // straight up. Every piece's own rise limit (GamePieceDragAndReleaseForce.
    // maxRiseSpeed) keeps the hop over it small.
    private static void BuildHinge(Transform parent, float x, Material metal)
    {
        const float proud = 0.010f;
        var hinge = new GameObject("Hinge").transform;
        hinge.SetParent(parent, false);
        hinge.localPosition = new Vector3(x, 0, 0);

        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "Barrel";
        Object.DestroyImmediate(barrel.GetComponent<Collider>());
        barrel.transform.SetParent(hinge, false);
        barrel.transform.localRotation = Quaternion.Euler(0, 0, 90); // axis along x, the fold
        barrel.transform.localScale = new Vector3(0.036f, 0.12f, 0.036f); // 0.24 long
        barrel.transform.localPosition = new Vector3(0, proud - 0.018f, 0);
        barrel.GetComponent<MeshRenderer>().sharedMaterial = metal;

        const float bumpRadius = 0.06f;
        var bump = new GameObject("Bump").AddComponent<CapsuleCollider>();
        bump.transform.SetParent(hinge, false);
        bump.direction = 0; // x
        bump.radius = bumpRadius;
        bump.height = 0.24f + 2 * bumpRadius;
        bump.center = new Vector3(0, proud - bumpRadius, 0);
        bump.sharedMaterial = HingePhysics();

        foreach (var side in new[] { -1, 1 })
        {
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = side < 0 ? "LeafBlack" : "LeafWhite";
            Object.DestroyImmediate(leaf.GetComponent<Collider>()); // flush with the board
            leaf.transform.SetParent(hinge, false);
            leaf.transform.localScale = new Vector3(0.22f, 0.004f, 0.05f);
            leaf.transform.localPosition = new Vector3(0, 0.002f, side * 0.035f);
            leaf.GetComponent<MeshRenderer>().sharedMaterial = metal;
        }
    }

    // A copy of the black go stone with its box colliders swapped for a
    // convex mesh collider and a letter on top.
    private static GamePieceDragAndReleaseForce BuildPieceTemplate(BoardSetup setup)
    {
        var templates = setup.blackTemplate.transform.parent;
        var old = templates.Find("JanggiTemplate");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var source = setup.blackTemplate;
        var physics = source.GetComponentsInChildren<Collider>(true).Select(c => c.sharedMaterial).FirstOrDefault(m => m != null);
        var piece = Object.Instantiate(source, templates);
        piece.name = "JanggiTemplate";
        piece.transform.localPosition = new Vector3(0, 0.005f, 0);
        piece.transform.localRotation = Quaternion.identity;
        for (var i = piece.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(piece.transform.GetChild(i).gameObject);
        foreach (var stoneCollider in piece.GetComponents<Collider>()) Object.DestroyImmediate(stoneCollider);
        var leftover = piece.GetComponent<PlayersManager>(); // came along from the old scene stones
        if (leftover != null) Object.DestroyImmediate(leftover);

        piece.GetComponent<MeshFilter>().sharedMesh = null; // BoardSetup builds one per size
        piece.GetComponent<MeshRenderer>().sharedMaterial = Material("JanggiPiece", new Color(0.90f, 0.79f, 0.57f), 0.35f, 0);
        var collider = piece.gameObject.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMaterial = physics;

        var label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshPro>();
        label.transform.SetParent(piece.transform, false);
        label.transform.localRotation = Quaternion.Euler(90, 0, 0); // flat, reading from its owner's side
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Kit/Fonts/Pretendard-Bold SDF.asset");
        label.text = Loc.Get("piece.cha");
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 0.1f;
        label.fontSizeMax = 5f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(0.15f, 0.15f);

        piece.gameObject.SetActive(false);
        return piece;
    }

    private static BoardVariant BuildChessBoard(Transform boardRoot, BoardVariant go)
    {
        var old = boardRoot.Find("ChessBoard");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("ChessBoard").transform;
        root.SetParent(boardRoot, false);

        var size = ChessSize;
        var boardMaterial = Material(ChessDir, "ChessBoard", Color.white, 0.3f, 0);
        boardMaterial.mainTexture = ChessTexture(size);

        var variant = root.gameObject.AddComponent<BoardVariant>();
        variant.type = BoardType.Chess;
        variant.topMaterial = boardMaterial;
        variant.sideMaterial = Material(ChessDir, "ChessBoardSide", new Color(0.36f, 0.24f, 0.15f), 0.3f, 0);
        variant.physics = go.physics;
        Rectangle(variant, size, size);
        variant.blackZone = ChessZone;
        variant.Build();
        root.gameObject.SetActive(false);
        return variant;
    }

    // Three sides, one to every other edge: a flat edge faces each (the
    // south's, then 120 and 240 degrees round), two play across from each
    // other. Rows a little closer than the square board's, to fit the
    // shorter edge.
    private static BoardVariant BuildHexagonBoard(Transform boardRoot, BoardVariant go)
    {
        var root = NewBoardRoot(boardRoot, "HexagonBoard");
        var variant = root.gameObject.AddComponent<BoardVariant>();
        variant.type = BoardType.Hexagon;
        variant.parts = new[] { BoardPart.Regular(6, HexRadius, 0) };
        var apothem = HexRadius * Mathf.Cos(30 * Mathf.Deg2Rad);
        variant.uvRect = Rect.MinMaxRect(-HexRadius, -apothem, HexRadius, apothem);
        var top = Material(BoardsDir, "HexagonBoard", Color.white, 0.25f, 0);
        top.mainTexture = HexagonTexture(variant.uvRect);
        variant.topMaterial = top;
        variant.sideMaterial = Material(BoardsDir, "HexagonBoardSide", new Color(0.55f, 0.36f, 0.20f), 0.3f, 0);
        variant.physics = go.physics;
        variant.blackZone = Rect.MinMaxRect(-0.75f, -1.4f, 0.75f, -0.4f);
        variant.multiSpacing = 0.28f;
        variant.sceneLayouts = false;
        variant.seats3 = new[] { 0f, 240f, 120f };
        variant.Build();
        root.gameObject.SetActive(false);
        return variant;
    }

    // Four sides, one at the end of each arm; two play along one arm to the
    // other, three leave the north's empty. The arms are two bars crossing.
    private static BoardVariant BuildCrossBoard(Transform boardRoot, BoardVariant go)
    {
        var root = NewBoardRoot(boardRoot, "CrossBoard");
        var variant = root.gameObject.AddComponent<BoardVariant>();
        variant.type = BoardType.Cross;
        variant.parts = new[]
        {
            BoardPart.Rect(Rect.MinMaxRect(-CrossHalf, -CrossArm, CrossHalf, CrossArm)),
            BoardPart.Rect(Rect.MinMaxRect(-CrossArm, -CrossHalf, CrossArm, CrossHalf)),
        };
        variant.uvRect = Rect.MinMaxRect(-CrossHalf, -CrossHalf, CrossHalf, CrossHalf);
        var top = Material(BoardsDir, "CrossBoard", Color.white, 0.25f, 0);
        top.mainTexture = CrossTexture();
        variant.topMaterial = top;
        variant.sideMaterial = Material(BoardsDir, "CrossBoardSide", new Color(0.62f, 0.44f, 0.26f), 0.3f, 0);
        variant.physics = go.physics;
        variant.blackZone = Rect.MinMaxRect(-0.8f, -1.65f, 0.8f, -0.9f);
        variant.multiZone = Rect.MinMaxRect(-0.75f, 0.15f, 0.75f, 0.75f);
        variant.multiSpacing = 0.28f;
        variant.sceneLayouts = false;
        variant.Build();
        root.gameObject.SetActive(false);
        return variant;
    }

    private static Transform NewBoardRoot(Transform boardRoot, string name)
    {
        var old = boardRoot.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject(name).transform;
        root.SetParent(boardRoot, false);
        return root;
    }

    // Like the janggi template, with the chess material (each side's colour
    // is set at spawn) and more angular damping: a piece lying on its side
    // would otherwise roll round and round, nothing on a flat board slowing it.
    private static GamePieceDragAndReleaseForce BuildChessTemplate(BoardSetup setup)
    {
        var templates = setup.blackTemplate.transform.parent;
        var old = templates.Find("ChessTemplate");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var source = setup.blackTemplate;
        var physics = source.GetComponentsInChildren<Collider>(true).Select(c => c.sharedMaterial).FirstOrDefault(m => m != null);
        var piece = Object.Instantiate(source, templates);
        piece.name = "ChessTemplate";
        piece.transform.localPosition = new Vector3(0, 0.005f, 0);
        piece.transform.localRotation = Quaternion.identity;
        for (var i = piece.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(piece.transform.GetChild(i).gameObject);
        foreach (var stoneCollider in piece.GetComponents<Collider>()) Object.DestroyImmediate(stoneCollider);
        var leftover = piece.GetComponent<PlayersManager>();
        if (leftover != null) Object.DestroyImmediate(leftover);

        piece.GetComponent<MeshFilter>().sharedMesh = null; // BoardSetup builds one per kind
        piece.GetComponent<MeshRenderer>().sharedMaterial = Material(ChessDir, "ChessPiece", Color.white, 0.55f, 0);
        var collider = piece.gameObject.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMaterial = physics;
        piece.GetComponent<Rigidbody>().angularDamping = 1.2f;

        piece.gameObject.SetActive(false);
        return piece;
    }

    // Like the chess template, with the shot inside a gonggi stone in its
    // physics. Loose shot soaks up part of a knock (a dead-blow hammer is
    // filled with it for that): it bounces less than a glass stone. And it
    // drags on the shell's spin - like a raw egg, which spins slowly and
    // stops soon - so the stone's spin dies away sooner. Mass and its low
    // centre are set at spawn (BoardSetup).
    private static GamePieceDragAndReleaseForce BuildGonggiTemplate(BoardSetup setup)
    {
        var templates = setup.blackTemplate.transform.parent;
        var old = templates.Find("GonggiTemplate");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var source = setup.blackTemplate;
        var stone = source.GetComponentsInChildren<Collider>(true).Select(c => c.sharedMaterial).FirstOrDefault(m => m != null);
        var piece = Object.Instantiate(source, templates);
        piece.name = "GonggiTemplate";
        piece.transform.localPosition = new Vector3(0, 0.005f, 0);
        piece.transform.localRotation = Quaternion.identity;
        for (var i = piece.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(piece.transform.GetChild(i).gameObject);
        foreach (var stoneCollider in piece.GetComponents<Collider>()) Object.DestroyImmediate(stoneCollider);
        var leftover = piece.GetComponent<PlayersManager>();
        if (leftover != null) Object.DestroyImmediate(leftover);

        piece.GetComponent<MeshFilter>().sharedMesh = null; // BoardSetup builds it (GonggiMesh)
        piece.GetComponent<MeshRenderer>().sharedMaterial = Material(GonggiDir, "GonggiPiece", Color.white, 0.8f, 0);
        var collider = piece.gameObject.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMaterial = GonggiPhysics(stone);
        piece.GetComponent<Rigidbody>().angularDamping = 1.8f;

        piece.gameObject.SetActive(false);
        return piece;
    }

    // Plastic on the board slides as a stone does; it bounces less. With no
    // bounce at all (as at first) a knocked stone went on with the one
    // that hit it instead of being knocked away.
    private static PhysicsMaterial GonggiPhysics(PhysicsMaterial stone)
    {
        var path = $"{GonggiDir}/Gonggi.physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial("Gonggi");
            AssetDatabase.CreateAsset(material, path);
        }
        material.dynamicFriction = stone.dynamicFriction;
        material.staticFriction = stone.staticFriction;
        material.frictionCombine = stone.frictionCombine;
        material.bounciness = 0.4f;
        material.bounceCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(material);
        return material;
    }

    // Smooth metal that doesn't bounce: bouncing is what launched pieces.
    private static PhysicsMaterial HingePhysics()
    {
        var path = $"{AssetDir}/Hinge.physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial("Hinge");
            AssetDatabase.CreateAsset(material, path);
        }
        material.dynamicFriction = 0.2f;
        material.staticFriction = 0.2f;
        material.bounciness = 0f;
        material.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Material(string name, Color color, float smoothness, float metallic) => Material(AssetDir, name, color, smoothness, metallic);

    private static Material Material(string dir, string name, Color color, float smoothness, float metallic)
    {
        var path = $"{dir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---- Board texture ----

    private static Texture2D BoardTexture()
    {
        const int w = 1024;
        var h = Mathf.RoundToInt(w * Depth / Width);
        var pixels = new Color[w * h];
        var wood = new Color(0.93f, 0.80f, 0.55f);
        var ink = new Color(0.20f, 0.13f, 0.07f);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            // Long grain running across the board, a little uneven.
            var grain = Mathf.PerlinNoise(x * 0.004f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.02f, y * 0.25f) * 0.4f;
            pixels[y * w + x] = wood * (0.90f + 0.12f * grain);
        }

        // Grid: 9 files x 10 ranks, inset from the edge like a real board.
        float Fx(float file) => w * (0.07f + 0.86f * file / 8f);
        float Ry(float rank) => h * (0.06f + 0.88f * rank / 9f);
        for (var file = 0; file <= 8; file++) Line(pixels, w, h, Fx(file), Ry(0), Fx(file), Ry(9), file == 0 || file == 8 ? 4f : 2.5f, ink);
        for (var rank = 0; rank <= 9; rank++) Line(pixels, w, h, Fx(0), Ry(rank), Fx(8), Ry(rank), rank == 0 || rank == 9 ? 4f : 2.5f, ink);
        // Both palaces: files 3-5, ranks 0-2 and 7-9, crossed corner to corner.
        foreach (var bottom in new[] { 0, 7 })
        {
            Line(pixels, w, h, Fx(3), Ry(bottom), Fx(5), Ry(bottom + 2), 2.5f, ink);
            Line(pixels, w, h, Fx(5), Ry(bottom), Fx(3), Ry(bottom + 2), 2.5f, ink);
        }
        // The fold: a thin dark seam right across, where the hinges sit.
        Line(pixels, w, h, 0, h / 2f, w, h / 2f, 1.5f, new Color(0.35f, 0.25f, 0.15f));

        var path = $"{AssetDir}/JanggiBoard.png";
        var texture = new Texture2D(w, h, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Eight by eight, a1 dark (light on each player's right), maple and
    // walnut with the grain running along the files, in a darker frame.
    private static Texture2D ChessTexture(float size)
    {
        const int w = 1024;
        var pixels = new Color[w * w];
        var light = new Color(0.92f, 0.82f, 0.64f);
        var dark = new Color(0.58f, 0.39f, 0.24f);
        var frame = new Color(0.36f, 0.24f, 0.15f);
        var inlay = new Color(0.80f, 0.66f, 0.45f);
        var border = ChessBorder / size * w;
        var square = (w - 2 * border) / 8f;
        for (var y = 0; y < w; y++)
        for (var x = 0; x < w; x++)
        {
            var grain = Mathf.PerlinNoise(x * 0.05f, y * 0.004f) * 0.6f + Mathf.PerlinNoise(x * 0.25f, y * 0.02f) * 0.4f;
            var fx = (x - border) / square;
            var fy = (y - border) / square;
            Color wood;
            if (fx < 0 || fy < 0 || fx >= 8 || fy >= 8) wood = frame;
            else wood = (Mathf.FloorToInt(fx) + Mathf.FloorToInt(fy)) % 2 == 0 ? dark : light;
            pixels[y * w + x] = wood * (0.90f + 0.12f * grain);
        }
        // A thin inlay round the squares.
        var edge = border - 3;
        Line(pixels, w, w, edge, edge, w - edge, edge, 2.5f, inlay);
        Line(pixels, w, w, w - edge, edge, w - edge, w - edge, 2.5f, inlay);
        Line(pixels, w, w, w - edge, w - edge, edge, w - edge, 2.5f, inlay);
        Line(pixels, w, w, edge, w - edge, edge, edge, 2.5f, inlay);

        var path = $"{ChessDir}/ChessBoard.png";
        var texture = new Texture2D(w, w, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Light maple, a triangle lattice of shallow dimples over the middle (a
    // diamond-game board's holes, none of its star), a darker wash on each
    // side's home ground, and an inlaid line round the edge.
    private static Texture2D HexagonTexture(Rect area)
    {
        const int w = 1024;
        var h = Mathf.RoundToInt(w * area.height / area.width);
        var pixels = new Color[w * h];
        var wood = new Color(0.90f, 0.76f, 0.52f);
        var home = new Color(0.80f, 0.64f, 0.42f);
        var dimple = new Color(0.55f, 0.40f, 0.24f);
        var inlay = new Color(0.42f, 0.27f, 0.15f);
        float Px(float x) => (x - area.xMin) / area.width * w;
        float Py(float z) => (z - area.yMin) / area.height * h;
        var apothem = HexRadius * Mathf.Cos(30 * Mathf.Deg2Rad);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var world = new Vector2(area.xMin + (x + 0.5f) / w * area.width, area.yMin + (y + 0.5f) / h * area.height);
            var grain = Mathf.PerlinNoise(x * 0.004f, y * 0.05f) * 0.6f + Mathf.PerlinNoise(x * 0.02f, y * 0.22f) * 0.4f;
            var colour = wood;
            // Home ground: within 0.75 of the south edge and of the edges 120 and 240 degrees round.
            for (var k = 0; k < 3; k++)
            {
                var a = (270 + 120 * k) * Mathf.Deg2Rad;
                var along = Vector2.Dot(world, new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                if (along > apothem - 0.75f) colour = Color.Lerp(wood, home, 0.55f);
            }
            pixels[y * w + x] = colour * (0.90f + 0.12f * grain);
        }
        // Dimples: a darker dot with a lighter rim, 0.2 apart, inside the edge.
        const float pitch = 0.2f;
        var dotRadius = 0.022f / area.width * w;
        for (var row = -10; row <= 10; row++)
        for (var col = -12; col <= 12; col++)
        {
            var p = new Vector2((col + (row & 1) * 0.5f) * pitch, row * pitch * Mathf.Sqrt(3) / 2);
            if (!new BoardShape(new[] { BoardPart.Regular(6, HexRadius - 0.14f, 0) }).Contains(p)) continue;
            Dot(pixels, w, h, Px(p.x), Py(p.y), dotRadius, dimple);
        }
        // The inlay, just in from the edge.
        var corners = BoardPart.Regular(6, HexRadius - 0.07f, 0).corners;
        for (var i = 0; i < 6; i++)
            Line(pixels, w, h, Px(corners[i].x), Py(corners[i].y), Px(corners[(i + 1) % 6].x), Py(corners[(i + 1) % 6].y), 3f, inlay);
        return SaveTexture(pixels, w, h, $"{BoardsDir}/HexagonBoard.png");
    }

    // Walnut-toned wood lined like a go board along both bars, the lines
    // stopping short of the edges, an inlaid line round the cross and a
    // star point at the middle and in each arm.
    private static Texture2D CrossTexture()
    {
        const int w = 1024;
        var pixels = new Color[w * w];
        var wood = new Color(0.86f, 0.70f, 0.47f);
        var ink = new Color(0.22f, 0.15f, 0.08f);
        var inlay = new Color(0.45f, 0.30f, 0.16f);
        float P(float v) => (v + CrossHalf) / (2 * CrossHalf) * w;
        for (var y = 0; y < w; y++)
        for (var x = 0; x < w; x++)
        {
            var grain = Mathf.PerlinNoise(x * 0.004f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.02f, y * 0.25f) * 0.4f;
            pixels[y * w + x] = wood * (0.90f + 0.12f * grain);
        }
        const float pitch = 0.2f, lineIn = 0.2f;
        for (var i = -8; i <= 8; i++)
        {
            // A line across the middle runs the bar's length; one out in an
            // arm only its width.
            var v = i * pitch;
            var reach = Mathf.Abs(v) <= CrossArm - lineIn + 1e-4f ? CrossHalf - lineIn : CrossArm - lineIn;
            Line(pixels, w, w, P(-reach), P(v), P(reach), P(v), 2.2f, ink);
            Line(pixels, w, w, P(v), P(-reach), P(v), P(reach), 2.2f, ink);
        }
        foreach (var star in new[] { Vector2.zero, new Vector2(0, 1.2f), new Vector2(0, -1.2f), new Vector2(1.2f, 0), new Vector2(-1.2f, 0) })
            Dot(pixels, w, w, P(star.x), P(star.y), 0.03f / (2 * CrossHalf) * w, ink);
        // The inlay: the cross's outline, 0.07 in.
        const float e = CrossHalf - 0.07f, a = CrossArm - 0.07f;
        var outline = new[]
        {
            new Vector2(a, -e), new Vector2(a, -a), new Vector2(e, -a), new Vector2(e, a), new Vector2(a, a), new Vector2(a, e),
            new Vector2(-a, e), new Vector2(-a, a), new Vector2(-e, a), new Vector2(-e, -a), new Vector2(-a, -a), new Vector2(-a, -e),
        };
        for (var i = 0; i < outline.Length; i++)
            Line(pixels, w, w, P(outline[i].x), P(outline[i].y), P(outline[(i + 1) % outline.Length].x), P(outline[(i + 1) % outline.Length].y), 3f, inlay);
        return SaveTexture(pixels, w, w, $"{BoardsDir}/CrossBoard.png");
    }

    private static Texture2D SaveTexture(Color[] pixels, int w, int h, string path)
    {
        var texture = new Texture2D(w, h, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // A filled, anti-aliased disc blended over what's there.
    private static void Dot(Color[] pixels, int w, int h, float cx, float cy, float radius, Color color)
    {
        for (var y = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 1)); y <= Mathf.Min(h - 1, Mathf.CeilToInt(cy + radius + 1)); y++)
        for (var x = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 1)); x <= Mathf.Min(w - 1, Mathf.CeilToInt(cx + radius + 1)); x++)
        {
            var coverage = Mathf.Clamp01(radius + 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)));
            if (coverage > 0) pixels[y * w + x] = Color.Lerp(pixels[y * w + x], color, coverage);
        }
    }

    // Anti-aliased line of the given pixel width, blended over what's there.
    private static void Line(Color[] pixels, int w, int h, float x0, float y0, float x1, float y1, float width, Color color)
    {
        var half = width / 2f;
        var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - half - 1));
        var maxX = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + half + 1));
        var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - half - 1));
        var maxY = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + half + 1));
        var a = new Vector2(x0, y0);
        var ab = new Vector2(x1, y1) - a;
        var lengthSq = Mathf.Max(ab.sqrMagnitude, 1e-6f);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            var distance = Vector2.Distance(p, a + ab * t);
            var coverage = Mathf.Clamp01(half + 0.5f - distance);
            if (coverage <= 0) continue;
            var i = y * w + x;
            pixels[i] = Color.Lerp(pixels[i], color, coverage);
        }
    }
}
