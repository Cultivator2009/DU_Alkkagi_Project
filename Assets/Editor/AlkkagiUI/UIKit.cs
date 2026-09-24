using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace AlkkagiUIEditor
{
    // The blended hanji/ink look: hanji-cream panels, ink outlines and text,
    // a red seal accent. Colors are baked into the generated prefabs - retune
    // them there (or here and rebuild) as the art direction settles.
    internal static class Theme
    {
        public static readonly Color Hanji = Hex("F4ECDC");
        public static readonly Color HanjiField = Hex("EDE3CF");
        public static readonly Color Ink = Hex("2E241A");
        public static readonly Color InkSoft = Hex("6B5A48");
        public static readonly Color InkMuted = Hex("8C7C66");
        public static readonly Color InkFaint = Hex("9A8A74");
        public static readonly Color InkIdleTitle = Hex("5E5040");
        public static readonly Color Divider = Hex("D6C7AC");
        public static readonly Color FieldBorder = Hex("C9B89C");
        public static readonly Color Seal = Hex("B3312A");
        public static readonly Color SealText = Hex("FBEFE6");
        public static readonly Color SealSub = Hex("F5C4B3");
        public static readonly Color StoneBlack = Hex("151515");
        public static readonly Color StoneWhite = Hex("F7F7F7");
        public static readonly Color LostRing = Hex("B9A88F");
        public static readonly Color Overlay = new Color(0.10f, 0.07f, 0.05f, 0.5f);
        public static readonly Color MenuBackground = Hex("CC8686");
        public static readonly Color StatusOk = Hex("639922");
        public static readonly Color StatusBusy = Hex("BA7517");
        public static readonly Color StatusError = Hex("A32D2D");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }

    // Generated kit assets (sprites + fonts) and small factories the screen
    // builders share. Every sprite is white and tinted through Image.color,
    // so one set serves every color in the theme.
    internal static class UIKit
    {
        public const string KitDir = "Assets/UI/Kit";
        private const string SpriteDir = KitDir + "/Sprites";
        private const string FontDir = KitDir + "/Fonts";
        private const string SourceFontDir = "Assets/Fonts/Pretendard";
        // A nine-glyph subset of Noto Serif KR Bold: the janggi letters in
        // Hanja (SideStyle.PieceLetter), which Pretendard doesn't have.
        private const string HanjaFontPath = "Assets/Fonts/NotoSerifKR/NotoSerifKR-Bold-Janggi.otf";
        private const string HanjaLetters = "楚漢車包馬象士卒兵";

        // Sprite geometry, in texture pixels (1 px = 1 canvas unit at multiplier 1).
        private const int ShapeSize = 128;
        private const int PanelRadius = 40;
        private const int PanelRing = 6;
        private const int PillRing = 8;
        private const int CircleRing = 10;

        // 56pt SDF stays crisp up to the largest UI text (~100px) while the
        // baked UI charset still fits a 1024 atlas - a text-serialized 2048
        // atlas is ~8MB per weight in git.
        private const int FontSampling = 56;
        private const int FontPadding = 6;
        private const int FontAtlasSize = 1024;

        public static Sprite RRect, RRectRing, Pill, PillOutline, Circle, CircleOutline, CircleDashed, Triangle, Board;
        public static TMP_FontAsset Regular, Bold;

        [MenuItem("Tools/Alkkagi UI/1. Generate kit assets")]
        public static void GenerateKitAssets()
        {
            Directory.CreateDirectory(SpriteDir);
            Directory.CreateDirectory(FontDir);

            WriteShape("rrect", ShapeSize, PanelRadius + 4, (x, y) => Coverage(RoundedRect(x, y, ShapeSize, PanelRadius)));
            WriteShape("rrect_ring", ShapeSize, PanelRadius + 4, (x, y) => Ring(RoundedRect(x, y, ShapeSize, PanelRadius), PanelRing));
            WriteShape("pill", ShapeSize, ShapeSize / 2, (x, y) => Coverage(RoundedRect(x, y, ShapeSize, ShapeSize / 2f)));
            WriteShape("pill_ring", ShapeSize, ShapeSize / 2, (x, y) => Ring(RoundedRect(x, y, ShapeSize, ShapeSize / 2f), PillRing));
            WriteShape("circle", ShapeSize, 0, (x, y) => Coverage(CircleDistance(x, y, ShapeSize)));
            WriteShape("circle_ring", ShapeSize, 0, (x, y) => Ring(CircleDistance(x, y, ShapeSize), CircleRing));
            WriteShape("circle_ring_dashed", ShapeSize, 0, (x, y) => Ring(CircleDistance(x, y, ShapeSize), CircleRing) * Dash(x, y, ShapeSize, 12));
            WriteShape("triangle", 64, 0, (x, y) => TriangleCoverage(x, y, 64));

            // The main menu shows the in-game board texture flat; a copy keeps the
            // 3D material's import settings (mipmaps etc.) untouched.
            var boardPath = SpriteDir + "/menu_board.png";
            File.Copy("Assets/Materials/GO_Board.png", boardPath, true);
            ImportSprite(boardPath, 0);

            GenerateFonts();
            AssetDatabase.SaveAssets();
            Debug.Log("[Alkkagi UI] Kit assets generated.");
        }

        public static void Load()
        {
            RRect = LoadSprite("rrect");
            RRectRing = LoadSprite("rrect_ring");
            Pill = LoadSprite("pill");
            PillOutline = LoadSprite("pill_ring");
            Circle = LoadSprite("circle");
            CircleOutline = LoadSprite("circle_ring");
            CircleDashed = LoadSprite("circle_ring_dashed");
            Triangle = LoadSprite("triangle");
            Board = LoadSprite("menu_board");
            Regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/Pretendard-Regular SDF.asset");
            Bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/Pretendard-Bold SDF.asset");
            if (RRect == null || Regular == null || Bold == null)
                throw new System.InvalidOperationException("UI kit assets missing - run Tools > Alkkagi UI > 1. Generate kit assets first.");
        }

        // ---- Fonts ----

        // A static atlas baked with every UI string keeps the committed font
        // asset stable: a purely dynamic atlas grows (and gets re-saved) on
        // every editor play session. The dynamic fallback only kicks in for
        // text outside the table, like Steam persona names.
        private static void GenerateFonts()
        {
            var charset = BuildCharset();
            var hanja = CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(HanjaFontPath), "NotoSerifKR-Janggi SDF", AtlasPopulationMode.Dynamic, 256);
            if (!hanja.TryAddCharacters(HanjaLetters, out var missingHanja))
                Debug.LogWarning($"[Alkkagi UI] The Hanja font is missing glyphs: {missingHanja}");
            hanja.atlasPopulationMode = AtlasPopulationMode.Static;
            AttachSubAssets(hanja);
            EditorUtility.SetDirty(hanja);

            foreach (var weight in new[] { "Regular", "Bold" })
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>($"{SourceFontDir}/Pretendard-{weight}.otf");
                var fallback = CreateFontAsset(source, $"Pretendard-{weight} Dynamic", AtlasPopulationMode.Dynamic);
                var primary = CreateFontAsset(source, $"Pretendard-{weight} SDF", AtlasPopulationMode.Dynamic);

                if (!primary.TryAddCharacters(charset, out var missing))
                    Debug.LogWarning($"[Alkkagi UI] Pretendard-{weight} is missing glyphs: {missing}");
                primary.atlasPopulationMode = AtlasPopulationMode.Static;
                // Hanja ahead of the dynamic fallback, which is Pretendard again
                // and has none of them.
                primary.fallbackFontAssetTable = new List<TMP_FontAsset> { hanja, fallback };
                AttachSubAssets(primary);
                EditorUtility.SetDirty(primary);
            }

            // Make Pretendard the project-wide TMP default so any TMP text -
            // including ones created from code - can render Hangul.
            var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/Pretendard-Regular SDF.asset");
            var settings = new SerializedObject(TMP_Settings.instance);
            settings.FindProperty("m_defaultFontAsset").objectReferenceValue = regular;
            var clearOnBuild = settings.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuild != null) clearOnBuild.boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(TMP_Settings.instance);
        }

        private static TMP_FontAsset CreateFontAsset(Font source, string name, AtlasPopulationMode mode, int atlasSize = FontAtlasSize)
        {
            var path = $"{FontDir}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null)
            {
                asset = TMP_FontAsset.CreateFontAsset(source, FontSampling, FontPadding, GlyphRenderMode.SDFAA, atlasSize, atlasSize, mode, true);
                asset.name = name;
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                // Rebake in place. Every prefab's text references this asset by
                // GUID - deleting and recreating it would orphan them all.
                asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                // A dynamic fallback starts empty and grows on demand; keeping
                // its full-size blank atlas would add ~2MB of text to git.
                asset.ClearFontAssetData(setAtlasSizeToZero: mode == AtlasPopulationMode.Dynamic);
                asset.atlasPopulationMode = mode;
            }
            AttachSubAssets(asset);

            var so = new SerializedObject(asset);
            var clearOnBuild = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuild != null) clearOnBuild.boolValue = mode == AtlasPopulationMode.Dynamic;
            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        // A font asset created in code keeps its atlas texture(s) and material
        // only in memory until they're added as sub-assets.
        private static void AttachSubAssets(TMP_FontAsset asset)
        {
            var existing = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset));
            for (var i = 0; i < asset.atlasTextures.Length; i++)
            {
                var tex = asset.atlasTextures[i];
                if (tex == null || existing.Contains(tex)) continue;
                tex.name = $"{asset.name} Atlas{(i == 0 ? "" : " " + i)}";
                AssetDatabase.AddObjectToAsset(tex, asset);
            }
            if (!existing.Contains(asset.material))
            {
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
        }

        private static string BuildCharset()
        {
            var chars = new HashSet<char>();
            for (var c = (char)32; c < 127; c++) chars.Add(c);
            foreach (var ch in "…·–—") chars.Add(ch);

            var table = (IDictionary)typeof(Loc).GetField("Table", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            foreach (var value in table.Values)
            {
                var tuple = ((string ko, string en))value;
                foreach (var ch in tuple.ko + tuple.en) chars.Add(ch);
            }
            return new string(chars.Where(c => !char.IsControl(c)).ToArray());
        }

        // ---- Sprites ----

        private static void WriteShape(string name, int size, int border, System.Func<float, float, float> alphaAt)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var a = Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
            }
            tex.SetPixels32(pixels);
            var path = $"{SpriteDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            ImportSprite(path, border);
        }

        private static void ImportSprite(string path, int border)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static Sprite LoadSprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{name}.png");
        }

        // Signed distance to a centered rounded square (negative inside).
        private static float RoundedRect(float x, float y, float size, float radius)
        {
            var half = size / 2f;
            var qx = Mathf.Abs(x - half) - half + radius;
            var qy = Mathf.Abs(y - half) - half + radius;
            var outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return Mathf.Min(Mathf.Max(qx, qy), 0) + outside - radius;
        }

        private static float CircleDistance(float x, float y, float size)
        {
            var half = size / 2f;
            return new Vector2(x - half, y - half).magnitude - (half - 1);
        }

        // Upward-pointing triangle (apex at the top), 4x4 supersampled.
        private static float TriangleCoverage(float x, float y, float size)
        {
            var inside = 0;
            for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
            {
                var px = x - 0.5f + (sx + 0.5f) / 4f;
                var py = y - 0.5f + (sy + 0.5f) / 4f;
                var halfWidth = (1f - py / size) * size / 2f;
                if (py >= 0 && py <= size && Mathf.Abs(px - size / 2f) <= halfWidth) inside++;
            }
            return inside / 16f;
        }

        private static float Coverage(float distance) => Mathf.Clamp01(0.5f - distance);

        private static float Ring(float distance, float width) => Coverage(distance) * Mathf.Clamp01(0.5f + distance + width);

        private static float Dash(float x, float y, float size, int dashes)
        {
            var half = size / 2f;
            var turns = (Mathf.Atan2(y - half, x - half) / (2 * Mathf.PI) + 1f) % 1f * dashes;
            var f = turns - Mathf.Floor(turns);
            var pxPerUnit = 2 * Mathf.PI * (half - 1) / dashes; // anti-alias ~1px at each dash end
            return Mathf.Clamp01((0.6f - f) * pxPerUnit) * Mathf.Clamp01(f * pxPerUnit);
        }

        // ---- Element factories ----

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // Anchor at a single point (0..1 per axis) and place relative to it.
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, float pixelsPerUnitMultiplier = 1f, bool raycast = false)
        {
            var image = Node(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero)
            {
                image.type = UnityEngine.UI.Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            }
            return image;
        }

        // Rounded panel: a fill with an optional ink ring on top. cornerScale
        // above 1 shrinks both the corner radius and the outline, for small
        // elements like the seal badge.
        public static Image Panel(Transform parent, string name, Color fill, Color? outline, float cornerScale = 1f, bool raycast = false)
        {
            var image = Image(parent, name, RRect, fill, cornerScale, raycast);
            if (outline.HasValue) Image(image.transform, "Outline", RRectRing, outline.Value, cornerScale).rectTransform.Stretch();
            return image;
        }

        // Fully rounded capsule; the sprite's 64px caps are rescaled to the height.
        public static Image Capsule(Transform parent, string name, float height, Color fill, Color? outline, bool raycast = false)
        {
            var multiplier = ShapeSize / height;
            var image = Image(parent, name, Pill, fill, multiplier, raycast);
            if (outline.HasValue) Image(image.transform, "Outline", PillOutline, outline.Value, multiplier).rectTransform.Stretch();
            return image;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string content, float size, bool bold, Color color, TextAlignmentOptions align)
        {
            var text = Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = bold ? Bold : Regular;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            // Overflow, not Ellipsis: Ellipsis also drops a whole line whose
            // line height exceeds the rect, which silently hid big numerals.
            // Text of unknown length (Steam names) opts into Ellipsis itself.
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        // Static label bound to a Loc key; the builder previews it in Korean.
        public static TextMeshProUGUI Label(Transform parent, string name, string key, float size, bool bold, Color color, TextAlignmentOptions align)
        {
            var text = Text(parent, name, Loc.Get(key), size, bold, color, align);
            text.gameObject.AddComponent<LocalizedText>().key = key;
            return text;
        }

        // Capsule button: primary = seal red, otherwise hanji. The CanvasGroup
        // lets controllers dim the whole button (fill, ring and label) when it
        // isn't interactable - Button's own color tint only reaches the fill.
        public static Button CapsuleButton(Transform parent, string name, string key, Vector2 size, bool primary, float fontSize, string subKey = null)
        {
            var fill = Capsule(parent, name, size.y, primary ? Theme.Seal : Theme.Hanji, Theme.Ink, raycast: true);
            fill.rectTransform.sizeDelta = size;
            fill.gameObject.AddComponent<CanvasGroup>();

            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;

            var textColor = primary ? Theme.SealText : Theme.Ink;
            if (subKey == null)
            {
                Label(fill.transform, "Label", key, fontSize, true, textColor, TextAlignmentOptions.Center).rectTransform.Stretch();
            }
            else
            {
                // Title + subtitle, left-aligned inside the capsule.
                var pad = size.y * 0.5f;
                Label(fill.transform, "Label", key, fontSize, true, textColor, TextAlignmentOptions.BottomLeft)
                    .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(pad, 2), new Vector2(size.x - pad * 2, fontSize * 1.3f), new Vector2(0, 0));
                Label(fill.transform, "Sub", subKey, fontSize * 0.68f, false, primary ? Theme.SealSub : Theme.InkSoft, TextAlignmentOptions.TopLeft)
                    .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(pad, -2), new Vector2(size.x - pad * 2, fontSize), new Vector2(0, 1));
            }
            return button;
        }

        // Go stone icon: fill + ring, with an optional dashed "lost" ring.
        public static RectTransform Stone(Transform parent, string name, float diameter, Color fill, Color ring, bool withLostState)
        {
            var root = Node(name, parent);
            root.sizeDelta = new Vector2(diameter, diameter);
            Image(root, "Fill", Circle, fill).rectTransform.Stretch();
            Image(root, "Ring", CircleOutline, ring).rectTransform.Stretch();
            if (withLostState)
            {
                var lost = Image(root, "Lost", CircleDashed, Theme.LostRing);
                lost.rectTransform.Stretch();
                lost.gameObject.SetActive(false);
            }
            return root;
        }

        // Lets the stone icon repaint for the pieces in play (black/white or
        // Cho/Han); see SideMark.
        public static SideMark Mark(RectTransform stone, int playerId)
        {
            var mark = stone.gameObject.AddComponent<SideMark>();
            mark.playerId = playerId;
            mark.fill = stone.Find("Fill").GetComponent<Image>();
            mark.ring = stone.Find("Ring").GetComponent<Image>();
            return mark;
        }

        // A side name that follows the pieces in play instead of a fixed Loc key.
        public static SideMark MarkLabel(TMP_Text text, int playerId)
        {
            var localized = text.GetComponent<LocalizedText>();
            if (localized != null) Object.DestroyImmediate(localized);
            var mark = text.gameObject.AddComponent<SideMark>();
            mark.playerId = playerId;
            mark.label = text;
            return mark;
        }

        // One segment of a capsule switch (half of it unless span says
        // otherwise): an ink highlight under the selected one, and a
        // transparent raycast target so the whole segment is clickable.
        // locKey null = literal text (e.g. 한 / EN).
        public static (Button button, Graphic highlight, TMP_Text label) Segment(Transform frame, string name, string text, string locKey, float anchorX, float height, float span = 0.5f)
        {
            var hit = Node(name, frame);
            hit.anchorMin = new Vector2(anchorX, 0);
            hit.anchorMax = new Vector2(anchorX + span, 1);
            hit.offsetMin = hit.offsetMax = Vector2.zero;
            var highlight = Capsule(hit, "Highlight", height - 16, Theme.Ink, null);
            highlight.rectTransform.Stretch(8);
            var label = locKey == null
                ? Text(hit, "Label", text, height * 0.42f, true, Theme.Ink, TextAlignmentOptions.Center)
                : Label(hit, "Label", locKey, height * 0.42f, true, Theme.Ink, TextAlignmentOptions.Center);
            label.rectTransform.Stretch();
            var target = hit.gameObject.AddComponent<Image>();
            target.color = Color.clear;
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            button.transition = Selectable.Transition.None;
            return (button, highlight, label);
        }

        // Every match rule (MatchSettings.Defs) as a "label  ◀ value ▶" row,
        // top to bottom inside a width-wide column. Rows bind by setting id, so
        // a new rule only needs a rebuild to show up.
        public static MatchSettingsPanel RulesPanel(Transform parent, string name, float width, float rowHeight, float gap, float labelSize)
        {
            var root = Node(name, parent);
            root.sizeDelta = new Vector2(width, MatchSettings.Defs.Length * (rowHeight + gap) - gap);
            var panel = root.gameObject.AddComponent<MatchSettingsPanel>();
            var stepperWidth = Mathf.Min(360, width * 0.55f);
            var topLeft = new Vector2(0, 1);

            var rows = new List<MatchSettingRow>();
            for (var i = 0; i < MatchSettings.Defs.Length; i++)
            {
                var def = MatchSettings.Defs[i];
                var rowRect = Node(def.Key, root).Place(topLeft, new Vector2(0, -i * (rowHeight + gap)), new Vector2(width, rowHeight));
                var row = rowRect.gameObject.AddComponent<MatchSettingRow>();
                row.settingId = def.Id;
                row.canvasGroup = rowRect.gameObject.AddComponent<CanvasGroup>();

                Label(rowRect, "Label", def.LabelKey, labelSize, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(width - stepperWidth - 16, rowHeight), new Vector2(0, 0.5f));

                var frame = Capsule(rowRect, "Stepper", rowHeight, Theme.HanjiField, Theme.FieldBorder);
                frame.rectTransform.Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(stepperWidth, rowHeight), new Vector2(1, 0.5f));
                row.valueText = Text(frame.transform, "Value", def.Format(def.Default), labelSize, true, Theme.Ink, TextAlignmentOptions.Center);
                row.valueText.rectTransform.Stretch();
                row.valueText.rectTransform.offsetMin = new Vector2(rowHeight, 0);
                row.valueText.rectTransform.offsetMax = new Vector2(-rowHeight, 0);
                row.previousButton = Arrow(frame.transform, "Previous", rowHeight, false);
                row.nextButton = Arrow(frame.transform, "Next", rowHeight, true);
                rows.Add(row);
            }
            panel.rows = rows.ToArray();
            return panel;
        }

        // A square hit area at one end of a stepper with a small triangle
        // pointing outward. The triangle is child 0: MatchSettingRow fades it
        // at the end of the list.
        private static Button Arrow(Transform frame, string name, float size, bool right)
        {
            var hit = Node(name, frame).Place(new Vector2(right ? 1 : 0, 0.5f), Vector2.zero, new Vector2(size, size), new Vector2(right ? 1 : 0, 0.5f));
            var glyph = Image(hit, "Glyph", Triangle, Theme.Ink);
            glyph.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.34f, size * 0.3f));
            glyph.rectTransform.localRotation = Quaternion.Euler(0, 0, right ? -90 : 90);
            var target = hit.gameObject.AddComponent<Image>();
            target.color = Color.clear;
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            button.transition = Selectable.Transition.None;
            return button;
        }

        // 0..1 slider: a thin field-coloured track, a seal-red fill and a hanji
        // knob. The transparent root image takes the drags.
        public static Slider Slider(Transform parent, string name, Vector2 size)
        {
            const float trackHeight = 12;
            var knob = size.y;
            var root = Node(name, parent);
            root.sizeDelta = size;
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var track = Capsule(root, "Track", trackHeight, Theme.HanjiField, Theme.FieldBorder);
            track.rectTransform.anchorMin = new Vector2(0, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, trackHeight);

            var fillArea = Node("FillArea", root);
            fillArea.anchorMin = new Vector2(0, 0.5f);
            fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(-knob / 2, trackHeight);
            fillArea.anchoredPosition = new Vector2(-knob / 4, 0);
            var fill = Capsule(fillArea, "Fill", trackHeight, Theme.Seal, null);
            fill.rectTransform.sizeDelta = new Vector2(knob / 2, 0);

            var handleArea = Node("HandleArea", root);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(knob / 2, 0);
            handleArea.offsetMax = new Vector2(-knob / 2, 0);
            var handle = Image(handleArea, "Handle", Circle, Theme.Hanji);
            handle.rectTransform.sizeDelta = new Vector2(knob, 0);
            Image(handle.transform, "Ring", CircleOutline, Theme.Ink).rectTransform.Stretch();

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = GameSettings.DefaultVolume;
            var colors = slider.colors;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f);
            slider.colors = colors;
            return slider;
        }

        // A capsule of equal segments, one per Loc key, driven by a
        // SegmentedToggle. The first one shows as selected in the prefab.
        public static SegmentedToggle SegmentedToggle(Transform parent, string name, string[] keys, float height)
        {
            var frame = Capsule(parent, name, height, Theme.Hanji, Theme.Ink);
            var toggle = frame.gameObject.AddComponent<SegmentedToggle>();
            toggle.selectedTextColor = Theme.Hanji;
            toggle.idleTextColor = Theme.Ink;
            var span = 1f / keys.Length;
            var segments = keys.Select((key, i) => Segment(frame.transform, key, null, key, i * span, height, span)).ToArray();
            toggle.buttons = segments.Select(s => s.button).ToArray();
            toggle.highlights = segments.Select(s => s.highlight).ToArray();
            toggle.labels = segments.Select(s => s.label).ToArray();
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i].highlight.enabled = i == 0;
                segments[i].label.color = i == 0 ? Theme.Hanji : Theme.Ink;
            }
            return toggle;
        }

        // Bakes a resting "left side selected" look into the prefab. The
        // runtime component repaints on enable, but in the editor nothing
        // runs, so without this both halves show as selected with unreadable
        // labels.
        public static void ShowSelected((Button button, Graphic highlight, TMP_Text label) selected, (Button button, Graphic highlight, TMP_Text label) other)
        {
            selected.highlight.enabled = true;
            selected.label.color = Theme.Hanji;
            other.highlight.enabled = false;
            other.label.color = Theme.Ink;
        }

        public static Canvas Canvas(string name, float matchWidthOrHeight, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = matchWidthOrHeight;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // Builds a prefab from a root created by `build`, in a throwaway preview
        // scene so the open scene is never touched or dirtied.
        public static GameObject SavePrefab(string path, System.Func<GameObject> build)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var root = build();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return prefab;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
