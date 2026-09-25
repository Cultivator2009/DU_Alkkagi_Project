using System.Collections.Generic;
using System.IO;
using System.Linq;

// Who plays an online match, in seat order: the host is player 0, the others
// follow in the order the host listed them. It travels with LoadGameScene, so
// every machine spawns the same sides and knows which is whose. Null for a
// local game, which is always two sides at one screen. Each seat also
// carries the player's rating as the host's lobby showed it, so every
// machine rates the match from the same numbers.
public sealed class MatchRoster
{
    public const int MaxPlayers = 4;

    public static MatchRoster Current { get; set; }

    public IReadOnlyList<ulong> SteamIds => steamIds;
    public IReadOnlyList<int> Ratings => ratings;
    public int Count => steamIds.Count;

    private readonly List<ulong> steamIds;
    private readonly List<int> ratings;

    // ratings: by seat; without them everyone is at the starting rating.
    public MatchRoster(IEnumerable<ulong> steamIds, IEnumerable<int> ratings = null)
    {
        this.steamIds = new List<ulong>(steamIds);
        this.ratings = ratings != null ? new List<int>(ratings) : this.steamIds.Select(_ => Elo.Start).ToList();
    }

    // -1 when that player isn't in this match.
    public int PlayerOf(ulong steamId) => steamIds.IndexOf(steamId);

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)steamIds.Count);
        for (var i = 0; i < steamIds.Count; i++)
        {
            writer.Write(steamIds[i]);
            writer.Write(ratings[i]);
        }
    }

    public static MatchRoster Read(BinaryReader reader)
    {
        var count = reader.ReadByte();
        var ids = new List<ulong>(count);
        var ratings = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            ids.Add(reader.ReadUInt64());
            ratings.Add(reader.ReadInt32());
        }
        return new MatchRoster(ids, ratings);
    }
}
