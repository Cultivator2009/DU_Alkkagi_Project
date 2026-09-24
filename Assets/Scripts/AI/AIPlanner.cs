using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public enum AILevel : byte
{
    Easy,
    Normal,
    Hard
}

public struct PlannedShot
{
    public GamePieceDragAndReleaseForce Piece;
    public Vector3 Direction; // unit, level
    public float Power;       // 0..1, as a pull
    public float Score;
}

// Picks a shot by trying candidates on a copy of the board: a physics scene
// of its own (Unity never steps it by itself) holding the board's colliders
// and a stand-in for every piece, stepped just as the game steps (the same
// launch speeds, the same rise limit). Candidates go from each own piece at
// each opponent piece, dead on and cut a little either way, at the strength
// that should just clear it off the board, a bit more, and full. A shot scores
// for the opponent's pieces it knocks out and against its own, the match
// result above all; after that, opponents left near the edge and its own
// kept away from it.
public class AIPlanner : IDisposable
{
    // Both measured on the board: a flicked piece slows at about 17.5 m/s²,
    // and one piece hitting another square passes on about 70% of its speed.
    private const float Deceleration = 17.5f;
    private const float Restitution = 0.4f;
    private const int MaxSteps = 250;        // 5 s of play; nearly every shot has settled by then
    private const float OutHeight = -0.3f;
    private const float FrameBudgetMs = 6f;  // per frame, so the game keeps its frame rate while the AI thinks

    private class Stand
    {
        public GamePieceDragAndReleaseForce Source;
        public Rigidbody Body;
        public int Owner;
        public float Radius;
        public float MaxRise;
        public bool Out;
    }

    private struct Candidate
    {
        public Stand Shooter;
        public Vector3 Direction;
        public float Power;
        public float Priority;
    }

    private readonly Scene scene;
    private readonly PhysicsScene physics;
    private readonly List<Stand> stands = new List<Stand>();
    private readonly Bounds surface;
    private readonly BothOutRule bothOutRule;

    public AIPlanner(BoardSetup board, IEnumerable<GamePieceDragAndReleaseForce> pieces, BothOutRule rule)
    {
        scene = SceneManager.CreateScene("AIPlanner " + Time.frameCount, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        physics = scene.GetPhysicsScene();
        surface = board.SurfaceBounds;
        bothOutRule = rule;

        Copy(board.Active.gameObject);
        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            var manager = piece.GetComponent<GamePieceManager>();
            stands.Add(new Stand
            {
                Source = piece,
                Body = Copy(piece.gameObject).GetComponent<Rigidbody>(),
                Owner = manager.playerIndex,
                Radius = manager.radius,
                MaxRise = piece.maxRiseSpeed,
            });
        }
    }

    public void Dispose()
    {
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
    }

    // Colliders and bodies only: no scripts, nothing drawn.
    private GameObject Copy(GameObject original)
    {
        var copy = Object.Instantiate(original, original.transform.position, original.transform.rotation);
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        copy.SetActive(true);
        SceneManager.MoveGameObjectToScene(copy, scene);
        return copy;
    }

    // Tries the most promising candidates until they or the time run out;
    // done gets the best (or, for an easy AI, now and then one of the next
    // best). No candidate at all - no piece to shoot - gives null.
    public IEnumerator Plan(int player, AILevel level, float secondsBudget, Action<PlannedShot?> done)
    {
        var tries = level == AILevel.Easy ? 12 : level == AILevel.Normal ? 30 : 60;
        var candidates = Candidates(player).OrderByDescending(c => c.Priority).Take(tries).ToList();
        var results = new List<(Candidate candidate, float score)>();
        var total = Stopwatch.StartNew();
        var frame = Stopwatch.StartNew();
        foreach (var candidate in candidates)
        {
            var score = 0f;
            var run = Run(player, candidate, s => score = s, frame);
            while (run.MoveNext())
            {
                yield return null;
                frame.Restart();
            }
            results.Add((candidate, score));
            if (total.Elapsed.TotalSeconds > secondsBudget) break;
        }

        if (results.Count == 0)
        {
            done(null);
            yield break;
        }
        var ranked = results.OrderByDescending(r => r.score).ToList();
        var pick = ranked[0];
        if (level == AILevel.Easy && ranked.Count > 2 && UnityEngine.Random.value < 0.4f) pick = ranked[UnityEngine.Random.Range(1, 3)];
        done(new PlannedShot { Piece = pick.candidate.Shooter.Source, Direction = pick.candidate.Direction, Power = pick.candidate.Power, Score = pick.score });
    }

