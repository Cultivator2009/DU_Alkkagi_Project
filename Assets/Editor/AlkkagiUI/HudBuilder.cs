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
    // side's stones start. With three or four sides MainGameUIController
    // shrinks the four panels and moves each next to its side's edge.
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
            controller.turnTextColor = Theme.Ink;
            controller.clockWarningColor = Theme.Seal;
            controller.hudMargin = Margin;

            BuildTurnPill(root, controller);
            // The third and fourth sides' panels are placed at match start.
            controller.playerPanels = new[]
            {
                BuildPlayerPanel(root, "BlackPanel", 0, new Vector2(0, 0), new Vector2(Margin, Margin), Theme.StoneBlack, Theme.Ink),
                BuildPlayerPanel(root, "WhitePanel", 1, new Vector2(1, 1), new Vector2(-Margin, -Margin), Theme.StoneWhite, Theme.InkMuted),
                BuildPlayerPanel(root, "BluePanel", 2, new Vector2(1, 1), new Vector2(-Margin, -Margin), SideStyle.StoneBlue, Theme.Ink),
                BuildPlayerPanel(root, "RedPanel", 3, new Vector2(0, 0), new Vector2(Margin, Margin), SideStyle.StoneRed, Theme.Ink),
            };
            BuildPlacementPanel(root);
            BuildKillFeed(root, controller);
            BuildAim(root);
            // Left of the Menu button (BuildPauseMenu).
            controller.resetViewButton = UIKit.CapsuleButton(root, "ResetViewButton", "hud.resetView", new Vector2(200, 64), false, 26);
            controller.resetViewButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-Margin - 180 - 16, Margin), new Vector2(200, 64));
            controller.resetViewButton.gameObject.SetActive(false);
            BuildControlsHint(root, controller);
            BuildGameOverPanel(root, controller);
            BuildPauseMenu(root, controller);
            return canvas.gameObject;
        }

        private static void BuildTurnPill(Transform root, MainGameUIController controller)
        {
            var pill = UIKit.Capsule(root, "TurnPill", 56, Theme.Hanji, Theme.Ink);
            // Wide enough for "흑 차례 · 30" when the turn timer is on.
            pill.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(340, 56));
            controller.turnPill = pill.gameObject;
            controller.turnPulse = Pulse(pill.gameObject, 1.12f, 0.3f);

            var stone = UIKit.Stone(pill.transform, "TurnStone", 28, Theme.StoneBlack, Theme.Ink, false);
            stone.Place(new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(28, 28), new Vector2(0, 0.5f));
            controller.turnStone = UIKit.Mark(stone, 0); // the controller switches its side each turn

            var text = UIKit.Text(pill.transform, "TurnText", "흑 차례", 30, true, Theme.Ink, TextAlignmentOptions.Center);
            text.rectTransform.Stretch();
            text.rectTransform.offsetMin = new Vector2(48, 0);
            text.rectTransform.offsetMax = new Vector2(-20, 0);
            controller.turnText = text;

            // Timed-out / skipped turn, shown for a moment in the pill's place:
            // under it would cover the board's top row.
            var notice = UIKit.Capsule(root, "Notice", 56, Theme.Seal, null);
            notice.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(460, 56));
            controller.notice = notice.gameObject;
            controller.noticePulse = Pulse(notice.gameObject, 0.8f, 0.22f); // grows into place
            controller.noticeText = UIKit.Text(notice.transform, "Text", "흑 시간 초과 · 턴을 넘겨요", 24, true, Theme.SealText, TextAlignmentOptions.Center);
            controller.noticeText.rectTransform.Stretch();
            notice.gameObject.SetActive(false);
        }

        private static PlayerHudPanel BuildPlayerPanel(Transform root, string name, int playerId, Vector2 corner, Vector2 offset, Color stoneFill, Color stoneRing)
        {
            var bg = UIKit.Panel(root, name, Theme.Hanji, Theme.Ink);
            bg.rectTransform.Place(corner, offset, PanelSize);
            var t = bg.transform;
            var panel = bg.gameObject.AddComponent<PlayerHudPanel>();
            panel.canvasGroup = bg.gameObject.AddComponent<CanvasGroup>();
            var topLeft = new Vector2(0, 1);
            var topRight = new Vector2(1, 1);

            var bigStone = UIKit.Stone(t, "Stone", 64, stoneFill, stoneRing, false).Place(topLeft, new Vector2(Pad, -Pad), new Vector2(64, 64));
            UIKit.Mark(bigStone, playerId);
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
            panel.remainingPulse = Pulse(panel.remainingText.gameObject, 1.4f, 0.35f);

            var row = UIKit.Node("StoneRow", t).Place(topLeft, new Vector2(Pad, -238), new Vector2(PanelSize.x - Pad * 2, 40));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12.8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            panel.stoneRow = row;
            var template = UIKit.Stone(row, "StoneTemplate", 40, stoneFill, stoneRing, true);
            UIKit.Mark(template, playerId); // copies keep it
            panel.stoneTemplate = template.gameObject;
            panel.stoneTemplate.SetActive(false);

            panel.capturedText = UIKit.Text(t, "Captured", "잡은 돌 0", 24, false, Theme.InkSoft, TextAlignmentOptions.TopLeft);
            panel.capturedText.rectTransform.Place(topLeft, new Vector2(Pad, -302), new Vector2(PanelSize.x - Pad * 2, 30));

            // Skip turn: just outside the panel, on the side facing the middle
            // of the column (above black's, below white's).
            var below = corner.y > 0.5f;
            panel.skipButton = UIKit.CapsuleButton(root, name + "Skip", "hud.skip", new Vector2(PanelSize.x, 72), false, 28);
            panel.skipButton.GetComponent<RectTransform>().Place(corner, offset + new Vector2(0, below ? -(PanelSize.y + 16) : PanelSize.y + 16), new Vector2(PanelSize.x, 72));
            panel.skipButton.gameObject.SetActive(false);
            return panel;
        }

        // Top of the left column, clear of the board: whose go it is, what to
        // do, both clocks, and Ready.
        private static void BuildPlacementPanel(Transform root)
        {
            var area = UIKit.Node("Placement", root).Stretch();
            var hud = area.gameObject.AddComponent<PlacementHud>();
            hud.clockColor = Theme.Ink;
            hud.clockIdleColor = Theme.InkFaint;
            hud.clockWarningColor = Theme.Seal;

            var size = new Vector2(PanelSize.x, 500);
            var bg = UIKit.Panel(area, "Panel", Theme.Hanji, Theme.Ink);
            bg.rectTransform.Place(new Vector2(0, 1), new Vector2(Margin, -Margin), size);
            hud.panel = bg.gameObject;
            var t = bg.transform;
            var topLeft = new Vector2(0, 1);
            var width = size.x - Pad * 2;

            hud.titleText = UIKit.Text(t, "Title", "돌 배치", 36, true, Theme.Ink, TextAlignmentOptions.TopLeft);
            hud.titleText.rectTransform.Place(topLeft, new Vector2(Pad, -Pad), new Vector2(width, 44));
            hud.hintText = UIKit.Text(t, "Hint", "내 진영을 눌러 돌을 놓으세요", 24, false, Theme.InkSoft, TextAlignmentOptions.TopLeft);
            hud.hintText.rectTransform.Place(topLeft, new Vector2(Pad, -84), new Vector2(width, 100));
            hud.hintText.textWrappingMode = TextWrappingModes.Normal;

            UIKit.Image(t, "Divider", null, Theme.Divider).rectTransform.Place(topLeft, new Vector2(Pad, -196), new Vector2(width, 2));
            // A clock per side; PlacementHud shows as many as are playing and
            // grows the panel to fit.
            hud.clockTexts = new TMP_Text[4];
            hud.clockRows = new RectTransform[4];
            for (var player = 0; player < 4; player++)
            {
                var row = UIKit.Node("Clock" + player, t).Place(topLeft, new Vector2(Pad, -214 - player * hud.rowSpacing), new Vector2(width, 40));
                var clockStone = UIKit.Stone(row, "Stone", 28, player == 0 ? Theme.StoneBlack : Theme.StoneWhite, player == 0 ? Theme.Ink : Theme.InkMuted, false)
                    .Place(topLeft, new Vector2(0, -8), new Vector2(28, 28));
                UIKit.Mark(clockStone, player);
                hud.clockTexts[player] = UIKit.Text(row, "Clock", player == 0 ? "흑 1:00" : "백 1:00", 30, true, Theme.Ink, TextAlignmentOptions.TopLeft);
                hud.clockTexts[player].rectTransform.Place(topLeft, new Vector2(44, 0), new Vector2(width - 44, 40));
                hud.clockRows[player] = row;
            }

            hud.statusText = UIKit.Text(t, "Status", "시간이 끝나면 남은 돌은 무작위로 놓여요", 22, false, Theme.InkFaint, TextAlignmentOptions.TopLeft);
            hud.statusText.rectTransform.Place(topLeft, new Vector2(Pad, -316), new Vector2(width, 48));
            hud.statusText.textWrappingMode = TextWrappingModes.Normal;

            hud.readyButton = UIKit.CapsuleButton(t, "ReadyButton", "placement.ready", new Vector2(width, 72), true, 30);
            hud.readyButton.GetComponent<RectTransform>().Place(new Vector2(0.5f, 0), new Vector2(0, Pad), new Vector2(width, 72), new Vector2(0.5f, 0));
            bg.gameObject.SetActive(false);
        }

        // A key legend in the right column, above the Menu button: how to
        // flick, the camera keys (ControlsHint fills in this machine's
        // bindings) and the menu. The controller places it for the number of
        // sides.
        private static void BuildControlsHint(Transform root, MainGameUIController controller)
        {
            const float width = 420, lineHeight = 34, keyWidth = 140, inset = 20;
            var bound = new[] { GameAction.CancelAim, GameAction.CameraView, GameAction.PanView, GameAction.ResetView };
            var bg = UIKit.Panel(root, "ControlsHint", Theme.Hanji, Theme.FieldBorder);
            bg.rectTransform.Place(new Vector2(1, 0), new Vector2(-Margin, Margin + 64 + 16), new Vector2(width, (bound.Length + 3) * lineHeight + inset * 2));
            var hint = bg.gameObject.AddComponent<ControlsHint>();
            hint.actions = bound;
            hint.keyTexts = new TMP_Text[bound.Length];
            var t = bg.transform;
            var topLeft = new Vector2(0, 1);

            // keyLoc null: the key is literal text (a binding, filled in at runtime, or Esc).
            TMP_Text Line(int index, string key, string keyLoc, string actionLoc)
            {
                var y = -inset - index * lineHeight;
                var keyText = keyLoc != null
                    ? UIKit.Label(t, "Key" + index, keyLoc, 20, true, Theme.Ink, TextAlignmentOptions.MidlineRight)
                    : UIKit.Text(t, "Key" + index, key, 20, true, Theme.Ink, TextAlignmentOptions.MidlineRight);
                keyText.rectTransform.Place(topLeft, new Vector2(inset, y), new Vector2(keyWidth, lineHeight));
                UIKit.Label(t, "Action" + index, actionLoc, 20, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Place(topLeft, new Vector2(inset + keyWidth + 16, y), new Vector2(width - inset * 2 - keyWidth - 16, lineHeight));
                return keyText;
            }
            var line = 0;
            Line(line++, null, "hint.flickKey", "hint.flick");
            for (var i = 0; i < bound.Length; i++)
            {
                hint.keyTexts[i] = Line(line++, "Ctrl", null, "hint." + bound[i]);
                if (bound[i] == GameAction.PanView) Line(line++, null, "hint.zoomKey", "hint.zoom"); // the wheel isn't rebindable
            }
            Line(line, "Esc", null, "hud.menu");
            controller.controlsHint = hint;
        }

        // CS2-style kill feed at the top of the left column. The placement
        // panel shares the spot, but only before the first turn, when nothing
        // can be knocked out yet. KillFeed copies the entry template per line.
        private static void BuildKillFeed(Transform root, MainGameUIController controller)
        {
            const float height = 48;
            var list = UIKit.Node("KillFeed", root).Place(new Vector2(0, 1), new Vector2(Margin, -Margin), new Vector2(460, 440));
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperLeft;
            // Each line as wide as its own contents want; set here, not by a
            // fitter on the line, which would resize it after it was placed.
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var feed = list.gameObject.AddComponent<KillFeed>();
            feed.outlineColor = Theme.Ink;
            feed.localOutlineColor = Theme.Seal;
            controller.killFeed = feed;

            var fill = UIKit.Capsule(list, "EntryTemplate", height, new Color(Theme.Hanji.r, Theme.Hanji.g, Theme.Hanji.b, 0.94f), Theme.Ink);
            fill.rectTransform.sizeDelta = new Vector2(320, height);
            var entry = fill.gameObject.AddComponent<KillFeedEntry>();
            entry.canvasGroup = fill.gameObject.AddComponent<CanvasGroup>();
            var row = fill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(20, 20, 0, 0);
            row.spacing = 8;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var outline = fill.transform.Find("Outline");
            outline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            entry.outline = outline.GetComponent<Image>();

            TMP_Text Name(string name, string text)
            {
                var label = UIKit.Text(fill.transform, name, text, 24, true, Theme.Ink, TextAlignmentOptions.MidlineLeft);
                label.rectTransform.sizeDelta = new Vector2(40, height);
                return label;
            }
            // A side's stone; for janggi pieces the letter shows on it too.
            (SideMark, TMP_Text) Icon(string name, int playerId)
            {
                var stone = UIKit.Stone(fill.transform, name, 32, Theme.StoneBlack, Theme.Ink, false);
                var size = stone.gameObject.AddComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = 32;
                var mark = UIKit.Mark(stone, playerId);
                var letter = UIKit.Text(stone, "Letter", "차", 19, true, SideStyle.Cho, TextAlignmentOptions.Center);
                letter.rectTransform.Stretch();
                letter.gameObject.SetActive(false);
                return (mark, letter);
            }

            entry.shooterName = Name("Shooter", "흑");
            (entry.shotIcon, entry.shotLetter) = Icon("ShotIcon", 0);
            var arrow = UIKit.Image(fill.transform, "Arrow", UIKit.Triangle, Theme.Ink);
            arrow.rectTransform.sizeDelta = new Vector2(16, 14);
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -90); // the sprite points up
            var arrowSize = arrow.gameObject.AddComponent<LayoutElement>();
            arrowSize.minWidth = arrowSize.preferredWidth = 16;
            entry.arrow = arrow.gameObject;
            (entry.victimIcon, entry.victimLetter) = Icon("VictimIcon", 1);
            entry.victimName = Name("Victim", "백");

            // The capsule is a child: on the badge itself, the pill sprite's own
            // preferred width (128) would win over the text's.
            var badge = UIKit.Node("Badge", fill.transform);
            badge.sizeDelta = new Vector2(64, 32);
            var badgeFill = UIKit.Capsule(badge, "Fill", 32, Theme.Seal, null);
            badgeFill.rectTransform.Stretch();
            badgeFill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var badgeRow = badge.gameObject.AddComponent<HorizontalLayoutGroup>();
            badgeRow.padding = new RectOffset(12, 12, 0, 0);
            badgeRow.childAlignment = TextAnchor.MiddleCenter;
            badgeRow.childControlWidth = badgeRow.childControlHeight = true;
            badgeRow.childForceExpandWidth = badgeRow.childForceExpandHeight = false;
            entry.badgeText = UIKit.Text(badge.transform, "Text", "논개", 20, true, Theme.SealText, TextAlignmentOptions.Center);
            entry.badge = badge.gameObject;

            fill.gameObject.SetActive(false);
            feed.template = entry;
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
            var hover = UIKit.Image(area, "HoverRing", UIKit.CircleOutline, Faded(Theme.Ink, 0.6f));
            hover.rectTransform.Place(center, Vector2.zero, new Vector2(80, 80));
            hover.gameObject.SetActive(false);
            aim.hoverRing = hover.rectTransform;

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
            UIKit.Appear(overlay, modal.rectTransform, swish: false); // the seal lands with its own sound
            modal.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 956));
            var m = modal.transform;
            var top = new Vector2(0.5f, 1);

            var stamp = UIKit.Panel(m, "Stamp", Theme.Seal, null, 2f);
            stamp.rectTransform.Place(top, new Vector2(0, -40), new Vector2(120, 120));
            stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
            // Comes down on the result just after the modal opens, and lands with a thump.
            var slam = Pulse(stamp.gameObject, 2.4f, 0.3f);
            slam.slam = true;
            slam.playOnEnable = true;
            slam.delay = 0.12f;
            slam.fade = stamp.gameObject.AddComponent<CanvasGroup>();
            slam.landSound = AssetDatabase.LoadAssetAtPath<SoundBank>("Assets/Resources/SoundBank.asset").stamp;
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
            controller.matchTimeText.rectTransform.Place(new Vector2(0, 1), new Vector2(60, -732), new Vector2(320, 36));
            controller.seriesText = UIKit.Text(m, "Series", "연속 전적  흑 1 : 0 백", 26, false, Theme.Ink, TextAlignmentOptions.MidlineRight);
            controller.seriesText.rectTransform.Place(new Vector2(1, 1), new Vector2(-60, -732), new Vector2(360, 36));
            controller.statusText = UIKit.Text(m, "Status", "", 26, false, Theme.Seal, TextAlignmentOptions.Center);
            controller.statusText.rectTransform.Place(top, new Vector2(0, -780), new Vector2(680, 36));

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

        // Esc or the Menu button (bottom right, the corner no panel uses):
        // resume, settings (the main menu's card), concede, main menu.
        private static void BuildPauseMenu(Transform root, MainGameUIController controller)
        {
            const float width = 560, pad = 64, buttonHeight = 88, gap = 20;
            var menu = root.gameObject.AddComponent<PauseMenu>();
            menu.game = controller;

            menu.openButton = UIKit.CapsuleButton(root, "MenuButton", "hud.menu", new Vector2(180, 64), false, 26);
            menu.openButton.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-Margin, Margin), new Vector2(180, 64));

            var overlay = UIKit.Image(root, "PauseMenu", null, Theme.Overlay, raycast: true);
            overlay.rectTransform.Stretch();
            menu.overlay = overlay.gameObject;
            var height = 148 + 4 * buttonHeight + 3 * gap + 40 + 36 + 48;
            var card = UIKit.Panel(overlay.transform, "Card", Theme.Hanji, Theme.Ink, 0.8f, raycast: true);
            UIKit.Appear(overlay, card.rectTransform);
            card.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var top = new Vector2(0.5f, 1);
            UIKit.Label(card.transform, "Title", "pause.title", 48, true, Theme.Ink, TextAlignmentOptions.Center)
                .rectTransform.Place(top, new Vector2(0, -44), new Vector2(width - pad * 2, 64));

            Button Row(string name, string key, int index, bool primary)
            {
                var button = UIKit.CapsuleButton(card.transform, name, key, new Vector2(width - pad * 2, buttonHeight), primary, 32);
                button.GetComponent<RectTransform>().Place(top, new Vector2(0, -148 - index * (buttonHeight + gap)), new Vector2(width - pad * 2, buttonHeight));
                return button;
            }
            menu.resumeButton = Row("ResumeButton", "pause.resume", 0, true);
            menu.settingsButton = Row("SettingsButton", "menu.settings", 1, false);
            menu.concedeButton = Row("ConcedeButton", "pause.concede", 2, false);
            menu.mainMenuButton = Row("MainMenuButton", "pause.mainMenu", 3, false);
            // Set by PauseMenu (the side conceding, or "press again").
            menu.concedeLabel = menu.concedeButton.GetComponentInChildren<TMP_Text>();
            Object.DestroyImmediate(menu.concedeLabel.GetComponent<LocalizedText>());
            menu.mainMenuLabel = menu.mainMenuButton.GetComponentInChildren<TMP_Text>();
            Object.DestroyImmediate(menu.mainMenuLabel.GetComponent<LocalizedText>());

            menu.caption = UIKit.Text(card.transform, "Caption", Loc.Get("pause.captionLocal"), 22, false, Theme.InkFaint, TextAlignmentOptions.Center);
            menu.caption.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(width - pad * 2, 36), new Vector2(0.5f, 0));
            overlay.gameObject.SetActive(false);

            // Above the menu, as it's opened from it.
            menu.settings = MenuBuilder.BuildSettingsPanel(root, out menu.settingsCloseButton);
        }

        private static void BuildScoreboard(Transform modal, MainGameUIController controller)
        {
            var board = UIKit.Panel(modal, "Scoreboard", Theme.HanjiField, Theme.FieldBorder, 1.4f);
            board.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -310), new Vector2(640, 406));
            var b = board.transform;
            var topLeft = new Vector2(0, 1);
            const float columnWidth = 120;

            // Kills are the shooter's own work; with two sides the panels'
            // captured count also takes the opponent's suicides and team kills.
            // The rating row (new rating and change) only shows for a rated match.
            string[] rows = { "Remaining", "Kills", "Nongae", "Suicides", "TeamKills", "Shots", "Rating" };
            string[] labels = { "hud.remaining", "stats.kills", "stats.nongae", "stats.suicides", "stats.teamKills", "stats.shots", "stats.rating" };
            var rowLabels = new GameObject[rows.Length];
            for (var r = 0; r < rows.Length; r++)
            {
                var label = UIKit.Label(b, rows[r] + "Label", labels[r], 28, false, Theme.InkSoft, TextAlignmentOptions.MidlineLeft);
                label.rectTransform.Place(topLeft, new Vector2(32, RowY(r)), new Vector2(280, 40));
                rowLabels[r] = label.gameObject;
            }

            // A column per side, its header and cells together; the controller
            // spaces the columns for the number of sides.
            var cells = new TMP_Text[rows.Length][];
            for (var r = 0; r < rows.Length; r++) cells[r] = new TMP_Text[4];
            controller.scoreColumns = new RectTransform[4];
            for (var player = 0; player < 4; player++)
            {
                var column = UIKit.Node("Column" + player, b).Place(topLeft, new Vector2(player == 0 ? 400 : 540, 0), new Vector2(columnWidth, 406), new Vector2(0.5f, 1));
                controller.scoreColumns[player] = column;
                var header = UIKit.Stone(column, "Header", 24, player == 0 ? Theme.StoneBlack : Theme.StoneWhite, player == 0 ? Theme.Ink : Theme.InkMuted, false)
                    .Place(topLeft, new Vector2(columnWidth / 2 - 34, -28), new Vector2(24, 24));
                UIKit.Mark(header, player);
                var headerLabel = UIKit.Text(column, "Label", Loc.Get(player == 0 ? "player.black" : "player.white"), 28, true, Theme.Ink, TextAlignmentOptions.MidlineLeft);
                headerLabel.rectTransform.Place(topLeft, new Vector2(columnWidth / 2 - 2, -20), new Vector2(80, 40));
                UIKit.MarkLabel(headerLabel, player);
                for (var r = 0; r < rows.Length; r++)
                {
                    var rating = rows[r] == "Rating";
                    // The rating cell is two numbers; four columns leave it ~100 wide.
                    cells[r][player] = UIKit.Text(column, rows[r], rating ? "1000" : "0", rating ? 26 : 32, true, Theme.Ink, TextAlignmentOptions.Center);
                    cells[r][player].rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, RowY(r)), new Vector2(columnWidth, 40), new Vector2(0.5f, 1));
                }
            }
            controller.remainingCells = cells[0];
            controller.killCells = cells[1];
            controller.nongaeCells = cells[2];
            controller.suicideCells = cells[3];
            controller.teamKillCells = cells[4];
            controller.shotsCells = cells[5];
            controller.ratingCells = cells[6];
            controller.ratingLabel = rowLabels[6];
            controller.scoreRowPitch = RowY(0) - RowY(1);
            controller.ratingUpColor = Theme.StatusOk;
            controller.ratingDownColor = Theme.Seal;
            controller.ratingSameColor = Theme.InkFaint;
        }

        private static float RowY(int row) => -72 - row * 46;

        private static UIPulse Pulse(GameObject target, float from, float seconds)
        {
            var pulse = target.AddComponent<UIPulse>();
            pulse.from = from;
            pulse.seconds = seconds;
            return pulse;
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
