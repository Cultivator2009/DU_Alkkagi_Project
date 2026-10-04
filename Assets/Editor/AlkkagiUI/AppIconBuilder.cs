using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlkkagiUIEditor
{
    // The app icon: the main menu's seal (알 on seal red), drawn at 1024 with
    // a margin, saved to Assets/UI/AppIcon.png and set as the default icon
    // in Player Settings. A placeholder until the real icon: a file saved
    // over that PNG takes its place, no rebuild needed.
    internal static class AppIconBuilder
    {
        private const string IconPath = "Assets/UI/AppIcon.png";
        private const int Size = 1024;
        private const float Seal = 880; // of Size: the rest is margin

        [MenuItem("Tools/Alkkagi UI/5. Build app icon")]
        public static void Build()
        {
            UIKit.Load();
            var scene = EditorSceneManager.NewPreviewScene();
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            try
            {
                var cameraObject = new GameObject("IconCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = target;

                var canvasObject = new GameObject("IconCanvas", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;

                // The menu's seal is 130 with its glyph at 68: drawn that size
                // and scaled up, so the corners keep their shape.
                var seal = UIKit.Panel(canvasObject.transform, "Seal", Theme.Seal, null, 1.4f);
                seal.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130, 130));
                seal.rectTransform.localScale = Vector3.one * (Seal / 130);
                UIKit.Text(seal.transform, "Glyph", "알", 68, true, Theme.SealText, TextAlignmentOptions.Center).rectTransform.Stretch();

                Canvas.ForceUpdateCanvases();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(IconPath, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                target.Release();
                Object.DestroyImmediate(target);
            }

            AssetDatabase.ImportAsset(IconPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log("[Alkkagi UI] App icon drawn to " + IconPath + " and set as the default icon.");
        }
    }
}
