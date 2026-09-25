using System.Collections.Generic;
using System.Linq;

public enum RatedResult : byte
{
    Win,
    Loss,
    Draw,
    NoContest // three or four, and the host left while this side was still in
}

// One rated online match as this screen follows it: who went out when, and
// from that every side's result against every other. A side beats everyone
// who went out before it and loses to everyone who outlasted it; sides out
// on the same flick tie. The host's own events and a guest's messages come
// in the same order, so every machine reaches the same standings.
public sealed class RatedMatch
{
    private readonly IReadOnlyList<int> ratings; // at the start, by player id
    private readonly int self;
    private readonly List<List<int>> outGroups = new List<List<int>>(); // earliest first
    private bool groupOpen; // the last group is a flick's knockouts, and more may follow

    // ratings: the roster's; this screen's own entry should be its own record.
    public RatedMatch(IReadOnlyList<int> ratings, int self)
    {
        this.ratings = ratings;
        this.self = self;
    }

    public int Self => self;
    public int Rating(int playerId) => ratings[playerId];
    public bool IsOut(int playerId) => outGroups.Any(g => g.Contains(playerId));

    // Knocked out on one flick together is one group; a concession or a
    // departure is always its own.
    public void PlayerOut(int playerId, MatchEndReason reason)
    {
        if (IsOut(playerId)) return;
        if (reason == MatchEndReason.Knockout && groupOpen) outGroups[outGroups.Count - 1].Add(playerId);
        else outGroups.Add(new List<int> { playerId });
        groupOpen = reason == MatchEndReason.Knockout;
    }

    // A new turn: the last flick's knockouts are all in.
    public void TurnStarted() => groupOpen = false;

    // What leaving now costs this side: a loss to everyone still in, and
    // once out, the place it went out in.
    public (int change, RatedResult result) IfLeftNow()
    {
        var places = OutPlaces();
        places[self] ??= outGroups.Count;
        return (ChangeFor(self, places), RatedResult.Loss);
    }

    // Every side's rating change, by player id, and this side's result.
    // winnerId -1: no winner (a draw, or the host left three or four).
    public (int[] changes, RatedResult result) Finish(int winnerId, MatchEndReason reason)
    {
        var places = OutPlaces();
        var final = outGroups.Count;
        for (var i = 0; i < places.Length; i++)
        {
            if (places[i].HasValue) continue;
            if (reason == MatchEndReason.HostLeft)
            {
                // The host went out last; the rest stopped still in, unranked
                // among themselves.
                if (i == 0) places[i] = final;
            }
            else places[i] = i == winnerId ? int.MaxValue : final;
        }
        var changes = Enumerable.Range(0, ratings.Count).Select(i => ChangeFor(i, places)).ToArray();
        RatedResult result;
        if (self == winnerId) result = RatedResult.Win;
        else if (!places[self].HasValue) result = RatedResult.NoContest;
        else if (winnerId < 0 && reason != MatchEndReason.HostLeft && places[self] == final) result = RatedResult.Draw;
        else result = RatedResult.Loss;
        return (changes, result);
    }

    private int?[] OutPlaces()
    {
        var places = new int?[ratings.Count];
        for (var g = 0; g < outGroups.Count; g++)
            foreach (var id in outGroups[g]) places[id] = g;
        return places;
    }

    // Higher places beat lower ones; a side with none (still in when the
    // match stopped) outlasted everyone placed.
    private int ChangeFor(int playerId, int?[] places)
    {
        var results = new List<(int, float)>();
        for (var other = 0; other < ratings.Count; other++)
        {
            if (other == playerId) continue;
            var mine = places[playerId];
            var theirs = places[other];
            if (!mine.HasValue && !theirs.HasValue) continue;
            var score = !mine.HasValue ? 1f : !theirs.HasValue ? 0f : mine > theirs ? 1f : mine < theirs ? 0f : 0.5f;
            results.Add((ratings[other], score));
        }
        return Elo.Change(ratings[playerId], results, ratings.Count - 1);
    }
}