    // One candidate, from the board as it is now, stepping until everything
    // stops. Yields (hands the frame back) whenever the frame budget is spent.
    private IEnumerator Run(int player, Candidate candidate, Action<float> score, Stopwatch frame)
    {
        foreach (var stand in stands)
        {
            stand.Out = stand.Source == null;
            stand.Body.gameObject.SetActive(!stand.Out);
            if (stand.Out) continue;
            var source = stand.Source.GetComponent<Rigidbody>();
            stand.Body.transform.SetPositionAndRotation(source.position, source.rotation);
            stand.Body.position = source.position;
            stand.Body.rotation = source.rotation;
            stand.Body.linearVelocity = Vector3.zero;
            stand.Body.angularVelocity = Vector3.zero;
            stand.Body.WakeUp();
        }

        var shooter = candidate.Shooter;
        var flick = candidate.Direction * (candidate.Power * shooter.Source.maxForce);
        shooter.Body.linearVelocity = flick / shooter.Source.referenceMass * Mathf.Pow(shooter.Source.referenceMass / shooter.Body.mass, shooter.Source.massExponent);

        var still = 0;
        for (var step = 0; step < MaxSteps && still < 3; step++)
        {
            var moving = false;
            foreach (var stand in stands)
            {
                if (stand.Out) continue;
                var velocity = stand.Body.linearVelocity;
                if (velocity.y > stand.MaxRise) stand.Body.linearVelocity = new Vector3(velocity.x, stand.MaxRise, velocity.z);
                if (velocity.sqrMagnitude > 0.0025f || stand.Body.angularVelocity.sqrMagnitude > 0.0025f) moving = true;
            }
            physics.Simulate(Time.fixedDeltaTime);
            foreach (var stand in stands)
            {
                if (stand.Out || stand.Body.position.y > OutHeight) continue;
                stand.Out = true;
                stand.Body.gameObject.SetActive(false);
            }
            still = step > 5 && !moving ? still + 1 : 0;
            if (frame.Elapsed.TotalMilliseconds > FrameBudgetMs) yield return null;
        }
        score(Score(player));
    }

    private float Score(int player)
    {
        int mineOut = 0, theirsOut = 0, mineLeft = 0, theirsLeft = 0;
        var position = 0f;
        foreach (var stand in stands)
        {
            if (stand.Source == null) continue; // gone before this turn
            var mine = stand.Owner == player;
            if (stand.Out)
            {
                if (mine) mineOut++;
                else theirsOut++;
                continue;
            }
            if (mine) mineLeft++;
            else theirsLeft++;
            // Near the edge is where a piece gets knocked out next.
            var nearEdge = 1f - Mathf.Clamp01(EdgeMargin(stand.Body.position) / 0.5f);
            position += mine ? -0.8f * nearEdge : 0.8f * nearEdge;
        }

        var score = 10f * theirsOut - 12f * mineOut + position;
        if (theirsLeft == 0 && mineLeft > 0) score += 1000;
        else if (mineLeft == 0 && theirsLeft > 0) score -= 1000;
        else if (mineLeft == 0 && theirsLeft == 0)
            score += bothOutRule == BothOutRule.ShooterWins ? 1000 : bothOutRule == BothOutRule.Draw ? -50 : -1000;
        return score;
    }

