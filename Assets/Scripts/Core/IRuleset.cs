using System.Collections.Generic;

public enum MatchEndReason : byte
{
    Knockout,     // the loser has no stones left
    BothOut,      // both sides' last stones went out on one flick: MatchSettings.BothOutRule decides
    OpponentLeft, // online opponent quit or disconnected mid-match: forfeit
    Surrender     // the loser conceded from the in-game menu
}

public interface IRuleset
{
    void OnBeforeFlick(GamePieceManager piece);
    void OnPieceRemoved(GamePieceManager removedPiece, List<PlayersManager> players);
    // shooterId: the player whose flick just resolved. winner is null for a draw.
    bool TryGetMatchWinner(List<PlayersManager> players, int shooterId, out PlayersManager winner, out MatchEndReason reason);
}
