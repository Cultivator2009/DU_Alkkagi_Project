// Running score across rematches against the same opponent: one online
// lobby, or one local session started from the main menu. Lives only for
// the app session - nothing is saved.
public static class MatchSeries
{
    private static readonly int[] wins = new int[2];

    public static string Key { get; private set; }
    public static int Draws { get; private set; }
    public static int Played => wins[0] + wins[1] + Draws;

    public static int Wins(int playerId) => wins[playerId];

    // Keeps counting while the key is unchanged, starts over when it differs.
    public static void Begin(string key)
    {
        if (key == Key) return;
        Key = key;
        Clear();
    }

    public static void Reset()
    {
        Key = null;
        Clear();
    }

    // winnerId -1 records a draw.
    public static void Record(int winnerId)
    {
        if (winnerId == 0 || winnerId == 1) wins[winnerId]++;
        else if (winnerId == -1) Draws++;
    }

    private static void Clear()
    {
        wins[0] = wins[1] = 0;
        Draws = 0;
    }
}
