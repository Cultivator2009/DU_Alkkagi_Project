using UnityEngine;

// The closing circle, 자기장 (MatchSettings.Zone): a circle round the
// board's middle that closes in a stage at a time - as the FirstRound'th
// round ends, then every Every rounds - until only the middle is left. A
// piece outside it as it closes is lost to it, as one off the edge is
// (GameManager.CloseZone). Each circle shows from the start, or from when
// the one before it closed, so a side sees rounds ahead where it has to be;
// the round it closes at the end of is announced (Warned). Once it's
// announced, no one may pass, so no one can hold a match by passing.
//
// The first circle takes only the board's far corners, the last is round
// its middle, the ones between evenly between. The same on every board and
// on every screen: worked out from the board's shape (Centre, Reach).
//
// Plain state: TurnController drives it (a round is every side still in
// having had a turn), MatchState carries it to the guests.
public sealed class ZoneRule
{
    public const int Stages = 5;
    public const int FirstRound = 4;          // the first circle closes as this round ends
    // And each after it this many rounds later. Two gave a side, moving one
    // piece a turn, too few turns to bring its pieces in: a closing took
    // most of the board's.
    public const int Every = 3;
    public const float CloseSeconds = 1.2f;   // game time: the circle sweeping in, taking what it passes
    private const float FirstShare = 0.85f;   // of Reach: inside the far corners only
    private const float LastShare = 0.2f;
    private const float LeastLast = 0.5f;     // the last circle at least this (room for a few pieces)

    public bool Enabled;
    public Vector2 Centre;     // the board's middle (world x, z)
    public float Reach;        // from there to the board's furthest corner
    public int Stage;          // circles closed so far
    public bool Warned;        // the next one closes as this round ends

    public bool Active => Enabled && (Stage > 0 || Warned);
    public bool AllowsPass => !Active;

    // Stage s's circle, 1 to Stages.
    public float Radius(int stage)
    {
        var first = Reach * FirstShare;
        var last = Mathf.Min(first, Mathf.Max(LeastLast, Reach * LastShare));
        return Mathf.Lerp(first, last, (Mathf.Clamp(stage, 1, Stages) - 1) / (float)(Stages - 1));
    }

    // What's safe now (before the first circle, all of it), and the next
    // circle (none after the last).
    public float Safe => Enabled && Stage > 0 ? Radius(Stage) : float.PositiveInfinity;
    public float Next => Enabled && Stage < Stages ? Radius(Stage + 1) : float.PositiveInfinity;

    public bool Outside(Vector2 point, float radius) => (point - Centre).sqrMagnitude > radius * radius;

    // How far from a point, going one way (a unit), a circle round centre is
    // left: infinite for no circle.
    public static float Exit(Vector2 from, Vector2 way, Vector2 centre, float radius)
    {
        if (float.IsInfinity(radius)) return float.PositiveInfinity;
        var off = from - centre;
        var b = Vector2.Dot(off, way);
        var c = off.sqrMagnitude - radius * radius;
        return Mathf.Max(0, -b + Mathf.Sqrt(Mathf.Max(0, b * b - c)));
    }

    // The round the next circle closes at the end of.
    public int ClosingRound => FirstRound + Stage * Every;

    // A round begins: announced if the next circle closes as it ends.
    public void BeginRound(int round) => Warned = Enabled && Stage < Stages && round == ClosingRound;

    // The round it was announced for is over: the next circle closes now.
    public bool TakeDue()
    {
        if (!Warned) return false;
        Warned = false;
        Stage++;
        return true;
    }
}
