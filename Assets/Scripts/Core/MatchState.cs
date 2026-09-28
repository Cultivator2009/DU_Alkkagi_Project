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
        return state;
    }
}
