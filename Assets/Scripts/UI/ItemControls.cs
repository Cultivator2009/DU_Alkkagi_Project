using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The item mode on the HUD (built by Tools > Alkkagi UI > 2. Build HUD):
// every side's items on its panel, pressable by the screen that plays that
// side when it may use one (ItemSystem.MayUse); then, for an item with a
// target, the player picks it on the board - a piece, a spot, a way - told
// what to pick on the prompt under the turn pill, a right click or Cancel
// backing out. A side with a third item lets one go on a card. The prompt
// also tells what a hovered item does and what this turn's shot carries.
public class ItemControls : MonoBehaviour
{
    public GameObject prompt;
    public TMP_Text promptText;
    public Button leftButton;   // Curve's
    public Button rightButton;
    public Button cancelButton;
    public GameObject discardCard;
    public ItemSlotView[] discardTiles; // the two slots, then the new one

    private MainGameUIController hud;
    private ItemSystem items;
    private Camera worldCamera;
    private PieceOutline outline;

    private struct Picking
    {
        public int Side;
        public int Slot;
        public ItemId Item;
        public ItemAim Aim;
        public char First; // OwnThenEnemy: the own piece, once picked
    }

    private Picking? picking;
    private (int side, int slot)? hovered;
    private int discardSide = -1;
    private Transform marker;        // where a spot would go, green or red
    private LineRenderer way;        // a quake's way, from the middle
    private Material markerMaterial;
    private static readonly Color Fits = new Color(0.39f, 0.6f, 0.13f, 0.6f);
    private static readonly Color Misfits = new Color(0.7f, 0.19f, 0.16f, 0.55f);

    public void Init(MainGameUIController owner, ItemSystem itemSystem)
    {
        hud = owner;
        items = itemSystem;
        worldCamera = Camera.main;
        if (worldCamera != null) outline = PieceOutline.For(worldCamera);
        for (var side = 0; side < hud.playerPanels.Length; side++)
        {
            var panel = hud.playerPanels[side];
            panel.ShowItems(side < GameManager.manager.Sides.Count);
            for (var slot = 0; slot < panel.itemSlots.Length; slot++)
            {
                var (s, i) = (side, slot);
                panel.itemSlots[slot].button.onClick.AddListener(() => Begin(s, i));
                panel.itemSlots[slot].OnHover += on => hovered = on ? (s, i) : hovered == (s, i) ? null : hovered;
            }
        }
        for (var i = 0; i < discardTiles.Length; i++)
        {
            var index = i;
            discardTiles[i].button.onClick.AddListener(() =>
            {
                if (discardSide >= 0) items.RequestDiscard(discardSide, index);
            });
        }
        leftButton.onClick.AddListener(() => Choose(1));
        rightButton.onClick.AddListener(() => Choose(-1));
        cancelButton.onClick.AddListener(Cancel);
        items.OnChanged += Render;
        items.OnEvent += Tell;
        MakeMarkers();
        Render();
    }

    private void OnDestroy()
    {
        if (items != null)
        {
            items.OnChanged -= Render;
            items.OnEvent -= Tell;
        }
        PieceSelector.Held = false;
        if (markerMaterial != null) Destroy(markerMaterial);
        if (marker != null) Destroy(marker.gameObject);
        if (way != null) Destroy(way.gameObject);
    }

    // Usable on this screen, now: its side to move, and the item has something to do.
    private bool MayUse(int side, byte item) =>
        ItemDefs.IsValid(item) && hud.MayPickUp(side) && items.MayUse(side) && items.Usable(side, (ItemId)item) && !picking.HasValue;

    public void Render()
    {
        if (items == null) return;
        var sides = GameManager.manager.Sides.Count;
        for (var side = 0; side < sides && side < hud.playerPanels.Length; side++)
        {
            var state = items.ItemsOf(side);
            var panel = hud.playerPanels[side];
            for (var slot = 0; slot < panel.itemSlots.Length; slot++) panel.itemSlots[slot].Show(state.Slots[slot], MayUse(side, state.Slots[slot]));
        }

        // A third item, for a side this screen plays: which goes?
        discardSide = -1;
        for (var s = 0; s < sides && discardSide < 0; s++)
            if (items.ItemsOf(s).Pending != SideItems.None && hud.PlaysHere(s)) discardSide = s;
        discardCard.SetActive(discardSide >= 0);
        if (discardSide >= 0)
        {
            var state = items.ItemsOf(discardSide);
            discardTiles[0].Show(state.Slots[0], true);
            discardTiles[1].Show(state.Slots[1], true);
            discardTiles[2].Show(state.Pending, true);
        }
        RenderPrompt();
    }

    private void Update()
    {
        if (items == null) return;
        if (picking.HasValue)
        {
            var p = picking.Value;
            // The turn moved on (or the screen may no longer act): over.
            if (!hud.MayPickUp(p.Side) || !items.MayUse(p.Side))
            {
                Cancel();
                return;
            }
            Pick(p);
        }
        else if (hud.isActiveAndEnabled) RenderPrompt();
    }

