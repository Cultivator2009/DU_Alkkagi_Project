using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// How a turn finished, for the HUD and the network guest.
public enum TurnEnd : byte
{
    None,    // no turn has finished yet (the opening turn), or a resync
    Shot,
    Timeout,
    Skipped,
    Collapse // not a side's turn: the board's edge gave way and what fell was counted (ZoneRule)
}

// The turns of a match: whose go it is, the clock, the shot playing out,
// and what the ruleset makes of it. On the authority (a local game, the
// network host) it runs the match; on a network guest it's a mirror that
// takes the host's MatchState over (ApplyRemote). Either way its events
// are the match as the screen should tell it - the HUD, the rating and
// the network host listen to them and nothing else.
public class TurnController
{
    public event Action<int> OnTurnStarted;                     // the side to move
    public event Action<int> OnShotEnded;                       // the side that shot, once everything has stopped
    public event Action<int, TurnEnd> OnTurnPassed;             // timed out or skipped, nothing moved
    public event Action<int, MatchEndReason> OnMatchEnded;      // winner -1: no one (a draw, or the host left)
    // A side is out while the match goes on without it (three or four
    // sides): knocked out, conceded or gone. Its turns are skipped.
    public event Action<int, MatchEndReason> OnPlayerOut;
    public event Action OnSidesChanged;                         // score, pieces, health
    public event Action OnZoneChanged;                          // the edge announced, or giving way (Zone)

    public GameManager.GameState State { get; private set; } = GameManager.GameState.Mainmenu;
    public bool HasStarted => State != GameManager.GameState.Mainmenu;
    // A network guest's copy of the host's turns: it never decides anything.
    public bool IsMirror { get; private set; }
    public int CurrentPlayerID { get; private set; }
    public int Turn { get; private set; }       // turns begun
    public int TurnsEnded { get; private set; } // shot, passed, or the edge giving way
    public int Round { get; private set; } = 1;  // every side still in has a turn in a round
    public ZoneRule Zone { get; }
    public int RoundLimit { get; }               // 0 = none: at its end the side with the most left wins
    // The board's edge is giving way and what stood on it falling; no one's turn.
    public bool Collapsing { get; private set; }
    // Passing is refused once the edge is coming in.
    public bool MayPass => Zone.AllowsPass;
    public PieceSelector PieceSelector => pieceSelector;
    public TurnEnd LastTurnEnd { get; private set; }
    public int LastPlayerId { get; private set; } = -1; // whose turn last finished
    public KillLog Kills { get; }
    public IReadOnlyList<Side> Sides => sides;
    // The side whose shot is playing out, or -1 (none, or the edge giving way).
    public int Shooter => State == GameManager.GameState.ProcessingTurn && !Collapsing ? CurrentPlayerID : -1;
    // The piece the shot playing out was flicked with ('\0' otherwise): a
    // battle of health spares it its knocks.
    public char ShotPieceId => Shooter >= 0 && selGamePiece != null ? selGamePiece.Manager.pieceID : '\0';

    // 0 = no turn timer. The clock runs while the side to move is choosing
    // and aiming, and stops once the stone is flicked.
    public int TurnSeconds { get; }
    public float TurnTimeRemaining { get; private set; }
    public bool IsAwaitingShot => State == GameManager.GameState.WaitingForInput || State == GameManager.GameState.WaitingForEndTurn;
    // The shot has played out long enough that it may be fast-forwarded (GamePace).
    public bool MayFastForward => !IsMirror && State == GameManager.GameState.ProcessingTurn && Time.unscaledTime - shotStartedAt >= GamePace.FastForwardAfter;

    // Online the host's clock also times the guests' turns, but a guest's
    // flick reaches the host a network trip after it was let go. A guest's
    // turn runs this long past zero before it's passed, so a flick released
    // just in time still counts. Set by NetworkMatchBridge on the host.
    public int HostPlayerId { get; set; } = -1;
    public float RemoteGraceSeconds { get; set; }

    // The edge gives way to this step: the board shrinks and whatever stood
    // on the lost ground is let fall (GameManager, BoardSetup.Crumble). On
    // a guest too, for the look of it.
    public Action<int> Crumble { get; set; }

