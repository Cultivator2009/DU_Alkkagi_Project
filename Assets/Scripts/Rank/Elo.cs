using System.Collections.Generic;
using UnityEngine;

// The rating maths: classic Elo, scored pairwise when three or four play.
// Every other side is a game of its own - beaten, lost to or tied with - and
// K is shared out among them, so beating three equals in a four-way match is
// worth what beating one is head to head.
public static class Elo
{
    public const int Start = 1000;
    public const int K = 32;

    // The chance that rating beats opponent.
    public static float Expected(int rating, int opponent) => 1f / (1f + Mathf.Pow(10f, (opponent - rating) / 400f));

    // results: each opponent the outcome ranks this side against, with 1 for
    // beating them, 0.5 for a tie and 0 for losing. opponents: how many other
    // sides played, ranked or not.
    public static int Change(int rating, IEnumerable<(int rating, float score)> results, int opponents)
    {
        if (opponents <= 0) return 0;
        var sum = 0f;
        foreach (var (other, score) in results) sum += score - Expected(rating, other);
        return Mathf.RoundToInt(K * sum / opponents);
    }
}
