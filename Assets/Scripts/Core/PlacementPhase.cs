using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public struct PlacedStone
{
    public char PieceId;
    public float X;
    public float Z;
}

// The placement phase as one player may see it (NetMessage.PlacementState).
public class PlacementSnapshot
{
    public int Placer;
    public bool Done;
    public float[] Clocks;
    public bool[] Ready;
    public List<PlacedStone> Stones;
}

// Before the first turn under SpawnMode.Placement, each side puts its own
// stones inside its zone. Rules and state only: BoardSetup draws it,
// PlacementInput feeds it. Each player has a clock that runs while they may
// place; when it runs out their remaining stones are placed at random and
// they're done. The authority (host, or the local game) runs the rules; a
// network guest keeps a mirror that NetworkMatchBridge overwrites with the
// host's PlacementState.
public class PlacementPhase
{
    public const int Everyone = -1;

    public PlacementStyle Style { get; }
    public bool IsAuthority { get; }
    // Everyone (both place at once), or the one player who may place now:
    // taking turns, or a local hot-seat game where one screen means one side
    // at a time.
    public int Placer { get; private set; }
    public bool Done { get; private set; }
    // Placed stones can be dragged again until their owner is ready, except
    // when taking turns: a stone put down stays put.
    public bool CanMoveStones => Style != PlacementStyle.Alternating;

    public event Action OnChanged;
    public event Action OnFinished;

    private readonly BoardSetup board;
    private readonly float[] clocks;
    private readonly bool[] ready;
    private readonly List<GamePieceManager>[] stones; // per player, in id order
    private readonly Dictionary<char, GamePieceManager> byId = new Dictionary<char, GamePieceManager>();
    private readonly Dictionary<char, Vector3> placed = new Dictionary<char, Vector3>();
    private readonly System.Random random = new System.Random();
    private bool finishRaised;

    public PlacementPhase(BoardSetup board, IEnumerable<GamePieceManager> pieces, int playerCount, MatchSettings settings, bool hotSeat, bool authority)
    {
        this.board = board;
        Style = settings.PlacementStyle;
        IsAuthority = authority;
        clocks = Enumerable.Repeat((float)settings.PlacementSeconds, playerCount).ToArray();
        ready = new bool[playerCount];
        stones = new List<GamePieceManager>[playerCount];
        for (var i = 0; i < playerCount; i++) stones[i] = new List<GamePieceManager>();
        foreach (var piece in pieces.OrderBy(p => p.pieceID))
        {
            stones[piece.playerIndex].Add(piece);
            byId[piece.pieceID] = piece;
        }
        // Black goes first whenever it's one side at a time.
        Placer = Style == PlacementStyle.Alternating || hotSeat ? 0 : Everyone;
    }

    public float Clock(int player) => clocks[player];
    public bool IsReady(int player) => ready[player];
    public int Unplaced(int player) => stones[player].Count(s => !placed.ContainsKey(s.pieceID));
    public bool TryGetPosition(char id, out Vector3 position) => placed.TryGetValue(id, out position);
    public bool CanAct(int player) => !Done && !ready[player] && (Placer == Everyone || Placer == player);

    public GamePieceManager NextUnplaced(int player) => stones[player].FirstOrDefault(s => !placed.ContainsKey(s.pieceID));

    // Its owner always sees a placed stone; the other side only once
    // placement is over when the style is Hidden.
    public bool IsVisibleTo(char id, int viewer)
    {
        return placed.ContainsKey(id) && (Done || Style != PlacementStyle.Hidden || byId[id].playerIndex == viewer);
    }

    // Puts a stone down, or moves one already placed.
    public bool TryPlace(int player, char id, Vector3 position)
    {
        if (!CanAct(player) || !byId.TryGetValue(id, out var stone) || stone.playerIndex != player) return false;
        var moving = placed.ContainsKey(id);
        if (moving && !CanMoveStones) return false;
        if (!board.InZone(player, position) || !board.IsClear(position, placed.Where(kv => kv.Key != id).Select(kv => kv.Value))) return false;

        placed[id] = board.ClampToZone(player, position);
        if (IsAuthority && Style == PlacementStyle.Alternating)
        {
            if (Unplaced(player) == 0) ready[player] = true;
            Advance();
        }
        Changed();
        return true;
    }

    // Done placing (both-at-once styles). Taking turns ends by itself when
    // the last stone is down.
    public bool TryReady(int player)
    {
        if (Style == PlacementStyle.Alternating || !CanAct(player) || Unplaced(player) > 0) return false;
        ready[player] = true;
        Advance();
        Changed();
        return true;
    }

    // Runs the clocks. On a guest this only keeps the countdown moving between
    // host updates; running out is the host's call.
    public void Tick(float deltaTime)
    {
        if (Done) return;
        for (var player = 0; player < clocks.Length; player++)
        {
            if (!CanAct(player)) continue;
            clocks[player] = Mathf.Max(0, clocks[player] - deltaTime);
            if (clocks[player] > 0 || !IsAuthority) continue;

            PlaceRemainingAtRandom(player);
            ready[player] = true;
            Advance();
            Changed();
        }
    }

    public PlacementSnapshot SnapshotFor(int viewer)
    {
        return new PlacementSnapshot
        {
            Placer = Placer,
            Done = Done,
            Clocks = (float[])clocks.Clone(),
            Ready = (bool[])ready.Clone(),
            Stones = placed.Where(kv => IsVisibleTo(kv.Key, viewer))
                .Select(kv => new PlacedStone { PieceId = kv.Key, X = kv.Value.x, Z = kv.Value.z })
                .ToList(),
        };
    }

    // Guest side: the host's word replaces whatever this mirror had.
    public void Apply(PlacementSnapshot snapshot)
    {
        Placer = snapshot.Placer;
        Done = snapshot.Done;
        Array.Copy(snapshot.Clocks, clocks, Mathf.Min(clocks.Length, snapshot.Clocks.Length));
        Array.Copy(snapshot.Ready, ready, Mathf.Min(ready.Length, snapshot.Ready.Length));
        placed.Clear();
        foreach (var stone in snapshot.Stones)
        {
            if (!byId.TryGetValue(stone.PieceId, out var piece)) continue;
            placed[stone.PieceId] = board.ClampToZone(piece.playerIndex, new Vector3(stone.X, 0, stone.Z));
        }
        Changed();
    }

    private void PlaceRemainingAtRandom(int player)
    {
        foreach (var stone in stones[player])
        {
            if (placed.ContainsKey(stone.pieceID)) continue;
            placed[stone.pieceID] = board.RandomFreePosition(player, placed.Values.ToList(), random);
        }
    }

    private void Advance()
    {
        if (ready.All(r => r))
        {
            Done = true;
            Placer = Everyone;
            return;
        }
        if (Placer == Everyone) return;

        // Taking turns: the next side still placing. Hot seat: the first one.
        var start = Style == PlacementStyle.Alternating ? Placer + 1 : 0;
        for (var i = 0; i < ready.Length; i++)
        {
            var candidate = (start + i) % ready.Length;
            if (ready[candidate]) continue;
            Placer = candidate;
            return;
        }
    }

    private void Changed()
    {
        OnChanged?.Invoke();
        if (!Done || finishRaised) return;
        finishRaised = true;
        OnFinished?.Invoke();
    }
}