    // The pull has been let go: the flick lands on the next physics step, or
    // already has on this frame's, but the state only catches up in
    // EndTurnReady. The shot is taken from here on - timing out now would
    // pass the turn while the stone flies, and online the guest would get
    // none of that movement (snapshots only go out while a shot is processed).
    private bool ShotReleased => State == GameManager.GameState.WaitingForEndTurn && selGamePiece != null
                                 && (selGamePiece.isOnfire || (!selGamePiece.isDragging && !selGamePiece.isCancelled));

    private readonly IRuleset ruleset;
    private readonly List<Side> sides;
    private readonly List<GamePieceDragAndReleaseForce> gamePieceScripts;
    private readonly PieceSelector pieceSelector;

    private GamePieceDragAndReleaseForce selGamePiece;
    private float shotStartedAt; // real time
    private List<Side> standingAtShot = new List<Side>();
    private bool resolved; // the result is in (not just stopped: Abort)
    private int outsThisRound;
    private float collapseStartedAt; // game time: a crumble plays out a moment before anything can be settled
    private const float CollapseMinSeconds = 0.4f;
    private int winner = -1;
    private MatchEndReason endReason;
    private int firstPlayer; // who opened the match: a round starts with them
    private readonly Func<int, int> teamOf; // MatchSettings.TeamOf: each side its own team, or two of two

    public TurnController(
        IRuleset ruleset,
        List<Side> sides,
        List<GamePieceDragAndReleaseForce> gamePieceScripts,
        PieceSelector pieceSelector,
        int turnSeconds,
        ZoneRule zone = null,
        int roundLimit = 0,
        Func<int, int> teamOf = null)
    {
        this.teamOf = teamOf ?? (id => id);
        Zone = zone ?? new ZoneRule();
        RoundLimit = roundLimit;
        this.ruleset = ruleset;
        this.sides = sides;
        this.gamePieceScripts = gamePieceScripts;
        this.pieceSelector = pieceSelector;
        TurnSeconds = turnSeconds;
        Kills = new KillLog(sides.Count, this.teamOf);
    }

    // first: who moves first (black, unless a ranked series says otherwise).
    public void StartMatch(int first = 0)
    {
        LastTurnEnd = TurnEnd.None;
        firstPlayer = first;
        BeginTurn(first);
    }

    public void Tick()
    {
        if (TurnSeconds > 0 && IsAwaitingShot && !ShotReleased)
        {
            TurnTimeRemaining = Mathf.Max(IsMirror ? 0 : float.MinValue, TurnTimeRemaining - GamePace.ClockDelta);
            var grace = HostPlayerId >= 0 && CurrentPlayerID != HostPlayerId ? RemoteGraceSeconds : 0;
            if (!IsMirror && TurnTimeRemaining <= -grace)
            {
                PassTurn(TurnEnd.Timeout);
                return;
            }
        }
        if (IsMirror) return; // the host's MatchState moves it on

        switch (State)
        {
            case GameManager.GameState.WaitingForInput:
                InputReady();
                break;
            case GameManager.GameState.WaitingForEndTurn:
                EndTurnReady();
                break;
            case GameManager.GameState.ProcessingTurn:
                TurnProcess();
                break;
        }
    }

    // A flick that didn't come from local input - the network guest's
    // Flick, applied on the host, or the AI's. It goes through the same turn
    // flow as a local flick. Only accepted for the side to move, while this
    // controller is waiting for input.
    public bool TryApplyExternalFlick(GamePieceDragAndReleaseForce piece, Vector3 force)
    {
        if (IsMirror || State != GameManager.GameState.WaitingForInput || piece == null) return false;
        var pieceManager = piece.Manager;
        if (pieceManager.playerIndex != CurrentPlayerID) return false;

        if (selGamePiece != null) selGamePiece.isCancelled = false;
        selGamePiece = piece;
        BeginShot(pieceManager);
        piece.ApplyFlick(force);
        State = GameManager.GameState.ProcessingTurn;
        return true;
    }

    // The skip-turn button (or the guest's Pass on the host). Only for the
    // side to move, before its stone is flicked.
    public bool TryPassTurn(int playerId)
    {
        if (IsMirror || !IsAwaitingShot || ShotReleased || playerId != CurrentPlayerID || !MayPass) return false;
        PassTurn(TurnEnd.Skipped);
        return true;
    }

