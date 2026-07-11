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

    public PieceSelector(List<GamePieceDragAndReleaseForce> pieces)
    {
        this.pieces = pieces;
    }

    // Piece hit-testing is handled by Unity's own OnMouseDown on each piece's
    // collider, so this only needs to read the isSelected flag it sets and
    // gate it by turn ownership.
    public GamePieceDragAndReleaseForce TrySelect(int currentPlayerID)
    {
        foreach (var piece in pieces)
        {
            if (!piece.isSelected || Input.GetMouseButtonDown(1)) continue;

            var pieceManager = piece.GetComponent<GamePieceManager>();
            if (LocalPlayerId.HasValue && pieceManager.playerIndex != LocalPlayerId.Value)
            {
                piece.isSelected = false;
                continue;
            }
            if (pieceManager.playerIndex == currentPlayerID) return piece;

            piece.isSelected = false;
        }
        return null;
    }
}