    private IEnumerable<Candidate> Candidates(int player)
    {
        var mine = stands.Where(s => s.Owner == player && s.Source != null).ToList();
        var theirs = stands.Where(s => s.Owner != player && s.Source != null).ToList();
        foreach (var shooter in mine)
        foreach (var target in theirs)
        {
            var from = Flat(shooter.Source.transform.position);
            var to = Flat(target.Source.transform.position);
            var toTarget = to - from;
            var distance = toTarget.magnitude;
            if (distance < 1e-3f) continue;
            var side = new Vector3(-toTarget.z, 0, toTarget.x) / distance;
            var blocked = Blocked(shooter, target, from, to);
            foreach (var cut in new[] { 0f, -0.45f, 0.45f })
            {
                var aim = (to + side * (cut * (shooter.Radius + target.Radius)) - from).normalized;
                var power = PowerToClear(shooter, target, aim, distance);
                // Targets close to the edge they'd be pushed over first,
                // near shots before far ones, clear lines before blocked.
                var priority = -EdgeDistance(to, aim) - 0.3f * distance - (blocked ? 2f : 0f) - Mathf.Abs(cut);
                yield return new Candidate { Shooter = shooter, Direction = aim, Power = power, Priority = priority };
                if (power < 0.8f) yield return new Candidate { Shooter = shooter, Direction = aim, Power = power * 1.25f, Priority = priority - 0.2f };
                if (power < 1f) yield return new Candidate { Shooter = shooter, Direction = aim, Power = 1f, Priority = priority - 0.4f };
            }
        }
    }

    // The pull that should send target just over the edge when shooter hits
    // it square: friction on the way in, a near-elastic hit, friction out.
    private float PowerToClear(Stand shooter, Stand target, Vector3 direction, float distance)
    {
        var toEdge = EdgeDistance(Flat(target.Source.transform.position), direction) + 0.1f;
        var targetSpeed = Mathf.Sqrt(2 * Deceleration * toEdge);
        var m1 = shooter.Body.mass;
        var m2 = target.Body.mass;
        var impact = targetSpeed * (m1 + m2) / ((1 + Restitution) * m1);
        var gap = Mathf.Max(0, distance - shooter.Radius - target.Radius);
        var launch = Mathf.Sqrt(impact * impact + 2 * Deceleration * gap);
        var source = shooter.Source;
        var fullSpeed = source.maxForce / source.referenceMass * Mathf.Pow(source.referenceMass / m1, source.massExponent);
        return Mathf.Clamp(launch / fullSpeed, 0.12f, 1f);
    }

    private bool Blocked(Stand shooter, Stand target, Vector3 from, Vector3 to)
    {
        var line = to - from;
        foreach (var other in stands)
        {
            if (other == shooter || other == target || other.Source == null) continue;
            var point = Flat(other.Source.transform.position);
            var t = Mathf.Clamp01(Vector3.Dot(point - from, line) / line.sqrMagnitude);
            if ((from + line * t - point).magnitude < shooter.Radius + other.Radius) return true;
        }
        return false;
    }

    // How far from point along direction the board ends.
    private float EdgeDistance(Vector3 point, Vector3 direction)
    {
        var best = float.MaxValue;
        if (direction.x > 1e-4f) best = Mathf.Min(best, (surface.max.x - point.x) / direction.x);
        if (direction.x < -1e-4f) best = Mathf.Min(best, (surface.min.x - point.x) / direction.x);
        if (direction.z > 1e-4f) best = Mathf.Min(best, (surface.max.z - point.z) / direction.z);
        if (direction.z < -1e-4f) best = Mathf.Min(best, (surface.min.z - point.z) / direction.z);
        return Mathf.Max(0, best);
    }

    private float EdgeMargin(Vector3 point)
    {
        return Mathf.Min(Mathf.Min(point.x - surface.min.x, surface.max.x - point.x), Mathf.Min(point.z - surface.min.z, surface.max.z - point.z));
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
}
