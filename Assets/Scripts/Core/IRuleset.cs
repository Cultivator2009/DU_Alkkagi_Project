using System.Collections.Generic;

public enum MatchEndReason : byte
{
    Knockout,     // the loser has no stones left
    BothOut,      // both sides' last stones went out on one flick: the shooter loses
    OpponentLeft  // online opponent quit or disconnected mid-match: forfeit
}

public interface IRuleset
{
    void OnBeforeFlick(GamePieceManager piece);
    void OnPieceRemoved(GamePieceManager removedPiece, List<PlayersManager> players);
    // shooterId: the player whose flick just resolved.
    bool TryGetMatchWinner(List<PlayersManager> players, int shooterId, out PlayersManager winner, out MatchEndReason reason);
}
