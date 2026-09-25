using System.Collections.Generic;

public enum MatchEndReason : byte
{
    Knockout,     // the loser has no stones left
    BothOut,      // both sides' last stones went out on one flick: MatchSettings.BothOutRule decides
    OpponentLeft, // online opponent quit or disconnected mid-match: forfeit
    Surrender,    // the loser conceded from the in-game menu
    HostLeft      // three or four online and the host left: no one can go on (no winner)
}

public interface IRuleset
{
    void OnBeforeFlick(GamePieceManager piece);
    // shooterId: the player whose flick knocked it out.
    void OnPieceRemoved(GamePieceManager removedPiece, List<PlayersManager> players, int shooterId);
    // The sides still in (pieces on the board, not conceded or gone) before
    // and after the flick shooterId just resolved. winner is null for a draw.
    bool TryGetMatchWinner(IReadOnlyList<PlayersManager> standingBefore, IReadOnlyList<PlayersManager> standingAfter, int shooterId, out PlayersManager winner, out MatchEndReason reason);
}