    // playerId gives up during the turns (the in-game menu, or a guest's
    // Concede on the host).
    public void Concede(int playerId) => Forfeit(playerId, MatchEndReason.Surrender);

    // playerId is out for good: conceded, or gone from an online match. Its
    // pieces stay on the board. With one side still standing, that side wins
    // (two sides: the other one, straight away); otherwise the match goes on
    // and skips it.
    public void Forfeit(int playerId, MatchEndReason reason)
    {
        var side = Find(playerId);
        if (IsMirror || State == GameManager.GameState.MatchOver || side == null || !side.Standing) return;
        side.Out = reason;
        var standing = sides.Where(s => s.Standing).ToList();
        OnSidesChanged?.Invoke();
        if (standing.Select(s => teamOf(s.Id)).Distinct().Count() <= 1)
        {
            EndMatch(standing.Count >= 1 ? standing[0].Id : -1, reason);
            return;
        }
        OnPlayerOut?.Invoke(playerId, reason);
        if (playerId == CurrentPlayerID && IsAwaitingShot && !ShotReleased) PassTurn(TurnEnd.Skipped);
    }

    // Still in the match: not knocked out, conceded or gone.
    public bool IsStanding(int playerId) => Find(playerId)?.Standing ?? false;

    // Stops the match without a ruleset result, e.g. when the online
    // opponent leaves. Whoever called it reports the outcome.
    public void Abort()
    {
        State = GameManager.GameState.MatchOver;
    }

    // A piece is out of the match: into the death trigger, or broken by
    // its knocks (GameManager).
    public void PieceOut(GamePieceManager piece)
    {
        Kills.PieceRemoved(piece.pieceID, piece.playerIndex);
        outsThisRound++;
        ruleset.OnPieceOut(piece, sides, Shooter);
    }

    // The match as the host sends it (MatchState).
    public MatchState Snapshot()
    {
        return new MatchState
        {
            Turn = Turn,
            TurnsEnded = TurnsEnded,
            CurrentPlayer = CurrentPlayerID,
            LastTurnEnd = LastTurnEnd,
            LastPlayer = LastPlayerId,
            Over = resolved,
            Winner = winner,
            Reason = endReason,
            Round = Round,
            QuietRounds = Zone.QuietRounds,
            ZoneStage = Zone.Stage,
            ZoneWarned = Zone.Warned,
            Collapsing = Collapsing,
            Sides = sides.Select(s =>
            {
                var copy = new Side(s.Id);
                copy.CopyFrom(s);
                return copy;
            }).ToList(),
        };
    }

    // ---- Mirror (network guest) ----

    public void BecomeMirror() => IsMirror = true;

    // The host's word on the match, with the kill feed of the shot that
    // ended the turn before. What changed is told through the same events
    // the host's own controller raised, in the same order.
    public void ApplyRemote(MatchState state, IReadOnlyList<KillEvent> kills)
    {
        if (!IsMirror || State == GameManager.GameState.MatchOver) return;
        var newTurn = state.Turn != Turn;
        var turnEnded = state.TurnsEnded != TurnsEnded;
        TurnsEnded = state.TurnsEnded;
        LastTurnEnd = state.LastTurnEnd;
        LastPlayerId = state.LastPlayer;
        if (turnEnded && state.LastTurnEnd == TurnEnd.Shot)
        {
            Kills.Record(kills);
            OnShotEnded?.Invoke(state.LastPlayer);
        }
        else if (turnEnded && state.LastTurnEnd == TurnEnd.Collapse) Kills.Record(kills);
        else if (turnEnded) OnTurnPassed?.Invoke(state.LastPlayer, state.LastTurnEnd);

        Round = state.Round;
        Zone.QuietRounds = state.QuietRounds;
        Collapsing = state.Collapsing;
        if (state.ZoneStage != Zone.Stage || state.ZoneWarned != Zone.Warned)
        {
            var crumbled = state.ZoneStage != Zone.Stage;
            Zone.Stage = state.ZoneStage;
            Zone.Warned = state.ZoneWarned;
            if (crumbled) Crumble?.Invoke(Zone.Stage);
            OnZoneChanged?.Invoke();
        }

        var newlyOut = new List<Side>();
        foreach (var remote in state.Sides)
        {
            var side = Find(remote.Id);
            if (side == null) continue;
            if (side.Standing && !remote.Standing) newlyOut.Add(remote);
            side.CopyFrom(remote);
        }
        OnSidesChanged?.Invoke();

        Turn = state.Turn;
        CurrentPlayerID = state.CurrentPlayer;
        if (state.Over)
        {
            // Two sides: a side's end is the match's, not a PlayerOut.
            resolved = true;
            winner = state.Winner;
            endReason = state.Reason;
            State = GameManager.GameState.MatchOver;
            OnMatchEnded?.Invoke(state.Winner, state.Reason);
            return;
        }
        foreach (var side in newlyOut) OnPlayerOut?.Invoke(side.Id, side.Out.Value);
        if (Collapsing) State = GameManager.GameState.ProcessingTurn;
        if (!newTurn) return;
        TurnTimeRemaining = TurnSeconds;
        State = GameManager.GameState.WaitingForInput;
        OnTurnStarted?.Invoke(CurrentPlayerID);
    }

