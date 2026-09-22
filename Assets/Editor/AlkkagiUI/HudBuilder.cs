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
            BuildAim(root);
            BuildGameOverPanel(root, controller);
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

        // Option A from the aim-UI mockups: power ring around the stone, shot
        // arrow, % label, a faint pull line, and the optional first-contact
        // guide. AimIndicator positions everything each frame; sizes here are
        // just the resting shape.
        private static void BuildAim(Transform root)
        {
            var area = UIKit.Node("Aim", root).Stretch();
            var aim = area.gameObject.AddComponent<AimIndicator>();
            var center = new Vector2(0.5f, 0.5f);
            Color Faded(Color c, float a) => new Color(c.r, c.g, c.b, a);

            // Drawn first so everything else sits on top of it.
            var pull = UIKit.Image(area, "PullLine", null, Faded(Theme.Ink, 0.35f));
            pull.rectTransform.Place(center, Vector2.zero, new Vector2(3, 10), new Vector2(0.5f, 0));
            aim.pullLine = pull.rectTransform;
            var pullMark = UIKit.Image(area, "PullMark", UIKit.CircleOutline, Faded(Theme.Ink, 0.6f));
            pullMark.rectTransform.Place(center, Vector2.zero, new Vector2(18, 18));
            aim.pullMark = pullMark.rectTransform;

            aim.guideRoot = UIKit.Node("Guide", area).Stretch();
            var dot = UIKit.Image(aim.guideRoot, "DotTemplate", UIKit.Circle, Faded(Theme.Ink, 0.7f));
            dot.rectTransform.Place(center, Vector2.zero, new Vector2(7, 7));
            dot.gameObject.SetActive(false);
            aim.dotTemplate = dot.gameObject;
            var targetMark = UIKit.Image(area, "TargetMark", UIKit.CircleOutline, Faded(Theme.Seal, 0.85f));
            targetMark.rectTransform.Place(center, Vector2.zero, new Vector2(60, 60));
            aim.targetMark = targetMark.rectTransform;

            aim.ring = UIKit.Node("Ring", area).Place(center, Vector2.zero, new Vector2(80, 80));
            UIKit.Image(aim.ring, "Track", UIKit.CircleOutline, Faded(Theme.Ink, 0.2f)).rectTransform.Stretch();
            aim.ringFill = UIKit.Image(aim.ring, "Fill", UIKit.CircleOutline, Theme.Seal);
            aim.ringFill.rectTransform.Stretch();
            aim.ringFill.type = UnityEngine.UI.Image.Type.Filled;
            aim.ringFill.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            aim.ringFill.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            aim.ringFill.fillClockwise = true;
            aim.ringFill.fillAmount = 0.72f;

            // Pivot at the base so it rotates about the ring edge; the head rides
            // on the shaft's tip as the shaft grows with power.
            aim.arrow = UIKit.Node("Arrow", area).Place(center, Vector2.zero, new Vector2(30, 10), new Vector2(0.5f, 0));
            var shaft = UIKit.Image(aim.arrow, "Shaft", null, Theme.Seal);
            shaft.rectTransform.Place(new Vector2(0.5f, 0), Vector2.zero, new Vector2(8, 100), new Vector2(0.5f, 0));
            aim.arrowShaft = shaft.rectTransform;
            UIKit.Image(shaft.transform, "Head", UIKit.Triangle, Theme.Seal).rectTransform
                .Place(new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(30, 26), new Vector2(0.5f, 0));

            var label = UIKit.Capsule(area, "PowerLabel", 40, Theme.Hanji, Theme.Ink);
            label.rectTransform.Place(center, Vector2.zero, new Vector2(96, 40));
            aim.powerLabel = label.rectTransform;
            aim.powerText = UIKit.Text(label.transform, "Text", "72%", 24, true, Theme.Seal, TextAlignmentOptions.Center);
            aim.powerText.rectTransform.Stretch();
        }

        // Result, reason, a per-side scoreboard, the running series, and the
        // next-step buttons (Lobby only shows online).
        private static void BuildGameOverPanel(Transform root, MainGameUIController controller)
        {
            var overlay = UIKit.Image(root, "GameOverPanel", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            controller.gameOverPanel = overlay.gameObject;

            var modal = UIKit.Panel(overlay.transform, "Modal", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            modal.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 760));
            var m = modal.transform;
            var top = new Vector2(0.5f, 1);

            var stamp = UIKit.Panel(m, "Stamp", Theme.Seal, null, 2f);
            stamp.rectTransform.Place(top, new Vector2(0, -40), new Vector2(120, 120));
            stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
            controller.stampText = UIKit.Text(stamp.transform, "Label", "승", 60, true, Theme.SealText, TextAlignmentOptions.Center);
            controller.stampText.rectTransform.Stretch(10);
            controller.stampText.enableAutoSizing = true;
            controller.stampText.fontSizeMin = 28;
            controller.stampText.fontSizeMax = 60;

            controller.resultTitleText = UIKit.Text(m, "Title", "흑 승리", 60, true, Theme.Ink, TextAlignmentOptions.Center);
            controller.resultTitleText.rectTransform.Place(top, new Vector2(0, -176), new Vector2(680, 76));
            controller.resultReasonText = UIKit.Text(m, "Reason", "백의 돌이 모두 떨어졌어요", 28, false, Theme.InkSoft, TextAlignmentOptions.Center);
            controller.resultReasonText.rectTransform.Place(top, new Vector2(0, -252), new Vector2(680, 40));

            BuildScoreboard(m, controller);

            controller.matchTimeText = UIKit.Text(m, "MatchTime", "경기 시간 1:23", 26, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
            controller.matchTimeText.rectTransform.Place(new Vector2(0, 1), new Vector2(60, -548), new Vector2(320, 36));
            controller.seriesText = UIKit.Text(m, "Series", "연속 전적  흑 1 : 0 백", 26, false, Theme.Ink, TextAlignmentOptions.MidlineRight);
            controller.seriesText.rectTransform.Place(new Vector2(1, 1), new Vector2(-60, -548), new Vector2(360, 36));
            controller.statusText = UIKit.Text(m, "Status", "", 26, false, Theme.Seal, TextAlignmentOptions.Center);
            controller.statusText.rectTransform.Place(top, new Vector2(0, -596), new Vector2(680, 36));

            // Layout group so the row re-centers when Lobby is hidden locally.
            var buttons = UIKit.Node("Buttons", m).Place(new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(680, 84));
            var layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            controller.rematchButton = UIKit.CapsuleButton(buttons, "RematchButton", "win.rematch", new Vector2(220, 84), true, 30);
            controller.rematchLabel = controller.rematchButton.GetComponentInChildren<TMP_Text>();
            // Play again / Rematch / Accept / Waiting - set by the controller.
            Object.DestroyImmediate(controller.rematchLabel.GetComponent<LocalizedText>());
            controller.lobbyButton = UIKit.CapsuleButton(buttons, "LobbyButton", "gameover.lobby", new Vector2(210, 84), false, 30);
            controller.mainMenuButton = UIKit.CapsuleButton(buttons, "MainMenuButton", "win.menu", new Vector2(210, 84), false, 30);

            overlay.gameObject.SetActive(false);
        }

        private static void BuildScoreboard(Transform modal, MainGameUIController controller)
        {
            var board = UIKit.Panel(modal, "Scoreboard", Theme.HanjiField, Theme.FieldBorder, 1.4f);
            board.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -310), new Vector2(640, 212));
            var b = board.transform;
            var topLeft = new Vector2(0, 1);
            float[] columns = { 400, 540 }; // value column centers, from the board's left edge

            for (var player = 0; player < 2; player++)
            {
                var x = columns[player];
                var black = player == 0;
                UIKit.Stone(b, black ? "BlackHeader" : "WhiteHeader", 24, black ? Theme.StoneBlack : Theme.StoneWhite, black ? Theme.Ink : Theme.InkMuted, false)
                    .Place(topLeft, new Vector2(x - 34, -28), new Vector2(24, 24));
                UIKit.Label(b, black ? "BlackLabel" : "WhiteLabel", black ? "player.black" : "player.white", 28, true, Theme.Ink, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Place(topLeft, new Vector2(x - 2, -20), new Vector2(80, 40));
            }

            TMP_Text[] Row(string name, string labelKey, float y)
            {
                UIKit.Label(b, name + "Label", labelKey, 28, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Place(topLeft, new Vector2(32, y), new Vector2(280, 40));
                var cells = new TMP_Text[2];
                for (var player = 0; player < 2; player++)
                {
                    cells[player] = UIKit.Text(b, $"{name}{player}", "0", 32, true, Theme.Ink, TextAlignmentOptions.Center);
                    cells[player].rectTransform.Place(topLeft, new Vector2(columns[player], y), new Vector2(120, 40), new Vector2(0.5f, 1));
                }
                return cells;
            }

            controller.remainingCells = Row("Remaining", "hud.remaining", -72);
            controller.capturedCells = Row("Captured", "stats.captured", -118);
            controller.shotsCells = Row("Shots", "stats.shots", -164);
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
