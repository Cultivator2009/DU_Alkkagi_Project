using System.Collections.Generic;

// A battle of health (GameVariant.Health). Every side starts with the same
// health, and a piece off the board costs its side what it's worth
// (GamePieceManager.value: a stone 2, a janggi general or chess king 6).
// A: the piece comes back into its side's zone once the shot is over,
// and a side is out when its health is gone. B: the piece is gone, and a
// side is out when its health or its pieces are. Scoring and the rest are
// alkkagi's.
public class HealthRuleset : ClassicRuleset
{
    private readonly HealthRule rule;
    private readonly int health;

    public HealthRuleset(BothOutRule bothOutRule, HealthRule rule, int health) : base(bothOutRule)
    {
        this.rule = rule;
        this.health = health;
    }

    public override void Begin(IReadOnlyList<Side> sides)
    {
        foreach (var side in sides) side.Health = side.MaxHealth = health;
    }

    public override bool OnPieceOut(GamePieceManager piece, IReadOnlyList<Side> sides, int shooterId)
    {
        Credit(piece, sides, shooterId);
        var owner = Find(sides, piece.playerIndex);
        if (owner == null) return true;
        owner.Health = System.Math.Max(0, owner.Health - piece.value);
        if (rule == HealthRule.Respawn && owner.Health > 0) return false;
        owner.Pieces = System.Math.Max(0, owner.Pieces - 1);
        return true;
    }

    public override bool IsKnockedOut(Side side) => side.Health <= 0 || side.Pieces <= 0;

    public override int Value(GamePieceManager piece) => piece.value;

    public override bool WouldBeKnockedOut(Side side, int piecesLost, int valueLost) =>
        side.Health - valueLost <= 0 || (rule == HealthRule.Pool && side.Pieces - piecesLost <= 0);
}
