using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The AI's use of items (AIOpponent), by rule of thumb rather than by
// trying them on the board: a piece of its own near the edge gets an
// anchor, a shield or a post in front of it; a piece of the others' near
// the edge gets greased, shrunk or iced; a quake goes the way more of
// theirs than its own would go over; health and pieces lost come back when
// they can. Its shot's items it takes as they come. An easy AI uses fewer.
public static class ItemAI
{
    // How much the AI wants to keep an item (Discard drops the least).
    private static readonly Dictionary<ItemId, int> Worth = new Dictionary<ItemId, int>
    {
        { ItemId.Rewind, 9 }, { ItemId.DoubleShot, 8 }, { ItemId.Revive, 8 }, { ItemId.Quake, 6 }, { ItemId.Heal, 6 },
        { ItemId.Blast, 6 }, { ItemId.Weight, 5 }, { ItemId.Shield, 5 }, { ItemId.Swap, 5 }, { ItemId.Freeze, 4 },
        { ItemId.Grease, 4 }, { ItemId.Shrink, 4 }, { ItemId.Anchor, 4 }, { ItemId.Fog, 3 }, { ItemId.Pillar, 3 },
        { ItemId.Ice, 3 }, { ItemId.Grow, 3 }, { ItemId.Glue, 2 }, { ItemId.Ghost, 2 }, { ItemId.Foresight, 1 }, { ItemId.Curve, 0 },
    };

    // Which of the three to let go: 0, 1 a slot, 2 the new one.
    public static int DiscardIndex(SideItems items)
    {
        var three = new[] { items.Slots[0], items.Slots[1], items.Pending };
        var worst = 0;
        for (var i = 1; i < 3; i++)
            if (Value(three[i]) < Value(three[worst])) worst = i;
        return worst;
    }

    private static int Value(byte item) => ItemDefs.IsValid(item) ? Worth[(ItemId)item] : -1;

    // One item before the shot, if one is worth it now. True if it used one.
    public static bool TryUse(ItemSystem items, GameManager game, int side, AILevel level)
    {
        if (!items.MayUse(side)) return false;
        var odds = level == AILevel.Easy ? 0.5f : level == AILevel.Normal ? 0.8f : 1f;
        if (Random.value > odds) return false;
        var slots = items.ItemsOf(side).Slots;
        // The one it values most first.
        foreach (var slot in new[] { 0, 1 }.Where(s => ItemDefs.IsValid(slots[s])).OrderByDescending(s => Worth[(ItemId)slots[s]]))
        {
            var id = (ItemId)slots[slot];
            if (!items.Usable(side, id) || !Plan(items, game, side, id, out var target)) continue;
            if (items.TryUse(side, slot, target)) return true;
        }
        return false;
    }

