using System.Collections.Generic;
using UnityEngine;

// How each side is named and drawn, which depends on the pieces in play:
// black and white go stones, or the janggi sides Cho (green, moves first,
// like black) and Han (red). Online games of three or four add blue and red
// stones, or janggi pieces lettered in blue and black.
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
        return Loc.Get(playerId switch { 0 => "player.black", 1 => "player.white", 2 => "player.blue", _ => "player.red" });
    }

    // The 3D stone of the third and fourth sides (the first two have their
    // own templates).
    public static Color StoneColor(int playerId) => playerId == 2 ? StoneBlue : StoneRed;

    // A janggi side's letters. The first and third sides carry Cho's set
    // (楚, 卒), the second and fourth Han's (漢, 兵).
    public static Color LetterColor(int playerId) => playerId switch { 0 => Cho, 1 => Han, 2 => JanggiBlue, _ => JanggiBlack };
    public static bool ChoLetters(int playerId) => playerId % 2 == 0;

    // A janggi piece's letter (a piece.* Loc key) in the script this player
    // picked in Settings.
    public static string PieceLetter(string key) => GameSettings.JanggiHanja ? Hanja[key] : Loc.Get(key);

    public static Color Fill(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return JanggiWood;
        return playerId switch { 0 => StoneBlack, 1 => StoneWhite, 2 => StoneBlue, _ => StoneRed };
    }

    public static Color Ring(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return LetterColor(playerId);
        return playerId switch { 0 => InkRing, 1 => MutedRing, 2 => BlueRing, _ => RedRing };
    }
}
