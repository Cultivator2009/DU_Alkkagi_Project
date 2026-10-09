using System.Collections.Generic;
using UnityEngine;

// A side's items: two slots, and a third it has just got with both full,
// until it lets one go (ItemSystem.Discard).
public sealed class SideItems
{
    public const byte None = 255;

    public byte[] Slots = { None, None };
    public byte Pending = None;
    public bool CaughtUp;   // its catch-up item given
    public int UsedRares;   // a bit per rare item used: it gets that one no more

    public bool Has(int slot) => slot >= 0 && slot < Slots.Length && Slots[slot] != None;
    public bool Full => Slots[0] != None && Slots[1] != None;
    public bool Used(ItemId id) => (UsedRares & (1 << (int)id)) != 0;

    public void Write(NetWriter w)
    {
        w.Byte(Slots[0]);
        w.Byte(Slots[1]);
        w.Byte(Pending);
        w.Bool(CaughtUp);
        w.Int(UsedRares);
    }

    public static SideItems Read(NetReader r) => new SideItems { Slots = new[] { r.Byte(), r.Byte() }, Pending = r.Byte(), CaughtUp = r.Bool(), UsedRares = r.Int() };
}

// An item at work on a piece, until its user's turn in round Expires (or,
// for Freeze, the end of the piece's owner's next turn: TargetTurn).
public struct PieceEffect
{
    public char Piece;
    public ItemId Kind;
    public int Owner;      // the side that used it
    public int Expires;    // the round; -1: until something ends it
    public int TargetTurn; // Freeze: the turn of the piece's side it holds, once it's begun (-1 before)
    public char Partner;   // Glue: what it stuck to ('\0' nothing yet)

    public static void Write(NetWriter w, PieceEffect e)
    {
        w.Piece(e.Piece);
        w.Byte((byte)e.Kind);
        w.Int(e.Owner);
        w.Int(e.Expires);
        w.Int(e.TargetTurn);
        w.Piece(e.Partner);
    }

    public static PieceEffect Read(NetReader r) => new PieceEffect { Piece = r.Piece(), Kind = (ItemId)r.Byte(), Owner = r.Int(), Expires = r.Int(), TargetTurn = r.Int(), Partner = r.Piece() };
}

// A pillar or a patch of ice on the board, until its user's turn in round Expires.
public struct BoardObject
{
    public int Id;
    public ItemId Kind;
    public Vector2 Position; // (x, z)
    public int Owner;
    public int Expires;

    public static void Write(NetWriter w, BoardObject o)
    {
        w.Int(o.Id);
        w.Byte((byte)o.Kind);
        w.Float(o.Position.x);
        w.Float(o.Position.y);
        w.Int(o.Owner);
        w.Int(o.Expires);
    }

    public static BoardObject Read(NetReader r) => new BoardObject { Id = r.Int(), Kind = (ItemId)r.Byte(), Position = new Vector2(r.Float(), r.Float()), Owner = r.Int(), Expires = r.Int() };
}

// An item box on the board: a piece that comes to it on a side's shot
// gives that side an item.
public struct ItemBox
{
    public int Id;
    public Vector2 Position;

    public static void Write(NetWriter w, ItemBox b)
    {
        w.Int(b.Id);
        w.Float(b.Position.x);
        w.Float(b.Position.y);
    }

    public static ItemBox Read(NetReader r) => new ItemBox { Id = r.Int(), Position = new Vector2(r.Float(), r.Float()) };
}

// The items as the host has them: every screen draws and tells the match
// from this (ItemSystem, ItemView, the HUD), the guests from the host's word.
public sealed class ItemState
{
    public const byte NoShot = 255;

