using System.Collections.Generic;
using UnityEngine;

public class PieceSelector
{
    private readonly List<GamePieceDragAndReleaseForce> pieces;

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
            if (pieceManager.playerIndex == currentPlayerID) return piece;

            piece.isSelected = false;
        }
        return null;
    }
}
