using System.Collections.Generic;
using System.Linq;

// Alkkagi as it's played: a piece off the board is gone, and a side with
// none left is out. With teams (teamOf, MatchSettings.TeamOf) a team wins
// once every other team is out, and a teammate's piece is as one's own.
public class ClassicRuleset : IRuleset
{
    private readonly BothOutRule bothOutRule;
    private readonly System.Func<int, int> teamOf;

    public ClassicRuleset(BothOutRule bothOutRule, System.Func<int, int> teamOf = null)
    {
        this.bothOutRule = bothOutRule;
        this.teamOf = teamOf ?? (id => id);
    }

    public virtual void Begin(IReadOnlyList<Side> sides)
    {
    }

    public void OnBeforeFlick(GamePieceManager piece)
    {
        // Classic Alkkagi has no pre-flick action. Item modes hook in here.
    }

    public virtual void OnPieceOut(GamePieceManager piece, IReadOnlyList<Side> sides, int shooterId)
    {
        var owner = Find(sides, piece.playerIndex);
        if (owner != null) owner.Pieces = System.Math.Max(0, owner.Pieces - 1);
        Credit(piece, sides, shooterId);
    }

    // The shooter's score, or for a piece of their own the other side's.
    protected void Credit(GamePieceManager piece, IReadOnlyList<Side> sides, int shooterId)
    {
        var scorer = Find(sides, ScorerFor(piece.playerIndex, shooterId, sides.Count));
        if (scorer != null) scorer.Score++;
    }

    public virtual bool IsKnockedOut(Side side) => side.Pieces <= 0;

    public virtual int Value(GamePieceManager piece) => 1;

    public virtual bool WouldBeKnockedOut(Side side, int piecesLost, int valueLost) => side.Pieces - piecesLost <= 0;

    // Who a knocked-out piece counts for: the shooter, unless it was their
    // own (or their teammate's). Then, with two sides, the other one (their
    // suicide or team kill is the opponent's gain, as it always was); with
    // more, no one. Nor when no one shot it (-1).
    public int ScorerFor(int ownerId, int shooterId, int sides)
    {
        if (shooterId < 0) return -1;
        if (teamOf(ownerId) != teamOf(shooterId)) return shooterId;
        return sides == 2 ? 1 - ownerId : -1;
    }

    private int Teams(IEnumerable<Side> sides) => sides.Select(s => teamOf(s.Id)).Distinct().Count();

    protected static Side Find(IReadOnlyList<Side> sides, int id) => id >= 0 && id < sides.Count ? sides[id] : null;

    public bool TryGetMatchWinner(IReadOnlyList<Side> standingBefore, IReadOnlyList<Side> standingAfter, int shooterId, out Side winner, out MatchEndReason reason)
    {
        winner = null;
        reason = MatchEndReason.Knockout;
        // One side (or one team's) left standing has won: winner is one of them.
        if (standingAfter.Count > 0)
        {
            if (Teams(standingAfter) > 1) return false;
            winner = standingAfter[0];
            return true;
        }

        // The last pieces of every side still in went out on one flick; the
        // lobby decides who that favours. A null winner is a draw: the
        // shooter losing leaves a winner only if one other side (or team) was in.
        reason = MatchEndReason.BothOut;
        var others = standingBefore.Where(p => teamOf(p.Id) != teamOf(shooterId)).ToList();
        winner = bothOutRule switch
        {
            BothOutRule.ShooterWins => standingBefore.FirstOrDefault(p => teamOf(p.Id) == teamOf(shooterId)),
            BothOutRule.Draw => null,
            _ => others.Count > 0 && Teams(others) == 1 ? others[0] : null,
        };
        return true;
    }
}
