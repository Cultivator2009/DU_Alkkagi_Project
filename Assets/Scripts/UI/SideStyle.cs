using System.Collections.Generic;
using UnityEngine;

// How each side is named and drawn, which depends on the pieces in play:
// black and white go stones (gonggi stones too), the janggi sides Cho
// (green, moves first, like black) and Han (red), or chess's white (first,
// as in chess) and black. Online games of three or four add blue and red
// stones, janggi pieces lettered in blue and black, or red and blue chess
// pieces.
public static class SideStyle
{
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

    // Only these nine are in the Hanja font (Assets/Fonts/NotoSerifKR).
    private static readonly Dictionary<string, string> Hanja = new Dictionary<string, string>
    {
        { "piece.cho", "楚" }, { "piece.han", "漢" }, { "piece.cha", "車" }, { "piece.po", "包" }, { "piece.ma", "馬" },
        { "piece.sang", "象" }, { "piece.sa", "士" }, { "piece.jol", "卒" }, { "piece.byeong", "兵" },
    };

    public static string Name(int playerId) => Name(playerId, MatchSettings.Current.PieceType);

    public static string Name(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return Loc.Get(playerId switch { 0 => "player.cho", 1 => "player.han", 2 => "player.blue", _ => "player.black" });
        if (pieces == PieceType.ChessPieces) return Loc.Get(playerId switch { 0 => "player.white", 1 => "player.black", 2 => "player.red", _ => "player.blue" });
        return Loc.Get(playerId switch { 0 => "player.black", 1 => "player.white", 2 => "player.blue", _ => "player.red" });
    }

    // The 3D stone of the third and fourth sides (the first two have their
    // own templates).
    public static Color StoneColor(int playerId) => playerId == 2 ? StoneBlue : StoneRed;

    // A piece's letter in the UI: a janggi side's own colour (the first and
    // third sides carry Cho's set - 楚, 卒 - the second and fourth Han's),
    // or on a chess piece whatever stands out from its colour.
    public static Color LetterColor(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.ChessPieces) return playerId == 0 ? InkRing : ChessWhite;
        return playerId switch { 0 => Cho, 1 => Han, 2 => JanggiBlue, _ => JanggiBlack };
    }

    public static bool ChoLetters(int playerId) => playerId % 2 == 0;

    // A piece's letter from its Loc key: a janggi piece's (piece.*) in the
    // script this player picked in Settings, a chess piece's (chess.*) in
    // the notation's own letters.
    public static string PieceLetter(string key) => GameSettings.JanggiHanja && Hanja.TryGetValue(key, out var hanja) ? hanja : Loc.Get(key);

    public static Color Fill(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return JanggiWood;
        if (pieces == PieceType.ChessPieces) return playerId switch { 0 => ChessWhite, 1 => ChessBlack, 2 => StoneRed, _ => StoneBlue };
        return playerId switch { 0 => StoneBlack, 1 => StoneWhite, 2 => StoneBlue, _ => StoneRed };
    }

    public static Color Ring(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return LetterColor(playerId, pieces);
        if (pieces == PieceType.ChessPieces) return playerId switch { 0 => MutedRing, 1 => InkRing, 2 => RedRing, _ => BlueRing };
        return playerId switch { 0 => InkRing, 1 => MutedRing, 2 => BlueRing, _ => RedRing };
    }
}
