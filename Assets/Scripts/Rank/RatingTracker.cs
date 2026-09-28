using System;
using System.Collections.Generic;
using System.Linq;

// A rated online match as this screen follows it (NetworkMatchBridge makes
// one when MatchSettings.Rated): from the first moment it holds what
// walking out would cost, it keeps who went out when, and with the result
// it applies this player's change to their record. Every machine rates the
// match from the roster's numbers - this player's own from their record -
// and hears the same events, so every screen shows the same changes.
public sealed class RatingTracker
{
    public event Action OnFinished;

    public RatedMatch Match { get; }
    public int[] Changes { get; private set; } // by player id, once the result is in

    public RatingTracker(TurnController controller, IReadOnlyList<int> rosterRatings, int self)
    {
        var ratings = rosterRatings.ToArray();
        ratings[self] = PlayerRating.Current.Rating;
        Match = new RatedMatch(ratings, self);
        Hold();
        controller.OnTurnStarted += _ => Match.TurnStarted();
        controller.OnPlayerOut += (playerId, reason) =>
        {
            Match.PlayerOut(playerId, reason);
            // Out: leaving now costs only the place this side finished in
            // (and a side out on the same flick may still tie it).
            if (Match.IsOut(Match.Self)) Hold();
        };
        controller.OnMatchEnded += Finish;
    }

    private void Hold()
    {
        var (change, result) = Match.IfLeftNow();
        PlayerRating.Hold(change, result);
    }

    private void Finish(int winnerId, MatchEndReason reason)
    {
        if (Changes != null) return;
        var (changes, result) = Match.Finish(winnerId, reason);
        Changes = changes;
        PlayerRating.Apply(changes[Match.Self], result);
        OnFinished?.Invoke();
    }
}
