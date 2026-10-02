using System.Collections.Generic;

// A battle of health (GameVariant.Health): knocks cost health, as in a
// shooting game - from 1 for a tap to MaxDamage for the hardest hit (by the
// impulse, GameManager.DamageFor), every piece in it but the one flicked,
// walls too. A piece off the board is gone, as in alkkagi.
// A (HealthRule.PerPiece): each piece has PieceHealth; one with none left
// breaks where it is, and a side is out with no pieces left.
// B (HealthRule.Side): a side shares SideHealth; a piece off the board
// costs it FallDamage, and it's out when its health or its pieces are gone.
// Scoring and the rest are alkkagi's.
public class HealthRuleset : ClassicRuleset
{
    public const int MaxDamage = 50;
    public const int FallDamage = 50; // B: a piece off the board

    public HealthRule Rule { get; }
    public int PieceHealth { get; }
    private readonly int sideHealth;

    public HealthRuleset(BothOutRule bothOutRule, HealthRule rule, int pieceHealth, int sideHealth) : base(bothOutRule)
    {
        Rule = rule;
        PieceHealth = pieceHealth;
        this.sideHealth = sideHealth;
    }

    // A: a side's health is its pieces' together (GameManager gives each its own).
    public override void Begin(IReadOnlyList<Side> sides)
    {
        foreach (var side in sides) side.Health = side.MaxHealth = Rule == HealthRule.PerPiece ? side.Pieces * PieceHealth : sideHealth;
    }

    public override void OnPieceOut(GamePieceManager piece, IReadOnlyList<Side> sides, int shooterId)
    {
        base.OnPieceOut(piece, sides, shooterId);
        var owner = Find(sides, piece.playerIndex);
        if (owner == null) return;
        var lost = Rule == HealthRule.PerPiece ? piece.health : FallDamage;
        owner.Health = System.Math.Max(0, owner.Health - lost);
        piece.health = 0;
    }

    // A knock. True when it leaves the piece with nothing (A): it breaks.
    public bool Damage(GamePieceManager piece, int amount, IReadOnlyList<Side> sides)
    {
        var owner = Find(sides, piece.playerIndex);
        if (owner == null || amount <= 0) return false;
        if (Rule == HealthRule.Side)
        {
            owner.Health = System.Math.Max(0, owner.Health - amount);
            return false;
        }
        var lost = System.Math.Min(amount, piece.health);
        piece.health -= lost;
        owner.Health = System.Math.Max(0, owner.Health - lost);
        return piece.health <= 0;
    }

    public override bool IsKnockedOut(Side side) => side.Pieces <= 0 || (Rule == HealthRule.Side && side.Health <= 0);

    // What going out costs: A, the piece's health; B, the fall.
    public override int Value(GamePieceManager piece) => Rule == HealthRule.PerPiece ? piece.health : FallDamage;

    public override bool WouldBeKnockedOut(Side side, int piecesLost, int valueLost) =>
        side.Pieces - piecesLost <= 0 || (Rule == HealthRule.Side && side.Health - valueLost <= 0);
}
