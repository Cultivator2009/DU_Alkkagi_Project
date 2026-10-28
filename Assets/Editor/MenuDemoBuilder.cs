using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts the main menu's living board into MainMenuScene (MenuDemo): copies
// of GameScene's go, janggi, hexagon and cross boards and of its black and
// white go stones, scripts taken off (their meshes copied, since GameScene
// owns them), the room round them (RoomBuilder), and the menu's
// camera and light as the match's. Again replaces it. Run after
// Tools > Alkkagi > Build boards and piece templates changes the boards.
public static class MenuDemoBuilder
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string MenuScenePath = "Assets/Scenes/MainMenuScene.unity";
    private const string RootName = "MenuDemo";

    [MenuItem("Tools/Alkkagi/Build menu demo")]
    private static void Build()
    {
        var game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        var setup = Object.FindAnyObjectByType<BoardSetup>();
        var gameLight = game.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional);
        var menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Additive);
        foreach (var old in menu.GetRootGameObjects().Where(g => g.name == RootName || g.name == RoomBuilder.Name || g.name == "Table")) Object.DestroyImmediate(old);

        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, menu);
        var demo = root.AddComponent<MenuDemo>();
        var stages = new[]
        {
            (BoardType.Go, 2, 1.1f, 3.3f),
            (BoardType.Janggi, 2, 1.15f, 3.3f),
            (BoardType.Hexagon, 3, 1.0f, 3.6f),
            (BoardType.Cross, 4, 1.85f, 5.0f),
        };
        demo.stages = stages.Select(s =>
        {
            var source = setup.boards.First(b => b.type == s.Item1);
            var board = Copy(source.gameObject, root.transform);
            board.name = s.Item1 + " board";
            Uncover(board, source);
            return new MenuDemo.Stage { board = board, sides = s.Item2, spawnDistance = s.Item3, view = s.Item4 };
        }).ToArray();

        var template = setup.blackTemplate;
        var grip = PieceSet.Of(PieceType.GoStones).Grip;
        var mass = template.GetComponent<Rigidbody>().mass;
        demo.launchSpeed = template.maxForce * Mathf.Sqrt(grip) / template.referenceMass * Mathf.Pow(template.referenceMass / mass, template.massExponent);
        demo.slowing = GamePieceDragAndReleaseForce.BoardSlowing * (grip - 1);
        demo.blackStone = Copy(setup.blackTemplate.gameObject, root.transform);
        demo.whiteStone = Copy(setup.whiteTemplate.gameObject, root.transform);

        var room = RoomBuilder.Make();
        SceneManager.MoveGameObjectToScene(room, menu);

        // The menu's camera and light: lit as the match is.
        var camera = menu.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.09f, 0.06f, 0.04f);
        camera.fieldOfView = 40;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 60;
        var light = menu.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional);
        if (light != null && gameLight != null)
        {
            light.transform.rotation = gameLight.transform.rotation;
            light.intensity = gameLight.intensity;
            light.color = gameLight.color;
            light.shadows = gameLight.shadows;
            light.shadowStrength = gameLight.shadowStrength;
        }

        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);
        EditorSceneManager.CloseScene(game, true);
        Debug.Log("[Alkkagi] Menu demo built into MainMenuScene.");
    }

    private static bool CanRemove(Component component) =>
        !component.gameObject.GetComponents<Component>().Any(other => other != component && other.GetType().GetCustomAttributes(typeof(RequireComponent), true)
            .Cast<RequireComponent>().Any(r => r.m_Type0 == component.GetType() || r.m_Type1 == component.GetType() || r.m_Type2 == component.GetType()));

    // A board of several parts stands in a match on one box round them all,
    // its touches outside them dropped (BoardVariant.Clip) - which takes
    // the board's script, gone from the copy: a box for each part instead.
    private static void Uncover(GameObject board, BoardVariant source)
    {
        var slab = board.transform.Find(BoardVariant.SlabName);
        if (slab == null || source.Shape.Parts.Count < 2) return;
        foreach (var solid in slab.GetComponents<Collider>()) Object.DestroyImmediate(solid);
        foreach (var part in source.Shape.Parts)
        {
            if (!BoardShape.IsRect(part, out var rect)) continue;
            var box = slab.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(rect.center.x, -source.thickness / 2, rect.center.y);
            box.size = new Vector3(rect.width, source.thickness, rect.height);
            box.sharedMaterial = source.physics;
        }
    }

    // A copy with no scripts, inactive, its scene-made meshes its own.
    private static GameObject Copy(GameObject source, Transform parent)
    {
        var copy = Object.Instantiate(source, parent);
        copy.name = source.name;
        // Last added first, a few times over, for scripts another needs (RequireComponent).
        for (var pass = 0; pass < 3; pass++)
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
                if (behaviour != null && CanRemove(behaviour)) Object.DestroyImmediate(behaviour);
        foreach (var filter in copy.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null && !AssetDatabase.Contains(filter.sharedMesh)) filter.sharedMesh = Object.Instantiate(filter.sharedMesh);
        foreach (var solid in copy.GetComponentsInChildren<MeshCollider>(true))
            if (solid.sharedMesh != null && !AssetDatabase.Contains(solid.sharedMesh)) solid.sharedMesh = Object.Instantiate(solid.sharedMesh);
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            if (renderer.sharedMaterials.Any(m => m != null && !AssetDatabase.Contains(m))) renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m != null && !AssetDatabase.Contains(m) ? new Material(m) : m).ToArray();
        copy.SetActive(false);
        return copy;
    }
}
