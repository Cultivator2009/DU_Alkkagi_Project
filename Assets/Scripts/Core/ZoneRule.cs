using UnityEngine;

// The crumbling edge (MatchSettings.Zone), so no one can hold a match by
// passing. Once three rounds in a row have gone by with nothing off the
// board, the next step is announced as a new round begins; when that
// round's first turn ends, the board gives way a step all round
// (BoardSetup.Crumble) and whatever stood on the lost ground falls. From
// then on it comes in a step every round, announced the same way, until
// only the middle is left. Once it's announced, no one may pass.
//
// Plain state: TurnController drives it (a round is every side still in
// having had a turn), MatchState carries it to the guests.
public sealed class ZoneRule
{
    public const int QuietRoundsToStart = 3;
    public const float Step = 0.15f;       // how far the edge comes in each time
    public const float KeepMiddle = 0.45f; // it stops before less than this is left round the middle

    public bool Enabled;
    public int MaxStage;       // steps the board has room for (StagesFor)
    public int Stage;          // steps crumbled so far
    public bool Warned;        // the next step comes when this turn ends
    public int QuietRounds;    // in a row, nothing off the board

    public bool Active => Enabled && (Stage > 0 || Warned);
    public bool AllowsPass => !Active;

    // A round is over (outs: pieces off the board during it): whether the
    // next step is announced.
    public void EndRound(int outs)
    {
        QuietRounds = outs == 0 ? QuietRounds + 1 : 0;
        if (!Enabled || Warned || Stage >= MaxStage) return;
        if (Stage > 0 || QuietRounds >= QuietRoundsToStart) Warned = true;
    }

    // The turn after the announcement ended: true if the edge gives way now.
    public bool TakeDue()
    {
        if (!Warned) return false;
        Warned = false;
        Stage++;
        return true;
    }

    public static int StagesFor(BoardShape shape)
    {
        var stages = 0;
        while (true)
        {
            var next = shape.Inset((stages + 1) * Step);
            if (next.IsEmpty || next.Margin(Vector2.zero) < KeepMiddle) return stages;
            stages++;
        }
    }
}
