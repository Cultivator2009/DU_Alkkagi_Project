using System.Collections.Generic;
using UnityEngine;

public class PieceSelector
{
    private readonly List<GamePieceDragAndReleaseForce> pieces;

    // Restricts which player's pieces this input source is allowed to touch
    // at all, independent of whose turn it currently is. Null (default)
    // means any piece is fair game - the local hot-seat behavior from before
    // networking existed. The network host sets this to its own player id so
    // its mouse can never hijack a piece it doesn't physically own, even
    // though it remains the physics authority for every piece.
    public int? LocalPlayerId { get; set; }
    // Locally against the AI: the sides played at this screen (null: any).
    public HashSet<int> Players { get; set; }

    public PieceSelector(List<GamePieceDragAndReleaseForce> pieces)
    {
        this.pieces = pieces;
    }

    // A click marks every piece under it (OnMouseDown, with PickRank);
    // this reads the marks, gated by turn ownership: the nearest piece the
    // side to move may take. Every other mark goes.
    public GamePieceDragAndReleaseForce TrySelect(int currentPlayerID)
    {
        GamePieceDragAndReleaseForce picked = null;
        foreach (var piece in pieces)
        {
            if (!piece.isSelected || KeyBindings.Down(GameAction.CancelAim)) continue;

            var playerIndex = piece.Manager.playerIndex;
            var mayTake = playerIndex == currentPlayerID && (!LocalPlayerId.HasValue || playerIndex == LocalPlayerId.Value) && (Players == null || Players.Contains(playerIndex));
            if (mayTake && (picked == null || piece.PickRank < picked.PickRank)) picked = piece;
        }
        foreach (var piece in pieces)
            if (piece != picked) piece.isSelected = false;
        return picked;
    }
}
