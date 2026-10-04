using System.Collections.Generic;
using System.Linq;
using Steamworks;

public enum AchievementId
{
    Practice,
    FirstWin,
    Double,
    Triple,
    Nongae,
    Flawless,
    Comeback,
    BeatHard,
    OnlineWin,
    RankedSeries,
    LastStanding,
    TeamWin,
    HealthWin,
    AllPieces,
    Kills100,
    Matches50,
    Streak5
}

// What there is to earn, each under its Steam API name (the Steamworks
// backend has to have the same names: HANDOFF lists them) with its strings
// (ach.<Id>.title, ach.<Id>.desc). Earned on this machine first
// (PlayerRecords); with Steam running it's set there too, and whatever was
// earned while Steam wasn't is set the next time it is (Sync). The counted
// ones show how far along they are.
public static class Achievements
{
    public readonly struct Def
    {
        public readonly AchievementId Id;
        public readonly string SteamName;
        public readonly int Goal; // a count to reach; 0, done once

        public Def(AchievementId id, string steamName, int goal = 0)
        {
            Id = id;
            SteamName = steamName;
            Goal = goal;
        }

        public string Title => Loc.Get("ach." + Id + ".title");
        public string Description => Loc.Get("ach." + Id + ".desc");
    }

    // In AchievementId's order.
    public static readonly Def[] All =
    {
        new Def(AchievementId.Practice, "ACH_PRACTICE"),
        new Def(AchievementId.FirstWin, "ACH_FIRST_WIN"),
        new Def(AchievementId.Double, "ACH_DOUBLE"),
        new Def(AchievementId.Triple, "ACH_TRIPLE"),
        new Def(AchievementId.Nongae, "ACH_NONGAE"),
        new Def(AchievementId.Flawless, "ACH_FLAWLESS"),
        new Def(AchievementId.Comeback, "ACH_COMEBACK"),
        new Def(AchievementId.BeatHard, "ACH_BEAT_HARD"),
        new Def(AchievementId.OnlineWin, "ACH_ONLINE_WIN"),
        new Def(AchievementId.RankedSeries, "ACH_RANKED_SERIES"),
        new Def(AchievementId.LastStanding, "ACH_LAST_STANDING"),
        new Def(AchievementId.TeamWin, "ACH_TEAM_WIN"),
        new Def(AchievementId.HealthWin, "ACH_HEALTH_WIN"),
        new Def(AchievementId.AllPieces, "ACH_ALL_PIECES", 4),
        new Def(AchievementId.Kills100, "ACH_KILLS_100", 100),
        new Def(AchievementId.Matches50, "ACH_MATCHES_50", 50),
        new Def(AchievementId.Streak5, "ACH_STREAK_5", 5),
    };

    public static Def Of(AchievementId id) => All[(int)id];

    // Every one earned here, set on Steam if it isn't yet. Without Steam,
    // or one the backend doesn't have, nothing happens.
    public static void Sync(IEnumerable<AchievementId> earned)
    {
        if (!SteamClient.IsValid) return;
        var names = new HashSet<string>(earned.Select(id => Of(id).SteamName));
        var changed = false;
        foreach (var achievement in SteamUserStats.Achievements)
            if (names.Contains(achievement.Identifier) && !achievement.State) changed |= achievement.Trigger(false);
        if (changed) SteamUserStats.StoreStats();
    }
}
