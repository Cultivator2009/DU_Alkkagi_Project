using System.Linq;
using UnityEngine;

// The item mode's items (ItemSystem), by number on the wire: give a new one
// the next and never reuse one.
public enum ItemId : byte
{
    Weight,     // this shot's piece three times as heavy
    Ghost,      // this shot passes through its own side's pieces
    Foresight,  // the aim shows where what the shot hits goes
    Blast,      // the shot's first knock throws everything round it outward
    Curve,      // the shot bends left or right
    DoubleShot, // another shot after this one
    Anchor,     // one of its pieces hardly moves
    Shield,     // one of its pieces comes back from the edge once
    Glue,       // one of its pieces sticks to the next it touches
    Grow,       // one of its pieces half as big again, and heavier
    Grease,     // another side's piece slides far
    Freeze,     // another side's piece can't be flicked on its next turn
    Shrink,     // another side's piece smaller and lighter
    Fog,        // another side aims its next turn without the aim's lines and power
    Pillar,     // a post on the board
    Ice,        // a patch of the board where nothing slows
    Quake,      // the board tips one way a moment, everything sliding
    Rewind,     // the last shot, another side's, undone
    Revive,     // one of its pieces back from off the board
    Swap,       // one of its pieces and another side's change places
    Heal        // a battle of health: health back
}

// What an item does its work on, picked by the player who uses it.
public enum ItemAim : byte
{
    None,
    Side,         // left or right (Curve)
    OwnPiece,
    EnemyPiece,   // a piece of a side on another team, still in
    Point,        // a spot on the board clear of the pieces
    Direction,    // a spot on the board, as a way from its middle
    OwnZone,      // a spot in its own placement zone (Revive)
    OwnThenEnemy  // one of its own pieces, then another side's (Swap)
}

public enum ItemCategory : byte
{
    Shot,    // on this turn's shot
    Piece,   // on one of its own pieces, a while
    Hinder,  // on another side's piece or turn
    Board,   // on the board, a while
    Special
}

public enum ItemRarity : byte
{
    Common,
    Uncommon,
    Rare // a side gets each only until it has used it once
}

public readonly struct ItemDef
{
    public readonly ItemId Id;
    public readonly ItemCategory Category;
    public readonly ItemRarity Rarity;
    public readonly ItemAim Aim;
    public readonly int Rounds;       // how long it lasts, in its user's turns; 0 = this turn, or once
    public readonly bool HealthOnly;  // only in a battle of health

    public ItemDef(ItemId id, ItemCategory category, ItemRarity rarity, ItemAim aim, int rounds = 0, bool healthOnly = false)
    {
        Id = id;
        Category = category;
        Rarity = rarity;
        Aim = aim;
        Rounds = rounds;
        HealthOnly = healthOnly;
    }

    public string Name => Loc.Get("item." + Id + ".name");
    public string Description => Loc.Get("item." + Id + ".desc");
}

public static class ItemDefs
{
    // In ItemId's order.
    public static readonly ItemDef[] All =
    {
        new ItemDef(ItemId.Weight, ItemCategory.Shot, ItemRarity.Common, ItemAim.None),
        new ItemDef(ItemId.Ghost, ItemCategory.Shot, ItemRarity.Common, ItemAim.None),
        new ItemDef(ItemId.Foresight, ItemCategory.Shot, ItemRarity.Common, ItemAim.None),
        new ItemDef(ItemId.Blast, ItemCategory.Shot, ItemRarity.Uncommon, ItemAim.None),
        new ItemDef(ItemId.Curve, ItemCategory.Shot, ItemRarity.Uncommon, ItemAim.Side),
        new ItemDef(ItemId.DoubleShot, ItemCategory.Shot, ItemRarity.Rare, ItemAim.None),
        new ItemDef(ItemId.Anchor, ItemCategory.Piece, ItemRarity.Common, ItemAim.OwnPiece, 2),
        new ItemDef(ItemId.Shield, ItemCategory.Piece, ItemRarity.Uncommon, ItemAim.OwnPiece, 4),
        new ItemDef(ItemId.Glue, ItemCategory.Piece, ItemRarity.Uncommon, ItemAim.OwnPiece, 2),
        new ItemDef(ItemId.Grow, ItemCategory.Piece, ItemRarity.Uncommon, ItemAim.OwnPiece, 3),
        new ItemDef(ItemId.Grease, ItemCategory.Hinder, ItemRarity.Common, ItemAim.EnemyPiece, 2),
        new ItemDef(ItemId.Freeze, ItemCategory.Hinder, ItemRarity.Common, ItemAim.EnemyPiece),
        new ItemDef(ItemId.Shrink, ItemCategory.Hinder, ItemRarity.Uncommon, ItemAim.EnemyPiece, 3),
        new ItemDef(ItemId.Fog, ItemCategory.Hinder, ItemRarity.Uncommon, ItemAim.EnemyPiece),
        new ItemDef(ItemId.Pillar, ItemCategory.Board, ItemRarity.Uncommon, ItemAim.Point, 3),
        new ItemDef(ItemId.Ice, ItemCategory.Board, ItemRarity.Uncommon, ItemAim.Point, 3),
        new ItemDef(ItemId.Quake, ItemCategory.Board, ItemRarity.Rare, ItemAim.Direction),
        new ItemDef(ItemId.Rewind, ItemCategory.Special, ItemRarity.Rare, ItemAim.None),
        new ItemDef(ItemId.Revive, ItemCategory.Special, ItemRarity.Rare, ItemAim.OwnZone),
        new ItemDef(ItemId.Swap, ItemCategory.Special, ItemRarity.Rare, ItemAim.OwnThenEnemy),
        new ItemDef(ItemId.Heal, ItemCategory.Special, ItemRarity.Rare, ItemAim.OwnPiece, healthOnly: true),
    };

    public static ItemDef Of(ItemId id) => All[(int)id];

    // How often a draw is of each rarity, from the items it may be.
    public static readonly float[] RarityOdds = { 0.6f, 0.3f, 0.1f };

    // What the player points at: a battle of health's B heals the side, not a piece.
    public static ItemAim AimFor(ItemId id, MatchSettings rules) =>
        id == ItemId.Heal && rules.HealthRule == HealthRule.Side ? ItemAim.None : Of(id).Aim;

    // The HUD's tile colour by what it's for.
    public static Color ColorOf(ItemCategory category) => category switch
    {
        ItemCategory.Shot => new Color32(0xB3, 0x31, 0x2A, 0xFF),
        ItemCategory.Piece => new Color32(0x2F, 0x6A, 0x4F, 0xFF),
        ItemCategory.Hinder => new Color32(0x2F, 0x5D, 0xA8, 0xFF),
        ItemCategory.Board => new Color32(0x9A, 0x6B, 0x0C, 0xFF),
        _ => new Color32(0x6E, 0x3F, 0x8E, 0xFF),
    };

    public static bool IsValid(byte id) => id < All.Length;

    public static int Count => All.Length;

    public static bool Checked => All.Select((def, i) => (int)def.Id == i).All(ok => ok);
}
