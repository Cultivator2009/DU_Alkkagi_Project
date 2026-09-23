using System.Collections.Generic;

public class ClassicRuleset : IRuleset
{
    private readonly BothOutRule bothOutRule;

    public ClassicRuleset(BothOutRule bothOutRule)
    {
        this.bothOutRule = bothOutRule;
    }

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

    public bool TryGetMatchWinner(List<PlayersManager> players, int shooterId, out PlayersManager winner, out MatchEndReason reason)
    {
        winner = null;
        reason = MatchEndReason.Knockout;
        PlayersManager remaining = null;
        var remainingCount = 0;
        foreach (var player in players)
        {
            if (player.totalPieceCnt <= 0) continue;
            remaining = player;
            remainingCount++;
        }

        if (remainingCount == 1)
        {
            winner = remaining;
            return true;
        }
        if (remainingCount == 0)
        {
            // Both last stones went out on the same flick; the lobby decides
            // who that favours. A null winner is a draw.
            reason = MatchEndReason.BothOut;
            winner = bothOutRule switch
            {
                BothOutRule.ShooterWins => players.Find(p => p.ID == shooterId),
                BothOutRule.Draw => null,
                _ => players.Find(p => p.ID != shooterId),
            };
            return true;
        }
        return false;
    }
}
