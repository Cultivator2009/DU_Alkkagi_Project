using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AlkkagiUIEditor
{
    // Builds Assets/UI/MainMenu_UI.prefab and swaps it into MainMenuScene in
    // place of the old fake login screen and lobby hub (Login_UI,
    // MainLobby_Ui, and the GameCreation_panel/UIManager leftovers whose
    // scripts were deleted in the Phase 1 rewrite).
    internal static class MenuBuilder
    {
        private const string PrefabPath = "Assets/UI/MainMenu_UI.prefab";
        private const string ScenePath = "Assets/Scenes/MainMenuScene.unity";
        private static readonly string[] ObsoletePrefabs = { "Assets/UI/Login_UI.prefab", "Assets/UI/MainLobby_Ui.prefab" };
        private static readonly string[] ObsoleteSceneObjects = { "GameCreation_panel", "UIManager", "SceneChanger" };
        private const string ObsoleteScript = "Assets/Scripts/SceneChanger.cs";

        [MenuItem("Tools/Alkkagi UI/3. Build main menu")]
        public static void Build()
        {
            UIKit.Load();
            var prefab = UIKit.SavePrefab(PrefabPath, BuildRoot);
            WireScene(prefab);
            Debug.Log("[Alkkagi UI] Main menu built and wired into MainMenuScene.");
        }

        private static GameObject BuildRoot()
        {
            var canvas = UIKit.Canvas("MainMenu_UI", 0.5f, 0);
            var root = canvas.transform;
            var menu = canvas.gameObject.AddComponent<MainMenuUI>();

            UIKit.Image(root, "Background", null, Theme.MenuBackground).rectTransform.Stretch();
            BuildBoardIllustration(root);

            var topLeft = new Vector2(0, 1);
            var seal = UIKit.Panel(root, "Seal", Theme.Seal, null, 1.4f);
            seal.rectTransform.Place(topLeft, new Vector2(112, -146), new Vector2(130, 130));
            // The seal is the logo mark, so it stays 알 in both languages.
            UIKit.Text(seal.transform, "Glyph", "알", 68, true, Theme.SealText, TextAlignmentOptions.Center).rectTransform.Stretch();
            UIKit.Label(root, "Title", "menu.title", 96, true, Theme.Ink, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(topLeft, new Vector2(276, -140), new Vector2(760, 150));

            menu.localButton = UIKit.CapsuleButton(root, "LocalButton", "menu.local", new Vector2(600, 120), true, 40, "menu.localSub");
            menu.localButton.GetComponent<RectTransform>().Place(topLeft, new Vector2(112, -366), new Vector2(600, 120));
            menu.onlineButton = UIKit.CapsuleButton(root, "OnlineButton", "menu.online", new Vector2(600, 120), false, 40, "menu.onlineSub");
            menu.onlineButton.GetComponent<RectTransform>().Place(topLeft, new Vector2(112, -518), new Vector2(600, 120));
            menu.settingsButton = UIKit.CapsuleButton(root, "SettingsButton", "menu.settings", new Vector2(290, 104), false, 36);
            menu.settingsButton.GetComponent<RectTransform>().Place(topLeft, new Vector2(112, -670), new Vector2(290, 104));
            menu.quitButton = UIKit.CapsuleButton(root, "QuitButton", "menu.quit", new Vector2(290, 104), false, 36);
            menu.quitButton.GetComponent<RectTransform>().Place(topLeft, new Vector2(422, -670), new Vector2(290, 104));

            BuildLanguageToggle(root, "LanguageToggle").GetComponent<RectTransform>()
                .Place(new Vector2(1, 1), new Vector2(-40, -40), new Vector2(236, 72));

            var steamRow = UIKit.Node("SteamUser", root).Place(new Vector2(0, 0), new Vector2(112, 56), new Vector2(760, 40));
            UIKit.Image(steamRow, "Dot", UIKit.Circle, Theme.StatusOk).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16), new Vector2(0, 0.5f));
            // ASCII placeholder: anything outside the baked charset would land in
            // the dynamic fallback atlas the moment the editor draws it.
            menu.steamUserText = UIKit.Text(steamRow, "Name", "Steam · Player", 28, false, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            menu.steamUserText.overflowMode = TextOverflowModes.Ellipsis;
            menu.steamUserText.rectTransform.Stretch();
            menu.steamUserText.rectTransform.offsetMin = new Vector2(32, 0);
            menu.steamUserRow = steamRow.gameObject;

            BuildSettingsPanel(root, menu);
            return canvas.gameObject;
        }

        // The real board texture, drawn flat, with a few stones mid-game.
        // Stones sit on grid intersections: in GO_Board.png the 19 lines run
        // from x 0.035 to 0.959 and y 0.040 to 0.966 (measured, from bottom).
        private static void BuildBoardIllustration(Transform root)
        {
            var board = UIKit.Image(root, "Board", UIKit.Board, Color.white);
            board.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-86, -14), new Vector2(740, 740));
            var stones = new (int col, int row, bool black)[]
            {
                (4, 15, false), (8, 13, false), (14, 15, false),
                (5, 3, true), (10, 6, true), (15, 3, true),
            };
            foreach (var (col, row, black) in stones)
            {
                var point = new Vector2(0.035f + col * (0.924f / 18), 0.040f + row * (0.926f / 18));
                UIKit.Stone(board.transform, black ? "Black" : "White", 34, black ? Theme.StoneBlack : Theme.StoneWhite, black ? Theme.Ink : Theme.InkMuted, false)
                    .Place(point, Vector2.zero, new Vector2(34, 34), new Vector2(0.5f, 0.5f));
            }
        }

        // 한 | EN capsule; the ink highlight slides under the active language.
        internal static LanguageToggle BuildLanguageToggle(Transform parent, string name)
        {
            var frame = UIKit.Capsule(parent, name, 72, Theme.Hanji, Theme.Ink);
            var toggle = frame.gameObject.AddComponent<LanguageToggle>();
            toggle.selectedTextColor = Theme.Hanji;
            toggle.idleTextColor = Theme.Ink;

            var ko = UIKit.Segment(frame.transform, "Korean", "한", null, 0f, 72);
            var en = UIKit.Segment(frame.transform, "English", "EN", null, 0.5f, 72);
            UIKit.ShowSelected(ko, en);
            toggle.koreanButton = ko.button;
            toggle.koreanHighlight = ko.highlight;
            toggle.koreanLabel = ko.label;
            toggle.englishButton = en.button;
            toggle.englishHighlight = en.highlight;
            toggle.englishLabel = en.label;
            return toggle;
        }

        private static void BuildSettingsPanel(Transform root, MainMenuUI menu)
        {
            var overlay = UIKit.Image(root, "SettingsPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            menu.settingsPanel = overlay.gameObject;

            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 520));
            var top = new Vector2(0.5f, 1);

            UIKit.Label(card.transform, "Title", "settings.title", 48, true, Theme.Ink, TextAlignmentOptions.Center)
                .rectTransform.Place(top, new Vector2(0, -44), new Vector2(560, 64));
            UIKit.Label(card.transform, "LanguageLabel", "settings.language", 30, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(64, -172), new Vector2(240, 72));
            BuildLanguageToggle(card.transform, "LanguageToggle").GetComponent<RectTransform>()
                .Place(new Vector2(1, 1), new Vector2(-64, -172), new Vector2(236, 72));
            // Local matches only; an online match uses the lobby host's choice.
            UIKit.Label(card.transform, "AimGuideLabel", "settings.aimGuide", 30, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(64, -268), new Vector2(240, 72));
            menu.aimGuideToggle = UIKit.OnOffToggle(card.transform, "AimGuideToggle", 72);
            menu.aimGuideToggle.GetComponent<RectTransform>().Place(new Vector2(1, 1), new Vector2(-64, -268), new Vector2(236, 72));

            menu.settingsCloseButton = UIKit.CapsuleButton(card.transform, "CloseButton", "settings.close", new Vector2(240, 84), false, 32);
            menu.settingsCloseButton.GetComponent<RectTransform>().Place(new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(240, 84), new Vector2(0.5f, 0));
            overlay.gameObject.SetActive(false);
        }

        private static void WireScene(GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
            {
                var source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (ObsoletePrefabs.Contains(source) || source == PrefabPath || ObsoleteSceneObjects.Contains(go.name))
                    Object.DestroyImmediate(go);
            }
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Nothing else references these: the old UI only lived in this scene,
            // and SceneChanger was only bound to its buttons.
            foreach (var path in ObsoletePrefabs.Append(ObsoleteScript))
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        }
    }
}
