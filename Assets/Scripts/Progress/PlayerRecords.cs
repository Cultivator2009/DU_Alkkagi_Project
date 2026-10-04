using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// What a finished match tells the records: this screen's side (none in a
// hot-seat game) and how it went for it.
public struct MatchFacts
{
    public int? Own;
    public bool Won;           // its side, or its team
    public bool Lost;
    public bool Online;
    public bool SeriesWon;     // a ranked series, over now, taken
    public bool BeatHard;      // a hard AI among the others (a local game)
    public MatchEndReason Reason;
    public int Sides;
    public bool Teams;
    public GameVariant Variant;
    public PieceType Pieces;
    public int PiecesAtStart;  // its side's
    public int PiecesLeft;
    public int Kills;          // the others' pieces its shots knocked off (with 논개)
    public int Nongae;
    public int BestShot;       // the most of those in one shot
    public float Seconds;
}

// This player's own record on this machine (PlayerPrefs, as JSON) and what
// they've earned (Achievements). A finished match counts once (Record, from
// the HUD as the result comes up): as played whoever played it; won, lost
// or drawn, its knocks and its achievements only where this screen played
// one side (against the AI, or online). A match the host left counts for
// nothing; the practice match only for its own achievement (Earn).
[Serializable]
public class PlayerRecords
{
    private const string PrefsKey = "records";

    public int played;
    public int won;
    public int lost;
    public int drawn;
    public int aiWon;
    public int aiLost;
    public int onlineWon;
    public int onlineLost;
    public int knockedOff;
    public int bestShot;
    public int streak;
    public int bestStreak;
    public float seconds;
    public int piecesWonWith; // a bit per PieceType
    public List<string> earned = new List<string>(); // AchievementId names, as earned

    private static PlayerRecords current;

    public static PlayerRecords Current
    {
        get
        {
            if (current != null) return current;
            var json = PlayerPrefs.GetString(PrefsKey, null);
            current = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PlayerRecords>(json);
            current ??= new PlayerRecords();
            return current;
        }
    }

    public static event Action OnChanged;

    public bool Has(AchievementId id) => earned.Contains(id.ToString());

    public IEnumerable<AchievementId> Earned =>
        earned.Select(name => Enum.TryParse(name, out AchievementId id) ? id : (AchievementId?)null).Where(id => id.HasValue).Select(id => id.Value);

    // How far along a counted achievement is (Achievements.Def.Goal).
    public int Progress(AchievementId id) => id switch
    {
        AchievementId.AllPieces => Enum.GetValues(typeof(PieceType)).Cast<PieceType>().Count(type => (piecesWonWith & (1 << (int)type)) != 0),
        AchievementId.Kills100 => knockedOff,
        AchievementId.Matches50 => played,
        AchievementId.Streak5 => bestStreak,
        _ => Has(id) ? 1 : 0
    };

    // The achievements this match earned, in order.
    public static List<AchievementId> Record(MatchFacts match)
    {
        var records = Current;
        var newly = new List<AchievementId>();
        if (match.Reason == MatchEndReason.HostLeft) return newly;
        records.played++;
        records.seconds += match.Seconds;
        if (match.Own.HasValue) records.Count(match, newly);
        foreach (var def in Achievements.All)
            if (def.Goal > 0 && records.Progress(def.Id) >= def.Goal) records.Add(def.Id, newly);
        records.Save(newly);
        return newly;
    }

    // One earned outside a match's count (the practice match's): empty if
    // it already was.
    public static List<AchievementId> Earn(AchievementId id)
    {
        var newly = new List<AchievementId>();
        Current.Add(id, newly);
        Current.Save(newly);
        return newly;
    }

    private void Count(MatchFacts match, List<AchievementId> newly)
    {
        knockedOff += match.Kills;
        bestShot = Mathf.Max(bestShot, match.BestShot);
        if (match.Won)
        {
            won++;
            if (match.Online) onlineWon++;
            else aiWon++;
            streak++;
            bestStreak = Mathf.Max(bestStreak, streak);
            piecesWonWith |= 1 << (int)match.Pieces;
        }
        else if (match.Lost)
        {
            lost++;
            if (match.Online) onlineLost++;
            else aiLost++;
            streak = 0;
        }
        else drawn++;

        var onBoard = match.Reason == MatchEndReason.Knockout || match.Reason == MatchEndReason.BothOut;
        var duel = match.Sides == 2 && !match.Teams;
        if (match.Won) Add(AchievementId.FirstWin, newly);
        if (match.BestShot >= 2) Add(AchievementId.Double, newly);
        if (match.BestShot >= 3) Add(AchievementId.Triple, newly);
        if (match.Nongae > 0) Add(AchievementId.Nongae, newly);
        if (match.Won && onBoard && duel && match.PiecesLeft == match.PiecesAtStart) Add(AchievementId.Flawless, newly);
        if (match.Won && onBoard && duel && match.PiecesLeft == 1 && match.PiecesAtStart > 1) Add(AchievementId.Comeback, newly);
        if (match.Won && match.BeatHard) Add(AchievementId.BeatHard, newly);
        if (match.Won && match.Online) Add(AchievementId.OnlineWin, newly);
        if (match.SeriesWon) Add(AchievementId.RankedSeries, newly);
        if (match.Won && match.Sides > 2 && !match.Teams) Add(AchievementId.LastStanding, newly);
        if (match.Won && match.Teams) Add(AchievementId.TeamWin, newly);
        if (match.Won && match.Variant == GameVariant.Health) Add(AchievementId.HealthWin, newly);
    }

    private void Add(AchievementId id, List<AchievementId> newly)
    {
        if (Has(id)) return;
        earned.Add(id.ToString());
        newly.Add(id);
    }

    private void Save(List<AchievementId> newly)
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
        PlayerPrefs.Save();
        if (newly.Count > 0) Achievements.Sync(Earned);
        OnChanged?.Invoke();
    }
}
