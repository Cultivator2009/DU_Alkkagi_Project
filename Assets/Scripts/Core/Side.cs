// One side of a match: its score, its pieces and, in a battle of health,
// its health. The authority (a local game, the network host) keeps them
// through the ruleset; a network guest copies the host's with every
// MatchState, so every screen shows the same numbers.
public sealed class Side
{
    public readonly int Id;
    public int Score;     // pieces this side knocked out that count for it (ClassicRuleset.ScorerFor)
    public int Pieces;    // still in play, a piece waiting to come back included
    public int Health;    // a battle of health's (HealthRuleset); 0 otherwise
    public int MaxHealth;
    public int Shots;
    // Out of the match, and why: knocked out, conceded or gone. A match of
    // three or four goes on without it; the last side in wins.
    public MatchEndReason? Out;

    public Side(int id)
    {
        Id = id;
    }

    public bool Standing => !Out.HasValue;
    public bool HasHealth => MaxHealth > 0;

    public void CopyFrom(Side other)
    {
        Score = other.Score;
        Pieces = other.Pieces;
        Health = other.Health;
        MaxHealth = other.MaxHealth;
        Shots = other.Shots;
        Out = other.Out;
    }

    public void Write(NetWriter writer)
    {
        writer.Int(Score);
        writer.Int(Pieces);
        writer.Int(Health);
        writer.Int(MaxHealth);
        writer.Int(Shots);
        writer.Int(Out.HasValue ? (int)Out.Value : -1);
    }

    public void Read(NetReader reader)
    {
        Score = reader.Int();
        Pieces = reader.Int();
        Health = reader.Int();
        MaxHealth = reader.Int();
        Shots = reader.Int();
        var out_ = reader.Int();
        Out = out_ >= 0 ? (MatchEndReason)out_ : (MatchEndReason?)null;
    }
}
