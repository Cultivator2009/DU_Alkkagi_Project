using System.Collections.Generic;

public interface IRuleset
{
    void OnBeforeFlick(GamePieceManager piece);
    void OnPieceRemoved(GamePieceManager removedPiece, List<PlayersManager> players);
    bool TryGetMatchWinner(List<PlayersManager> players, out PlayersManager winner);
}
