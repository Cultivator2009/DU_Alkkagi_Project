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
            var canvas = UIKit.Canvas("MainMenu_UI", null, 0);
            var root = canvas.transform;
            var menu = canvas.gameObject.AddComponent<MainMenuUI>();

            UIKit.Image(root, "Background", null, Theme.MenuBackground).rectTransform.Stretch();
            BuildBoardIllustration(root);

            // The logo and buttons hang from the left edge's middle, as the board
            // does from the right's: on a taller canvas (4:3, 5:4 - see
            // UIKit.Canvas) the pair stays level instead of the buttons riding
            // up to the top. The offsets read as from the top at 1080.
            var topLeft = new Vector2(0, 1);
            var leftMiddle = new Vector2(0, 0.5f);
            const float fromTop = 540;
            var seal = UIKit.Panel(root, "Seal", Theme.Seal, null, 1.4f);
            seal.rectTransform.Place(leftMiddle, new Vector2(112, -146 + fromTop), new Vector2(130, 130), topLeft);
            // The seal is the logo mark, so it stays 알 in both languages.
            UIKit.Text(seal.transform, "Glyph", "알", 68, true, Theme.SealText, TextAlignmentOptions.Center).rectTransform.Stretch();
            UIKit.Label(root, "Title", "menu.title", 96, true, Theme.Ink, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(leftMiddle, new Vector2(276, -140 + fromTop), new Vector2(760, 150), topLeft);

            menu.localButton = UIKit.CapsuleButton(root, "LocalButton", "menu.local", new Vector2(600, 120), true, 40, "menu.localSub");
            menu.localButton.GetComponent<RectTransform>().Place(leftMiddle, new Vector2(112, -366 + fromTop), new Vector2(600, 120), topLeft);
            menu.onlineButton = UIKit.CapsuleButton(root, "OnlineButton", "menu.online", new Vector2(600, 120), false, 40, "menu.onlineSub");
            menu.onlineButton.GetComponent<RectTransform>().Place(leftMiddle, new Vector2(112, -518 + fromTop), new Vector2(600, 120), topLeft);
            // Practice, Settings, Rankings, Quit: four to a row under the two big buttons.
            var small = new Vector2(141, 104);
            Button Small(string name, string key, int index)
            {
                var button = UIKit.CapsuleButton(root, name, key, small, false, 32);
                button.GetComponent<RectTransform>().Place(leftMiddle, new Vector2(112 + index * (small.x + 12), -670 + fromTop), small, topLeft);
                return button;
            }
            menu.practiceButton = Small("PracticeButton", "menu.practice", 0);
            menu.settingsButton = Small("SettingsButton", "menu.settings", 1);
            menu.rankingButton = Small("RankingButton", "menu.ranking", 2);
            menu.quitButton = Small("QuitButton", "menu.quit", 3);

            var steamRow = UIKit.Node("SteamUser", root).Place(new Vector2(0, 0), new Vector2(112, 56), new Vector2(760, 40));
            UIKit.Image(steamRow, "Dot", UIKit.Circle, Theme.StatusOk).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16), new Vector2(0, 0.5f));
            // ASCII placeholder: anything outside the baked charset would land in
            // the dynamic fallback atlas the moment the editor draws it.
            menu.steamUserText = UIKit.Text(steamRow, "Name", "Steam · Player · 1000", 28, false, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            menu.steamUserText.overflowMode = TextOverflowModes.Ellipsis;
            menu.steamUserText.rectTransform.Stretch();
            menu.steamUserText.rectTransform.offsetMin = new Vector2(32, 0);
            menu.steamUserRow = steamRow.gameObject;

            menu.settingsPanel = BuildSettingsPanel(root, out menu.settingsCloseButton).gameObject;
            BuildSetupPanel(root, menu);
            menu.leaderboard = BuildLeaderboardPanel(root);
            BuildPracticeOffer(root, menu);
            return canvas.gameObject;
        }

        // The first start: a card offering the practice match.
        private static void BuildPracticeOffer(Transform root, MainMenuUI menu)
        {
            const float width = 760, height = 420, pad = 64;
            var overlay = UIKit.Image(root, "PracticeOffer", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            menu.offerPanel = overlay.gameObject;
            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            UIKit.Appear(overlay, card.rectTransform);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var top = new Vector2(0.5f, 1);
            UIKit.Label(card.transform, "Title", "practice.offerTitle", 48, true, Theme.Ink, TextAlignmentOptions.Center)
                .rectTransform.Place(top, new Vector2(0, -48), new Vector2(width - pad * 2, 64));
            var body = UIKit.Label(card.transform, "Body", "practice.offerBody", 28, false, Theme.InkSoft, TextAlignmentOptions.Center);
            body.rectTransform.Place(top, new Vector2(0, -132), new Vector2(width - pad * 2, 120));
            body.textWrappingMode = TextWrappingModes.Normal;
            menu.offerLaterButton = UIKit.CapsuleButton(card.transform, "LaterButton", "practice.later", new Vector2(240, 84), false, 32);
            menu.offerLaterButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(pad, 48), new Vector2(240, 84));
            menu.offerStartButton = UIKit.CapsuleButton(card.transform, "StartButton", "practice.start", new Vector2(320, 84), true, 32);
            menu.offerStartButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-pad, 48), new Vector2(320, 84), new Vector2(1, 0));
            overlay.gameObject.SetActive(false);
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

        // Four pages behind tabs, one column each: general (language, janggi
        // letters, the blocked players), display, sound and controls.
        // SettingsPanel keeps each control and its saved value in step and
        // shows one page. The in-game menu (HUD) uses the same card, so it
        // has to fit the HUD's narrowest canvas (5:4, 1350 wide).
        internal static SettingsPanel BuildSettingsPanel(Transform root, out Button closeButton)
        {
            const float width = 1000, pad = 64, rowHeight = 60, rowPitch = 76, labelSize = 26, firstRow = -236;
            const float column = width - pad * 2, left = pad;
            var actions = (GameAction[])System.Enum.GetValues(typeof(GameAction));
            var pageRows = new[] { 3, 3, 4, actions.Length };
            var overlay = UIKit.Image(root, "SettingsPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            var panel = overlay.gameObject.AddComponent<SettingsPanel>();

            float RowY(int row) => firstRow - row * rowPitch;
            var cardHeight = -RowY(pageRows.Max() - 1) + rowHeight + 40 + 84 + 48;

            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            UIKit.Appear(overlay, card.rectTransform);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, cardHeight));
            var c = card.transform;
            var topLeft = new Vector2(0, 1);

            UIKit.Label(c, "Title", "settings.title", 48, true, Theme.Ink, TextAlignmentOptions.Center)
                .rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(width - pad * 2, 64));

            void RowLabel(Transform parent, string name, string key, float y, float labelWidth = 520) =>
                UIKit.Label(parent, name, key, labelSize, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Place(topLeft, new Vector2(left, y), new Vector2(labelWidth, rowHeight));
            // At the right edge of the column, centred on the row.
            void Control(RectTransform rect, float y, float controlWidth, float height) =>
                rect.Place(topLeft, new Vector2(left + column - controlWidth, y - (rowHeight - height) / 2), new Vector2(controlWidth, height));

            // English ("Hangul", "Fullscreen") runs wider than half a capsule
            // at the usual size.
            void FitLabels(SegmentedToggle toggle)
            {
                foreach (var label in toggle.labels)
                {
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 18;
                    label.fontSizeMax = label.fontSize;
                    label.margin = new Vector4(14, 0, 14, 0);
                }
            }

            panel.tabs = UIKit.SegmentedToggle(c, "Tabs", new[] { "settings.general", "settings.display", "settings.sound", "settings.controls" }, 72);
            FitLabels(panel.tabs);
            panel.tabs.GetComponent<RectTransform>().Place(new Vector2(0.5f, 1), new Vector2(0, -124), new Vector2(column, 72));
            Transform Page(string name) => UIKit.Node(name, c).Place(topLeft, Vector2.zero, new Vector2(width, cardHeight));

            var general = Page("GeneralPage");
            RowLabel(general, "LanguageLabel", "settings.language", RowY(0));
            Control(BuildLanguageToggle(general, "LanguageToggle").GetComponent<RectTransform>(), RowY(0), 236, 72);
            RowLabel(general, "LettersLabel", "settings.janggiLetters", RowY(1));
            panel.janggiLetters = UIKit.SegmentedToggle(general, "LettersToggle", new[] { "settings.hangul", "settings.hanja" }, 72);
            FitLabels(panel.janggiLetters);
            Control(panel.janggiLetters.GetComponent<RectTransform>(), RowY(1), 236, 72);
            // The players this player blocked online (BlockList), all let go at once.
            panel.blockedText = UIKit.Text(general, "BlockedLabel", Loc.Get("settings.blocked", 0), labelSize, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
            panel.blockedText.rectTransform.Place(topLeft, new Vector2(left, RowY(2)), new Vector2(520, rowHeight));
            panel.unblockAllButton = UIKit.CapsuleButton(general, "UnblockAllButton", "settings.unblockAll", new Vector2(236, rowHeight), false, 24);
            Control(panel.unblockAllButton.GetComponent<RectTransform>(), RowY(2), 236, rowHeight);

            var display = Page("DisplayPage");
            RowLabel(display, "WindowModeLabel", "settings.windowMode", RowY(0));
            panel.windowMode = UIKit.SegmentedToggle(display, "WindowModeToggle", new[] { "settings.fullscreen", "settings.windowed" }, 72);
            FitLabels(panel.windowMode);
            Control(panel.windowMode.GetComponent<RectTransform>(), RowY(0), 320, 72);
            var sizeRow = UIKit.Node("WindowSizeRow", display).Place(topLeft, Vector2.zero, new Vector2(width, cardHeight));
            panel.windowSizeRow = sizeRow.gameObject.AddComponent<CanvasGroup>();
            RowLabel(sizeRow, "WindowSizeLabel", "settings.windowSize", RowY(1));
            var (sizeFrame, sizeText, smaller, larger) = UIKit.Stepper(sizeRow, "WindowSize", rowHeight, "1920 × 1080", labelSize);
            Control(sizeFrame.rectTransform, RowY(1), 320, rowHeight);
            panel.windowSizeText = sizeText;
            panel.windowSmallerButton = smaller;
            panel.windowLargerButton = larger;
            RowLabel(display, "ScreenShakeLabel", "settings.screenShake", RowY(2));
            panel.screenShake = UIKit.SegmentedToggle(display, "ScreenShakeToggle", new[] { "option.on", "option.off" }, 72);
            FitLabels(panel.screenShake);
            Control(panel.screenShake.GetComponent<RectTransform>(), RowY(2), 320, 72);

            var sound = Page("SoundPage");
            (Slider, TMP_Text) VolumeRow(string name, string key, int row)
            {
                var y = RowY(row);
                RowLabel(sound, name + "Label", key, y, 360);
                var slider = UIKit.Slider(sound, name + "Slider", new Vector2(320, 36));
                slider.GetComponent<RectTransform>().Place(topLeft, new Vector2(left + column - 100 - 320, y - (rowHeight - 36) / 2), new Vector2(320, 36));
                var value = UIKit.Text(sound, name + "Value", "80%", 26, true, Theme.Ink, TextAlignmentOptions.MidlineRight);
                Control(value.rectTransform, y, 84, rowHeight);
                return (slider, value);
            }
            (panel.masterSlider, panel.masterValue) = VolumeRow("Master", "settings.masterVolume", 0);
            (panel.musicSlider, panel.musicValue) = VolumeRow("Music", "settings.musicVolume", 1);
            (panel.effectsSlider, panel.effectsValue) = VolumeRow("Effects", "settings.effectsVolume", 2);
            (panel.interfaceSlider, panel.interfaceValue) = VolumeRow("Interface", "settings.interfaceVolume", 3);

            var controls = Page("ControlsPage");
            var rows = new System.Collections.Generic.List<KeyBindRow>();
            for (var i = 0; i < actions.Length; i++)
            {
                var y = RowY(i);
                RowLabel(controls, "Bind" + actions[i] + "Label", "bind." + actions[i], y);
                var button = UIKit.CapsuleButton(controls, "Bind" + actions[i], "settings.reset", new Vector2(240, rowHeight), false, 24);
                Control(button.GetComponent<RectTransform>(), y, 240, rowHeight);
                var keyText = button.GetComponentInChildren<TMP_Text>();
                // The key name is set by SettingsPanel, not a Loc key.
                Object.DestroyImmediate(keyText.GetComponent<LocalizedText>());
                keyText.text = "Ctrl";
                // "Press a key · Esc cancels" while it waits.
                keyText.enableAutoSizing = true;
                keyText.fontSizeMin = 16;
                keyText.fontSizeMax = 24;
                keyText.margin = new Vector4(16, 0, 16, 0);
                var row = button.gameObject.AddComponent<KeyBindRow>();
                row.action = actions[i];
                row.button = button;
                row.keyText = keyText;
                rows.Add(row);
            }
            panel.keyRows = rows.ToArray();
            panel.pages = new[] { general, display, sound, controls }.Select(page => page.gameObject).ToArray();
            for (var i = 1; i < panel.pages.Length; i++) panel.pages[i].SetActive(false);

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
            const float width = 840, pad = 64, rowHeight = 52, gap = 8, seatHeight = 60, seatPitch = 72;
            const int seats = 4;
            // A row per seat (who plays it), then as many rule rows as fit a
            // 1080 screen with the card's other parts; more scroll. MainMenuUI
            // closes the card up by the seats a match doesn't have.
            var rulesHeight = Mathf.Min(UIKit.RuleRows(false) * (rowHeight + gap) - gap, 7 * (rowHeight + gap) - gap);
            var overlay = UIKit.Image(root, "SetupPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            menu.setupPanel = overlay.gameObject;

            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            UIKit.Appear(overlay, card.rectTransform);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 148 + seats * seatPitch + rulesHeight + 40 + 84 + 48));
            menu.setupCard = card.rectTransform;
            menu.seatRowPitch = seatPitch;
            var top = new Vector2(0.5f, 1);

            UIKit.Label(card.transform, "Title", "match.title", 48, true, Theme.Ink, TextAlignmentOptions.Center)
                .rectTransform.Place(top, new Vector2(0, -44), new Vector2(width - pad * 2, 64));
            menu.seatRows = new GameObject[seats];
            menu.seatLabels = new TMP_Text[seats];
            menu.seatToggles = new SegmentedToggle[seats];
            for (var i = 0; i < seats; i++)
            {
                var y = -136 - i * seatPitch;
                var row = UIKit.Node("Seat" + i, card.transform).Place(new Vector2(0, 1), new Vector2(pad, y), new Vector2(width - pad * 2, seatHeight));
                menu.seatRows[i] = row.gameObject;
                // The side's name, as the pieces name it: set by MainMenuUI.
                menu.seatLabels[i] = UIKit.Text(row, "Label", SideStyle.Name(i), 28, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
                menu.seatLabels[i].rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(140, seatHeight), new Vector2(0, 0.5f));
                menu.seatToggles[i] = UIKit.SegmentedToggle(row, "Who",
                    new[] { "opponent.Human", "opponent.AIEasy", "opponent.AINormal", "opponent.AIHard" }, seatHeight);
                menu.seatToggles[i].GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(width - pad * 2 - 150, seatHeight), new Vector2(1, 0.5f));
            }
            menu.setupRules = UIKit.RulesPanel(card.transform, "Rules", width - pad * 2, rowHeight, gap, 28, online: false, height: rulesHeight);
            menu.setupRules.GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(pad, -148 - seats * seatPitch), menu.setupRules.GetComponent<RectTransform>().sizeDelta);

            menu.setupCancelButton = UIKit.CapsuleButton(card.transform, "CancelButton", "setup.cancel", new Vector2(240, 84), false, 32);
            menu.setupCancelButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(pad, 48), new Vector2(240, 84));
            menu.setupStartButton = UIKit.CapsuleButton(card.transform, "StartButton", "setup.start", new Vector2(280, 84), true, 32);
            menu.setupStartButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-pad, 48), new Vector2(280, 84));
            overlay.gameObject.SetActive(false);
        }

        // The rankings: Top / Around me / Friends, this player's own line, ten
        // rows of place, name, rating and record. LeaderboardPanel fills it.
        private static LeaderboardPanel BuildLeaderboardPanel(Transform root)
        {
            const float width = 1000, pad = 64, rowHeight = 48, rowGap = 2;
            const int rowCount = 10;
            var inner = width - pad * 2;
            var overlay = UIKit.Image(root, "LeaderboardPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            var panel = overlay.gameObject.AddComponent<LeaderboardPanel>();

            const float rowsTop = -260;
            var rowsBottom = rowsTop - rowCount * (rowHeight + rowGap);
            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            UIKit.Appear(overlay, card.rectTransform);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, -rowsBottom + 20 + 84 + 40));
            var c = card.transform;
            var topLeft = new Vector2(0, 1);
            var topRight = new Vector2(1, 1);

            UIKit.Label(c, "Title", "rank.title", 48, true, Theme.Ink, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(topLeft, new Vector2(pad, -40), new Vector2(300, 72));
            panel.scopeToggle = UIKit.SegmentedToggle(c, "ScopeToggle", new[] { "rank.scope.top", "rank.scope.around", "rank.scope.friends" }, 64);
            panel.scopeToggle.GetComponent<RectTransform>().Place(topRight, new Vector2(-pad, -44), new Vector2(480, 64));

            var summary = UIKit.Panel(c, "Summary", Theme.HanjiField, Theme.FieldBorder, 2f);
            summary.rectTransform.Place(topLeft, new Vector2(pad, -136), new Vector2(inner, 64));
            panel.summaryText = UIKit.Text(summary.transform, "Text", Loc.Get("rank.summary", 1000, Loc.Get("rank.noGames")), 28, true, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            panel.summaryText.rectTransform.Stretch();
            panel.summaryText.rectTransform.offsetMin = new Vector2(28, 0);
            panel.summaryText.rectTransform.offsetMax = new Vector2(-28, 0);

            // Columns, left to right, as x and width within a row.
            var place = (x: 0f, w: 96f);
            var name = (x: 112f, w: 400f);
            var rating = (x: 520f, w: 140f);
            var record = (x: 676f, w: inner - 676f - 24);
            TMP_Text Cell(Transform row, string cellName, string content, (float x, float w) column, bool bold, Color color, TextAlignmentOptions align, float size = 26)
            {
                var text = UIKit.Text(row, cellName, content, size, bold, color, align);
                text.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(column.x, 0), new Vector2(column.w, rowHeight), new Vector2(0, 0.5f));
                return text;
            }
            TMP_Text HeaderCell(Transform row, string cellName, string key, (float x, float w) column, TextAlignmentOptions align)
            {
                var text = Cell(row, cellName, Loc.Get(key), column, false, Theme.InkFaint, align, 22);
                text.gameObject.AddComponent<LocalizedText>().key = key;
                return text;
            }
            var header = UIKit.Node("Header", c).Place(topLeft, new Vector2(pad, -212), new Vector2(inner, 40));
            HeaderCell(header, "Place", "rank.place", place, TextAlignmentOptions.Center);
            HeaderCell(header, "Name", "rank.name", name, TextAlignmentOptions.MidlineLeft);
            HeaderCell(header, "Rating", "rank.rating", rating, TextAlignmentOptions.Center);
            HeaderCell(header, "Record", "rank.recordHeader", record, TextAlignmentOptions.MidlineRight);

            panel.rows = new LeaderboardRow[rowCount];
            for (var i = 0; i < rowCount; i++)
            {
                var bg = UIKit.Panel(c, "Row" + i, new Color(1, 1, 1, 0.45f), null, 2.5f);
                bg.rectTransform.Place(topLeft, new Vector2(pad, rowsTop - i * (rowHeight + rowGap)), new Vector2(inner, rowHeight));
                var row = bg.gameObject.AddComponent<LeaderboardRow>();
                var own = UIKit.Panel(bg.transform, "Own", new Color(Theme.Seal.r, Theme.Seal.g, Theme.Seal.b, 0.16f), Theme.Seal, 2.5f);
                own.rectTransform.Stretch();
                row.ownHighlight = own.gameObject;
                own.gameObject.SetActive(false);
                row.placeText = Cell(bg.transform, "Place", (i + 1).ToString(), place, true, Theme.Ink, TextAlignmentOptions.Center);
                // ASCII placeholder: the Steam name replaces it at runtime.
                row.nameText = Cell(bg.transform, "Name", "Player", name, false, Theme.Ink, TextAlignmentOptions.MidlineLeft);
                row.nameText.overflowMode = TextOverflowModes.Ellipsis;
                row.ratingText = Cell(bg.transform, "Rating", "1000", rating, true, Theme.Ink, TextAlignmentOptions.Center);
                row.recordText = Cell(bg.transform, "Record", Loc.Get("rank.record", 0, 0), record, false, Theme.InkSoft, TextAlignmentOptions.MidlineRight);
                bg.gameObject.SetActive(false);
                panel.rows[i] = row;
            }

            panel.statusText = UIKit.Text(c, "Status", Loc.Get("rank.loading"), 28, false, Theme.InkFaint, TextAlignmentOptions.Center);
            panel.statusText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, rowsTop - 2 * (rowHeight + rowGap)), new Vector2(inner, 48));

            UIKit.Label(c, "Caption", "rank.caption", 22, false, Theme.InkFaint, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(new Vector2(0, 0), new Vector2(pad, 72), new Vector2(inner - 260, 36));
            panel.closeButton = UIKit.CapsuleButton(c, "CloseButton", "settings.close", new Vector2(240, 84), true, 32);
            panel.closeButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-pad, 40), new Vector2(240, 84));
            overlay.gameObject.SetActive(false);
            return panel;
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
