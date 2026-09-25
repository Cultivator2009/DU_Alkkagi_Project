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

            var steamRow = UIKit.Node("SteamUser", root).Place(new Vector2(0, 0), new Vector2(112, 56), new Vector2(760, 40));
            UIKit.Image(steamRow, "Dot", UIKit.Circle, Theme.StatusOk).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16), new Vector2(0, 0.5f));
            // ASCII placeholder: anything outside the baked charset would land in
            // the dynamic fallback atlas the moment the editor draws it.
            menu.steamUserText = UIKit.Text(steamRow, "Name", "Steam · Player", 28, false, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            menu.steamUserText.overflowMode = TextOverflowModes.Ellipsis;
            menu.steamUserText.rectTransform.Stretch();
            menu.steamUserText.rectTransform.offsetMin = new Vector2(32, 0);
            menu.steamUserRow = steamRow.gameObject;

            menu.settingsPanel = BuildSettingsPanel(root, out menu.settingsCloseButton).gameObject;
            BuildSetupPanel(root, menu);
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

        // Language, janggi letters, sound and controls. SettingsPanel keeps
        // each control and its saved value in step. The in-game menu (HUD)
        // uses the same card.
        internal static SettingsPanel BuildSettingsPanel(Transform root, out Button closeButton)
        {
            const float width = 780, pad = 64, rowHeight = 60;
            var actions = (GameAction[])System.Enum.GetValues(typeof(GameAction));
            var overlay = UIKit.Image(root, "SettingsPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            var panel = overlay.gameObject.AddComponent<SettingsPanel>();

            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 728 + actions.Length * 76));
            var c = card.transform;
            var topLeft = new Vector2(0, 1);
            var topRight = new Vector2(1, 1);
            var labelWidth = 300f;

            UIKit.Label(c, "Title", "settings.title", 48, true, Theme.Ink, TextAlignmentOptions.Center)
                .rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(width - pad * 2, 64));

            void RowLabel(string name, string key, float y) =>
                UIKit.Label(c, name, key, 28, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Place(topLeft, new Vector2(pad, y), new Vector2(labelWidth, rowHeight));
            void Section(string name, string key, float y)
            {
                UIKit.Label(c, name, key, 24, true, Theme.InkFaint, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Place(topLeft, new Vector2(pad, y), new Vector2(width - pad * 2, 32));
                UIKit.Image(c, name + "Divider", null, Theme.Divider).rectTransform.Place(topLeft, new Vector2(pad, y - 38), new Vector2(width - pad * 2, 2));
            }

            RowLabel("LanguageLabel", "settings.language", -136);
            BuildLanguageToggle(c, "LanguageToggle").GetComponent<RectTransform>()
                .Place(topRight, new Vector2(-pad, -130), new Vector2(236, 72));
            RowLabel("LettersLabel", "settings.janggiLetters", -216);
            panel.janggiLetters = UIKit.SegmentedToggle(c, "LettersToggle", new[] { "settings.hangul", "settings.hanja" }, 72);
            panel.janggiLetters.GetComponent<RectTransform>().Place(topRight, new Vector2(-pad, -210), new Vector2(236, 72));

            Section("Sound", "settings.sound", -312);
            (Slider, TMP_Text) VolumeRow(string name, string key, float y)
            {
                RowLabel(name + "Label", key, y);
                var slider = UIKit.Slider(c, name + "Slider", new Vector2(250, 36));
                slider.GetComponent<RectTransform>().Place(topRight, new Vector2(-pad - 100, y - 12), new Vector2(250, 36));
                var value = UIKit.Text(c, name + "Value", "80%", 26, true, Theme.Ink, TextAlignmentOptions.MidlineRight);
                value.rectTransform.Place(topRight, new Vector2(-pad, y), new Vector2(84, rowHeight));
                return (slider, value);
            }
            (panel.masterSlider, panel.masterValue) = VolumeRow("Master", "settings.masterVolume", -366);
            (panel.interfaceSlider, panel.interfaceValue) = VolumeRow("Interface", "settings.interfaceVolume", -436);

            Section("Controls", "settings.controls", -524);
            var rows = new System.Collections.Generic.List<KeyBindRow>();
            for (var i = 0; i < actions.Length; i++)
            {
                var y = -578 - i * 76;
                RowLabel("Bind" + actions[i] + "Label", "bind." + actions[i], y);
                var button = UIKit.CapsuleButton(c, "Bind" + actions[i], "settings.reset", new Vector2(300, rowHeight), false, 24);
                button.GetComponent<RectTransform>().Place(topRight, new Vector2(-pad, y), new Vector2(300, rowHeight));
                var keyText = button.GetComponentInChildren<TMP_Text>();
                // The key name is set by SettingsPanel, not a Loc key.
                Object.DestroyImmediate(keyText.GetComponent<LocalizedText>());
                keyText.text = "Ctrl";
                var row = button.gameObject.AddComponent<KeyBindRow>();
                row.action = actions[i];
                row.button = button;
                row.keyText = keyText;
                rows.Add(row);
            }
            panel.keyRows = rows.ToArray();

            panel.resetButton = UIKit.CapsuleButton(c, "ResetButton", "settings.reset", new Vector2(240, 84), false, 30);
            panel.resetButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(pad, 48), new Vector2(240, 84));
            closeButton = UIKit.CapsuleButton(c, "CloseButton", "settings.close", new Vector2(240, 84), true, 32);
            closeButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-pad, 48), new Vector2(240, 84));
            overlay.gameObject.SetActive(false);
            return panel;
        }

        // Local match setup: who plays white (someone at this screen, or the
        // AI), then the same rules card the online lobby shows, with Start /
        // Cancel. MainMenuUI fills it from the last-used choices.
        private static void BuildSetupPanel(Transform root, MainMenuUI menu)
        {
            const float width = 840, pad = 64, rowHeight = 52, gap = 8, opponentRow = 80;
            var rulesHeight = UIKit.RuleRows(false) * (rowHeight + gap) - gap;
            var overlay = UIKit.Image(root, "SetupPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            menu.setupPanel = overlay.gameObject;

            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 148 + opponentRow + rulesHeight + 40 + 84 + 48));
            var top = new Vector2(0.5f, 1);

            UIKit.Label(card.transform, "Title", "match.title", 48, true, Theme.Ink, TextAlignmentOptions.Center)
                .rectTransform.Place(top, new Vector2(0, -44), new Vector2(width - pad * 2, 64));
            UIKit.Label(card.transform, "OpponentLabel", "setup.opponent", 28, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(pad, -144), new Vector2(160, 60));
            menu.opponentToggle = UIKit.SegmentedToggle(card.transform, "OpponentToggle",
                new[] { "opponent.Human", "opponent.AIEasy", "opponent.AINormal", "opponent.AIHard" }, 60);
            menu.opponentToggle.GetComponent<RectTransform>().Place(new Vector2(1, 1), new Vector2(-pad, -144), new Vector2(width - pad * 2 - 150, 60));
            menu.setupRules = UIKit.RulesPanel(card.transform, "Rules", width - pad * 2, rowHeight, gap, 28, online: false);
            menu.setupRules.GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(pad, -148 - opponentRow), menu.setupRules.GetComponent<RectTransform>().sizeDelta);

            menu.setupCancelButton = UIKit.CapsuleButton(card.transform, "CancelButton", "setup.cancel", new Vector2(240, 84), false, 32);
            menu.setupCancelButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(pad, 48), new Vector2(240, 84));
            menu.setupStartButton = UIKit.CapsuleButton(card.transform, "StartButton", "setup.start", new Vector2(280, 84), true, 32);
            menu.setupStartButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-pad, 48), new Vector2(280, 84));
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
