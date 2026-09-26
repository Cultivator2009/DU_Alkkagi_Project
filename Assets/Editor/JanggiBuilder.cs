using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the janggi board variant and the janggi piece template into
// GameScene, and wraps the existing go board as a variant too. Safe to run
// again: it replaces what it made before.
//
// The board: a generated wood texture with the 9 x 10 grid and both
// palaces, and two metal hinges across the fold in the middle - on a real
// folding board they stand proud of the surface, and in alkkagi they're
// obstacles. Pieces get their mesh, letter and mass per kind at spawn
// (BoardSetup), so the template only carries the shared parts.
internal static class JanggiBuilder
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const string AssetDir = "Assets/Materials/Janggi";
    private const float Width = 2.8f;  // 9 files
    private const float Depth = 3.0f;  // 10 ranks, player to player
    private static readonly Rect BlackZone = new Rect(-1.25f, -1.35f, 2.5f, 1.05f);
    private static readonly float[] HingeX = { -0.8f, 0.8f };

    [MenuItem("Tools/Alkkagi/Build boards and janggi pieces")]
    private static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var setup = Object.FindAnyObjectByType<BoardSetup>();
        Directory.CreateDirectory(AssetDir);

        var go = WrapGoBoard(setup.transform);
        var janggi = BuildJanggiBoard(setup.transform, go);
        setup.boards = new[] { go, janggi };
        setup.janggiTemplate = BuildPieceTemplate(setup);

        EditorUtility.SetDirty(setup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Alkkagi] Go and janggi boards and the janggi piece template built into GameScene.");
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
        variant.surface = cube.GetComponent<Collider>();
        return variant;
    }

    private static BoardVariant BuildJanggiBoard(Transform boardRoot, BoardVariant go)
    {
        var old = boardRoot.Find("JanggiBoard");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("JanggiBoard").transform;
        root.SetParent(boardRoot, false);

        var goCube = go.surface;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "BoardCube";
        cube.transform.SetParent(root, false);
        cube.transform.localPosition = new Vector3(0, -0.25f, 0);
        cube.transform.localScale = new Vector3(Width, 0.5f, Depth);
        cube.GetComponent<MeshRenderer>().sharedMaterial = Material("JanggiBoardSide", new Color(0.86f, 0.70f, 0.44f), 0.25f, 0);
        cube.GetComponent<Collider>().sharedMaterial = goCube.sharedMaterial;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Quad";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(root, false);
        quad.transform.localPosition = new Vector3(0, 0.0005f, 0);
        quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
        quad.transform.localScale = new Vector3(Width, Depth, 1);
        var boardMaterial = Material("JanggiBoard", Color.white, 0.2f, 0);
        boardMaterial.mainTexture = BoardTexture();
        quad.GetComponent<MeshRenderer>().sharedMaterial = boardMaterial;

        var hinges = new GameObject("Hinges").transform;
        hinges.SetParent(root, false);
        var metal = Material("JanggiHinge", new Color(0.74f, 0.72f, 0.68f), 0.65f, 0.85f);
        foreach (var x in HingeX) BuildHinge(hinges, x, metal);

        var variant = root.gameObject.AddComponent<BoardVariant>();
        variant.type = BoardType.Janggi;
        variant.surface = cube.GetComponent<Collider>();
        variant.blackZone = BlackZone;
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
        foreach (var box in piece.GetComponents<BoxCollider>()) Object.DestroyImmediate(box);
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

    private static Material Material(string name, Color color, float smoothness, float metallic)
    {
        var path = $"{AssetDir}/{name}.mat";
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
