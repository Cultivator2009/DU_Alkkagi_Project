using UnityEngine;

// How each side is named and drawn, which depends on the pieces in play:
// black and white go stones, or the janggi sides Cho (green, moves first,
// like black) and Han (red).
public static class SideStyle
{
    private static readonly Color StoneBlack = new Color32(0x15, 0x15, 0x15, 0xFF);
    private static readonly Color StoneWhite = new Color32(0xF7, 0xF7, 0xF7, 0xFF);
    private static readonly Color InkRing = new Color32(0x2E, 0x24, 0x1A, 0xFF);
    private static readonly Color MutedRing = new Color32(0x8C, 0x7C, 0x66, 0xFF);
    private static readonly Color JanggiWood = new Color32(0xE6, 0xCB, 0x94, 0xFF);
    public static readonly Color Cho = new Color32(0x2F, 0x7A, 0x4B, 0xFF);
    public static readonly Color Han = new Color32(0xB3, 0x31, 0x2A, 0xFF);

    public static string Name(int playerId) => Name(playerId, MatchSettings.Current.PieceType);

    public static string Name(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return Loc.Get(playerId == 0 ? "player.cho" : "player.han");
        return Loc.Get(playerId == 0 ? "player.black" : "player.white");
    }

    public static Color Fill(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return JanggiWood;
        return playerId == 0 ? StoneBlack : StoneWhite;
    }

    public static Color Ring(int playerId, PieceType pieces)
    {
        if (pieces == PieceType.JanggiPieces) return playerId == 0 ? Cho : Han;
        return playerId == 0 ? InkRing : MutedRing;
    }
}
