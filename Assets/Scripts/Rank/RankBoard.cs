using System;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

// The Steam leaderboard behind the rankings: an entry's score is the
// player's rating and its details their games, wins, losses and draws. The
// first game to look for it creates it. The Spacewar test app (480) holds
// every developer's boards, hence the game's own name on it.
//
// Every player writes their own entry, so the board trusts each game's
// word, just as a match trusts its host's.
public static class RankBoard
{
    private const string BoardName = "du-alkkagi-rating";

    public enum Scope : byte
    {
        Top,      // the best, from first place
        AroundMe, // this player's neighbours
        Friends   // Steam friends and this player
    }

    public struct Entry
    {
        public ulong SteamId;
        public string Name;
        public int Place; // 1 = top; among friends, the place in that list
        public PlayerRating.Record Record;
    }

    private static Leaderboard? board;

    public static bool Available => SteamClient.IsValid;

    private static async Task<Leaderboard?> Board()
    {
        if (!SteamClient.IsValid) return null;
        if (!board.HasValue) board = await SteamUserStats.FindOrCreateLeaderboardAsync(BoardName, LeaderboardSort.Descending, LeaderboardDisplay.Numeric);
        return board;
    }

    // null: Steam couldn't be reached. Empty: no one there yet.
    public static async Task<Entry[]> Fetch(Scope scope, int count)
    {
        try
        {
            var found = await Board();
            if (!found.HasValue) return null;
            var entries = scope switch
            {
                Scope.Top => await found.Value.GetScoresAsync(count),
                Scope.AroundMe => await found.Value.GetScoresAroundUserAsync(-(count / 2 - 1), count - count / 2),
                _ => await found.Value.GetScoresFromFriendsAsync(),
            };
            if (entries == null) return Array.Empty<Entry>();
            // Steam knows the names of friends and recent players; anyone
            // else's arrives a moment later, or not at all.
            await Task.WhenAny(Task.WhenAll(entries.Select(e => e.User.RequestInfoAsync())), Task.Delay(1500));
            var list = entries.Take(count).Select(From).ToArray();
            if (scope == Scope.Friends)
                for (var i = 0; i < list.Length; i++) list[i].Place = i + 1;
            return list;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leaderboard fetch failed: {e.Message}");
            return null;
        }
    }

    // reached false: Steam couldn't be asked. entry null: no entry yet.
    public static async Task<(bool reached, Entry? entry)> FetchOwn(ulong steamId)
    {
        try
        {
            var found = await Board();
            if (!found.HasValue) return (false, null);
            var entries = await found.Value.GetScoresForUsersAsync(new SteamId[] { steamId });
            return (true, entries != null && entries.Length > 0 ? From(entries[0]) : (Entry?)null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leaderboard fetch failed: {e.Message}");
            return (false, null);
        }
    }

    // Steam only takes a player's own entry: nothing is sent for anyone else.
    public static async void Submit(ulong steamId, PlayerRating.Record record)
    {
        try
        {
            if (!SteamClient.IsValid || SteamClient.SteamId.Value != steamId) return;
            var found = await Board();
            if (!found.HasValue) return;
            var update = await found.Value.ReplaceScore(record.Rating, new[] { record.Games, record.Wins, record.Losses, record.Draws });
            if (!update.HasValue) Debug.LogWarning("Leaderboard upload failed; it's tried again next start.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leaderboard upload failed: {e.Message}");
        }
    }

    private static Entry From(LeaderboardEntry entry)
    {
        var details = entry.Details ?? Array.Empty<int>();
        int Detail(int i) => i < details.Length ? details[i] : 0;
        return new Entry
        {
            SteamId = entry.User.Id.Value,
            Name = entry.User.Name,
            Place = entry.GlobalRank,
            Record = new PlayerRating.Record { Rating = entry.Score, Games = Detail(0), Wins = Detail(1), Losses = Detail(2), Draws = Detail(3) },
        };
    }
}
