using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AlkkagiUIEditor
{
    // Builds Assets/UI/Lobby_UI.prefab and swaps it into LobbyScene in place
    // of LobbyBootstrap, whose LobbySceneUI used to build a plain uGUI layout
    // in code at runtime.
    internal static class LobbyBuilder
    {
        private const string PrefabPath = "Assets/UI/Lobby_UI.prefab";
        private const string ScenePath = "Assets/Scenes/LobbyScene.unity";
        private const string OldBootstrapName = "LobbyBootstrap";

        private static readonly Vector2 CardSize = new Vector2(1000, 940);
        private static readonly Vector2 RulesCardSize = new Vector2(600, 940);
        private const float SeatHeight = 80, SeatGap = 10;
        private const float CardGap = 32;
        private const float Pad = 64;

        [MenuItem("Tools/Alkkagi UI/4. Build lobby")]
        public static void Build()
        {
            UIKit.Load();
            var prefab = UIKit.SavePrefab(PrefabPath, BuildRoot);
            WireScene(prefab);
            Debug.Log("[Alkkagi UI] Lobby built and wired into LobbyScene.");
        }

        private static GameObject BuildRoot()
        {
            var canvas = UIKit.Canvas("Lobby_UI", null, 0);
            var root = canvas.transform;
            var ui = canvas.gameObject.AddComponent<LobbySceneUI>();
            ui.okColor = Theme.StatusOk;
            ui.busyColor = Theme.StatusBusy;
            ui.errorColor = Theme.StatusError;

            UIKit.Image(root, "Background", null, Theme.MenuBackground).rectTransform.Stretch();
            var card = UIKit.Panel(root, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, CardSize);
            var c = card.transform;
            ui.card = card.rectTransform;
            // A side card sits to the right in both views (lobby browser, then
            // rules); shift the pair back to center.
            ui.lobbyCardShift = -(RulesCardSize.x + CardGap) / 2;

            UIKit.Label(c, "Title", "lobby.title", 48, true, Theme.Ink, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(Pad, -52), new Vector2(360, 72));

            var chip = UIKit.Capsule(c, "Status", 58, Theme.HanjiField, Theme.FieldBorder);
            chip.rectTransform.Place(TopRight, new Vector2(-Pad, -59), new Vector2(520, 58));
            ui.statusDot = UIKit.Image(chip.transform, "Dot", UIKit.Circle, Theme.StatusBusy);
            ui.statusDot.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(16, 16), new Vector2(0, 0.5f));
            ui.statusText = UIKit.Text(chip.transform, "Text", Loc.Get("lobby.status.idle"), 24, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
            ui.statusText.overflowMode = TextOverflowModes.Ellipsis;
            ui.statusText.rectTransform.Stretch();
            ui.statusText.rectTransform.offsetMin = new Vector2(52, 0);
            ui.statusText.rectTransform.offsetMax = new Vector2(-20, 0);

            BuildIdleView(c, ui);
            BuildLobbyView(c, ui);
            return canvas.gameObject;
        }

        private static Vector2 TopLeft => new Vector2(0, 1);
        private static Vector2 TopRight => new Vector2(1, 1);

        private static void BuildIdleView(Transform card, LobbySceneUI ui)
        {
            var view = UIKit.Node("IdleView", card).Stretch();
            ui.idleView = view.gameObject;

            var half = new Vector2((CardSize.x - Pad * 2 - 24) / 2, 120);
            ui.createButton = UIKit.CapsuleButton(view, "CreateButton", "lobby.create", half, true, 36, "lobby.createSub");
            ui.createButton.GetComponent<RectTransform>().Place(TopLeft, new Vector2(Pad, -170), half);
            ui.quickButton = UIKit.CapsuleButton(view, "QuickButton", "lobby.quick", half, false, 36, "lobby.quickSub");
            ui.quickButton.GetComponent<RectTransform>().Place(TopRight, new Vector2(-Pad, -170), half);

            UIKit.Image(view, "Divider", null, Theme.Divider).rectTransform.Place(TopLeft, new Vector2(Pad, -334), new Vector2(CardSize.x - Pad * 2, 2));
            UIKit.Label(view, "JoinLabel", "lobby.joinLabel", 26, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(Pad, -360), new Vector2(600, 40));

            ui.joinCodeInput = CodeInput(view);
            ui.joinCodeInput.GetComponent<RectTransform>().Place(TopLeft, new Vector2(Pad, -414), new Vector2(640, 92));
            ui.joinButton = UIKit.CapsuleButton(view, "JoinButton", "lobby.join", new Vector2(212, 92), false, 32);
            ui.joinButton.GetComponent<RectTransform>().Place(TopRight, new Vector2(-Pad, -414), new Vector2(212, 92));

            ui.backButton = UIKit.CapsuleButton(view, "BackButton", "lobby.back", new Vector2(220, 92), false, 32);
            ui.backButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(Pad, 56), new Vector2(220, 92));

            BuildBrowserCard(view, ui);
        }

        // Open public lobbies, beside the main card: host, rules, Join.
        private static void BuildBrowserCard(Transform idleView, LobbySceneUI ui)
        {
            const float pad = 48, rowHeight = 100, gap = 12;
            const int rows = 5;
            var width = RulesCardSize.x - pad * 2;
            var card = UIKit.Panel(idleView, "BrowserCard", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            card.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(CardGap, 0), RulesCardSize, new Vector2(0, 0.5f));
            var c = card.transform;

            UIKit.Label(c, "Title", "lobby.browser", 40, true, Theme.Ink, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(pad, -52), new Vector2(300, 72));
            ui.refreshButton = UIKit.CapsuleButton(c, "RefreshButton", "lobby.refresh", new Vector2(176, 64), false, 26);
            ui.refreshButton.GetComponent<RectTransform>().Place(TopRight, new Vector2(-pad, -56), new Vector2(176, 64));

            ui.browserRows = new LobbyListRow[rows];
            for (var i = 0; i < rows; i++)
            {
                var rowBg = UIKit.Panel(c, "Row" + i, new Color(1, 1, 1, 0.55f), Theme.FieldBorder);
                rowBg.rectTransform.Place(TopLeft, new Vector2(pad, -148 - i * (rowHeight + gap)), new Vector2(width, rowHeight));
                var row = rowBg.gameObject.AddComponent<LobbyListRow>();
                // ASCII placeholder: the host's Steam name replaces it at runtime.
                row.hostText = UIKit.Text(rowBg.transform, "Host", "Host", 30, true, Theme.Ink, TextAlignmentOptions.MidlineLeft);
                row.hostText.overflowMode = TextOverflowModes.Ellipsis;
                row.hostText.rectTransform.Place(TopLeft, new Vector2(28, -12), new Vector2(width - 220, 42));
                row.rulesText = UIKit.Text(rowBg.transform, "Rules", Loc.Get("lobby.rowRules", Loc.Get("board.Go"), Loc.Get("pieces.GoStones"), 6, 6), 22, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
                row.rulesText.overflowMode = TextOverflowModes.Ellipsis;
                row.rulesText.rectTransform.Place(TopLeft, new Vector2(28, -56), new Vector2(width - 220, 32));
                row.joinButton = UIKit.CapsuleButton(rowBg.transform, "JoinButton", "lobby.join", new Vector2(144, 64), true, 28);
                row.joinButton.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-18, 0), new Vector2(144, 64), new Vector2(1, 0.5f));
                rowBg.gameObject.SetActive(false);
                ui.browserRows[i] = row;
            }

            ui.browserEmptyText = UIKit.Text(c, "Empty", Loc.Get("lobby.browserEmpty"), 28, false, Theme.InkFaint, TextAlignmentOptions.Center);
            ui.browserEmptyText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -320), new Vector2(width, 48));
            UIKit.Label(c, "Caption", "lobby.browserCaption", 22, false, Theme.InkFaint, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(new Vector2(0, 0), new Vector2(pad, 44), new Vector2(width, 36));
        }

        private static void BuildLobbyView(Transform card, LobbySceneUI ui)
        {
            var view = UIKit.Node("LobbyView", card).Stretch();
            ui.lobbyView = view.gameObject;

            UIKit.Label(view, "CodeLabel", "lobby.code", 26, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(Pad, -148), new Vector2(400, 40));
            var codeField = UIKit.Panel(view, "CodeField", Theme.HanjiField, Theme.FieldBorder, 2f);
            codeField.rectTransform.Place(TopLeft, new Vector2(Pad, -196), new Vector2(684, 92));
            ui.lobbyCodeText = UIKit.Text(codeField.transform, "Code", "109775241234567890", 34, false, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            ui.lobbyCodeText.rectTransform.Stretch(28);
            ui.copyButton = UIKit.CapsuleButton(view, "CopyButton", "lobby.copy", new Vector2(172, 92), false, 30);
            ui.copyButton.GetComponent<RectTransform>().Place(TopRight, new Vector2(-Pad, -196), new Vector2(172, 92));
            ui.copyLabel = ui.copyButton.GetComponentInChildren<TMP_Text>();
            // Toggled between Copy/Copied by LobbySceneUI, so no static binding.
            Object.DestroyImmediate(ui.copyLabel.GetComponent<LocalizedText>());

            // Four seats, the host's first; LobbySceneUI shows as many as the
            // rules allow.
            ui.seats = new LobbyPlayerSlot[MatchRoster.MaxPlayers];
            for (var i = 0; i < ui.seats.Length; i++)
                ui.seats[i] = Slot(view, "Seat" + i, i, new Vector2(Pad, -318 - i * (SeatHeight + SeatGap)));
            var belowSeats = -318 - ui.seats.Length * (SeatHeight + SeatGap) - 12;

            // Who can join: the host picks, the guest sees it greyed out.
            UIKit.Label(view, "VisibilityLabel", "lobby.visibility", 26, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(Pad, belowSeats), new Vector2(300, 72));
            ui.visibilityToggle = UIKit.SegmentedToggle(view, "VisibilityToggle", new[] { "lobby.vis.public", "lobby.vis.friends", "lobby.vis.private" }, 72);
            ui.visibilityToggle.GetComponent<RectTransform>().Place(TopRight, new Vector2(-Pad, belowSeats), new Vector2(520, 72));

            ui.leaveButton = UIKit.CapsuleButton(view, "LeaveButton", "lobby.leave", new Vector2(264, 92), false, 32);
            ui.leaveButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(Pad, 56), new Vector2(264, 92));
            ui.startButton = UIKit.CapsuleButton(view, "StartButton", "lobby.start", new Vector2(312, 92), true, 32);
            ui.startButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-Pad, 56), new Vector2(312, 92));

            BuildRulesCard(view, ui);
            view.gameObject.SetActive(false);
        }

        // The match rules, beside the main card: the host edits them, the
        // guest sees them read-only.
        private static void BuildRulesCard(Transform lobbyView, LobbySceneUI ui)
        {
            const float pad = 48, rowHeight = 46, gap = 4;
            var card = UIKit.Panel(lobbyView, "RulesCard", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            card.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(CardGap, 0), RulesCardSize, new Vector2(0, 0.5f));
            var c = card.transform;

            UIKit.Label(c, "Title", "match.title", 40, true, Theme.Ink, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(pad, -52), new Vector2(RulesCardSize.x - pad * 2, 72));
            ui.rulesPanel = UIKit.RulesPanel(c, "Rules", RulesCardSize.x - pad * 2, rowHeight, gap, 24, online: true);
            var rules = ui.rulesPanel.GetComponent<RectTransform>();
            rules.Place(TopLeft, new Vector2(pad, -148), rules.sizeDelta);

            ui.rulesCaption = UIKit.Text(c, "Caption", Loc.Get("lobby.rulesHost"), 22, false, Theme.InkFaint, TextAlignmentOptions.MidlineLeft);
            ui.rulesCaption.rectTransform.Place(new Vector2(0, 0), new Vector2(pad, 44), new Vector2(RulesCardSize.x - pad * 2, 36));
        }

        // A seat row: filled (stone, Steam name, role, and for the host a
        // Remove button on the guests' seats) or empty (dashed stone, "open
        // slot", the side it would play, and the host's invite button).
        private static LobbyPlayerSlot Slot(Transform parent, string name, int seat, Vector2 position)
        {
            var size = new Vector2(CardSize.x - Pad * 2, SeatHeight);
            var node = UIKit.Node(name, parent).Place(TopLeft, position, size);
            var slot = node.gameObject.AddComponent<LobbyPlayerSlot>();
            var colorKey = seat == 0 ? "player.black" : "player.white";
            var right = new Vector2(1, 0.5f);

            var filled = UIKit.Panel(node, "Filled", new Color(1, 1, 1, 0.55f), Theme.Ink);
            filled.rectTransform.Stretch();
            var stone = UIKit.Stone(filled.transform, "Stone", 52, seat == 0 ? Theme.StoneBlack : Theme.StoneWhite, seat == 0 ? Theme.Ink : Theme.InkMuted, false)
                .Place(new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(52, 52), new Vector2(0, 0.5f));
            UIKit.Mark(stone, seat);
            // ASCII placeholders: the real Steam name replaces them at runtime.
            slot.nameText = UIKit.Text(filled.transform, "Name", seat == 0 ? "Host" : "Guest", 32, true, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            slot.nameText.overflowMode = TextOverflowModes.Ellipsis;
            slot.nameText.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(96, 0), new Vector2(250, 56), new Vector2(0, 0.5f));
            // Their rating, once their game has shared it.
            var chip = UIKit.Capsule(filled.transform, "Rating", 44, Theme.HanjiField, Theme.FieldBorder);
            chip.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(358, 0), new Vector2(98, 44), new Vector2(0, 0.5f));
            slot.ratingText = UIKit.Text(chip.transform, "Text", "1000", 24, true, Theme.Ink, TextAlignmentOptions.Center);
            slot.ratingText.rectTransform.Stretch();
            slot.ratingChip = chip.gameObject;
            slot.roleText = UIKit.Text(filled.transform, "Role", $"{Loc.Get(seat == 0 ? "lobby.host" : "lobby.guest")} · {Loc.Get(colorKey)}", 24, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
            slot.roleText.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(472, 0), new Vector2(190, 56), new Vector2(0, 0.5f));
            slot.filledView = filled.gameObject;
            if (seat > 0)
            {
                // The host can send a guest away (public lobbies let anyone in).
                slot.kickButton = UIKit.CapsuleButton(filled.transform, "KickButton", "lobby.kick", new Vector2(176, 56), false, 24);
                slot.kickButton.GetComponent<RectTransform>().Place(right, new Vector2(-12, 0), new Vector2(176, 56), right);
                slot.kickButton.gameObject.SetActive(false);
            }

            var empty = UIKit.Panel(node, "Empty", Color.clear, Theme.InkFaint);
            empty.rectTransform.Stretch();
            UIKit.Image(empty.transform, "Stone", UIKit.CircleDashed, Theme.InkFaint)
                .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(52, 52), new Vector2(0, 0.5f));
            UIKit.Label(empty.transform, "Name", "lobby.emptySlot", 32, true, Theme.InkFaint, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(96, 0), new Vector2(360, 56), new Vector2(0, 0.5f));
            var emptyRole = UIKit.Text(empty.transform, "Role", Loc.Get(colorKey), 24, false, Theme.InkFaint, TextAlignmentOptions.MidlineLeft);
            emptyRole.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(472, 0), new Vector2(190, 56), new Vector2(0, 0.5f));
            UIKit.MarkLabel(emptyRole, seat);
            slot.inviteButton = UIKit.CapsuleButton(empty.transform, "InviteButton", "lobby.invite", new Vector2(236, 56), false, 22);
            slot.inviteButton.GetComponent<RectTransform>().Place(right, new Vector2(-12, 0), new Vector2(236, 56), right);
            slot.emptyView = empty.gameObject;
            empty.gameObject.SetActive(false);
            return slot;
        }

        private static TMP_InputField CodeInput(Transform parent)
        {
            var field = UIKit.Panel(parent, "CodeInput", Theme.HanjiField, Theme.FieldBorder, 2f, raycast: true);
            var viewport = UIKit.Node("TextArea", field.transform).Stretch();
            viewport.offsetMin = new Vector2(28, 8);
            viewport.offsetMax = new Vector2(-28, -8);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = UIKit.Text(viewport, "Text", "", 32, false, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            text.rectTransform.Stretch();
            var placeholder = UIKit.Label(viewport, "Placeholder", "lobby.codePlaceholder", 32, false, Theme.InkFaint, TextAlignmentOptions.MidlineLeft);
            placeholder.rectTransform.Stretch();

            var input = field.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = field;
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = UIKit.Regular;
            input.pointSize = 32;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 20;
            return input;
        }

        private static void WireScene(GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == OldBootstrapName || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == PrefabPath)
                    Object.DestroyImmediate(go);
            }
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