    // The host left: a guest's match ends where it is, the guest deciding
    // the result it can (NetworkMatchBridge).
    public void EndWithoutHost(int winnerId, MatchEndReason reason)
    {
        if (!IsMirror || resolved) return;
        EndMatch(winnerId, reason);
    }

    // A shot is playing out on the host (this guest's own, sent, or a
    // snapshot of anyone's): the clock stops until the next turn.
    public void MirrorShotUnderway()
    {
        if (IsMirror && IsAwaitingShot) State = GameManager.GameState.ProcessingTurn;
    }

    // ---- Authority ----

    private void InputReady()
    {
        var picked = pieceSelector.TrySelect(CurrentPlayerID);
        if (picked == null) return;

        if (selGamePiece != null) selGamePiece.isCancelled = false;
        selGamePiece = picked;
        selGamePiece.isDragging = true;
        State = GameManager.GameState.WaitingForEndTurn;
    }

    private void EndTurnReady()
    {
        if (selGamePiece.isCancelled)
        {
            State = GameManager.GameState.WaitingForInput;
        }
        else if (!selGamePiece.isDragging)
        {
            BeginShot(selGamePiece.Manager);
            State = GameManager.GameState.ProcessingTurn;
        }
    }

    private void BeginShot(GamePieceManager piece)
    {
        shotStartedAt = Time.unscaledTime;
        ruleset.OnBeforeFlick(piece);
        Kills.BeginShot(CurrentPlayerID, piece.pieceID);
        standingAtShot = sides.Where(s => s.Standing).ToList();
    }

    private void TurnProcess()
    {
        if (Collapsing && Time.time - collapseStartedAt < CollapseMinSeconds) return;
        foreach (var piece in gamePieceScripts)
            if (!piece.IsSettled) return;
        if (Collapsing) EndCollapse();
        else if (!selGamePiece.isCancelled && !selGamePiece.isDragging) EndTurn();
    }

    private void EndTurn()
    {
        var shooter = CurrentPlayerID;
        TurnsEnded++;
        LastTurnEnd = TurnEnd.Shot;
        LastPlayerId = shooter;
        sides[shooter].Shots++;
        Kills.EndShot();

        // Who this shot put out.
        var knockedOut = standingAtShot.Where(s => s.Standing && ruleset.IsKnockedOut(s)).ToList();
        foreach (var side in knockedOut) side.Out = MatchEndReason.Knockout;
        OnShotEnded?.Invoke(shooter);
        OnSidesChanged?.Invoke();

        var standing = sides.Where(s => s.Standing).ToList();
        if (ruleset.TryGetMatchWinner(standingAtShot, standing, shooter, out var matchWinner, out var reason))
        {
            EndMatch(matchWinner != null ? matchWinner.Id : -1, reason);
            return;
        }
        foreach (var side in knockedOut) OnPlayerOut?.Invoke(side.Id, MatchEndReason.Knockout);

        if (!TryCrumble()) BeginTurn(NextPlayerIndex());
    }