    // ---- Picking ----

    private void Begin(int side, int slot)
    {
        var item = items.ItemsOf(side).Slots[slot];
        if (!MayUse(side, item)) return;
        var id = (ItemId)item;
        var aim = ItemDefs.AimFor(id, MatchSettings.Current);
        if (aim == ItemAim.None)
        {
            items.Request(side, slot, default);
            return;
        }
        picking = new Picking { Side = side, Slot = slot, Item = id, Aim = aim };
        PieceSelector.Held = true;
        GameAudio.PlayInterface(GameAudio.Bank.click, 0.6f);
        Render();
    }

    private void Cancel()
    {
        if (!picking.HasValue) return;
        picking = null;
        PieceSelector.Held = false;
        if (outline != null) outline.Set(PieceOutline.Mark.Hover, null);
        marker.gameObject.SetActive(false);
        way.gameObject.SetActive(false);
        Render();
    }

    private void Choose(int sign)
    {
        if (!picking.HasValue || picking.Value.Aim != ItemAim.Side) return;
        Done(new ItemTarget { Sign = sign });
    }

    private void Done(ItemTarget target)
    {
        var p = picking.Value;
        picking = null;
        PieceSelector.Held = false;
        if (outline != null) outline.Set(PieceOutline.Mark.Hover, null);
        marker.gameObject.SetActive(false);
        way.gameObject.SetActive(false);
        items.Request(p.Side, p.Slot, target);
        Render();
    }

    // Each frame while picking: what's under the cursor, and a click on it.
    private void Pick(Picking p)
    {
        if (Input.GetMouseButtonDown(1))
        {
            Cancel();
            return;
        }
        var overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        var click = Input.GetMouseButtonDown(0) && !overUI && Pointer.OnScreen;
        switch (p.Aim)
        {
            case ItemAim.OwnPiece:
            case ItemAim.EnemyPiece:
            case ItemAim.OwnThenEnemy:
                var piece = overUI ? null : PieceUnderCursor(p);
                if (outline != null) outline.Set(PieceOutline.Mark.Hover, piece);
                if (!click || piece == null) break;
                var id = piece.Manager.pieceID;
                if (p.Aim == ItemAim.OwnThenEnemy && p.First == '\0')
                {
                    p.First = id;
                    picking = p;
                    GameAudio.PlayInterface(GameAudio.Bank.click, 0.6f);
                    RenderPrompt();
                    break;
                }
                Done(p.Aim == ItemAim.OwnThenEnemy ? new ItemTarget { Piece = p.First, Other = id } : new ItemTarget { Piece = id });
                break;
            case ItemAim.Point:
            case ItemAim.Direction:
            case ItemAim.OwnZone:
                if (!BoardPoint(out var point))
                {
                    marker.gameObject.SetActive(false);
                    way.gameObject.SetActive(false);
                    break;
                }
                var target = new ItemTarget { Point = point };
                var fits = items.CanTarget(p.Side, p.Item, target);
                ShowMarker(p, point, fits);
                if (!click) break;
                if (fits) Done(target);
                else GameAudio.PlayInterface(GameAudio.Bank.cancel, 0.5f);
                break;
        }
    }

    // The nearest piece under the cursor the item may be used on.
    private GamePieceDragAndReleaseForce PieceUnderCursor(Picking p)
    {
        if (worldCamera == null || !Pointer.OnScreen) return null;
        foreach (var piece in PiecePicker.UnderCursor(worldCamera))
        {
            var target = p.Aim == ItemAim.OwnThenEnemy
                ? (p.First == '\0' ? new ItemTarget { Piece = piece.Manager.pieceID, Other = FirstEnemyPiece(p.Side) } : new ItemTarget { Piece = p.First, Other = piece.Manager.pieceID })
                : new ItemTarget { Piece = piece.Manager.pieceID };
            if (items.CanTarget(p.Side, p.Item, target)) return piece;
        }
        return null;
    }

    // Any piece of another team, to try the first of a swap's two picks against.
    private char FirstEnemyPiece(int side)
    {
        var enemy = GameManager.manager.gamePieceScripts.FirstOrDefault(x => x != null && x.gameObject.activeSelf && items.IsEnemy(side, x.Manager.playerIndex));
        return enemy != null ? enemy.Manager.pieceID : '\0';
    }

    private bool BoardPoint(out Vector2 point)
    {
        point = default;
        var board = GameManager.manager.Board;
        if (worldCamera == null || board == null || !Pointer.OnScreen) return false;
        var plane = new Plane(Vector3.up, new Vector3(0, board.Active.transform.position.y, 0));
        var ray = worldCamera.ScreenPointToRay(Input.mousePosition);
        if (!plane.Raycast(ray, out var distance)) return false;
        var hit = ray.GetPoint(distance);
        point = new Vector2(hit.x, hit.z);
        return true;
    }

