using System;
using System.Collections.Generic;

public class TurnController
{
    public event Action<PlayersManager> OnTurnStarted;
    public event Action<PlayersManager> OnTurnEnded;
    public event Action<PlayersManager> OnMatchEnded;

    public GameManager.GameState State { get; private set; } = GameManager.GameState.Mainmenu;
    public int CurrentPlayerID { get; private set; }

    private readonly IRuleset ruleset;
    private readonly List<PlayersManager> players;
    private readonly List<GamePieceDragAndReleaseForce> gamePieceScripts;
    private readonly PieceSelector pieceSelector;

    private GamePieceDragAndReleaseForce selGamePiece;

    public TurnController(
        IRuleset ruleset,
        List<PlayersManager> players,
        List<GamePieceDragAndReleaseForce> gamePieceScripts,
        PieceSelector pieceSelector)
    {
        this.ruleset = ruleset;
        this.players = players;
        this.gamePieceScripts = gamePieceScripts;
        this.pieceSelector = pieceSelector;
    }

    public void StartMatch()
    {
        CurrentPlayerID = players[0].ID;
        State = GameManager.GameState.WaitingForInput;
        OnTurnStarted?.Invoke(players[0]);
    }

    public void Tick()
    {
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
        OnTurnEnded?.Invoke(finishedPlayer);

        if (ruleset.TryGetMatchWinner(players, out var winner))
        {
            State = GameManager.GameState.MatchOver;
            OnMatchEnded?.Invoke(winner);
            return;
        }

        selGamePiece = null;
        var nextPlayerIndex = (players.FindIndex(p => p.ID == CurrentPlayerID) + 1) % players.Count;
        CurrentPlayerID = players[nextPlayerIndex].ID;
        State = GameManager.GameState.WaitingForInput;
        OnTurnStarted?.Invoke(players[nextPlayerIndex]);
    }
}
