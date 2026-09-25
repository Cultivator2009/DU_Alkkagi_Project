using System.Collections.Generic;
using System.IO;

// Who plays an online match, in seat order: the host is player 0, the others
// follow in the order the host listed them. It travels with LoadGameScene, so
// every machine spawns the same sides and knows which is whose. Null for a
// local game, which is always two sides at one screen.
public sealed class MatchRoster
{
    public const int MaxPlayers = 4;

    public static MatchRoster Current { get; set; }

    public IReadOnlyList<ulong> SteamIds => steamIds;
    public int Count => steamIds.Count;

    private readonly List<ulong> steamIds;

    public MatchRoster(IEnumerable<ulong> steamIds)
    {
        this.steamIds = new List<ulong>(steamIds);
    }

    // -1 when that player isn't in this match.
    public int PlayerOf(ulong steamId) => steamIds.IndexOf(steamId);

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)steamIds.Count);
        foreach (var id in steamIds) writer.Write(id);
    }

    public static MatchRoster Read(BinaryReader reader)
    {
        var count = reader.ReadByte();
        var ids = new List<ulong>(count);
        for (var i = 0; i < count; i++) ids.Add(reader.ReadUInt64());
        return new MatchRoster(ids);
    }
}
