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

        private static readonly Vector2 CardSize = new Vector2(1000, 820);
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
            var canvas = UIKit.Canvas("Lobby_UI", 0.5f, 0);
            var root = canvas.transform;
            var ui = canvas.gameObject.AddComponent<LobbySceneUI>();
            ui.okColor = Theme.StatusOk;
            ui.busyColor = Theme.StatusBusy;
            ui.errorColor = Theme.StatusError;

            UIKit.Image(root, "Background", null, Theme.MenuBackground).rectTransform.Stretch();
            var card = UIKit.Panel(root, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, CardSize);
            var c = card.transform;

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

            ui.createButton = UIKit.CapsuleButton(view, "CreateButton", "lobby.create", new Vector2(CardSize.x - Pad * 2, 120), true, 40, "lobby.createSub");
            ui.createButton.GetComponent<RectTransform>().Place(TopLeft, new Vector2(Pad, -170), new Vector2(CardSize.x - Pad * 2, 120));

            UIKit.Image(view, "Divider", null, Theme.Divider).rectTransform.Place(TopLeft, new Vector2(Pad, -334), new Vector2(CardSize.x - Pad * 2, 2));
            UIKit.Label(view, "JoinLabel", "lobby.joinLabel", 26, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(Pad, -360), new Vector2(600, 40));

            ui.joinCodeInput = CodeInput(view);
            ui.joinCodeInput.GetComponent<RectTransform>().Place(TopLeft, new Vector2(Pad, -414), new Vector2(640, 92));
            ui.joinButton = UIKit.CapsuleButton(view, "JoinButton", "lobby.join", new Vector2(212, 92), false, 32);
            ui.joinButton.GetComponent<RectTransform>().Place(TopRight, new Vector2(-Pad, -414), new Vector2(212, 92));

            ui.backButton = UIKit.CapsuleButton(view, "BackButton", "lobby.back", new Vector2(220, 92), false, 32);
            ui.backButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(Pad, 56), new Vector2(220, 92));
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

            // Match option: the host sets it here, the guest sees it read-only.
            UIKit.Label(view, "AimGuideLabel", "settings.aimGuide", 26, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(Pad, -312), new Vector2(400, 60));
            ui.aimGuideToggle = UIKit.OnOffToggle(view, "AimGuideToggle", 60);
            ui.aimGuideToggle.GetComponent<RectTransform>().Place(TopRight, new Vector2(-Pad, -312), new Vector2(236, 60));

            ui.hostSlot = Slot(view, "HostSlot", TopLeft, new Vector2(Pad, -396), true, "Host", "lobby.host");
            ui.guestSlot = Slot(view, "GuestSlot", TopRight, new Vector2(-Pad, -396), false, "Guest", "lobby.guest");

            ui.leaveButton = UIKit.CapsuleButton(view, "LeaveButton", "lobby.leave", new Vector2(264, 92), false, 32);
            ui.leaveButton.GetComponent<RectTransform>().Place(new Vector2(0, 0), new Vector2(Pad, 56), new Vector2(264, 92));
            ui.startButton = UIKit.CapsuleButton(view, "StartButton", "lobby.start", new Vector2(312, 92), true, 32);
            ui.startButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-Pad, 56), new Vector2(312, 92));

            view.gameObject.SetActive(false);
        }

        // A seat card: filled (stone, Steam name, role) or empty (dashed
        // stone, "open slot", and the invite button for the host to use).
        private static LobbyPlayerSlot Slot(Transform parent, string name, Vector2 corner, Vector2 position, bool black, string placeholderName, string roleKey)
        {
            var size = new Vector2(424, 240);
            var node = UIKit.Node(name, parent).Place(corner, position, size);
            var slot = node.gameObject.AddComponent<LobbyPlayerSlot>();
            var colorKey = black ? "player.black" : "player.white";

            var filled = UIKit.Panel(node, "Filled", new Color(1, 1, 1, 0.55f), Theme.Ink);
            filled.rectTransform.Stretch();
            UIKit.Stone(filled.transform, "Stone", 64, black ? Theme.StoneBlack : Theme.StoneWhite, black ? Theme.Ink : Theme.InkMuted, false)
                .Place(TopLeft, new Vector2(32, -32), new Vector2(64, 64));
            // ASCII placeholders: the real Steam name replaces them at runtime.
            slot.nameText = UIKit.Text(filled.transform, "Name", placeholderName, 36, true, Theme.Ink, TextAlignmentOptions.MidlineLeft);
            slot.nameText.overflowMode = TextOverflowModes.Ellipsis;
            slot.nameText.rectTransform.Place(TopLeft, new Vector2(116, -28), new Vector2(280, 48));
            slot.roleText = UIKit.Text(filled.transform, "Role", $"{Loc.Get(roleKey)} · {Loc.Get(colorKey)}", 26, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
            slot.roleText.rectTransform.Place(TopLeft, new Vector2(116, -78), new Vector2(280, 36));
            slot.filledView = filled.gameObject;

            var empty = UIKit.Panel(node, "Empty", Color.clear, Theme.InkFaint);
            empty.rectTransform.Stretch();
            UIKit.Image(empty.transform, "Stone", UIKit.CircleDashed, Theme.InkFaint).rectTransform.Place(TopLeft, new Vector2(32, -32), new Vector2(64, 64));
            UIKit.Label(empty.transform, "Name", "lobby.emptySlot", 36, true, Theme.InkFaint, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(116, -28), new Vector2(280, 48));
            UIKit.Label(empty.transform, "Role", colorKey, 26, false, Theme.InkFaint, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Place(TopLeft, new Vector2(116, -78), new Vector2(280, 36));
            slot.inviteButton = UIKit.CapsuleButton(empty.transform, "InviteButton", "lobby.invite", new Vector2(340, 72), false, 26);
            slot.inviteButton.GetComponent<RectTransform>().Place(new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(340, 72));
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
