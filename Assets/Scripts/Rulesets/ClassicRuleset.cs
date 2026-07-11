using System.Collections.Generic;

public class ClassicRuleset : IRuleset
{
    public void OnBeforeFlick(GamePieceManager piece)
    {
        // Classic Alkkagi has no pre-flick action. Item modes hook in here.
    }

    public void OnPieceRemoved(GamePieceManager removedPiece, List<PlayersManager> players)
    {
        var owner = players.Find(p => p.ID == removedPiece.playerIndex);
        var scorer = players.Find(p => p.ID != removedPiece.playerIndex);
        owner?.OnPieceLost();
        scorer?.AddScore(1);
    }

    public bool TryGetMatchWinner(List<PlayersManager> players, out PlayersManager winner)
    {
        winner = null;
        PlayersManager remaining = null;
        var remainingCount = 0;
        foreach (var player in players)
        {
            if (player.totalPieceCnt <= 0) continue;
            remaining = player;
            remainingCount++;
        }
        if (remainingCount != 1) return false;
        winner = remaining;
        return true;
    }
}
