using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The item mode (MatchSettings.ItemsOn): each side holds two items, from
// the item boxes that turn up on the board (a piece that comes to one on a
// side's shot wins that side an item) and one more when it's down to its
// last fifth (the catch-up item). On its turn, before it shoots, a side may
// use one: on its shot, on a piece, on the board, or on another side
// (ItemDefs). A third item with both slots full waits until the side lets
// one of the three go (Discard).
//
// On the authority (a local game, the network host) this runs the items:
// their rules, their physics and how long they last. Everything that
// shows - slots, boxes, what's on which piece, the board's posts and ice -
// is in State, which a network guest takes over whole from the host
// (ApplyRemote) and draws from (ItemView, the HUD) as the host does. A
// guest asks the host to use or let go of an item (OnRequest...).
public class ItemSystem : MonoBehaviour
{
    public static ItemSystem Instance { get; private set; }

    public const float BoxRadius = 0.07f;
    public const float PillarRadius = 0.06f;
    public const float PillarHeight = 0.3f;
    public const float IceRadius = 0.45f;
    public const float BlastRadius = 0.75f;
    public const float BlastSpeed = 7f;        // m/s at the blast's heart, less out to its edge
    public const float CurveRadius = 2.5f;     // a curving shot's arc, whatever its speed
    public const float QuakeSlide = 0.5f;      // how far a quake sends a lone piece
    public const int MaxBoxes = 2;
    public const float CatchUpShare = 0.2f;    // pieces (or health) left that bring the catch-up item
    public const float HealShare = 0.3f;
    public const float GrowScale = 1.5f;
    public const float ShrinkScale = 0.7f;

    public ItemState State { get; private set; } = new ItemState();
    public bool IsMirror { get; private set; }
    // Waiting for the host's word on a guest's request.
    public bool Requesting { get; private set; }

    public event Action OnChanged;
    public event Action<ItemEvent> OnEvent;
    // The authority's news for the guests, gathered over a frame: what
    // happened, and the pieces an item moved or brought back (NetworkMatchBridge).
    public event Action<IReadOnlyList<ItemEvent>, IReadOnlyList<char>, IReadOnlyList<char>> OnUpdate;
    // A guest's asks of the host: use (slot, target), let go (0..2).
    public event Action<int, ItemTarget> OnRequestUse;
    public event Action<int> OnRequestDiscard;
    // A guest's piece back on the board (Revive, Rewind): the bridge looks it up again.
    public event Action<GamePieceDragAndReleaseForce> OnPieceRestored;

    private struct PieceBase
    {
        public Vector3 Scale;
        public float Mass;
        public float Radius;
        public float ExtraSlowing;
        public Quaternion Rotation; // as it was spawned: a piece brought back stands so
        public Collider[] Solids;
        public PhysicsMaterial[] Materials;
    }

    // The board before a shot, for Rewind.
    private sealed class Snapshot
    {
        public int Shooter;
        public int TurnsEnded; // when it was taken: the shot it's of ends with one more
        public int Stage;      // the edge's: a crumble since and there's no going back
        public int Standing;
        public List<(GamePieceDragAndReleaseForce piece, Vector3 position, Quaternion rotation, int health)> Pieces;
        public List<(int pieces, int score, int health)> Sides;
    }

    private GameManager game;
    private TurnController turns;
    private MatchSettings rules;
    private readonly System.Random random = new System.Random();
    private readonly Dictionary<GamePieceDragAndReleaseForce, PieceBase> bases = new Dictionary<GamePieceDragAndReleaseForce, PieceBase>();
    private readonly List<GamePieceDragAndReleaseForce> benched = new List<GamePieceDragAndReleaseForce>(); // out, kept to come back; latest last
    private readonly HashSet<GamePieceDragAndReleaseForce> onIce = new HashSet<GamePieceDragAndReleaseForce>();
    private readonly Dictionary<PhysicsMaterial, PhysicsMaterial> slippery = new Dictionary<PhysicsMaterial, PhysicsMaterial>();
    private readonly List<ItemEvent> news = new List<ItemEvent>();
    private readonly HashSet<char> moved = new HashSet<char>();
    private readonly HashSet<char> revived = new HashSet<char>();
    private bool dirty;
    private GamePieceDragAndReleaseForce shotPiece; // what this shot's item rides on
    private readonly List<(Collider a, Collider b)> ghosted = new List<(Collider, Collider)>();
    private Snapshot picked, lastShot;
    private int round;

    private BoardSetup Board => game.Board;
    private bool Authority => !IsMirror && turns != null && !turns.IsMirror;

    // ---- Setup ----

    // With the match prepared (GameManager): every side's slots, every piece's own self.
    public void Init(GameManager gameManager)
    {
        Instance = this;
        game = gameManager;
        turns = gameManager.TurnController;
        rules = MatchSettings.Current;
        State = new ItemState();
        foreach (var _ in turns.Sides) State.Sides.Add(new SideItems());
        foreach (var piece in game.gamePieceScripts) Capture(piece);
        PieceSelector.MayMove = piece => !State.Has(piece.Manager.pieceID, ItemId.Freeze);
        PieceSelector.Held = false;

        turns.OnTurnStarted += TurnStarted;
        turns.OnShotTaking += side => picked = Take(side);
        turns.OnShotBegun += ShotBegun;
        turns.OnShotEnded += _ => EndShotItems();
        turns.OnMotionEnded += () => Changed();
        turns.OnSidesChanged += CatchUp;
        turns.OnZoneChanged += ZoneChanged;
        gameObject.AddComponent<ItemView>().Init(this, gameManager);
        gameObject.AddComponent<ItemForesight>().Init(this, gameManager);
    }

