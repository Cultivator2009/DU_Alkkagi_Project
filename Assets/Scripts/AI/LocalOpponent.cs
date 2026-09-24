using UnityEngine;

public enum Opponent : byte
{
    Human, // two players at this screen
    AIEasy,
    AINormal,
    AIHard
}

// Who a local game's second side is, chosen on the local setup card and
// remembered for next time. Online games are always two people.
public static class LocalOpponent
{
    private const string PrefsKey = "local.opponent";

    public static Opponent Current
    {
        get => (Opponent)PlayerPrefs.GetInt(PrefsKey, (int)Opponent.Human);
        set => PlayerPrefs.SetInt(PrefsKey, (int)value);
    }

    public static bool IsAI => Current != Opponent.Human;

    public static AILevel Level => Current == Opponent.AIEasy ? AILevel.Easy : Current == Opponent.AIHard ? AILevel.Hard : AILevel.Normal;

    public static string Name => Loc.Get("opponent." + Current);
}
