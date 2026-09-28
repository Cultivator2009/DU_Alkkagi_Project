using System.Collections.Generic;
using System.Linq;

// Alkkagi as it's played: a piece off the board is gone, and a side with
// none left is out.
public class ClassicRuleset : IRuleset
{
    private readonly BothOutRule bothOutRule;

    public ClassicRuleset(BothOutRule bothOutRule)
    {
        this.bothOutRule = bothOutRule;
    }

    public virtual void Begin(IReadOnlyList<Side> sides)
    {
    }

    public void OnBeforeFlick(GamePieceManager piece)
    {
        // Classic Alkkagi has no pre-flick action. Item modes hook in here.
    }

    public virtual bool OnPieceOut(GamePieceManager piece, IReadOnlyList<Side> sides, int shooterId)
    {
        var owner = Find(sides, piece.playerIndex);
        if (owner != null) owner.Pieces = System.Math.Max(0, owner.Pieces - 1);
        Credit(piece, sides, shooterId);
        return true;
    }

    // The shooter's score, or for a piece of their own the other side's.
    protected static void Credit(GamePieceManager piece, IReadOnlyList<Side> sides, int shooterId)
    {
        var scorer = Find(sides, ScorerFor(piece.playerIndex, shooterId, sides.Count));
        if (scorer != null) scorer.Score++;
    }

    public virtual bool IsKnockedOut(Side side) => side.Pieces <= 0;

    public virtual int Value(GamePieceManager piece) => 1;

    public virtual bool WouldBeKnockedOut(Side side, int piecesLost, int valueLost) => side.Pieces - piecesLost <= 0;

    // Who a knocked-out piece counts for: the shooter, unless it was their
    // own. Then, with two sides, the other one (their suicide or team kill is
    // the opponent's gain, as it always was); with more, no one. Nor when
    // no one shot it (-1).
    public static int ScorerFor(int ownerId, int shooterId, int sides)
    {
        if (shooterId < 0) return -1;
        if (ownerId != shooterId) return shooterId;
        return sides == 2 ? 1 - ownerId : -1;
    }

    protected static Side Find(IReadOnlyList<Side> sides, int id) => id >= 0 && id < sides.Count ? sides[id] : null;

    public bool TryGetMatchWinner(IReadOnlyList<Side> standingBefore, IReadOnlyList<Side> standingAfter, int shooterId, out Side winner, out MatchEndReason reason)
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
        var others = standingBefore.Where(p => p.Id != shooterId).ToList();
        winner = bothOutRule switch
        {
            BothOutRule.ShooterWins => standingBefore.FirstOrDefault(p => p.Id == shooterId),
            BothOutRule.Draw => null,
            _ => others.Count == 1 ? others[0] : null,
        };
        return true;
    }
}
