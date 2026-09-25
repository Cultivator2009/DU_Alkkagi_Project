using System.Collections.Generic;
using System.Linq;

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

    public void OnPieceRemoved(GamePieceManager removedPiece, List<PlayersManager> players, int shooterId)
    {
        players.Find(p => p.ID == removedPiece.playerIndex)?.OnPieceLost();
        var scorer = ScorerFor(removedPiece.playerIndex, shooterId, players);
        players.Find(p => p.ID == scorer)?.AddScore(1);
    }

    // Who a knocked-out piece counts for: the shooter, unless it was their
    // own. Then, with two sides, the other one (their suicide or team kill is
    // the opponent's gain, as it always was); with more, no one.
    public static int ScorerFor(int ownerId, int shooterId, List<PlayersManager> players)
    {
        if (ownerId != shooterId) return shooterId;
        if (players.Count != 2) return -1;
        var other = players.Find(p => p.ID != ownerId);
        return other != null ? other.ID : -1;
    }

    public bool TryGetMatchWinner(IReadOnlyList<PlayersManager> standingBefore, IReadOnlyList<PlayersManager> standingAfter, int shooterId, out PlayersManager winner, out MatchEndReason reason)
    {
        winner = null;
        reason = MatchEndReason.Knockout;
        if (standingAfter.Count == 1)
        {
            winner = standingAfter[0];
            return true;
        }
        if (standingAfter.Count > 1) return false;

        // The last pieces of every side still in went out on one flick; the
        // lobby decides who that favours. A null winner is a draw: the
        // shooter losing leaves a winner only if one other side was in.
        reason = MatchEndReason.BothOut;
        var others = standingBefore.Where(p => p.ID != shooterId).ToList();
        winner = bothOutRule switch
        {
            BothOutRule.ShooterWins => standingBefore.FirstOrDefault(p => p.ID == shooterId),
            BothOutRule.Draw => null,
            _ => others.Count == 1 ? others[0] : null,
        };
        return true;
    }
}
