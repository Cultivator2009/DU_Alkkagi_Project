using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The room the boards are played in, in GameScene and behind the main menu:
// a hanok hall at night. The board sits on the hall's wooden floor (a
// 우물마루, short boards framed by beams, falling into shade away from the
// board), pillars and lintels round it, lattice doors glowing faintly with
// the moon behind their paper, rafters above. By the board, the stones'
// bowls, two cushions, a low tea table and an oil lamp, whose light is the
// room's warmth. All drawn here (textures too); Tools > Alkkagi > Build room
// puts it in GameScene in place of what was there, and saves the lobby's
// backdrop (the floor round the board, from above). Tools > Alkkagi >
// Build menu demo puts it behind the menu.
public static class RoomBuilder
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const string Dir = "Assets/Materials/Room";
    public const string BackdropPath = Dir + "/Backdrop.png";
    public const string Name = "Room";
    public const float Top = -0.495f;            // the floor: just under the boards' foot (-0.5 + a hair), where a broken piece's shards come to rest (PieceShatter.TableTop)
    public static readonly Vector2 Half = new Vector2(23, 17); // inside the walls
    public const float Height = 14f;            // floor to ceiling (CameraRig.ceiling stays under it)
    private static readonly Vector2 FloorTile = new Vector2(3f, 2.4f);

    [MenuItem("Tools/Alkkagi/Build room")]
    private static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == Name || g.name == "Table" || g.name == "Environment_House"))
            Object.DestroyImmediate(old);
        var room = Make();
        Backdrop(room);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Alkkagi] Room built into GameScene.");
    }

    public static GameObject Make()
    {
        var m = Materials();
        var room = new GameObject(Name);
        var t = room.transform;

        // The floor, the walls' foot to foot.
        Box(t, "Floor", new Vector3(0, Top - 0.05f, 0), new Vector3(Half.x * 2 + 2, 0.1f, Half.y * 2 + 2), m.floor);

        // Pillars every few metres, a sill and a lintel between them, the
        // middle bays doors and the rest plaster.
        var walls = new GameObject("Walls").transform;
        walls.SetParent(t, false);
        Wall(walls, new Vector3(0, 0, Half.y), Vector3.right, Half.x, 8, m);
        Wall(walls, new Vector3(0, 0, -Half.y), Vector3.left, Half.x, 8, m);
        Wall(walls, new Vector3(Half.x, 0, 0), Vector3.back, Half.y, 6, m);
        Wall(walls, new Vector3(-Half.x, 0, 0), Vector3.forward, Half.y, 6, m);

        // The ceiling: boards, rafters under them, two great beams across.
        var ceiling = new GameObject("Ceiling").transform;
        ceiling.SetParent(t, false);
        var high = Top + Height;
        Box(ceiling, "Boards", new Vector3(0, high + 0.1f, 0), new Vector3(Half.x * 2 + 2, 0.2f, Half.y * 2 + 2), m.ceiling);
        for (var x = -Half.x + 0.6f; x < Half.x; x += 1.3f)
            Box(ceiling, "Rafter", new Vector3(x, high - 0.18f, 0), new Vector3(0.36f, 0.36f, Half.y * 2), m.wood);
        foreach (var z in new[] { -6.5f, 6.5f })
            Box(ceiling, "Beam", new Vector3(0, high - 0.9f, z), new Vector3(Half.x * 2, 1.1f, 0.95f), m.pillar);

        // The house round the board casts no shadow: the light that draws the
        // board comes from above it, through where the rafters would be.
        foreach (var renderer in room.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Props(t, m);
        return room;
    }

    // ---- The walls ----

    // One wall: centre at the floor's edge, along its length (the room on
    // its right as you walk it), bays between pillars.
    private static void Wall(Transform parent, Vector3 centre, Vector3 along, float half, int bays, Mats m)
    {
        var wall = new GameObject("Wall").transform;
        wall.SetParent(parent, false);
        var inward = Vector3.Cross(Vector3.up, along); // the room's side
        var bay = half * 2 / bays;
        const float sill = 1.7f, lintel = 9.6f, thick = 0.5f;
        var high = Top + Height;
        for (var i = 0; i <= bays; i++)
        {
            var at = centre + along * (-half + i * bay);
            Box(wall, "Pillar", at + Vector3.up * (Top + Height / 2), Turned(new Vector3(0.8f, Height, 0.8f), along), m.pillar);
        }
        Vector3 Span(float from, float to) => Turned(new Vector3(half * 2, to - from, thick), along);
        Box(wall, "Sill", centre + inward * 0.05f + Vector3.up * (Top + sill - 0.25f), Span(0, 0.5f), m.pillar);
        Box(wall, "Lintel", centre + inward * 0.05f + Vector3.up * (Top + lintel + 0.3f), Span(0, 0.6f), m.pillar);
        Box(wall, "Foot", centre + Vector3.up * (Top + (sill - 0.5f) / 2), Span(0, sill - 0.5f), m.wood);
        Box(wall, "Upper", centre + Vector3.up * ((Top + lintel + 0.6f + high) / 2), Span(Top + lintel + 0.6f, high), m.plaster);
        for (var i = 0; i < bays; i++)
        {
            var middle = centre + along * (-half + (i + 0.5f) * bay);
            var door = i > 0 && i < bays - 1;
            var height = lintel - sill;
            var size = Turned(new Vector3(bay - 0.8f, height, door ? 0.12f : thick), along);
            var y = Top + sill + height / 2;
            if (!door)
            {
                Box(wall, "Plaster", middle + Vector3.up * y, size, m.plaster);
                continue;
            }
            // Two leaves to a bay, a little in from the plaster's face.
            for (var leaf = 0; leaf < 2; leaf++)
            {
                var leafSize = Turned(new Vector3((bay - 0.8f) / 2, height, 0.12f), along);
                var offset = along * ((leaf - 0.5f) * (bay - 0.8f) / 2);
                var panel = Box(wall, "Door", middle + offset + Vector3.up * y, leafSize, m.door);
                // The quad's front toward the room.
                panel.transform.rotation = Quaternion.LookRotation(-inward);
                panel.transform.localScale = new Vector3((bay - 0.8f) / 2, height, 0.12f);
            }
        }
    }

    // A box measured along x set to run along the given way.
    private static Vector3 Turned(Vector3 size, Vector3 along) =>
        Mathf.Abs(along.x) > 0.5f ? size : new Vector3(size.z, size.y, size.x);

    // ---- By the board ----

    private static void Props(Transform parent, Mats m)
    {
        var props = new GameObject("Props").transform;
        props.SetParent(parent, false);

        // The stones' bowls, lids beside them.
        Bowl(props, new Vector3(-3.8f, 0, -1.9f), m.bowl, m.stoneBlack, 20);
        Bowl(props, new Vector3(3.8f, 0, 1.9f), m.bowl, m.stoneWhite, -35);

        // Two cushions where the players sit.
        foreach (var z in new[] { -4.6f, 4.6f })
        {
            var cushion = Box(props, "Cushion", new Vector3(0, Top + 0.09f, z), new Vector3(2.1f, 0.18f, 2.1f), m.cushion);
            cushion.transform.rotation = Quaternion.Euler(0, z > 0 ? 4 : -3, 0);
        }

        // A low round table, a teapot and two cups on it.
        var table = new GameObject("Tea table").transform;
        table.SetParent(props, false);
        table.localPosition = new Vector3(-6.2f, 0, 3.4f);
        Cylinder(table, "Top", new Vector3(0, Top + 0.62f, 0), 1.05f, 0.08f, m.wood);
        Cylinder(table, "Rim", new Vector3(0, Top + 0.55f, 0), 0.98f, 0.06f, m.pillar);
        for (var i = 0; i < 4; i++)
        {
            var a = (45 + i * 90) * Mathf.Deg2Rad;
            Box(table, "Leg", new Vector3(Mathf.Cos(a) * 0.7f, Top + 0.27f, Mathf.Sin(a) * 0.7f), new Vector3(0.1f, 0.54f, 0.1f), m.pillar);
        }
        Cylinder(table, "Teapot", new Vector3(0.15f, Top + 0.82f, 0.2f), 0.24f, 0.32f, m.celadon);
        Sphere(table, "Lid", new Vector3(0.15f, Top + 0.99f, 0.2f), new Vector3(0.3f, 0.12f, 0.3f), m.celadon);
        Cylinder(table, "Cup", new Vector3(-0.45f, Top + 0.73f, -0.25f), 0.11f, 0.14f, m.celadon);
        Cylinder(table, "Cup", new Vector3(0.35f, Top + 0.73f, -0.5f), 0.11f, 0.14f, m.celadon);

        // An oil lamp on a stand: the room's warm light.
        var lamp = new GameObject("Lamp").transform;
        lamp.SetParent(props, false);
        lamp.localPosition = new Vector3(6.0f, 0, -3.6f);
        Cylinder(lamp, "Foot", new Vector3(0, Top + 0.06f, 0), 0.42f, 0.12f, m.brass);
        Cylinder(lamp, "Stem", new Vector3(0, Top + 1.0f, 0), 0.05f, 1.9f, m.pillar);
        Cylinder(lamp, "Dish", new Vector3(0, Top + 1.97f, 0), 0.26f, 0.06f, m.brass);
        Sphere(lamp, "Flame", new Vector3(0, Top + 2.1f, 0), new Vector3(0.07f, 0.16f, 0.07f), m.flame);
        var light = new GameObject("Lamp light").AddComponent<Light>();
        light.transform.SetParent(lamp, false);
        light.transform.localPosition = new Vector3(0, Top + 2.25f, 0);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.42f);
        light.intensity = 1.5f;
        light.range = 9f;
        light.shadows = LightShadows.None;
    }

    private static void Bowl(Transform parent, Vector3 at, Material wood, Material stones, float turn)
    {
        var bowl = new GameObject("Bowl").transform;
        bowl.SetParent(parent, false);
        bowl.localPosition = at;
        bowl.localRotation = Quaternion.Euler(0, turn, 0);
        Cylinder(bowl, "Body", new Vector3(0, Top + 0.22f, 0), 0.42f, 0.44f, wood);
        Sphere(bowl, "Belly", new Vector3(0, Top + 0.26f, 0), new Vector3(0.92f, 0.36f, 0.92f), wood);
        // A heap of its stones showing over the rim.
        Sphere(bowl, "Stones", new Vector3(0, Top + 0.44f, 0), new Vector3(0.7f, 0.12f, 0.7f), stones);
        // The lid off, upside down beside it.
        Sphere(bowl, "Lid", new Vector3(0.85f, Top + 0.07f, 0.1f), new Vector3(0.86f, 0.16f, 0.86f), wood);
    }

    // ---- Shapes ----

    private static GameObject Box(Transform parent, string name, Vector3 at, Vector3 size, Material material) =>
        Shape(PrimitiveType.Cube, parent, name, at, size, material);

    private static GameObject Cylinder(Transform parent, string name, Vector3 at, float radius, float height, Material material) =>
        Shape(PrimitiveType.Cylinder, parent, name, at, new Vector3(radius * 2, height / 2, radius * 2), material);

    private static GameObject Sphere(Transform parent, string name, Vector3 at, Vector3 size, Material material) =>
        Shape(PrimitiveType.Sphere, parent, name, at, size, material);

    private static GameObject Shape(PrimitiveType type, Transform parent, string name, Vector3 at, Vector3 size, Material material)
    {
        var shape = GameObject.CreatePrimitive(type);
        shape.name = name;
        Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = at;
        shape.transform.localScale = size;
        var renderer = shape.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        shape.isStatic = true;
        return shape;
    }

    // ---- The lobby's backdrop: the floor round the board, from above ----

    private static void Backdrop(GameObject room)
    {
        const int w = 1920, h = 1080;
        const int layer = 31;
        var layers = room.GetComponentsInChildren<Transform>(true).ToDictionary(x => x, x => x.gameObject.layer);
        foreach (var x in layers.Keys) x.gameObject.layer = layer;
        var camera = new GameObject("Backdrop camera").AddComponent<Camera>();
        camera.cullingMask = 1 << layer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.fieldOfView = 50;
        camera.transform.SetPositionAndRotation(new Vector3(0, 7.5f, -4.2f), Quaternion.Euler(64, 0, 0));
        var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var picture = new Texture2D(w, h, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        // Darker, and more so toward the edges, for the cards over it.
        var pixels = picture.GetPixels();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var u = (x / (float)w - 0.5f) * 2;
            var v = (y / (float)h - 0.5f) * 2;
            pixels[y * w + x] *= 0.62f * Mathf.Lerp(1f, 0.45f, Mathf.SmoothStep(0.3f, 1.2f, Mathf.Sqrt(u * u * 0.6f + v * v)));
        }
        picture.SetPixels(pixels);
        picture.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(BackdropPath, picture.EncodeToPNG());
        Object.DestroyImmediate(picture);
        Object.DestroyImmediate(camera.gameObject);
        target.Release();
        Object.DestroyImmediate(target);
        foreach (var kv in layers) kv.Key.gameObject.layer = kv.Value;
        AssetDatabase.ImportAsset(BackdropPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(BackdropPath);
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    // ---- Materials and their textures ----

    private struct Mats
    {
        public Material floor, wood, pillar, plaster, door, ceiling, bowl, stoneBlack, stoneWhite, cushion, celadon, brass, flame;
    }

    private static Mats Materials()
    {
        Directory.CreateDirectory(Dir);
        var grain = Texture("Grain", 512, 512, Grain, true);
        var floor = Material("Floor", Shader.Find("Alkkagi/RoomFloor"), Color.white, 0.32f);
        floor.mainTexture = Texture("Maru", 1500, 1200, Maru, true);
        floor.SetVector("_Tile", new Vector4(FloorTile.x, FloorTile.y, 0, 0));
        floor.SetFloat("_ShadeFrom", 4.5f);
        floor.SetFloat("_ShadeTo", 17f);
        floor.SetFloat("_ShadeMin", 0.32f);

        Material Wood(string name, Color color, float gloss, Vector2 tiling)
        {
            var material = Material(name, Shader.Find("Standard"), color, gloss);
            material.mainTexture = grain;
            material.mainTextureScale = tiling;
            return material;
        }

        var door = Material("Door", Shader.Find("Standard"), Color.white, 0.05f);
        var lattice = Texture("Lattice", 512, 1024, Lattice, false);
        door.mainTexture = lattice;
        door.EnableKeyword("_EMISSION");
        door.SetTexture("_EmissionMap", Texture("LatticeGlow", 512, 1024, LatticeGlow, false));
        door.SetColor("_EmissionColor", new Color(0.36f, 0.42f, 0.56f));
        door.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        var cushion = Material("Cushion", Shader.Find("Standard"), Color.white, 0.18f);
        cushion.mainTexture = Texture("Cushion", 512, 512, Cushion, false);

        var flame = Material("Flame", Shader.Find("Standard"), new Color(1f, 0.8f, 0.5f), 0f);
        flame.EnableKeyword("_EMISSION");
        flame.SetColor("_EmissionColor", new Color(1f, 0.62f, 0.25f) * 3f);
        flame.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        var m = new Mats
        {
            floor = floor,
            wood = Wood("Wood", new Color(0.55f, 0.4f, 0.27f), 0.25f, new Vector2(1, 4)),
            pillar = Wood("Pillar", new Color(0.3f, 0.19f, 0.12f), 0.22f, new Vector2(1, 6)),
            ceiling = Wood("Ceiling", new Color(0.36f, 0.25f, 0.17f), 0.1f, new Vector2(8, 2)),
            plaster = Material("Plaster", Shader.Find("Standard"), new Color(0.86f, 0.81f, 0.71f), 0.04f),
            door = door,
            bowl = Wood("Bowl", new Color(0.62f, 0.36f, 0.2f), 0.62f, new Vector2(1, 2)),
            stoneBlack = Material("StonesBlack", Shader.Find("Standard"), new Color(0.06f, 0.06f, 0.07f), 0.7f),
            stoneWhite = Material("StonesWhite", Shader.Find("Standard"), new Color(0.92f, 0.91f, 0.87f), 0.6f),
            cushion = cushion,
            celadon = Material("Celadon", Shader.Find("Standard"), new Color(0.55f, 0.7f, 0.62f), 0.75f),
            brass = Material("Brass", Shader.Find("Standard"), new Color(0.62f, 0.47f, 0.24f), 0.55f),
            flame = flame,
        };
        m.brass.SetFloat("_Metallic", 0.7f);
        AssetDatabase.SaveAssets();
        return m;
    }

    private static Material Material(string name, Shader shader, Color color, float gloss)
    {
        var path = $"{Dir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.color = color;
        material.SetFloat("_Glossiness", gloss);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D Texture(string name, int w, int h, System.Func<float, float, Color> paint, bool repeat)
    {
        var pixels = new Color[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            pixels[y * w + x] = paint((x + 0.5f) / w, (y + 0.5f) / h);
        var file = $"{Dir}/{name}.png";
        var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
        texture.SetPixels(pixels);
        File.WriteAllBytes(file, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(file);
        importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 2048;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
    }

    // Wood grain along v, tiling both ways (noise on a torus).
    private static float Fibre(float u, float v, float across, float alongScale, float seed)
    {
        var a = u * Mathf.PI * 2;
        var nx = Mathf.Cos(a) * across / (Mathf.PI * 2);
        var ny = Mathf.Sin(a) * across / (Mathf.PI * 2);
        var wave = Mathf.PerlinNoise(nx + seed, ny + v * alongScale * 0.15f);
        return Mathf.PerlinNoise(nx * 1.0f + seed * 3 + wave * 2f, ny + v * alongScale) * 0.6f
             + Mathf.PerlinNoise(nx * 4f + seed, ny * 4f + v * alongScale * 3f) * 0.4f;
    }

    private static Color Grain(float u, float v)
    {
        var g = Mathf.SmoothStep(0.25f, 0.8f, Fibre(u, Mathf.Sin(v * Mathf.PI * 2) * 0.5f + 0.5f, 26, 1, 2.3f));
        return Color.Lerp(new Color(0.62f, 0.62f, 0.62f), Color.white, g);
    }

    // One tile of the 우물마루 (FloorTile in world units): a long beam along
    // x at its foot, a short one along z at its left, and between them five
    // boards along x, each its own shade, a dark gap round every piece.
    private static Color Maru(float u, float v)
    {
        var light = new Color(0.56f, 0.38f, 0.23f);
        var dark = new Color(0.29f, 0.18f, 0.1f);
        var x = u * FloorTile.x;
        var y = v * FloorTile.y;
        const float beam = 0.34f, cross = 0.3f, gap = 0.012f;
        float grain, shade;
        bool seam;
        if (y < beam)
        {
            // The long beam: its grain along x.
            grain = Mathf.PerlinNoise(x * 1.6f, y * 30f) * 0.6f + Mathf.PerlinNoise(x * 7f, y * 80f) * 0.4f;
            shade = 0.82f;
            seam = y < gap || y > beam - gap || x < gap * 2; // a joint where one beam meets the next
        }
        else if (x < cross)
        {
            // The short beam: its grain along z.
            grain = Mathf.PerlinNoise(x * 30f + 7, y * 1.6f) * 0.6f + Mathf.PerlinNoise(x * 80f, y * 7f + 3) * 0.4f;
            shade = 0.88f;
            seam = x < gap || x > cross - gap;
        }
        else
        {
            const int boards = 5;
            var width = (FloorTile.y - beam) / boards;
            var b = Mathf.FloorToInt((y - beam) / width);
            var within = y - beam - b * width;
            grain = Mathf.PerlinNoise(x * 1.8f + b * 13.1f, within * 26f + b * 5f) * 0.6f + Mathf.PerlinNoise(x * 9f + b * 3f, within * 90f) * 0.4f;
            shade = 0.92f + Mathf.PerlinNoise(b * 3.7f, 0.5f) * 0.16f;
            seam = within < gap || within > width - gap || x > FloorTile.x - gap;
        }
        var c = Color.Lerp(dark, light, Mathf.SmoothStep(0.2f, 0.85f, grain)) * shade;
        if (seam) c *= 0.4f;
        c.a = 1;
        return c;
    }

    // 띠살: a door's frame, thin bars close together up and down, bands of
    // three across at its top, middle and foot, the paper between.
    private static bool Bar(float u, float v)
    {
        const float frame = 0.07f;
        if (u < frame || u > 1 - frame || v < frame * 0.55f || v > 1 - frame * 0.55f) return true;
        var x = (u - frame) / (1 - frame * 2);
        if (Mathf.Repeat(x * 11, 1f) < 0.11f) return true;
        foreach (var band in new[] { 0.16f, 0.5f, 0.84f })
            for (var k = -1; k <= 1; k++)
                if (Mathf.Abs(v - band - k * 0.022f) < 0.0045f) return true;
        return false;
    }

    private static Color Lattice(float u, float v) =>
        Bar(u, v) ? new Color(0.27f, 0.17f, 0.1f) : new Color(0.9f, 0.86f, 0.76f);

    private static Color LatticeGlow(float u, float v) =>
        Bar(u, v) ? Color.black : Color.white * Mathf.Lerp(0.75f, 1f, v);

    // A cushion: a red border, an indigo middle, a gold ring in it.
    private static Color Cushion(float u, float v)
    {
        var edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
        if (edge < 0.14f) return new Color(0.55f, 0.13f, 0.1f) * (edge < 0.02f ? 0.75f : 1f);
        var r = new Vector2(u - 0.5f, v - 0.5f).magnitude;
        if (Mathf.Abs(r - 0.2f) < 0.018f || r < 0.04f) return new Color(0.78f, 0.62f, 0.3f);
        var weave = 0.94f + 0.06f * Mathf.PerlinNoise(u * 120, v * 120);
        return new Color(0.16f, 0.21f, 0.38f) * weave;
    }
}
