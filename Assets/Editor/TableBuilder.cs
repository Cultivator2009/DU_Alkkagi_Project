using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The table the boards sit on in GameScene: a walnut top under every board,
// darker toward its edges so the eye stays on the board, where before the
// boards hung over the house's rug far below. Tools > Alkkagi > Build table
// makes its texture and material and puts it in the scene (again replaces it).
public static class TableBuilder
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";
    private const string Dir = "Assets/Materials/Table";
    public const string Name = "Table";
    public const float Top = -0.49f;             // at the boards' feet (a go board's legs end at -0.5), clear of the house's own table
    public static readonly Vector2 Size = new Vector2(26f, 17f);

    [MenuItem("Tools/Alkkagi/Build table")]
    private static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = GameObject.Find(Name);
        if (old != null) Object.DestroyImmediate(old);
        Make(TableMaterial());
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static GameObject Make(Material material)
    {
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = Name;
        Object.DestroyImmediate(table.GetComponent<Collider>());
        table.transform.position = new Vector3(0, Top - 0.05f, 0);
        table.transform.localScale = new Vector3(Size.x, 0.1f, Size.y);
        var renderer = table.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.receiveShadows = true;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return table;
    }

    public static Material TableMaterial()
    {
        Directory.CreateDirectory(Dir);
        var path = $"{Dir}/Table.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.mainTexture = Texture();
        material.color = Color.white;
        material.SetFloat("_Glossiness", 0.32f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    // Walnut: long uneven grain across, darker streaks, planks every so
    // often, and the whole falling off to near black toward the edges.
    private static Texture2D Texture()
    {
        const int w = 2048;
        var h = Mathf.RoundToInt(w * Size.y / Size.x);
        var pixels = new Color[w * h];
        var light = new Color(0.43f, 0.29f, 0.19f);
        var dark = new Color(0.22f, 0.14f, 0.09f);
        const int planks = 9;
        for (var y = 0; y < h; y++)
        {
            var plank = Mathf.FloorToInt((float)y / h * planks);
            var seam = Mathf.Abs((float)y / h * planks - plank - 0.5f) > 0.497f;
            for (var x = 0; x < w; x++)
            {
                var px = x + plank * 731;
                var grain = Mathf.PerlinNoise(px * 0.0025f, y * 0.09f) * 0.55f + Mathf.PerlinNoise(px * 0.012f, y * 0.35f) * 0.3f + Mathf.PerlinNoise(px * 0.05f, y * 0.8f) * 0.15f;
                var c = Color.Lerp(dark, light, Mathf.SmoothStep(0.2f, 0.85f, grain));
                if (seam) c *= 0.55f;
                // Toward the edges, into shadow.
                var u = (x / (float)w - 0.5f) * 2;
                var v = (y / (float)h - 0.5f) * 2;
                var off = Mathf.Sqrt(u * u * 0.55f + v * v);
                c *= Mathf.Lerp(1f, 0.28f, Mathf.SmoothStep(0.25f, 1.05f, off));
                c.a = 1;
                pixels[y * w + x] = c;
            }
        }
        var file = $"{Dir}/Table.png";
        var texture = new Texture2D(w, h, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        File.WriteAllBytes(file, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(file);
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
    }
}