    // The turn that the edge was announced for is over: it gives way now.
    // Whatever stood on the lost ground falls as if on no one's shot.
    private bool TryCrumble()
    {
        if (!Zone.TakeDue()) return false;
        Collapsing = true;
        collapseStartedAt = Time.time;
        shotStartedAt = Time.unscaledTime;
        selGamePiece = null;
        Kills.BeginShot(-1, '\0');
        standingAtShot = sides.Where(s => s.Standing).ToList();
        State = GameManager.GameState.ProcessingTurn;
        Crumble?.Invoke(Zone.Stage);
        OnZoneChanged?.Invoke();
        return true;
    }

    // Everything has come to rest on what's left of the board.
    private void EndCollapse()
    {
        Collapsing = false;
        TurnsEnded++;
        LastTurnEnd = TurnEnd.Collapse;
        LastPlayerId = -1;
        Kills.EndShot();

        var knockedOut = standingAtShot.Where(s => s.Standing && ruleset.IsKnockedOut(s)).ToList();
        foreach (var side in knockedOut) side.Out = MatchEndReason.Knockout;
        OnSidesChanged?.Invoke();

        var standing = sides.Where(s => s.Standing).ToList();
        if (ruleset.TryGetMatchWinner(standingAtShot, standing, -1, out var matchWinner, out var reason))
        {
            EndMatch(matchWinner != null ? matchWinner.Id : -1, reason);
            return;
        }
        foreach (var side in knockedOut) OnPlayerOut?.Invoke(side.Id, MatchEndReason.Knockout);
        BeginTurn(NextPlayerIndex());
    }

    private void EndMatch(int winnerId, MatchEndReason reason)
    {
        resolved = true;
        winner = winnerId;
        endReason = reason;
        State = GameManager.GameState.MatchOver;
        OnMatchEnded?.Invoke(winnerId, reason);
    }

    // Nothing moved, so there's no winner to check: straight to the next side.
    private void PassTurn(TurnEnd why)
    {
        if (selGamePiece != null)
        {
            // Drop a drag in progress so the release can't fire a shot.
            selGamePiece.isDragging = false;
            selGamePiece.isSelected = false;
            selGamePiece.isCancelled = false;
        }
        var passer = CurrentPlayerID;
        TurnsEnded++;
        LastTurnEnd = why;
        LastPlayerId = passer;
        OnTurnPassed?.Invoke(passer, why);
        if (!TryCrumble()) BeginTurn(NextPlayerIndex());
    }

    // The next side round that is still standing.
    private int NextPlayerIndex()
    {
        for (var step = 1; step <= sides.Count; step++)
        {
            var index = (CurrentPlayerID + step) % sides.Count;
            if (sides[index].Standing) return index;
        }
        return (CurrentPlayerID + 1) % sides.Count;
    }

    private void BeginTurn(int playerIndex)
    {
        // Round again to the front: a round is over.
        if (Turn > 0 && InRound(playerIndex) <= InRound(CurrentPlayerID) && EndRound()) return;
        selGamePiece = null;
        CurrentPlayerID = sides[playerIndex].Id;
        Turn++;
        TurnTimeRemaining = TurnSeconds;
        State = GameManager.GameState.WaitingForInput;
        OnTurnStarted?.Invoke(CurrentPlayerID);
    }

    // True if the round limit ended the match: the side (or team) with the
    // most left (health in a battle of health, pieces otherwise) wins, a
    // tie is a draw.
    private bool EndRound()
    {
        Zone.EndRound(outsThisRound);
        outsThisRound = 0;
        if (RoundLimit > 0 && Round >= RoundLimit)
        {
            var teams = sides.Where(s => s.Standing).GroupBy(s => teamOf(s.Id))
                .Select(team => (first: team.First().Id, left: team.Sum(s => s.HasHealth ? s.Health : s.Pieces))).ToList();
            var most = teams.Max(t => t.left);
            var leaders = teams.Where(t => t.left == most).ToList();
            EndMatch(leaders.Count == 1 ? leaders[0].first : -1, MatchEndReason.RoundLimit);
            return true;
        }
        Round++;
        if (Zone.Warned) OnZoneChanged?.Invoke();
        return false;
    }

    // A side's place in a round, the opener first.
    private int InRound(int playerId) => (playerId - firstPlayer + sides.Count) % sides.Count;

    private Side Find(int playerId) => playerId >= 0 && playerId < sides.Count ? sides[playerId] : null;
}
