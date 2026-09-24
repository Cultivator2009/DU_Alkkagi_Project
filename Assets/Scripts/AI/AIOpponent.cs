using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The AI side of a local game. On its turn it plans a shot on a copy of the
// board (AIPlanner) and flicks through TurnController, the way a network
// guest's shot is applied, a little off by its level: aim and strength
// errors are what separate easy from hard. It places its own pieces when
// the rules call for placement.
public class AIOpponent : MonoBehaviour
{
    public int playerId = 1;
    public AILevel level = AILevel.Normal;
    public float thinkSeconds = 0.9f; // at the least, so a shot doesn't land the instant the turn starts

    // Spread (one sigma) of the aim, in degrees, and of the strength, as a
    // fraction. Planning itself is near perfect: with no error the AI
    // knocked out what it aimed at ten shots in ten.
    private static readonly Dictionary<AILevel, (float aimDegrees, float power)> Error = new Dictionary<AILevel, (float, float)>
    {
        { AILevel.Easy, (5f, 0.25f) },
        { AILevel.Normal, (2.5f, 0.12f) },
        { AILevel.Hard, (1f, 0.05f) },
    };

    private readonly System.Random random = new System.Random();
    private AIPlanner planner;
    private bool busy;

    private void OnDestroy()
    {
        planner?.Dispose();
    }

    private void Update()
    {
        var gameManager = GameManager.manager;
        if (busy || gameManager == null) return;
        if (gameManager.gameState == GameManager.GameState.Placement)
        {
            if (gameManager.Placement != null && gameManager.Placement.CanAct(playerId)) StartCoroutine(Place(gameManager));
            return;
        }
        var turns = gameManager.TurnController;
        if (turns != null && turns.State == GameManager.GameState.WaitingForInput && turns.CurrentPlayerID == playerId)
            StartCoroutine(TakeTurn(gameManager, turns));
    }

    private IEnumerator TakeTurn(GameManager gameManager, TurnController turns)
    {
        busy = true;
        var started = Time.time;
        // Leave a second and a half of a timed turn for the shot itself.
        var budget = turns.TurnSeconds > 0 ? Mathf.Max(0.3f, turns.TurnTimeRemaining - thinkSeconds - 1.5f) : 4f;
        PlannedShot? shot = null;
        planner = new AIPlanner(gameManager.Board, gameManager.gamePieceScripts, MatchSettings.Current.BothOutRule);
        yield return planner.Plan(playerId, level, budget, s => shot = s);
        planner.Dispose();
        planner = null;
        while (Time.time - started < thinkSeconds) yield return null;

        if (turns.State == GameManager.GameState.WaitingForInput && turns.CurrentPlayerID == playerId)
        {
            if (shot.HasValue && shot.Value.Piece != null)
            {
                var (aimError, powerError) = Error[level];
                var direction = Quaternion.Euler(0, Gaussian() * aimError, 0) * shot.Value.Direction;
                var power = Mathf.Clamp(shot.Value.Power * (1 + Gaussian() * powerError), 0.05f, 1f);
                var piece = shot.Value.Piece;
                turns.TryApplyExternalFlick(piece, direction * (power * piece.maxForce));
            }
            else turns.TryPassTurn(playerId);
        }
        busy = false;
    }

    // At random over its zone, the way a timed-out player's pieces go down.
    // Taking turns, one piece per turn.
    private IEnumerator Place(GameManager gameManager)
    {
        busy = true;
        yield return new WaitForSeconds(0.5f);
        var phase = gameManager.Placement;
        while (phase != null && !phase.Done && phase.CanAct(playerId))
        {
            var next = phase.NextUnplaced(playerId);
            if (next == null)
            {
                phase.TryReady(playerId);
                break;
            }
            var occupied = new List<(Vector3 position, float radius)>();
            foreach (var piece in gameManager.gamePieceScripts)
            {
                var manager = piece.GetComponent<GamePieceManager>();
                if (phase.TryGetPosition(manager.pieceID, out var at)) occupied.Add((at, manager.radius));
            }
            phase.TryPlace(playerId, next.pieceID, gameManager.Board.RandomFreePosition(playerId, next.radius, occupied, random));
            if (phase.Style == PlacementStyle.Alternating) break;
            yield return new WaitForSeconds(0.15f);
        }
        busy = false;
    }

    private static float Gaussian()
    {
        var u = 1f - Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Cos(2f * Mathf.PI * Random.value);
    }
}