    // A network guest: the host's word from here on.
    public void BecomeMirror() => IsMirror = true;

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        PieceSelector.MayMove = null;
        PieceSelector.Held = false;
    }

    private void Capture(GamePieceDragAndReleaseForce piece)
    {
        var solids = piece.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
        bases[piece] = new PieceBase
        {
            Scale = piece.transform.localScale,
            Mass = piece.Body.mass,
            Radius = piece.Manager.radius,
            ExtraSlowing = piece.extraSlowing,
            Rotation = piece.transform.rotation,
            Solids = solids,
            Materials = solids.Select(c => c.sharedMaterial).ToArray(),
        };
    }

    // ---- Asking ----

    public SideItems ItemsOf(int side) => side >= 0 && side < State.Sides.Count ? State.Sides[side] : new SideItems();

    // The side to move may use an item now: its turn, nothing picked up
    // yet, none used this turn, no third item waiting to be let go.
    public bool MayUse(int side) =>
        turns != null && turns.CurrentPlayerID == side && turns.State == GameManager.GameState.WaitingForInput && !turns.InMotion
        && State.UsedTurn != turns.Turn && ItemsOf(side).Pending == SideItems.None && !Requesting;

    // Whether there's anything for the item to do now.
    public bool Usable(int side, ItemId id)
    {
        var def = ItemDefs.Of(id);
        if (def.HealthOnly && rules.Variant != GameVariant.Health) return false;
        return id switch
        {
            ItemId.Rewind => State.RewindReady,
            ItemId.Revive => BenchedOf(side) != null,
            ItemId.Heal => rules.HealthRule == HealthRule.Side ? turns.Sides[side].Health < turns.Sides[side].MaxHealth : PiecesOf(side).Any(p => p.Manager.health < rules.PieceHealth),
            ItemId.DoubleShot => !State.ExtraTurn,
            _ => true,
        };
    }

    // Whether target is one this item may be used on, by this side.
    public bool CanTarget(int side, ItemId id, ItemTarget target)
    {
        var piece = PieceOf(target.Piece);
        switch (ItemDefs.AimFor(id, rules))
        {
            case ItemAim.None:
                return true;
            case ItemAim.Side:
                return target.Sign == 1 || target.Sign == -1;
            case ItemAim.OwnPiece:
                return piece != null && piece.Manager.playerIndex == side && (id != ItemId.Heal || piece.Manager.health < rules.PieceHealth);
            case ItemAim.EnemyPiece:
                return piece != null && IsEnemy(side, piece.Manager.playerIndex);
            case ItemAim.OwnThenEnemy:
                var other = PieceOf(target.Other);
                return piece != null && other != null && piece.Manager.playerIndex == side && IsEnemy(side, other.Manager.playerIndex);
            case ItemAim.Direction:
                return target.Point.sqrMagnitude > 0.01f;
            case ItemAim.Point:
                return id == ItemId.Ice ? Board.Playable.Contains(target.Point) : PostFits(target.Point);
            case ItemAim.OwnZone:
                var back = BenchedOf(side);
                return back != null && Board.InZone(side, Flat3(target.Point), back.Manager.radius) && Clear(target.Point, back.Manager.radius, null);
        }
        return false;
    }

    public bool IsFrozen(GamePieceDragAndReleaseForce piece) => piece != null && State.Has(piece.Manager.pieceID, ItemId.Freeze);

    // This side aims blind this turn (Fog).
    public bool IsFogged(int side) => State.FogSide == side && turns != null && State.FogTurn == turns.Turn && turns.CurrentPlayerID == side;

    // This turn's shot carries this item, for this side.
    public bool ShotCarries(int side, ItemId id) => State.Shot(id) && turns != null && turns.CurrentPlayerID == side;

    public bool IsEnemy(int side, int other) => other >= 0 && other < turns.Sides.Count && game.TeamOf(other) != game.TeamOf(side) && turns.Sides[other].Standing;

    public GamePieceDragAndReleaseForce PieceOf(char id) =>
        id == '\0' ? null : game.gamePieceScripts.Find(p => p != null && p.Manager.pieceID == id && p.gameObject.activeSelf);

    public IEnumerable<GamePieceDragAndReleaseForce> PiecesOf(int side) => game.gamePieceScripts.Where(p => p != null && p.gameObject.activeSelf && p.Manager.playerIndex == side);

    public GamePieceDragAndReleaseForce BenchedOf(int side) => benched.LastOrDefault(p => p != null && p.Manager.playerIndex == side);

    // A post fits here: on the board, its foot clear of the edge, every piece and the other posts.
    public bool PostFits(Vector2 point) =>
        Board.Playable.Contains(point, PillarRadius + 0.02f) && Clear(point, PillarRadius + 0.01f, null)
        && State.Objects.All(o => o.Kind != ItemId.Pillar || (o.Position - point).magnitude > PillarRadius * 2 + 0.02f);

    // Nothing within radius of point, outline to outline (but skip).
    private bool Clear(Vector2 point, float radius, GamePieceDragAndReleaseForce skip) =>
        game.gamePieceScripts.All(p => p == null || p == skip || !p.gameObject.activeSelf || (Flat(p.Body.position) - point).magnitude > p.Manager.radius + radius);

    // ---- Using ----

    // From the screen that plays side: on the authority it's used at once,
    // a guest asks the host.
    public void Request(int side, int slot, ItemTarget target)
    {
        if (!IsMirror)
        {
            TryUse(side, slot, target);
            return;
        }
        Requesting = true;
        OnRequestUse?.Invoke(slot, target);
        OnChanged?.Invoke();
    }

    public void RequestDiscard(int side, int index)
    {
        if (!IsMirror)
        {
            Discard(side, index);
            return;
        }
        OnRequestDiscard?.Invoke(index);
    }

    // On the authority: side uses its item in slot on target.
    public bool TryUse(int side, int slot, ItemTarget target)
    {
        if (!Authority || !MayUse(side)) return false;
        var items = State.Sides[side];
        if (!items.Has(slot)) return false;
        var id = (ItemId)items.Slots[slot];
        if (!Usable(side, id) || !CanTarget(side, id, target)) return false;
        items.Slots[slot] = SideItems.None;
        if (slot == 0)
        {
            items.Slots[0] = items.Slots[1];
            items.Slots[1] = SideItems.None;
        }
        if (ItemDefs.Of(id).Rarity == ItemRarity.Rare) items.UsedRares |= 1 << (int)id;
        State.UsedTurn = turns.Turn;
        var at = target.Point;
        var targetPiece = PieceOf(target.Piece);
        if (targetPiece != null && target.Point == Vector2.zero) at = Flat(targetPiece.Body.position);
        Emit(new ItemEvent { Kind = ItemEventKind.Used, Side = side, Item = id, Piece = target.Piece, Other = target.Other, Position = at });
        Apply(side, id, target);
        Changed();
        return true;
    }

    // side lets one of its three go: 0 or 1 its slots (the new one taking
    // its place), 2 the new one.
    public bool Discard(int side, int index)
    {
        if (!Authority || side < 0 || side >= State.Sides.Count) return false;
        var items = State.Sides[side];
        if (items.Pending == SideItems.None || index < 0 || index > 2) return false;
        var dropped = index == 2 ? items.Pending : items.Slots[index];
        if (index < 2) items.Slots[index] = items.Pending;
        items.Pending = SideItems.None;
        Emit(new ItemEvent { Kind = ItemEventKind.Discarded, Side = side, Item = (ItemId)dropped });
        Changed();
        return true;
    }

    private void Apply(int side, ItemId id, ItemTarget target)
    {
        var def = ItemDefs.Of(id);
        var piece = PieceOf(target.Piece);
        var expires = turns.Round + def.Rounds;
        switch (id)
        {
            case ItemId.Weight:
            case ItemId.Ghost:
            case ItemId.Foresight:
            case ItemId.Blast:
                State.ShotItem = (byte)id;
                break;
            case ItemId.Curve:
                State.ShotItem = (byte)id;
                State.CurveSign = target.Sign >= 0 ? 1 : -1;
                break;
            case ItemId.DoubleShot:
                State.ExtraTurn = true;
                turns.ExtraTurn = true;
                break;
            case ItemId.Anchor:
            case ItemId.Shield:
            case ItemId.Grow:
            case ItemId.Grease:
            case ItemId.Shrink:
                AddEffect(piece, id, side, expires);
                break;
            case ItemId.Glue:
                AddEffect(piece, id, side, expires);
                piece.gameObject.AddComponent<ItemGlue>().items = this;
                break;
            case ItemId.Freeze:
                AddEffect(piece, id, side, -1);
                break;
            case ItemId.Fog:
                State.FogSide = piece.Manager.playerIndex;
                State.FogTurn = -1;
                break;
            case ItemId.Pillar:
            case ItemId.Ice:
                State.Objects.Add(new BoardObject { Id = State.NextId++, Kind = id, Position = target.Point, Owner = side, Expires = expires });
                break;
            case ItemId.Quake:
                Quake(target.Point.normalized);
                break;
            case ItemId.Rewind:
                Rewind();
                break;
            case ItemId.Revive:
                Revive(side, target.Point);
                break;
            case ItemId.Swap:
                Swap(piece, PieceOf(target.Other));
                break;
            case ItemId.Heal:
                Heal(side, piece);
                break;
        }
    }

    private void AddEffect(GamePieceDragAndReleaseForce piece, ItemId id, int side, int expires)
    {
        var at = State.Effects.FindIndex(e => e.Piece == piece.Manager.pieceID && e.Kind == id);
        var effect = new PieceEffect { Piece = piece.Manager.pieceID, Kind = id, Owner = side, Expires = expires, TargetTurn = -1 };
        if (at >= 0) State.Effects[at] = effect;
        else State.Effects.Add(effect);
        Refresh(piece);
    }

    private void RemoveEffect(int index)
    {
        var effect = State.Effects[index];
        State.Effects.RemoveAt(index);
        var piece = PieceOf(effect.Piece);
        if (effect.Kind == ItemId.Glue) Unglue(piece, effect.Partner);
        if (piece != null) Refresh(piece);
    }

    // ---- The turn ----

    private void TurnStarted(int player)
    {
        if (!Authority) return;
        if (turns.Round != round)
        {
            round = turns.Round;
            MaybeBox();
        }
        for (var i = State.Effects.Count - 1; i >= 0; i--)
        {
            var effect = State.Effects[i];
            if (effect.Kind == ItemId.Freeze)
            {
                var owner = OwnerOf(effect.Piece);
                if (effect.TargetTurn < 0 && owner == player)
                {
                    effect.TargetTurn = turns.Turn;
                    State.Effects[i] = effect;
                }
                else if (effect.TargetTurn >= 0 && turns.Turn > effect.TargetTurn) RemoveEffect(i);
                else if (owner < 0 || !turns.IsStanding(owner)) RemoveEffect(i);
                continue;
            }
            if (Due(effect.Owner, effect.Expires, player)) RemoveEffect(i);
        }
        State.Objects.RemoveAll(o => Due(o.Owner, o.Expires, player));
        if (State.FogSide >= 0)
        {
            if (State.FogSide == player && State.FogTurn < 0) State.FogTurn = turns.Turn;
            else if (State.FogTurn >= 0 && turns.Turn > State.FogTurn || !turns.IsStanding(State.FogSide)) State.FogSide = State.FogTurn = -1;
        }
        State.ShotItem = ItemState.NoShot;
        State.CurveSign = 0;
        State.ExtraTurn = turns.ExtraTurn;
        State.RewindReady = RewindReady(player);
        Changed();
    }

    // Its user's turn in the round it ends in (or, the user out, anyone's).
    private bool Due(int owner, int expires, int player) => expires >= 0 && turns.Round >= expires && (owner == player || !turns.IsStanding(owner));

    private int OwnerOf(char piece)
    {
        var found = PieceOf(piece);
        return found != null ? found.Manager.playerIndex : -1;
    }

    // The shot's item on the piece flicked.
    private void ShotBegun(GamePieceDragAndReleaseForce piece)
    {
        if (!Authority) return;
        lastShot = picked != null && picked.Shooter == turns.CurrentPlayerID ? picked : Take(turns.CurrentPlayerID);
        picked = null;
        if (State.ShotItem == ItemState.NoShot || piece == null || piece.Manager.playerIndex != turns.CurrentPlayerID) return;
        shotPiece = piece;
        switch ((ItemId)State.ShotItem)
        {
            case ItemId.Weight:
                Refresh(piece);
                break;
            case ItemId.Ghost:
                var mine = Solids(piece).ToList();
                foreach (var own in game.gamePieceScripts.Where(p => p != null && p != piece && game.TeamOf(p.Manager.playerIndex) == game.TeamOf(piece.Manager.playerIndex)))
                foreach (var other in Solids(own))
                foreach (var solid in mine)
                {
                    Physics.IgnoreCollision(solid, other, true);
                    ghosted.Add((solid, other));
                }
                break;
            case ItemId.Blast:
                piece.gameObject.AddComponent<ItemBlast>().items = this;
                break;
            case ItemId.Curve:
                var curve = piece.gameObject.AddComponent<ItemCurve>();
                curve.sign = State.CurveSign;
                break;
        }
    }

    // The shot over: its item with it.
    private void EndShotItems()
    {
        if (!Authority) return;
        foreach (var (a, b) in ghosted)
            if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
        ghosted.Clear();
        var piece = shotPiece;
        shotPiece = null;
        State.ShotItem = ItemState.NoShot;
        if (piece != null)
        {
            foreach (var blast in piece.GetComponents<ItemBlast>()) Destroy(blast);
            foreach (var curve in piece.GetComponents<ItemCurve>()) Destroy(curve);
            Refresh(piece);
        }
        Changed();
    }

    private static IEnumerable<Collider> Solids(GamePieceDragAndReleaseForce piece) => piece.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger);

    // The edge gave way: what stood on the lost ground goes.
    private void ZoneChanged()
    {
        if (!Authority || !turns.Collapsing) return;
        var shape = Board.Playable;
        State.Boxes.RemoveAll(b => !shape.Contains(b.Position));
        State.Objects.RemoveAll(o => !shape.Contains(o.Position));
        Changed();
    }

    // ---- Getting items ----

    // A new round, every so many: a box where nothing is, if there's room.
    private void MaybeBox()
    {
        var every = rules.ItemBoxRounds;
        if (every <= 0 || round % every != 0 || State.Boxes.Count >= MaxBoxes) return;
        var shape = turns.Zone.Warned ? Board.Playable.Inset(ZoneRule.Step) : Board.Playable;
        var bounds = shape.Bounds;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var point = new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, (float)random.NextDouble()), Mathf.Lerp(bounds.yMin, bounds.yMax, (float)random.NextDouble()));
            if (!shape.Contains(point, 0.25f) || !Clear(point, BoxRadius + 0.15f, null)) continue;
            if (State.Boxes.Any(b => (b.Position - point).magnitude < 0.4f)) continue;
            if (State.Objects.Any(o => (o.Position - point).magnitude < (o.Kind == ItemId.Ice ? 0.1f : PillarRadius + BoxRadius + 0.1f))) continue;
            var box = new ItemBox { Id = State.NextId++, Position = point };
            State.Boxes.Add(box);
            Emit(new ItemEvent { Kind = ItemEventKind.BoxAppeared, Side = -1, Position = point });
            return;
        }
    }

    // A random item side may get: not a battle of health's out of one, nor
    // a rare one it has used. Rarity first (ItemDefs.RarityOdds), then any of it.
    public ItemId Draw(int side)
    {
        var items = ItemsOf(side);
        var pool = ItemDefs.All.Where(d => (!d.HealthOnly || rules.Variant == GameVariant.Health) && !(d.Rarity == ItemRarity.Rare && items.Used(d.Id))).ToList();
        var rarities = pool.Select(d => d.Rarity).Distinct().ToList();
        var total = rarities.Sum(r => ItemDefs.RarityOdds[(int)r]);
        var roll = (float)random.NextDouble() * total;
        var rarity = rarities[rarities.Count - 1];
        foreach (var r in rarities.OrderBy(r => r))
        {
            roll -= ItemDefs.RarityOdds[(int)r];
            if (roll > 0) continue;
            rarity = r;
            break;
        }
        var choices = pool.Where(d => d.Rarity == rarity).ToList();
        return choices[random.Next(choices.Count)].Id;
    }

    // Into a free slot, or waiting as the third.
    private void Give(int side, ItemId id, ItemEventKind why, Vector2 at)
    {
        var items = State.Sides[side];
        if (items.Slots[0] == SideItems.None) items.Slots[0] = (byte)id;
        else if (items.Slots[1] == SideItems.None) items.Slots[1] = (byte)id;
        else items.Pending = (byte)id;
        Emit(new ItemEvent { Kind = why, Side = side, Item = id, Position = at });
        Changed();
    }

    // A side down to its last fifth gets one item, once.
    private void CatchUp()
    {
        if (!Authority || !rules.ItemCatchUp || turns.TurnsEnded == 0) return;
        foreach (var side in turns.Sides)
        {
            if (!side.Standing || State.Sides[side.Id].CaughtUp) continue;
            var low = side.HasHealth ? side.Health <= side.MaxHealth * CatchUpShare : side.Pieces <= Mathf.Max(1, Mathf.FloorToInt(rules.StonesFor(side.Id) * CatchUpShare));
            if (!low) continue;
            State.Sides[side.Id].CaughtUp = true;
            Give(side.Id, Draw(side.Id), ItemEventKind.CatchUp, Vector2.zero);
        }
    }

    // ---- Physics, on the authority ----

    private void FixedUpdate()
    {
        if (!Authority || game == null || Board == null) return;
        Ice();
        if (turns.State != GameManager.GameState.ProcessingTurn) return;
        Boxes();
        Shields();
    }

    // A piece reaching a box on a side's shot: the box is that side's item.
    private void Boxes()
    {
        var side = turns.Shooter;
        if (side < 0 || State.Boxes.Count == 0) return;
        for (var i = State.Boxes.Count - 1; i >= 0; i--)
        {
            var box = State.Boxes[i];
            var reached = game.gamePieceScripts.Any(p => p != null && p.gameObject.activeSelf && (Flat(p.Body.position) - box.Position).magnitude < p.Manager.radius + BoxRadius);
            if (!reached) continue;
            State.Boxes.RemoveAt(i);
            Give(side, Draw(side), ItemEventKind.Gained, box.Position);
        }
    }

    // A shielded piece going over the edge comes back onto the board, its
    // way turned back off the edge, the shield spent.
    private void Shields()
    {
        var shape = Board.Playable;
        for (var i = State.Effects.Count - 1; i >= 0; i--)
        {
            var effect = State.Effects[i];
            if (effect.Kind != ItemId.Shield) continue;
            var piece = PieceOf(effect.Piece);
            if (piece == null) continue;
            var body = piece.Body;
            var at = Flat(body.position);
            if (shape.Contains(at) || body.position.y < Board.PieceHeight - 0.3f) continue;
            var edge = shape.Closest(at);
            var outward = at - edge;
            outward = outward.sqrMagnitude > 1e-8f ? outward.normalized : at.normalized;
            var inside = edge - outward * (piece.Manager.radius + 0.02f);
            body.position = new Vector3(inside.x, Mathf.Max(body.position.y, Board.PieceHeight) + 0.005f, inside.y);
            var velocity = new Vector2(body.linearVelocity.x, body.linearVelocity.z);
            var back = (velocity - 2 * Vector2.Dot(velocity, outward) * outward) * 0.5f;
            body.linearVelocity = new Vector3(back.x, 0, back.y);
            body.angularVelocity = Vector3.zero;
            State.Effects.RemoveAt(i);
            Refresh(piece);
            moved.Add(effect.Piece);
            Emit(new ItemEvent { Kind = ItemEventKind.Shielded, Side = effect.Owner, Item = ItemId.Shield, Piece = effect.Piece, Position = edge });
            Changed();
        }
    }

    // On the ice nothing slows: the pieces on it slide as greased ones do.
    private void Ice()
    {
        var patches = State.Objects.Where(o => o.Kind == ItemId.Ice).Select(o => o.Position).ToList();
        if (patches.Count == 0 && onIce.Count == 0) return;
        foreach (var piece in game.gamePieceScripts)
        {
            if (piece == null || !piece.gameObject.activeSelf) continue;
            var at = Flat(piece.Body.position);
            var on = patches.Any(p => (p - at).magnitude < IceRadius);
            if (on == onIce.Contains(piece)) continue;
            if (on) onIce.Add(piece);
            else onIce.Remove(piece);
            Refresh(piece);
        }
        onIce.RemoveWhere(p => p == null || !p.gameObject.activeSelf);
    }

    // The shot's first knock (ItemBlast): everything round it thrown
    // outward, the harder the nearer, the shot piece itself spared.
    public void Blast(Vector3 at, GamePieceDragAndReleaseForce shot)
    {
        if (!Authority) return;
        var center = Flat(at);
        foreach (var piece in game.gamePieceScripts.ToList())
        {
            if (piece == null || piece == shot || !piece.gameObject.activeSelf || piece.Body.isKinematic) continue;
            var away = Flat(piece.Body.position) - center;
            var distance = away.magnitude;
            if (distance > BlastRadius) continue;
            var direction = distance > 1e-3f ? away / distance : UnityEngine.Random.insideUnitCircle.normalized;
            var speed = BlastSpeed * (1f - 0.6f * distance / BlastRadius);
            piece.Body.AddForce(new Vector3(direction.x, 0, direction.y) * speed, ForceMode.VelocityChange);
            piece.Nudge();
            if (game.Ruleset is HealthRuleset) game.Damage(piece.Manager, game.DamageFor(piece.Body.mass * speed * 0.5f), piece.Body.position);
        }
        if (BoardSounds.Instance != null) BoardSounds.Instance.Emit(BoardSound.Hit, 12f, at);
        if (CameraRig.Instance != null) CameraRig.Shake(0.9f);
        Emit(new ItemEvent { Kind = ItemEventKind.Blast, Side = turns.CurrentPlayerID, Item = ItemId.Blast, Position = center });
        Changed();
    }

    // Glue's first touch (ItemGlue): the two held together till it wears off.
    public void Stick(GamePieceDragAndReleaseForce piece, GamePieceDragAndReleaseForce other)
    {
        if (!Authority || other == null || !other.gameObject.activeSelf) return;
        var at = State.Effects.FindIndex(e => e.Piece == piece.Manager.pieceID && e.Kind == ItemId.Glue && e.Partner == '\0');
        if (at < 0) return;
        var effect = State.Effects[at];
        effect.Partner = other.Manager.pieceID;
        State.Effects[at] = effect;
        var joint = piece.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = other.Body;
        joint.enableCollision = false;
        Emit(new ItemEvent { Kind = ItemEventKind.Stuck, Side = effect.Owner, Item = ItemId.Glue, Piece = effect.Piece, Other = effect.Partner, Position = Flat(piece.Body.position) });
        Changed();
    }

    private void Unglue(GamePieceDragAndReleaseForce piece, char partner)
    {
        if (piece == null) return;
        foreach (var glue in piece.GetComponents<ItemGlue>()) Destroy(glue);
        var other = partner != '\0' ? game.gamePieceScripts.Concat(benched).FirstOrDefault(p => p != null && p.Manager.pieceID == partner) : null;
        foreach (var joint in piece.GetComponents<FixedJoint>())
            if (other == null || joint.connectedBody == other.Body || joint.connectedBody == null) Destroy(joint);
    }

    // Every piece slides off one way, as far as friction lets a lone piece
    // go QuakeSlide (a little more or less each), and the side to move
    // shoots once they've stopped.
    private void Quake(Vector2 direction)
    {
        if (!turns.TryBeginMotion()) return;
        var slowing = GamePieceDragAndReleaseForce.BoardSlowing * PieceSet.Current.Grip;
        var speed = Mathf.Sqrt(2 * slowing * QuakeSlide);
        foreach (var piece in game.gamePieceScripts)
        {
            if (piece == null || !piece.gameObject.activeSelf) continue;
            var push = speed * (0.85f + 0.3f * (float)random.NextDouble());
            piece.Body.AddForce(new Vector3(direction.x, 0, direction.y) * push, ForceMode.VelocityChange);
            piece.Nudge();
        }
        if (CameraRig.Instance != null) CameraRig.Shake(1f);
    }

    // ---- Undoing and bringing back ----

    private Snapshot Take(int side) => new Snapshot
    {
        Shooter = side,
        TurnsEnded = turns.TurnsEnded,
        Stage = turns.Zone.Stage,
        Standing = turns.Sides.Count(s => s.Standing),
        Pieces = game.gamePieceScripts.Where(p => p != null && p.gameObject.activeSelf).Select(p => (p, p.Body.position, p.Body.rotation, p.Manager.health)).ToList(),
        Sides = turns.Sides.Select(s => (s.Pieces, s.Score, s.Health)).ToList(),
    };

    // The shot just over was another team's, nothing else has happened
    // since, and nothing it did can't be undone (a side put out, the edge given way).
    private bool RewindReady(int side) =>
        lastShot != null && game.TeamOf(lastShot.Shooter) != game.TeamOf(side) && turns.TurnsEnded == lastShot.TurnsEnded + 1
        && lastShot.Stage == turns.Zone.Stage && lastShot.Standing == turns.Sides.Count(s => s.Standing);

    // What undoing the last shot would win side back: its own pieces that
    // shot put out, less the others' (the AI's measure).
    public int RewindWorth(int side)
    {
        if (!State.RewindReady || lastShot == null) return 0;
        var worth = 0;
        for (var i = 0; i < turns.Sides.Count && i < lastShot.Sides.Count; i++)
        {
            var lost = lastShot.Sides[i].pieces - turns.Sides[i].Pieces;
            worth += game.TeamOf(i) == game.TeamOf(side) ? lost : -lost;
        }
        return worth;
    }

    private void Rewind()
    {
        var snapshot = lastShot;
        lastShot = null;
        State.RewindReady = false;
        if (snapshot == null) return;
        foreach (var (piece, position, rotation, health) in snapshot.Pieces)
        {
            if (piece == null) continue;
            if (!piece.gameObject.activeSelf) Unbench(piece);
            Place(piece, position, rotation);
            piece.Manager.health = health;
        }
        for (var i = 0; i < turns.Sides.Count && i < snapshot.Sides.Count; i++)
        {
            var side = turns.Sides[i];
            (side.Pieces, side.Score, side.Health) = snapshot.Sides[i];
        }
        turns.SidesChanged();
    }

    // The side's piece that went out last, back at point in its zone.
    private void Revive(int side, Vector2 point)
    {
        var piece = BenchedOf(side);
        if (piece == null) return;
        Unbench(piece);
        Place(piece, new Vector3(point.x, Board.PieceHeight, point.y), bases.TryGetValue(piece, out var b) ? b.Rotation : piece.transform.rotation);
        var owner = turns.Sides[side];
        owner.Pieces++;
        if (game.Ruleset is HealthRuleset health && health.Rule == HealthRule.PerPiece)
        {
            piece.Manager.health = Mathf.Max(1, health.PieceHealth / 2);
            owner.Health = Mathf.Min(owner.MaxHealth, owner.Health + piece.Manager.health);
        }
        turns.SidesChanged();
    }

    private void Swap(GamePieceDragAndReleaseForce a, GamePieceDragAndReleaseForce b)
    {
        if (a == null || b == null) return;
        var pa = a.Body.position;
        var pb = b.Body.position;
        Place(a, new Vector3(pb.x, pa.y, pb.z), a.Body.rotation);
        Place(b, new Vector3(pa.x, pb.y, pa.z), b.Body.rotation);
    }

    private void Heal(int side, GamePieceDragAndReleaseForce piece)
    {
        var owner = turns.Sides[side];
        if (rules.HealthRule == HealthRule.Side)
        {
            owner.Health = Mathf.Min(owner.MaxHealth, owner.Health + Mathf.RoundToInt(owner.MaxHealth * HealShare));
        }
        else if (piece != null)
        {
            var add = Mathf.Min(Mathf.RoundToInt(rules.PieceHealth * HealShare), rules.PieceHealth - piece.Manager.health);
            piece.Manager.health += add;
            owner.Health = Mathf.Min(owner.MaxHealth, owner.Health + add);
            moved.Add(piece.Manager.pieceID); // its health to the guests
        }
        turns.SidesChanged();
    }

    private void Place(GamePieceDragAndReleaseForce piece, Vector3 position, Quaternion rotation)
    {
        var body = piece.Body;
        body.position = position;
        body.rotation = rotation;
        piece.transform.SetPositionAndRotation(position, rotation);
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        moved.Add(piece.Manager.pieceID);
    }

    // A piece out of the match (GameManager.PieceOut), kept off the board
    // for Revive and Rewind. Whatever an item had on it goes with it.
    public void Bench(GamePieceDragAndReleaseForce piece)
    {
        var id = piece.Manager.pieceID;
        for (var i = State.Effects.Count - 1; i >= 0; i--)
        {
            var effect = State.Effects[i];
            if (effect.Piece == id) RemoveEffect(i);
            else if (effect.Kind == ItemId.Glue && effect.Partner == id)
            {
                Unglue(PieceOf(effect.Piece), id);
                effect.Partner = '\0';
                State.Effects.RemoveAt(i);
                var glued = PieceOf(effect.Piece);
                if (glued != null) Refresh(glued);
            }
        }
        onIce.Remove(piece);
        piece.gameObject.SetActive(false);
        benched.Add(piece);
        Changed();
    }

    // A guest's piece gone with the host's word: kept the same way.
    public void BenchRemote(GamePieceDragAndReleaseForce piece)
    {
        piece.gameObject.SetActive(false);
        benched.Add(piece);
    }

    // A piece given up for good (a side out): nothing to bring back.
    public void Forget(GamePieceDragAndReleaseForce piece)
    {
        benched.Remove(piece);
        bases.Remove(piece);
        onIce.Remove(piece);
    }

    private void Unbench(GamePieceDragAndReleaseForce piece)
    {
        benched.Remove(piece);
        piece.Manager.isDestroyed = false;
        piece.gameObject.SetActive(true);
        if (!game.gamePieceScripts.Contains(piece)) game.gamePieceScripts.Add(piece);
        revived.Add(piece.Manager.pieceID);
        Refresh(piece);
    }

    // On a guest: the host brought these back.
    public void RestoreRemote(IEnumerable<char> ids)
    {
        foreach (var id in ids)
        {
            var piece = benched.FirstOrDefault(p => p != null && p.Manager.pieceID == id);
            if (piece == null) continue;
            benched.Remove(piece);
            piece.Manager.isDestroyed = false;
            piece.gameObject.SetActive(true);
            if (!game.gamePieceScripts.Contains(piece)) game.gamePieceScripts.Add(piece);
            OnPieceRestored?.Invoke(piece);
        }
    }

    // ---- What's on a piece ----

    // Its size, weight, grip and give from what's on it now, from what it
    // was spawned as: the same on every screen (a guest's only shows).
    private void Refresh(GamePieceDragAndReleaseForce piece)
    {
        if (piece == null || !bases.TryGetValue(piece, out var b)) return;
        float scale = 1, mass = 1;
        var slip = onIce.Contains(piece);
        var extra = b.ExtraSlowing;
        foreach (var effect in State.Effects)
        {
            if (effect.Piece != piece.Manager.pieceID) continue;
            switch (effect.Kind)
            {
                case ItemId.Grow:
                    scale *= GrowScale;
                    mass *= 3f;
                    break;
                case ItemId.Shrink:
                    scale *= ShrinkScale;
                    mass *= 0.35f;
                    break;
                case ItemId.Anchor:
                    mass *= 5f;
                    extra += GamePieceDragAndReleaseForce.BoardSlowing * 3f;
                    break;
                case ItemId.Grease:
                    slip = true;
                    break;
            }
        }
        if (piece == shotPiece && State.Shot(ItemId.Weight)) mass *= 3f;
        Rescale(piece, b.Scale * scale);
        piece.Manager.radius = b.Radius * scale;
        piece.Body.mass = b.Mass * mass;
        piece.extraSlowing = slip ? 0 : extra;
        for (var i = 0; i < b.Solids.Length; i++)
            if (b.Solids[i] != null) b.Solids[i].sharedMaterial = slip ? Slippery(b.Materials[i]) : b.Materials[i];
    }

    // Grown or shrunk about its middle, its foot kept on the board (on the
    // authority: a guest's follows the host's).
    private void Rescale(GamePieceDragAndReleaseForce piece, Vector3 scale)
    {
        if ((piece.transform.localScale - scale).sqrMagnitude < 1e-8f) return;
        var solid = bases[piece].Solids.FirstOrDefault(c => c != null);
        var before = solid != null ? solid.bounds.min.y : 0f;
        piece.transform.localScale = scale;
        if (!Authority || solid == null) return;
        Physics.SyncTransforms();
        var lift = before - solid.bounds.min.y;
        piece.Body.position += Vector3.up * lift;
        piece.transform.position += Vector3.up * lift;
        moved.Add(piece.Manager.pieceID);
    }

    private PhysicsMaterial Slippery(PhysicsMaterial original)
    {
        var key = original != null ? original : Board.Active.physics;
        if (slippery.TryGetValue(key, out var made)) return made;
        made = key != null ? new PhysicsMaterial(key.name + " (greased)") { bounciness = key.bounciness, bounceCombine = key.bounceCombine } : new PhysicsMaterial("Greased");
        made.dynamicFriction = 0.06f;
        made.staticFriction = 0.06f;
        made.frictionCombine = PhysicsMaterialCombine.Minimum;
        slippery[key] = made;
        return made;
    }

    // ---- Telling ----

    private void Emit(ItemEvent e)
    {
        news.Add(e);
        OnEvent?.Invoke(e);
    }

    private void Changed()
    {
        dirty = true;
        OnChanged?.Invoke();
    }

    private void LateUpdate()
    {
        if (!Authority || (!dirty && news.Count == 0)) return;
        dirty = false;
        var events = news.ToList();
        news.Clear();
        var movedIds = moved.ToList();
        var revivedIds = revived.ToList();
        moved.Clear();
        revived.Clear();
        OnUpdate?.Invoke(events, movedIds, revivedIds);
    }

    // On the authority: tell the guests again, as it stands (a request turned down).
    public void Resend() => dirty = true;

    // On a guest: the host's items, and what happened.
    public void ApplyRemote(ItemState state, IReadOnlyList<ItemEvent> events)
    {
        if (!IsMirror || state == null) return;
        State = state;
        while (State.Sides.Count < turns.Sides.Count) State.Sides.Add(new SideItems());
        Requesting = false;
        foreach (var piece in game.gamePieceScripts)
            if (piece != null) Refresh(piece);
        OnChanged?.Invoke();
        if (events == null) return;
        foreach (var e in events) OnEvent?.Invoke(e);
    }

    public static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    private Vector3 Flat3(Vector2 v) => new Vector3(v.x, Board.PieceHeight, v.y);
}

