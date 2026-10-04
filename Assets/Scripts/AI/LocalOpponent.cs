using UnityEngine;

public enum Opponent : byte
{
    Human, // someone at this screen
    AIEasy,
    AINormal,
    AIHard
}

// Who plays each side of a local game - someone at this screen, or the AI
// at a level - chosen seat by seat on the local setup card and remembered
// for next time. A local game of two to four (MatchSettings.Seats); online
// every seat is a player or the host's bot (MatchRoster).
public static class LocalOpponent
{
    private const string PrefsKey = "local.seat";
    private const string LegacyKey = "local.opponent"; // white's, from before seats

    // Seats for one match only, not saved (the practice match); null: as picked.
    public static Opponent[] Override { get; set; }

    public static Opponent Of(int seat) => Override != null && seat < Override.Length ? Override[seat] : (Opponent)PlayerPrefs.GetInt(PrefsKey + seat, (int)Default(seat));

    public static void Set(int seat, Opponent who) => PlayerPrefs.SetInt(PrefsKey + seat, (int)who);

    public static bool IsAI(int seat) => Of(seat) != Opponent.Human;

    public static AILevel LevelOf(int seat) => Level(Of(seat));

    public static AILevel Level(Opponent who) => who == Opponent.AIEasy ? AILevel.Easy : who == Opponent.AIHard ? AILevel.Hard : AILevel.Normal;

    public static string Name(int seat) => Loc.Get("opponent." + Of(seat));

    // Black at this screen; white as it was picked before there were seats;
    // the third and fourth the AI.
    private static Opponent Default(int seat) =>
        seat == 0 ? Opponent.Human : seat == 1 ? (Opponent)PlayerPrefs.GetInt(LegacyKey, (int)Opponent.Human) : Opponent.AINormal;
}
