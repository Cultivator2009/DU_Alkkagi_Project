using System.Collections.Generic;
using System.Linq;
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
    public string Sound;       // the pieces' material in sound names: "hit_" + Sound (SoundBank.Get)
    // How hard the pieces are slowed on the board, against its friction as
    // PhysX has it since 2026-10-02 (improved patch friction): go stones and
    // janggi pieces 2, as they always played - stopping quickly - with a
    // flick √2 as strong, so a shot goes as far (BoardSetup.Spawn). The
    // extra slowing goes through the centre of mass (GamePieceDragAndReleaseForce.
    // Slow), so it can't tip them. Standing pieces keep 1: grip at a small
    // foot tipped them.
    public float Grip = 1f;

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

    // Colourblind mode's (GameSettings.ColorAssist), after Okabe and Ito:
    // told apart without telling red from green. Cho's green goes blue and
    // Han's red vermillion (both dark enough on the wood); the dyed third
    // and fourth stones blue and orange, janggi's third side purple.
    private static readonly Color AssistBlue = new Color32(0x00, 0x72, 0xB2, 0xFF);
    private static readonly Color AssistOrange = new Color32(0xE6, 0x9F, 0x00, 0xFF);
    private static readonly Color AssistVermillion = new Color32(0xC0, 0x4E, 0x00, 0xFF);
    private static readonly Color AssistPurple = new Color32(0x8E, 0x3B, 0x78, 0xFF);
    private static readonly Color AssistBlueRing = new Color32(0x00, 0x4A, 0x75, 0xFF);
    private static readonly Color AssistOrangeRing = new Color32(0x8A, 0x5F, 0x00, 0xFF);
    // Pieces' own colours, and the lines and letters on them.
    private static readonly Dictionary<Color, Color> AssistFills = new Dictionary<Color, Color>
    {
        { StoneBlue, AssistBlue }, { StoneRed, AssistOrange },
    };
    private static readonly Dictionary<Color, Color> AssistLines = new Dictionary<Color, Color>
    {
        { Cho, AssistBlue }, { Han, AssistVermillion }, { JanggiBlue, AssistPurple }, { BlueRing, AssistBlueRing }, { RedRing, AssistOrangeRing },
    };

    // Black and white go stones, and the third and fourth sides' dyed ones.
    private static PieceSet Stones(PieceType type, string sound, bool knocks, float grip) => new PieceSet
    {
        Type = type,
        SideKeys = new[] { "player.black", "player.white", "player.blue", "player.red" },
        Fills = new[] { StoneBlack, StoneWhite, StoneBlue, StoneRed },
        Rings = new[] { InkRing, MutedRing, BlueRing, RedRing },
        Letters = new[] { Cho, Han, JanggiBlue, JanggiBlack },
        KnocksBoard = knocks,
        Sound = sound,
        Grip = grip,
    };

    private static readonly Dictionary<PieceType, PieceSet> sets = new Dictionary<PieceType, PieceSet>
    {
        { PieceType.GoStones, Stones(PieceType.GoStones, "go", false, 2f) },
        // Gonggi stones rattle, and land with a knock.
        { PieceType.GonggiStones, Stones(PieceType.GonggiStones, "gonggi", true, 1f) },
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
                Sound = "janggi",
                Grip = 2f,
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
                Sound = "chess",
            }
        },
    };

    private static readonly Dictionary<PieceType, PieceSet> assisted = sets.ToDictionary(entry => entry.Key, entry => entry.Value.Assisted());

    private PieceSet Assisted()
    {
        Color Swap(Dictionary<Color, Color> palette, Color color) => palette.TryGetValue(color, out var swapped) ? swapped : color;
        var set = (PieceSet)MemberwiseClone();
        // Named as they now look: the red side orange, janggi's blue one purple.
        set.SideKeys = SideKeys.Select(key => key == "player.red" ? "player.orange" : key == "player.blue" && Type == PieceType.JanggiPieces ? "player.purple" : key).ToArray();
        set.Fills = Fills.Select(color => Swap(AssistFills, color)).ToArray();
        set.Rings = Rings.Select(color => Swap(AssistLines, color)).ToArray();
        set.Letters = Letters.Select(color => Swap(AssistLines, color)).ToArray();
        return set;
    }

    // Random is rolled before a match; anything unknown draws as go stones.
    // In colourblind mode, in its colours.
    public static PieceSet Of(PieceType type)
    {
        var table = GameSettings.ColorAssist ? assisted : sets;
        return table.TryGetValue(type, out var set) ? set : table[PieceType.GoStones];
    }

    public static PieceSet Current => Of(MatchSettings.Current.PieceType);
}