    private static bool Plan(ItemSystem items, GameManager game, int side, ItemId id, out ItemTarget target)
    {
        target = default;
        var shape = game.Board.Playable;
        var mine = items.PiecesOf(side).ToList();
        var theirs = game.gamePieceScripts.Where(p => p != null && p.gameObject.activeSelf && items.IsEnemy(side, p.Manager.playerIndex)).ToList();
        float Margin(GamePieceDragAndReleaseForce p) => shape.Margin(ItemSystem.Flat(p.Body.position));
        GamePieceDragAndReleaseForce Nearest(IEnumerable<GamePieceDragAndReleaseForce> pieces, float within) =>
            pieces.Where(p => Margin(p) < within).OrderBy(Margin).FirstOrDefault();

        switch (id)
        {
            case ItemId.Weight:
            case ItemId.Blast:
            case ItemId.DoubleShot:
                return mine.Count > 0 && theirs.Count > 0;
            case ItemId.Ghost:
                return Random.value < 0.4f;
            case ItemId.Foresight:
            case ItemId.Curve:
                return false; // nothing they'd do for it
            case ItemId.Anchor:
            case ItemId.Shield:
                return Pick(Nearest(mine.Where(p => !items.State.Has(p.Manager.pieceID, id)), 0.45f), out target);
            case ItemId.Glue:
                return Random.value < 0.4f && Pick(mine.OrderBy(_ => Random.value).FirstOrDefault(), out target);
            case ItemId.Grow:
                return Random.value < 0.5f && Pick(mine.OrderByDescending(Margin).FirstOrDefault(), out target);
            case ItemId.Grease:
            case ItemId.Shrink:
                return Pick(Nearest(theirs, 0.6f), out target);
            case ItemId.Freeze:
                return Random.value < 0.7f && Pick(theirs.OrderBy(p => mine.Count == 0 ? 0 : mine.Min(m => (m.Body.position - p.Body.position).sqrMagnitude)).FirstOrDefault(), out target);
            case ItemId.Fog:
                var next = NextEnemy(items, game, side);
                return next >= 0 && Pick(theirs.FirstOrDefault(p => p.Manager.playerIndex == next), out target);
            case ItemId.Pillar:
                var guarded = Nearest(mine, 0.4f);
                if (guarded == null) return false;
                var at = ItemSystem.Flat(guarded.Body.position);
                var edge = shape.Closest(at);
                var toward = (edge - at).sqrMagnitude > 1e-6f ? (edge - at).normalized : Vector2.up;
                target = new ItemTarget { Point = at + toward * (guarded.Manager.radius + ItemSystem.PillarRadius + 0.04f) };
                return items.CanTarget(side, id, target);
            case ItemId.Ice:
                var slid = Nearest(theirs, 0.6f);
                if (slid == null) return false;
                target = new ItemTarget { Point = ItemSystem.Flat(slid.Body.position) };
                return items.CanTarget(side, id, target);
            case ItemId.Quake:
                return Quake(items, game, side, mine, theirs, out target);
            case ItemId.Rewind:
                return items.RewindWorth(side) >= 1;
            case ItemId.Revive:
                var back = items.BenchedOf(side);
                if (back == null) return false;
                var occupied = game.gamePieceScripts.Where(p => p != null && p.gameObject.activeSelf).Select(p => (p.Manager, p.Body.position)).ToList();
                var random = new System.Random();
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    var spot = game.Board.RandomFreePosition(side, back.Manager, occupied, random);
                    target = new ItemTarget { Point = ItemSystem.Flat(spot) };
                    if (items.CanTarget(side, id, target)) return true;
                }
                return false;
            case ItemId.Swap:
                var risky = Nearest(mine, 0.3f);
                var safe = theirs.Where(p => Margin(p) > 0.7f).OrderByDescending(Margin).FirstOrDefault();
                if (risky == null || safe == null) return false;
                target = new ItemTarget { Piece = risky.Manager.pieceID, Other = safe.Manager.pieceID };
                return true;
            case ItemId.Heal:
                var sideState = game.Sides[side];
                if (MatchSettings.Current.HealthRule == HealthRule.Side) return sideState.Health < sideState.MaxHealth * 0.7f;
                var hurt = mine.Where(p => p.Manager.health < MatchSettings.Current.PieceHealth * 0.6f).OrderBy(p => p.Manager.health).FirstOrDefault();
                return Pick(hurt, out target);
        }
        return false;
    }

    private static bool Pick(GamePieceDragAndReleaseForce piece, out ItemTarget target)
    {
        target = piece != null ? new ItemTarget { Piece = piece.Manager.pieceID } : default;
        return piece != null;
    }

    // The way that sends more of theirs than its own off: eight ways tried,
    // a piece counted gone that has less board than a quake's slide that way.
    private static bool Quake(ItemSystem items, GameManager game, int side, List<GamePieceDragAndReleaseForce> mine, List<GamePieceDragAndReleaseForce> theirs, out ItemTarget target)
    {
        target = default;
        var shape = game.Board.Playable;
        var best = 0f;
        for (var i = 0; i < 8; i++)
        {
            var a = i * Mathf.PI / 4;
            var way = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            bool Gone(GamePieceDragAndReleaseForce p) => shape.Exit(ItemSystem.Flat(p.Body.position), way) < ItemSystem.QuakeSlide * 1.1f;
            var score = theirs.Count(Gone) - 1.2f * mine.Count(Gone);
            if (score <= best) continue;
            best = score;
            target = new ItemTarget { Point = way };
        }
        return best >= 1.5f;
    }

    // The next side round of another team still in.
    private static int NextEnemy(ItemSystem items, GameManager game, int side)
    {
        var count = game.Sides.Count;
        for (var step = 1; step < count; step++)
        {
            var other = (side + step) % count;
            if (items.IsEnemy(side, other)) return other;
        }
        return -1;
    }
}
