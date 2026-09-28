using System.Collections.Generic;

// The match as the host has it, besides where the pieces are: whose turn
// it is, how the last one ended, every side, and the result once there is
// one. It goes out with every TurnResult, and on its own when it changes
// mid-turn (a side conceding or leaving). A guest's TurnController takes
// it over whole (ApplyRemote) and tells the screen what changed, so the
// guest never works anything out for itself.
public sealed class MatchState
{
    public int Turn;              // turns begun so far
    public int TurnsEnded;        // shot or passed: a guest tells a turn's end by this going up
    public int CurrentPlayer = -1;
    public TurnEnd LastTurnEnd;
    public int LastPlayer = -1;
    public bool Over;
    public int Winner = -1;       // -1: no one (a draw, or the host left)
    public MatchEndReason Reason;
    public List<Side> Sides = new List<Side>();
    // The crumbling edge (ZoneRule), and the round it's counted in.
    public int Round = 1;
    public int QuietRounds;
    public int ZoneStage;
    public bool ZoneWarned;
    public bool Collapsing;     // the edge is giving way now: no one's turn

    public void Write(NetWriter writer)
    {
        writer.Int(Turn);
        writer.Int(TurnsEnded);
        writer.Int(CurrentPlayer);
        writer.Byte((byte)LastTurnEnd);
        writer.Int(LastPlayer);
        writer.Bool(Over);
        writer.Int(Winner);
        writer.Byte((byte)Reason);
        writer.List(Sides, (w, side) =>
        {
            w.Int(side.Id);
            side.Write(w);
        });
        writer.Int(Round);
        writer.Int(QuietRounds);
        writer.Int(ZoneStage);
        writer.Bool(ZoneWarned);
        writer.Bool(Collapsing);
    }

    public static MatchState Read(NetReader reader)
    {
        var state = new MatchState
        {
            Turn = reader.Int(),
            TurnsEnded = reader.Int(),
            CurrentPlayer = reader.Int(),
            LastTurnEnd = (TurnEnd)reader.Byte(),
            LastPlayer = reader.Int(),
            Over = reader.Bool(),
            Winner = reader.Int(),
            Reason = (MatchEndReason)reader.Byte(),
        };
        state.Sides = reader.List(r =>
        {
            var side = new Side(r.Int());
            side.Read(r);
            return side;
        });
        state.Round = reader.Int();
        state.QuietRounds = reader.Int();
        state.ZoneStage = reader.Int();
        state.ZoneWarned = reader.Bool();
        state.Collapsing = reader.Bool();
        return state;
    }
}
