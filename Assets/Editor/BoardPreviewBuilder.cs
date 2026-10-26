using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The match setup card's board pictures (MainMenuUI.boardPreviews): each of
// GameScene's boards seen from straight above, lit as in a match, on
// nothing (transparent), saved as sprites in Assets/UI/Previews. Run after
// Tools > Alkkagi > Build boards and piece templates changes the boards,
// then Tools > Alkkagi UI > 3. Build main menu.
public static class BoardPreviewBuilder
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    public const string Dir = "Assets/UI/Previews";
    private const int Size = 512;

    [MenuItem("Tools/Alkkagi/Build board previews")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var setup = Object.FindAnyObjectByType<BoardSetup>();
        Directory.CreateDirectory(Dir);
        // Only the boards drawn: every other renderer off for the shots (the
        // house, the table, the templates). The scene isn't saved after.
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            if (setup.boards.All(board => !renderer.transform.IsChildOf(board.transform))) renderer.enabled = false;

        var camera = new GameObject("Preview camera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0, 0, 0, 0);
        camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        camera.targetTexture = target;
        foreach (var board in setup.boards)
        {
            foreach (var other in setup.boards) other.gameObject.SetActive(other == board);
            var bounds = board.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            camera.transform.position = new Vector3(bounds.center.x, bounds.max.y + 5, bounds.center.z);
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.04f;
            camera.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            picture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            picture.Apply();
            RenderTexture.active = null;
            var path = $"{Dir}/Board_{board.type}.png";
            File.WriteAllBytes(path, picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        Object.DestroyImmediate(camera.gameObject);
        target.Release();
        Object.DestroyImmediate(target);
        // As it was on disk.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("[Alkkagi] Board previews built in " + Dir);
    }
}