    public List<SideItems> Sides = new List<SideItems>();
    public List<ItemBox> Boxes = new List<ItemBox>();
    public List<PieceEffect> Effects = new List<PieceEffect>();
    public List<BoardObject> Objects = new List<BoardObject>();
    public int FogSide = -1;  // aims its next turn blind
    public int FogTurn = -1;  // that turn, once begun
    public int UsedTurn = -1; // the turn an item was last used in: one a turn
    public byte ShotItem = NoShot; // what this turn's shot carries (Weight, Ghost, Foresight, Blast, Curve)
    public int CurveSign;     // Curve: 1 left, -1 right
    public bool ExtraTurn;    // DoubleShot: the side to move shoots again after this shot
    public bool RewindReady;  // the last shot may be undone by the side to move
    public int NextId = 1;

    public bool Has(char piece, ItemId kind) => Effects.Exists(e => e.Piece == piece && e.Kind == kind);
    public bool Shot(ItemId item) => ShotItem == (byte)item;

    public void Write(NetWriter w)
    {
        w.List(Sides, (x, s) => s.Write(x));
        w.List(Boxes, ItemBox.Write);
        w.List(Effects, PieceEffect.Write);
        w.List(Objects, BoardObject.Write);
        w.Int(FogSide);
        w.Int(FogTurn);
        w.Int(UsedTurn);
        w.Byte(ShotItem);
        w.Int(CurveSign);
        w.Bool(ExtraTurn);
        w.Bool(RewindReady);
        w.Int(NextId);
    }

    public static ItemState Read(NetReader r) => new ItemState
    {
        Sides = r.List(SideItems.Read),
        Boxes = r.List(ItemBox.Read),
        Effects = r.List(PieceEffect.Read),
        Objects = r.List(BoardObject.Read),
        FogSide = r.Int(),
        FogTurn = r.Int(),
        UsedTurn = r.Int(),
        ShotItem = r.Byte(),
        CurveSign = r.Int(),
        ExtraTurn = r.Bool(),
        RewindReady = r.Bool(),
        NextId = r.Int(),
    };

    public ItemState Clone()
    {
        var w = new NetWriter();
        Write(w);
        return Read(new NetReader(w.ToArray()));
    }
}

public enum ItemEventKind : byte
{
    Used,
    Gained,      // from a box
    CatchUp,     // the catch-up item
    BoxAppeared,
    Discarded,
    Blast,       // where the blast went off
    Shielded,    // a shield brought a piece back
    Stuck,       // glue took hold
    Expired
}

// Something an item did, for the HUD and the board's effects on every screen.
public struct ItemEvent
{
    public ItemEventKind Kind;
    public int Side;
    public ItemId Item;
    public char Piece;
    public char Other;
    public Vector2 Position;

    public static void Write(NetWriter w, ItemEvent e)
    {
        w.Byte((byte)e.Kind);
        w.Int(e.Side);
        w.Byte((byte)e.Item);
        w.Piece(e.Piece);
        w.Piece(e.Other);
        w.Float(e.Position.x);
        w.Float(e.Position.y);
    }

    public static ItemEvent Read(NetReader r) => new ItemEvent
    {
        Kind = (ItemEventKind)r.Byte(), Side = r.Int(), Item = (ItemId)r.Byte(), Piece = r.Piece(), Other = r.Piece(), Position = new Vector2(r.Float(), r.Float()),
    };
}

// What a player points an item at (ItemDef.Aim).
public struct ItemTarget
{
    public char Piece;
    public char Other;     // Swap: the other side's piece
    public Vector2 Point;  // (x, z)
    public int Sign;       // Curve: 1 left, -1 right

    public static void Write(NetWriter w, ItemTarget t)
    {
        w.Piece(t.Piece);
        w.Piece(t.Other);
        w.Float(t.Point.x);
        w.Float(t.Point.y);
        w.Int(t.Sign);
    }

    public static ItemTarget Read(NetReader r) => new ItemTarget { Piece = r.Piece(), Other = r.Piece(), Point = new Vector2(r.Float(), r.Float()), Sign = r.Int() };
}
