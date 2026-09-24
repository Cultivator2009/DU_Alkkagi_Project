using System;
using System.Collections.Generic;
using System.Linq;

public enum KillKind : byte
{
    Kill,     // an opponent's piece knocked out
    Nongae,   // a kill on a shot whose own piece went out too (논개)
    Suicide,  // the shot piece went out and took no opponent with it
    TeamKill  // the shot knocked out another of the shooter's own pieces
}

// One piece knocked out, as the kill feed shows it.
public struct KillEvent
{
    public int ShooterId;
    public char ShotPieceId;
    public char VictimId;
    public int VictimOwnerId;
    public KillKind Kind;
}

// Who knocked out what. A piece only ever goes out during a flick, and the
// player who flicked gets it: a kill for an opponent's piece, a team kill
// for one of their own, a suicide when the shot piece itself goes out alone.
// When the shot piece goes out alongside an opponent's, those kills are 논개
// and the shot piece gets no line of its own. Resolved when the shot has
// settled: the shot piece can go out before or after what it hit.
//
// The authority (host, local game) resolves shots; a network guest records
// the host's events as they arrive with each TurnResult.
public class KillLog
{
    public event Action<IReadOnlyList<KillEvent>> OnShotResolved;

    public IReadOnlyList<KillEvent> LastShot { get; private set; } = Array.Empty<KillEvent>();

    private readonly int[] kills;
    private readonly int[] nongae; // shots, not pieces: one per shot that traded its own piece
    private readonly int[] suicides;
    private readonly int[] teamKills;
    private readonly List<(char id, int owner)> removed = new List<(char, int)>();
    private int shooterId;
    private char shotPieceId;

    public KillLog(int playerCount)
    {
        kills = new int[playerCount];
        nongae = new int[playerCount];
        suicides = new int[playerCount];
        teamKills = new int[playerCount];
    }

    // Kills include the 논개 ones.
    public int Kills(int playerId) => kills[playerId];
    public int Nongae(int playerId) => nongae[playerId];
    public int Suicides(int playerId) => suicides[playerId];
    public int TeamKills(int playerId) => teamKills[playerId];

    public void BeginShot(int shooter, char shotPiece)
    {
        shooterId = shooter;
        shotPieceId = shotPiece;
    }

    public void PieceRemoved(char pieceId, int ownerId) => removed.Add((pieceId, ownerId));

    public void EndShot()
    {
        var tradedShotPiece = removed.Any(r => r.id == shotPieceId) && removed.Any(r => r.owner != shooterId);
        var events = new List<KillEvent>();
        foreach (var (id, owner) in removed)
        {
            KillKind kind;
            if (owner != shooterId) kind = tradedShotPiece ? KillKind.Nongae : KillKind.Kill;
            else if (id != shotPieceId) kind = KillKind.TeamKill;
            else if (tradedShotPiece) continue; // told by its 논개 kills
            else kind = KillKind.Suicide;
            events.Add(new KillEvent { ShooterId = shooterId, ShotPieceId = shotPieceId, VictimId = id, VictimOwnerId = owner, Kind = kind });
        }
        removed.Clear();
        Record(events);
    }

    // Counts a resolved shot's events and tells the feed. EndShot's own
    // result, or the host's on a network guest.
    public void Record(IReadOnlyList<KillEvent> events)
    {
        LastShot = events;
        if (events.Count == 0) return;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case KillKind.Kill:
                case KillKind.Nongae:
                    kills[e.ShooterId]++;
                    break;
                case KillKind.Suicide:
                    suicides[e.ShooterId]++;
                    break;
                case KillKind.TeamKill:
                    teamKills[e.ShooterId]++;
                    break;
            }
        }
        if (events.Any(e => e.Kind == KillKind.Nongae)) nongae[events[0].ShooterId]++;
        OnShotResolved?.Invoke(events);
    }
}
