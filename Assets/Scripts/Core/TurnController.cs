using System;
using System.Collections.Generic;
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

    public GameManager.GameState State { get; private set; } = GameManager.GameState.Mainmenu;
    public bool HasStarted => State != GameManager.GameState.Mainmenu;
    public int CurrentPlayerID { get; private set; }
    public PieceSelector PieceSelector => pieceSelector;
    public TurnEnd LastTurnEnd { get; private set; }

    // 0 = no turn timer. The clock runs while the side to move is choosing
    // and aiming, and stops once the stone is flicked.
    public int TurnSeconds { get; }
    public float TurnTimeRemaining { get; private set; }
    public bool IsAwaitingShot => State == GameManager.GameState.WaitingForInput || State == GameManager.GameState.WaitingForEndTurn;

    private readonly IRuleset ruleset;
    private readonly List<PlayersManager> players;
    private readonly List<GamePieceDragAndReleaseForce> gamePieceScripts;
    private readonly PieceSelector pieceSelector;

    private GamePieceDragAndReleaseForce selGamePiece;

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
    }

    public void StartMatch()
    {
        LastTurnEnd = TurnEnd.None;
        BeginTurn(0);
    }

    public void Tick()
    {
        if (TurnSeconds > 0 && IsAwaitingShot)
        {
            TurnTimeRemaining -= Time.deltaTime;
            if (TurnTimeRemaining <= 0)
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
        ruleset.OnBeforeFlick(pieceManager);
        piece.ApplyFlick(force);
        State = GameManager.GameState.ProcessingTurn;
        return true;
    }

    // The skip-turn button (or the guest's PassCommand on the host). Only for
    // the side to move, before its stone is flicked.
    public bool TryPassTurn(int playerId)
    {
        if (!IsAwaitingShot || playerId != CurrentPlayerID) return false;
        PassTurn(TurnEnd.Skipped);
        return true;
    }

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
            ruleset.OnBeforeFlick(selGamePiece.GetComponent<GamePieceManager>());
            State = GameManager.GameState.ProcessingTurn;
        }
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
        OnTurnEnded?.Invoke(finishedPlayer);

        if (ruleset.TryGetMatchWinner(players, CurrentPlayerID, out var winner, out var reason))
        {
            State = GameManager.GameState.MatchOver;
            OnMatchEnded?.Invoke(winner, reason);
            return;
        }

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
        OnTurnPassed?.Invoke(passer, why);
        BeginTurn(NextPlayerIndex());
    }

    private int NextPlayerIndex() => (players.FindIndex(p => p.ID == CurrentPlayerID) + 1) % players.Count;

    private void BeginTurn(int playerIndex)
    {
        selGamePiece = null;
        CurrentPlayerID = players[playerIndex].ID;
        TurnTimeRemaining = TurnSeconds;
        State = GameManager.GameState.WaitingForInput;
        OnTurnStarted?.Invoke(players[playerIndex]);
    }
}
