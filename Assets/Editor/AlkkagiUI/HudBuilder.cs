using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlkkagiUIEditor
{
    // Builds Assets/UI/MainGame_UI.prefab and swaps it into GameScene. The
    // board (top-down CM vcam_main) fills the screen height, leaving ~490px
    // columns either side at 1080p - so the player panels live in those
    // columns, black bottom-left and white top-right, next to where each
    // side's stones start.
    internal static class HudBuilder
    {
        private const string PrefabPath = "Assets/UI/MainGame_UI.prefab";
        private const string ScenePath = "Assets/Scenes/GameScene.unity";

        private static readonly Vector2 PanelSize = new Vector2(360, 360);
        private const float Pad = 28;
        private const float Margin = 28;

        [MenuItem("Tools/Alkkagi UI/2. Build HUD")]
        public static void Build()
        {
            UIKit.Load();
            var prefab = UIKit.SavePrefab(PrefabPath, BuildRoot);
            WireScene(prefab);
            Debug.Log("[Alkkagi UI] HUD built and wired into GameScene.");
        }

        private static GameObject BuildRoot()
        {
            // Match height: the board scales with screen height (fixed vertical
            // FOV), so the side panels keep their place next to it on 16:10 too.
            var canvas = UIKit.Canvas("MainGame_UI", 1f, 0);
            var root = canvas.transform;
            var controller = canvas.gameObject.AddComponent<MainGameUIController>();
            controller.blackStoneColor = Theme.StoneBlack;
            controller.whiteStoneColor = Theme.StoneWhite;

            BuildTurnPill(root, controller);
            controller.playerPanels = new[]
            {
                BuildPlayerPanel(root, "BlackPanel", new Vector2(0, 0), new Vector2(Margin, Margin), Theme.StoneBlack, Theme.Ink),
                BuildPlayerPanel(root, "WhitePanel", new Vector2(1, 1), new Vector2(-Margin, -Margin), Theme.StoneWhite, Theme.InkMuted),
            };
            BuildWinPanel(root, controller);
            return canvas.gameObject;
        }

        private static void BuildTurnPill(Transform root, MainGameUIController controller)
        {
            var pill = UIKit.Capsule(root, "TurnPill", 56, Theme.Hanji, Theme.Ink);
            pill.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(280, 56));
            controller.turnPill = pill.gameObject;

            var stone = UIKit.Image(pill.transform, "TurnStone", UIKit.Circle, Theme.StoneBlack);
            stone.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(28, 28), new Vector2(0, 0.5f));
            UIKit.Image(stone.transform, "Ring", UIKit.CircleOutline, Theme.Ink).rectTransform.Stretch();
            controller.turnStone = stone;

            var text = UIKit.Text(pill.transform, "TurnText", "흑 차례", 30, true, Theme.Ink, TextAlignmentOptions.Center);
            text.rectTransform.Stretch();
            text.rectTransform.offsetMin = new Vector2(48, 0);
            text.rectTransform.offsetMax = new Vector2(-20, 0);
            controller.turnText = text;
        }

        private static PlayerHudPanel BuildPlayerPanel(Transform root, string name, Vector2 corner, Vector2 offset, Color stoneFill, Color stoneRing)
        {
            var bg = UIKit.Panel(root, name, Theme.Hanji, Theme.Ink);
            bg.rectTransform.Place(corner, offset, PanelSize);
            var t = bg.transform;
            var panel = bg.gameObject.AddComponent<PlayerHudPanel>();
            panel.canvasGroup = bg.gameObject.AddComponent<CanvasGroup>();
            var topLeft = new Vector2(0, 1);
            var topRight = new Vector2(1, 1);

            UIKit.Stone(t, "Stone", 64, stoneFill, stoneRing, false).Place(topLeft, new Vector2(Pad, -Pad), new Vector2(64, 64));
            panel.nameText = UIKit.Text(t, "Name", "흑", 40, true, Theme.Ink, TextAlignmentOptions.TopLeft);
            panel.nameText.rectTransform.Place(topLeft, new Vector2(108, -24), new Vector2(140, 48));
            panel.numberText = UIKit.Text(t, "Number", "플레이어 1", 24, false, Theme.InkSoft, TextAlignmentOptions.TopLeft);
            panel.numberText.rectTransform.Place(topLeft, new Vector2(108, -70), new Vector2(150, 30));

            // Seal-stamp "turn" badge, shown only on the side to move.
            var badge = UIKit.Panel(t, "TurnBadge", Theme.Seal, null, 2.5f);
            badge.rectTransform.Place(topRight, new Vector2(-24, -24), new Vector2(76, 76));
            UIKit.Label(badge.transform, "Label", "hud.turnBadge", 26, true, Theme.SealText, TextAlignmentOptions.Center).rectTransform.Stretch(4);
            panel.turnBadge = badge.gameObject;

            var divider = UIKit.Image(t, "Divider", null, Theme.Divider);
            divider.rectTransform.Place(topLeft, new Vector2(Pad, -122), new Vector2(PanelSize.x - Pad * 2, 2));

            UIKit.Label(t, "RemainingLabel", "hud.remaining", 24, false, Theme.InkSoft, TextAlignmentOptions.TopLeft)
                .rectTransform.Place(topLeft, new Vector2(Pad, -162), new Vector2(180, 30));
            panel.remainingText = UIKit.Text(t, "Remaining", "6", 72, true, Theme.Ink, TextAlignmentOptions.TopRight);
            panel.remainingText.rectTransform.Place(topRight, new Vector2(-Pad, -132), new Vector2(160, 84));

            var row = UIKit.Node("StoneRow", t).Place(topLeft, new Vector2(Pad, -238), new Vector2(PanelSize.x - Pad * 2, 40));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12.8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            panel.stoneRow = row;
            panel.stoneTemplate = UIKit.Stone(row, "StoneTemplate", 40, stoneFill, stoneRing, true).gameObject;
            panel.stoneTemplate.SetActive(false);

            panel.capturedText = UIKit.Text(t, "Captured", "잡은 돌 0", 24, false, Theme.InkSoft, TextAlignmentOptions.TopLeft);
            panel.capturedText.rectTransform.Place(topLeft, new Vector2(Pad, -302), new Vector2(PanelSize.x - Pad * 2, 30));
            return panel;
        }

        private static void BuildWinPanel(Transform root, MainGameUIController controller)
        {
            var overlay = UIKit.Image(root, "WinPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            controller.winPanel = overlay.gameObject;

            var modal = UIKit.Panel(overlay.transform, "Modal", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            modal.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 500));
            var m = modal.transform;
            var top = new Vector2(0.5f, 1);

            var stamp = UIKit.Panel(m, "Stamp", Theme.Seal, null, 2f);
            stamp.rectTransform.Place(top, new Vector2(0, -48), new Vector2(128, 128));
            stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
            var stampText = UIKit.Label(stamp.transform, "Label", "win.stamp", 64, true, Theme.SealText, TextAlignmentOptions.Center);
            stampText.rectTransform.Stretch(10);
            stampText.enableAutoSizing = true;
            stampText.fontSizeMin = 28;
            stampText.fontSizeMax = 64;

            controller.winText = UIKit.Text(m, "Title", "흑 승리", 64, true, Theme.Ink, TextAlignmentOptions.Center);
            controller.winText.rectTransform.Place(top, new Vector2(0, -196), new Vector2(540, 80));
            controller.winDetailText = UIKit.Text(m, "Detail", "플레이어 1 · 남은 돌 4개", 30, false, Theme.InkSoft, TextAlignmentOptions.Center);
            controller.winDetailText.rectTransform.Place(top, new Vector2(0, -280), new Vector2(540, 40));

            // Layout group so the row re-centers when rematch is hidden online.
            var buttons = UIKit.Node("Buttons", m).Place(new Vector2(0.5f, 0), new Vector2(0, 56), new Vector2(540, 84));
            var layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            controller.rematchButton = UIKit.CapsuleButton(buttons, "RematchButton", "win.rematch", new Vector2(240, 84), true, 32);
            controller.mainMenuButton = UIKit.CapsuleButton(buttons, "MainMenuButton", "win.menu", new Vector2(240, 84), false, 32);

            overlay.gameObject.SetActive(false);
        }

        private static void WireScene(GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var uiRoot = scene.GetRootGameObjects().First(g => g.name == "UI").transform;

            for (var i = uiRoot.childCount - 1; i >= 0; i--)
            {
                var child = uiRoot.GetChild(i).gameObject;
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child) == PrefabPath) Object.DestroyImmediate(child);
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(uiRoot, false);

            // GameScene never had an EventSystem - the win modal's buttons need
            // one. Pieces use OnMouseDown (physics), so they're unaffected.
            if (!scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<EventSystem>(true) != null))
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(es, scene);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
