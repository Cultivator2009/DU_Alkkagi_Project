using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum SeriesEnd : byte
{
    Played, // the games were played out
    Left    // a player left: the other takes it
}

// A ranked match (MatchMode.Ranked): one on one, the best of three. A game
// won counts and a drawn one is no one's; two wins take the series, or
// after three games the more wins, level being a draw. The lower rating
// moves first in the first game (level: a coin toss), then whoever lost
// the last game (after a draw, whoever moved second). Leaving at any point
// loses the whole series. The rating moves once, at the end, on the
// series' result (Elo, two players), and walking out before then costs a
// loss (PlayerRating.Hold). Seats are the roster's: 0 the host, 1 the guest.
//
// The host starts each game with it (LoadGameScene); every screen then
// counts the same game from the same result.
public sealed class RankedSeries
{
    public const int MaxGames = 3;
    public const int WinsNeeded = 2;

    // The series being played, from its first game until a new one.
    public static RankedSeries Current { get; set; }

    public int Game = 1;            // the game being played, from 1
    public readonly int[] Wins = new int[2];
    public int First;               // the seat that moves first this game
    public bool Over;
    public int Winner = -1;         // the seat that took it, -1 for a draw
    public SeriesEnd End;
    public int[] Ratings;           // both seats' ratings it was rated from, once it's over
    public int[] Changes;           // and their changes (this screen's sums)
    private bool recorded;          // this game's result is in
    private int lastWinner = -1;    // its winner's seat, -1 a draw
    private bool applied;           // this screen's rating has moved

    // ratings: the roster's, by seat.
    public static RankedSeries Begin(IReadOnlyList<int> ratings)
    {
        var first = ratings[0] < ratings[1] ? 0 : ratings[1] < ratings[0] ? 1 : Random.Range(0, 2);
        return new RankedSeries { First = first };
    }

    // A game's result (winner a seat, -1 a draw).
    public void Record(int winner)
    {
        if (Over || recorded) return;
        recorded = true;
        lastWinner = winner >= 0 && winner < 2 ? winner : -1;
        if (lastWinner >= 0) Wins[lastWinner]++;
        if (Wins.Max() < WinsNeeded && Game < MaxGames) return;
        Over = true;
        End = SeriesEnd.Played;
        Winner = Wins[0] > Wins[1] ? 0 : Wins[1] > Wins[0] ? 1 : -1;
    }

    // seat left before the series was done: the other takes it.
    public void Forfeit(int seat)
    {
        if (Over) return;
        Over = true;
        End = SeriesEnd.Left;
        Winner = 1 - seat;
    }

    // The next game's series, as the host starts it: the loser of this one
    // moves first, after a draw whoever moved second.
    public RankedSeries Next()
    {
        var next = new RankedSeries { Game = Game + 1 };
        next.Wins[0] = Wins[0];
        next.Wins[1] = Wins[1];
        next.First = lastWinner >= 0 ? 1 - lastWinner : 1 - First;
        return next;
    }

    // From the first game on, walking out costs the series.
    public void Hold(IReadOnlyList<int> ratings, int self)
    {
        PlayerRating.Hold(ChangeFor(self, ratings, 0f), RatedResult.Loss);
    }

    // Once it's over, this screen's rating moves (once) and both changes
    // are kept for the result screen.
    public void Apply(IReadOnlyList<int> ratings, int self)
    {
        if (!Over || applied) return;
        applied = true;
        float Score(int seat) => Winner < 0 ? 0.5f : Winner == seat ? 1f : 0f;
        Ratings = ratings.ToArray();
        Changes = new[] { ChangeFor(0, ratings, Score(0)), ChangeFor(1, ratings, Score(1)) };
        var result = Winner < 0 ? RatedResult.Draw : Winner == self ? RatedResult.Win : RatedResult.Loss;
        PlayerRating.Apply(Changes[self], result);
    }

    private static int ChangeFor(int seat, IReadOnlyList<int> ratings, float score) =>
        Elo.Change(ratings[seat], new[] { (ratings[1 - seat], score) }, 1);

    public void Write(NetWriter w)
    {
        w.Int(Game);
        w.Int(Wins[0]);
        w.Int(Wins[1]);
        w.Int(First);
    }

    public static RankedSeries Read(NetReader r)
    {
        var series = new RankedSeries { Game = r.Int() };
        series.Wins[0] = r.Int();
        series.Wins[1] = r.Int();
        series.First = r.Int();
        return series;
    }
}
