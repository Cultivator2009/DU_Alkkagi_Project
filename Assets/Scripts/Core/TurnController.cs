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
    Skipped
}

public class TurnController
{
    public event Action<PlayersManager> OnTurnStarted;
    public event Action<PlayersManager> OnTurnEnded;            // after a shot
    public event Action<PlayersManager, TurnEnd> OnTurnPassed;  // timed out or skipped, nothing moved
    public event Action<PlayersManager, MatchEndReason> OnMatchEnded; // winner null = draw
    // A side is out while the match goes on without it (three or four
    // sides): knocked out, conceded or gone. Its turns are skipped.
    public event Action<int, MatchEndReason> OnPlayerOut;

    public GameManager.GameState State { get; private set; } = GameManager.GameState.Mainmenu;
    public bool HasStarted => State != GameManager.GameState.Mainmenu;
    public int CurrentPlayerID { get; private set; }
    public PieceSelector PieceSelector => pieceSelector;
    public TurnEnd LastTurnEnd { get; private set; }
    public int LastPlayerId { get; private set; } = -1; // whose turn last finished
    public KillLog Kills { get; }

    // 0 = no turn timer. The clock runs while the side to move is choosing
    // and aiming, and stops once the stone is flicked.
    public int TurnSeconds { get; }
    public float TurnTimeRemaining { get; private set; }
    public bool IsAwaitingShot => State == GameManager.GameState.WaitingForInput || State == GameManager.GameState.WaitingForEndTurn;

    // Online the host's clock also times the guests' turns, but a guest's
    // flick reaches the host a network trip after it was let go. A guest's
    // turn runs this long past zero before it's passed, so a flick released
    // just in time still counts. Set by NetworkMatchBridge on the host.
    public int HostPlayerId { get; set; } = -1;
    public float RemoteGraceSeconds { get; set; }

    // The pull has been let go: the flick lands on the next physics step, or
    // already has on this frame's, but the state only catches up in
    // EndTurnReady. The shot is taken from here on - timing out now would
    // pass the turn while the stone flies, and online the guest would get
    // none of that movement (snapshots only go out while a shot is processed).
    private bool ShotReleased => State == GameManager.GameState.WaitingForEndTurn && selGamePiece != null
                                 && (selGamePiece.isOnfire || (!selGamePiece.isDragging && !selGamePiece.isCancelled));

    private readonly IRuleset ruleset;
    private readonly List<PlayersManager> players;
    private readonly List<GamePieceDragAndReleaseForce> gamePieceScripts;
    private readonly PieceSelector pieceSelector;

    private GamePieceDragAndReleaseForce selGamePiece;
    private readonly HashSet<int> forfeited = new HashSet<int>(); // conceded or gone
    private List<PlayersManager> standingAtShot = new List<PlayersManager>();

    public TurnController(
        IRuleset ruleset,
        List<PlayersManager> players,
        List<GamePieceDragAndReleaseForce> gamePieceScripts,
        PieceSelector pieceSelector,
        int turnSeconds)
    {
        this.ruleset = ruleset;
        this.players = players;
        this.gamePieceScripts = gamePieceScripts;
        this.pieceSelector = pieceSelector;
        TurnSeconds = turnSeconds;
        Kills = new KillLog(players.Count);
    }

    public void StartMatch()
    {
        LastTurnEnd = TurnEnd.None;
        BeginTurn(0);
    }