// On the shot piece carrying a blast: its first knock against another piece sets it off.
public class ItemBlast : MonoBehaviour
{
    public ItemSystem items;
    private bool done;

    private void OnCollisionEnter(Collision collision)
    {
        if (done || items == null || collision.rigidbody == null) return;
        if (collision.rigidbody.GetComponent<GamePieceDragAndReleaseForce>() == null) return;
        done = true;
        items.Blast(collision.GetContact(0).point, GetComponent<GamePieceDragAndReleaseForce>());
    }
}

// On the shot piece curving: its way bends by CurveRadius as it goes (the
// same arc at any speed), until it knocks into something.
public class ItemCurve : MonoBehaviour
{
    public int sign = 1; // 1 left, -1 right
    private Rigidbody body;
    private bool done;

    private void Awake() => body = GetComponent<Rigidbody>();

    private void FixedUpdate()
    {
        if (done || body == null || body.isKinematic) return;
        var velocity = body.linearVelocity;
        var flat = new Vector2(velocity.x, velocity.z);
        var speed = flat.magnitude;
        if (speed < 0.3f) return;
        var turn = sign * speed * Time.fixedDeltaTime / ItemSystem.CurveRadius;
        var cos = Mathf.Cos(turn);
        var sin = Mathf.Sin(turn);
        body.linearVelocity = new Vector3(flat.x * cos - flat.y * sin, velocity.y, flat.x * sin + flat.y * cos);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.rigidbody != null || collision.collider.name == BoardVariant.WallName || collision.collider.name == ItemView.PillarName) done = true;
    }
}

// On a glued piece until it first touches another.
public class ItemGlue : MonoBehaviour
{
    public ItemSystem items;

    private void OnCollisionEnter(Collision collision)
    {
        var other = collision.rigidbody != null ? collision.rigidbody.GetComponent<GamePieceDragAndReleaseForce>() : null;
        if (items == null || other == null) return;
        items.Stick(GetComponent<GamePieceDragAndReleaseForce>(), other);
        Destroy(this);
    }
}