    private void ShowMarker(Picking p, Vector2 point, bool fits)
    {
        var board = GameManager.manager.Board;
        var top = board.Active.transform.position.y + 0.004f;
        if (p.Aim == ItemAim.Direction)
        {
            marker.gameObject.SetActive(false);
            way.gameObject.SetActive(true);
            var end = point.normalized * Mathf.Min(point.magnitude, 1.2f);
            way.SetPosition(0, new Vector3(0, top, 0));
            way.SetPosition(1, new Vector3(end.x, top, end.y));
            way.startColor = way.endColor = fits ? Fits : Misfits;
            return;
        }
        way.gameObject.SetActive(false);
        marker.gameObject.SetActive(true);
        var radius = p.Item == ItemId.Ice ? ItemSystem.IceRadius
            : p.Item == ItemId.Revive ? (items.BenchedOf(p.Side) != null ? items.BenchedOf(p.Side).Manager.radius : 0.1f)
            : ItemSystem.PillarRadius;
        marker.position = new Vector3(point.x, top, point.y);
        marker.localScale = Vector3.one * radius;
        markerMaterial.color = fits ? Fits : Misfits;
    }

    private void MakeMarkers()
    {
        markerMaterial = new Material(Shader.Find("Sprites/Default"));
        var disc = new GameObject("Item marker");
        var mesh = new Mesh { name = "Item marker" };
        var vertices = new Vector3[49];
        var triangles = new int[48 * 3];
        for (var i = 0; i < 48; i++)
        {
            var a = i * Mathf.PI * 2 / 48;
            vertices[i + 1] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i == 47 ? 1 : i + 2;
        }
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        disc.AddComponent<MeshFilter>().sharedMesh = mesh;
        disc.AddComponent<MeshRenderer>().sharedMaterial = markerMaterial;
        marker = disc.transform;
        disc.SetActive(false);

        way = new GameObject("Item way").AddComponent<LineRenderer>();
        way.material = markerMaterial;
        way.positionCount = 2;
        way.widthMultiplier = 0.05f;
        way.gameObject.SetActive(false);
    }

    // ---- Telling ----

    private void RenderPrompt()
    {
        string text = null;
        var choosing = false;
        if (picking.HasValue)
        {
            var p = picking.Value;
            choosing = p.Aim == ItemAim.Side;
            var key = p.Aim switch
            {
                ItemAim.Side => "item.pick.side",
                ItemAim.OwnPiece => "item.pick.own",
                ItemAim.EnemyPiece => "item.pick.enemy",
                ItemAim.Point => "item.pick.point",
                ItemAim.Direction => "item.pick.direction",
                ItemAim.OwnZone => "item.pick.zone",
                _ => p.First == '\0' ? "item.pick.swapOwn" : "item.pick.swapEnemy",
            };
            if (p.Aim == ItemAim.OwnThenEnemy && p.First != '\0') key = "item.pick.swapEnemy";
            text = Loc.Get(key, ItemDefs.Of(p.Item).Name);
        }
        else if (hovered.HasValue && items.ItemsOf(hovered.Value.side).Has(hovered.Value.slot))
        {
            var def = ItemDefs.Of((ItemId)items.ItemsOf(hovered.Value.side).Slots[hovered.Value.slot]);
            text = $"<b>{def.Name}</b> · {def.Description}";
        }
        else
        {
            var turns = GameManager.manager.TurnController;
            var side = turns != null ? turns.CurrentPlayerID : -1;
            if (side >= 0 && hud.MayPickUp(side))
            {
                if (items.IsFogged(side)) text = Loc.Get("item.status.fog");
                else if (items.State.ShotItem != ItemState.NoShot)
                {
                    var shot = (ItemId)items.State.ShotItem;
                    text = shot == ItemId.Curve
                        ? Loc.Get("item.status.curve", Loc.Get(items.State.CurveSign > 0 ? "item.left" : "item.right"))
                        : Loc.Get("item.status.shot", ItemDefs.Of(shot).Name);
                }
                else if (items.State.ExtraTurn) text = Loc.Get("item.status.double");
            }
        }
        prompt.SetActive(text != null);
        if (text != null && promptText.text != text) promptText.text = text;
        leftButton.gameObject.SetActive(choosing);
        rightButton.gameObject.SetActive(choosing);
        cancelButton.gameObject.SetActive(picking.HasValue);
    }

    private void Tell(ItemEvent e)
    {
        var def = ItemDefs.Of(e.Item);
        switch (e.Kind)
        {
            case ItemEventKind.Used:
                hud.Notice(Loc.Get("hud.itemUsed", hud.SideName(e.Side), def.Name));
                break;
            case ItemEventKind.Gained:
                hud.Notice(Loc.Get("hud.itemGained", hud.SideName(e.Side), def.Name));
                break;
            case ItemEventKind.CatchUp:
                hud.Notice(Loc.Get("hud.itemCatchUp", hud.SideName(e.Side), def.Name));
                break;
            case ItemEventKind.Shielded:
                hud.Notice(Loc.Get("hud.itemShielded", hud.SideName(e.Side)));
                break;
        }
    }
}