    public void Tick()
    {
        if (TurnSeconds > 0 && IsAwaitingShot && !ShotReleased)
        {
            TurnTimeRemaining -= GamePace.ClockDelta;
            var grace = HostPlayerId >= 0 && CurrentPlayerID != HostPlayerId ? RemoteGraceSeconds : 0;
            if (TurnTimeRemaining <= -grace)
            {
                PassTurn(TurnEnd.Timeout);
                return;
            }
        }

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
    // FlickCommand, applied on the host. It goes through the same turn flow
    // as a local flick: before this existed the host simulated the guest's
    // shot but its turn state never left WaitingForInput, so the guest's
    // turn could never end. Only accepted for the side to move, while this
    // controller is waiting for input.
    public bool TryApplyExternalFlick(GamePieceDragAndReleaseForce piece, Vector3 force)
    {
        if (State != GameManager.GameState.WaitingForInput || piece == null) return false;
        var pieceManager = piece.GetComponent<GamePieceManager>();
        if (pieceManager.playerIndex != CurrentPlayerID) return false;

        if (selGamePiece != null) selGamePiece.isCancelled = false;
        selGamePiece = piece;
        BeginShot(pieceManager);
        piece.ApplyFlick(force);
        State = GameManager.GameState.ProcessingTurn;
        return true;
    }

    // The skip-turn button (or the guest's PassCommand on the host). Only for
    // the side to move, before its stone is flicked.
    public bool TryPassTurn(int playerId)
    {
        if (!IsAwaitingShot || ShotReleased || playerId != CurrentPlayerID) return false;
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
        if (State == GameManager.GameState.MatchOver || !forfeited.Add(playerId)) return;
        var standing = players.Where(IsStanding).ToList();
        if (standing.Count <= 1)
        {
            State = GameManager.GameState.MatchOver;
            OnMatchEnded?.Invoke(standing.FirstOrDefault(), reason);
            return;
        }
        OnPlayerOut?.Invoke(playerId, reason);
        if (playerId == CurrentPlayerID && IsAwaitingShot && !ShotReleased) PassTurn(TurnEnd.Skipped);
    }

    // Still in the match: pieces on the board, and not conceded or gone.
    public bool IsStanding(int playerId) => IsStanding(players.Find(p => p.ID == playerId));
    private bool IsStanding(PlayersManager player) => player != null && player.totalPieceCnt > 0 && !forfeited.Contains(player.ID);

    // Stops the match without a ruleset result, e.g. when the online
    // opponent leaves. Whoever called it reports the outcome.
    public void Abort()
    {
        State = GameManager.GameState.MatchOver;
    }

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
            BeginShot(selGamePiece.GetComponent<GamePieceManager>());
            State = GameManager.GameState.ProcessingTurn;
        }
    }

    private void BeginShot(GamePieceManager piece)
    {
        ruleset.OnBeforeFlick(piece);
        Kills.BeginShot(CurrentPlayerID, piece.pieceID);
        standingAtShot = players.Where(IsStanding).ToList();
    }

    private void TurnProcess()
    {
        var allSettled = true;
        foreach (var piece in gamePieceScripts)
        {
            if (piece.IsSettled) continue;
            allSettled = false;
            break;
        }

        if (!selGamePiece.isCancelled && !selGamePiece.isDragging && allSettled)
        {
            EndTurn();
        }
    }

    private void EndTurn()
    {
        var finishedPlayer = players.Find(p => p.ID == CurrentPlayerID);
        LastTurnEnd = TurnEnd.Shot;
        LastPlayerId = CurrentPlayerID;
        Kills.EndShot();
        OnTurnEnded?.Invoke(finishedPlayer);

        var standing = players.Where(IsStanding).ToList();
        if (ruleset.TryGetMatchWinner(standingAtShot, standing, CurrentPlayerID, out var winner, out var reason))
        {
            State = GameManager.GameState.MatchOver;
            OnMatchEnded?.Invoke(winner, reason);
            return;
        }
        foreach (var knockedOut in standingAtShot.Except(standing)) OnPlayerOut?.Invoke(knockedOut.ID, MatchEndReason.Knockout);

        BeginTurn(NextPlayerIndex());
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
        var passer = players.Find(p => p.ID == CurrentPlayerID);
        LastTurnEnd = why;
        LastPlayerId = CurrentPlayerID;
        OnTurnPassed?.Invoke(passer, why);
        BeginTurn(NextPlayerIndex());
    }

    // The next side round that is still standing.
    private int NextPlayerIndex()
    {
        var current = players.FindIndex(p => p.ID == CurrentPlayerID);
        for (var step = 1; step <= players.Count; step++)
        {
            var index = (current + step) % players.Count;
            if (IsStanding(players[index])) return index;
        }
        return (current + 1) % players.Count;
    }

    private void BeginTurn(int playerIndex)
    {
        selGamePiece = null;
        CurrentPlayerID = players[playerIndex].ID;
        TurnTimeRemaining = TurnSeconds;
        State = GameManager.GameState.WaitingForInput;
        OnTurnStarted?.Invoke(players[playerIndex]);
    }
}
