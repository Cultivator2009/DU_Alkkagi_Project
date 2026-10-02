using System.Collections.Generic;
using System.Linq;

// Who plays an online match, in seat order: the host is player 0, the others
// follow in the order the host listed them (with teams, teammates two
// seats apart). It travels with LoadGameScene, so every machine spawns the
// same sides and knows which is whose. Null for a local game, which is the
// seats at one screen (LocalOpponent). Each seat also carries the player's
// rating as the host's lobby showed it, so every machine rates the match
// from the same numbers, and whether it's one of the host's bots (and how
// strong): the host's AI plays those.
public sealed class MatchRoster
{
    public const int MaxPlayers = 4;

    public static MatchRoster Current { get; set; }

    public IReadOnlyList<ulong> SteamIds => steamIds;
    public IReadOnlyList<int> Ratings => ratings;
    public int Count => steamIds.Count;

    private readonly List<ulong> steamIds;
    private readonly List<int> ratings;
    private readonly List<Opponent> who;

    // A bot's id: its place among the lobby's bots, from 1 - nowhere near a
    // Steam id (those run to the quintillions).
    public static ulong BotId(int index) => (ulong)(index + 1);
    public static bool IsBotId(ulong id) => id > 0 && id <= MaxPlayers;

    // ratings: by seat; without them everyone is at the starting rating.
    // who: by seat, Human for a player (the default).
    public MatchRoster(IEnumerable<ulong> steamIds, IEnumerable<int> ratings = null, IEnumerable<Opponent> who = null)
    {
        this.steamIds = new List<ulong>(steamIds);
        this.ratings = ratings != null ? new List<int>(ratings) : this.steamIds.Select(_ => Elo.Start).ToList();
        this.who = who != null ? new List<Opponent>(who) : this.steamIds.Select(_ => Opponent.Human).ToList();
    }

    // -1 when that player isn't in this match.
    public int PlayerOf(ulong steamId) => steamIds.IndexOf(steamId);

    public Opponent Who(int seat) => seat >= 0 && seat < who.Count ? who[seat] : Opponent.Human;
    public bool IsBot(int seat) => Who(seat) != Opponent.Human;

    public void Write(NetWriter writer)
    {
        writer.List(Enumerable.Range(0, steamIds.Count).ToList(), (w, i) =>
        {
            w.ULong(steamIds[i]);
            w.Int(ratings[i]);
            w.Byte((byte)who[i]);
        });
    }

    public static MatchRoster Read(NetReader reader)
    {
        var seats = reader.List(r => (id: r.ULong(), rating: r.Int(), who: (Opponent)r.Byte())).Take(MaxPlayers).ToList();
        return new MatchRoster(seats.Select(s => s.id), seats.Select(s => s.rating), seats.Select(s => s.who));
    }
}
