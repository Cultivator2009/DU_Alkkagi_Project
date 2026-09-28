using System.Collections.Generic;

public enum MatchEndReason : byte
{
    Knockout,     // the loser has no stones left
    BothOut,      // both sides' last stones went out on one flick: MatchSettings.BothOutRule decides
    OpponentLeft, // online opponent quit or disconnected mid-match: forfeit
    Surrender,    // the loser conceded from the in-game menu
    HostLeft,     // three or four online and the host left: no one can go on (no winner)
    RoundLimit    // the round limit came: the side with the most left won (a tie, no one)
}

// What a match is won by: what a piece going out does to its side, when a
// side is out, and who has won. The turns themselves (TurnController) are
// the same whatever the ruleset; it only ever runs on the authority (a
// local game, the network host), and a guest shows the numbers it leaves in
// the sides (MatchState).
public interface IRuleset
{
    // Before the first turn, each side's pieces counted: its health, say.
    void Begin(IReadOnlyList<Side> sides);

    void OnBeforeFlick(GamePieceManager piece);

    // A piece went off the board, on shooterId's shot (-1: on no one's, the
    // board giving way). True: it's gone for good. False: it comes back
    // into its side's zone once everything has stopped.
    bool OnPieceOut(GamePieceManager piece, IReadOnlyList<Side> sides, int shooterId);

    // Out of the match (checked once a shot is over), besides conceding or leaving.
    bool IsKnockedOut(Side side);

    // What losing the piece costs its side: what the AI weighs a shot by.
    int Value(GamePieceManager piece);

    // Whether side would be out after losing that many pieces worth that
    // much, for the AI to see a winning (or losing) shot.
    bool WouldBeKnockedOut(Side side, int piecesLost, int valueLost);

    // The sides still in (not knocked out, conceded or gone) before and
    // after the flick shooterId just resolved. winner is null for a draw.
    bool TryGetMatchWinner(IReadOnlyList<Side> standingBefore, IReadOnlyList<Side> standingAfter, int shooterId, out Side winner, out MatchEndReason reason);
}
