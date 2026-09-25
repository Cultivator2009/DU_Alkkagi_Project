using System;
using UnityEngine;

// This player's own rating and record: kept in PlayerPrefs per Steam
// account, and mirrored to the Steam leaderboard (RankBoard), which is what
// everyone else sees. Only a player's own game writes their entry, so after
// a rated match each machine updates just its own.
//
// A rated match counts from its first moment: until the result, what it
// would cost to walk out is held here and applied the next time the game
// gets back to the menus - or starts, after a quit or a crash.
public static class PlayerRating
{
    public struct Record
    {
        public int Rating, Games, Wins, Losses, Draws;
    }

    private const string Prefix = "rank.";

    public static event Action OnChanged;

    public static ulong Owner { get; private set; } // 0 until Steam is up
    public static Record Current { get; private set; } = new Record { Rating = Elo.Start };

    private static string Key(string field) => $"{Prefix}{Owner}.{field}";

    // Once Steam is up. The leaderboard is compared once it answers: a record
    // played further on another machine wins, one this machine couldn't
    // upload goes up again.
    public static void Load(ulong steamId)
    {
        Owner = steamId;
        Current = new Record
        {
            Rating = PlayerPrefs.GetInt(Key("rating"), Elo.Start),
            Games = PlayerPrefs.GetInt(Key("games"), 0),
            Wins = PlayerPrefs.GetInt(Key("wins"), 0),
            Losses = PlayerPrefs.GetInt(Key("losses"), 0),
            Draws = PlayerPrefs.GetInt(Key("draws"), 0),
        };
        SettlePending();
        OnChanged?.Invoke();
        Sync();
    }

    // What the match in progress counts as if this player leaves it now.
    public static void Hold(int change, RatedResult result)
    {
        if (Owner == 0) return;
        PlayerPrefs.SetString(Key("pending"), $"{change},{(int)result}");
        PlayerPrefs.Save(); // a crash wouldn't flush it
    }

    // The match's result, in place of what was held.
    public static void Apply(int change, RatedResult result)
    {
        if (Owner == 0) return;
        PlayerPrefs.DeleteKey(Key("pending"));
        var record = Current;
        record.Rating = Mathf.Max(0, record.Rating + change);
        record.Games++;
        if (result == RatedResult.Win) record.Wins++;
        else if (result == RatedResult.Loss) record.Losses++;
        else if (result == RatedResult.Draw) record.Draws++;
        Set(record);
        RankBoard.Submit(Owner, record);
    }

    // A match left before its result counts as it was held.
    public static void SettlePending()
    {
        if (Owner == 0) return;
        var held = PlayerPrefs.GetString(Key("pending"), string.Empty).Split(',');
        if (held.Length == 2 && int.TryParse(held[0], out var change) && byte.TryParse(held[1], out var result))
            Apply(change, (RatedResult)result);
        else PlayerPrefs.DeleteKey(Key("pending"));
    }

    private static async void Sync()
    {
        var owner = Owner;
        var (reached, remote) = await RankBoard.FetchOwn(owner);
        if (!reached || owner != Owner) return;
        if (remote.HasValue && remote.Value.Record.Games > Current.Games) Set(remote.Value.Record);
        else if (Current.Games > (remote?.Record.Games ?? 0)) RankBoard.Submit(owner, Current);
    }

    private static void Set(Record record)
    {
        Current = record;
        PlayerPrefs.SetInt(Key("rating"), record.Rating);
        PlayerPrefs.SetInt(Key("games"), record.Games);
        PlayerPrefs.SetInt(Key("wins"), record.Wins);
        PlayerPrefs.SetInt(Key("losses"), record.Losses);
        PlayerPrefs.SetInt(Key("draws"), record.Draws);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }
}
