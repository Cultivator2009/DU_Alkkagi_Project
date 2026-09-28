using System.Collections.Generic;
using UnityEngine;

// How each side is named and drawn, which depends on the pieces in play
// (PieceSet): black and white go stones (gonggi stones too), the janggi
// sides Cho and Han, or chess's white and black, and online the third and
// fourth sides.
public static class SideStyle
{
    public static Color StoneBlue => PieceSet.StoneBlue;
    public static Color StoneRed => PieceSet.StoneRed;
    public static Color Cho => PieceSet.Cho;
    public static Color Han => PieceSet.Han;

    // Only these nine are in the Hanja font (Assets/Fonts/NotoSerifKR).
    private static readonly Dictionary<string, string> Hanja = new Dictionary<string, string>
    {
        { "piece.cho", "楚" }, { "piece.han", "漢" }, { "piece.cha", "車" }, { "piece.po", "包" }, { "piece.ma", "馬" },
        { "piece.sang", "象" }, { "piece.sa", "士" }, { "piece.jol", "卒" }, { "piece.byeong", "兵" },
    };

    public static string Name(int playerId) => Name(playerId, MatchSettings.Current.PieceType);

    public static string Name(int playerId, PieceType pieces) => Loc.Get(PieceSet.Of(pieces).SideKeys[Side(playerId)]);

    // The 3D stone of the third and fourth sides (the first two have their
    // own templates).
    public static Color StoneColor(int playerId) => playerId == 2 ? StoneBlue : StoneRed;

    // A piece's letter: a janggi side's own colour (the first and third
    // sides carry Cho's set - 楚, 卒 - the second and fourth Han's), or on
    // a chess piece whatever stands out from its colour.
    public static Color LetterColor(int playerId, PieceType pieces) => PieceSet.Of(pieces).Letters[Side(playerId)];

    public static bool ChoLetters(int playerId) => playerId % 2 == 0;

    // A piece's letter from its Loc key: a janggi piece's (piece.*) in the
    // script this player picked in Settings, a chess piece's (chess.*) in
    // the notation's own letters.
    public static string PieceLetter(string key) => GameSettings.JanggiHanja && Hanja.TryGetValue(key, out var hanja) ? hanja : Loc.Get(key);

    public static Color Fill(int playerId, PieceType pieces) => PieceSet.Of(pieces).Fills[Side(playerId)];

    public static Color Ring(int playerId, PieceType pieces) => PieceSet.Of(pieces).Rings[Side(playerId)];

    private static int Side(int playerId) => Mathf.Clamp(playerId, 0, MatchRoster.MaxPlayers - 1);
}
