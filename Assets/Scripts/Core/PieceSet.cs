using System;
using System.Collections.Generic;
using UnityEngine;

// What differs with the pieces in play (MatchSettings.PieceType), besides
// how they're built (BoardSetup.Spawn): how each side is named and drawn,
// what a knock sounds like, and how the pieces stand. One entry per piece
// type, so a new kind of piece is a row here and a spawn method.
public sealed class PieceSet
{
    public PieceType Type;
    public string[] SideKeys;  // Loc keys of the sides' names, by player id
    public Color[] Fills;      // a side's piece, as the HUD draws it
    public Color[] Rings;
    public Color[] Letters;    // the letter on a lettered piece (janggi, chess)
    public bool Standing;      // topples and rolls: the camera frames the board from further back
    public bool KnocksBoard;   // lands with a knock of its own (PieceSounds)
    public Func<SoundBank, AudioClip> Hit;

    private static readonly Color StoneBlack = new Color32(0x15, 0x15, 0x15, 0xFF);
    private static readonly Color StoneWhite = new Color32(0xF7, 0xF7, 0xF7, 0xFF);
    public static readonly Color StoneBlue = new Color32(0x2F, 0x5D, 0xA8, 0xFF);
    public static readonly Color StoneRed = new Color32(0xB3, 0x31, 0x2A, 0xFF);
    private static readonly Color InkRing = new Color32(0x2E, 0x24, 0x1A, 0xFF);
    private static readonly Color MutedRing = new Color32(0x8C, 0x7C, 0x66, 0xFF);
    private static readonly Color BlueRing = new Color32(0x1D, 0x3B, 0x6E, 0xFF);
    private static readonly Color RedRing = new Color32(0x6E, 0x1B, 0x16, 0xFF);
    private static readonly Color JanggiWood = new Color32(0xE6, 0xCB, 0x94, 0xFF);
    public static readonly Color Cho = new Color32(0x2F, 0x7A, 0x4B, 0xFF);
    public static readonly Color Han = new Color32(0xB3, 0x31, 0x2A, 0xFF);
    private static readonly Color JanggiBlue = new Color32(0x2A, 0x55, 0x9E, 0xFF);
    private static readonly Color JanggiBlack = new Color32(0x2E, 0x24, 0x1A, 0xFF);
    private static readonly Color ChessWhite = new Color32(0xEC, 0xDF, 0xC4, 0xFF); // boxwood
    private static readonly Color ChessBlack = new Color32(0x2B, 0x24, 0x20, 0xFF); // ebony

    // Black and white go stones, and the third and fourth sides' dyed ones.
    private static PieceSet Stones(PieceType type, Func<SoundBank, AudioClip> hit, bool knocks) => new PieceSet
    {
        Type = type,
        SideKeys = new[] { "player.black", "player.white", "player.blue", "player.red" },
        Fills = new[] { StoneBlack, StoneWhite, StoneBlue, StoneRed },
        Rings = new[] { InkRing, MutedRing, BlueRing, RedRing },
        Letters = new[] { Cho, Han, JanggiBlue, JanggiBlack },
        KnocksBoard = knocks,
        Hit = hit,
    };

    private static readonly Dictionary<PieceType, PieceSet> sets = new Dictionary<PieceType, PieceSet>
    {
        { PieceType.GoStones, Stones(PieceType.GoStones, bank => bank.stoneHit, false) },
        // Gonggi stones rattle, and land with a knock.
        { PieceType.GonggiStones, Stones(PieceType.GonggiStones, bank => bank.gonggiHit, true) },
        // Cho (green, moves first, like black) and Han (red); the third and
        // fourth sides carry Cho's and Han's letters in blue and black.
        {
            PieceType.JanggiPieces, new PieceSet
            {
                Type = PieceType.JanggiPieces,
                SideKeys = new[] { "player.cho", "player.han", "player.blue", "player.black" },
                Fills = new[] { JanggiWood, JanggiWood, JanggiWood, JanggiWood },
                Rings = new[] { Cho, Han, JanggiBlue, JanggiBlack },
                Letters = new[] { Cho, Han, JanggiBlue, JanggiBlack },
                Hit = bank => bank.woodHit,
            }
        },
        // Chess's white moves first, as in chess.
        {
            PieceType.ChessPieces, new PieceSet
            {
                Type = PieceType.ChessPieces,
                SideKeys = new[] { "player.white", "player.black", "player.red", "player.blue" },
                Fills = new[] { ChessWhite, ChessBlack, StoneRed, StoneBlue },
                Rings = new[] { MutedRing, InkRing, RedRing, BlueRing },
                Letters = new[] { InkRing, ChessWhite, ChessWhite, ChessWhite },
                Standing = true,
                KnocksBoard = true,
                Hit = bank => bank.woodHit,
            }
        },
    };

    // Random is rolled before a match; anything unknown draws as go stones.
    public static PieceSet Of(PieceType type) => sets.TryGetValue(type, out var set) ? set : sets[PieceType.GoStones];

    public static PieceSet Current => Of(MatchSettings.Current.PieceType);
}
