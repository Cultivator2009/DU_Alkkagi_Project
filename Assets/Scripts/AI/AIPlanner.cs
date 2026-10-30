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
// for what the opponent's pieces it knocks out are worth and against its
// own (IRuleset.Value: a piece each, or what it costs in health) - in a
// battle of health the knocks' damage too, read off each stand-in's sudden
// changes of speed - the match result above all; after that, opponents
// left near the edge and its own kept away from it. With walls (a battle
// of health's) the copy has them too.
public class AIPlanner : IDisposable
{
    // Both measured on the board: a flicked piece slows as the physics
    // played has it (PhysicsTuning.Slowing: 17.6 m/s², 0.27 g). One piece
    // hitting another square passes on (1 + e) / 2 of its speed, e their
    // material's bounce (Restitution): glass or slate stones 0.85, wood 0.55.
    private static float Deceleration => PhysicsTuning.Current.Slowing(PieceSet.Current.Type);
    private static float Restitution(Stand piece) => piece.Solid != null && piece.Solid.sharedMaterial != null ? piece.Solid.sharedMaterial.bounciness : 0.4f;
    private const int MaxSteps = 250;        // 5 s of play; nearly every shot has settled by then
    private const float OutHeight = -0.3f;
    private const float FrameBudgetMs = 6f;  // per frame, so the game keeps its frame rate while the AI thinks

    private class Stand
    {
        public GamePieceDragAndReleaseForce Source;
        public Rigidbody Body;
        public Collider Solid;      // the copy's own, for its foot on the board (GamePieceDragAndReleaseForce.Slow)
        public int Owner;
        public int Value;
        public float Radius;
        public float MaxRise;
        public bool Out;
        public int Health;          // a battle of health, A: the piece's own
        public float Damage;        // what this candidate's knocks cost it (a battle of health)
        public Vector3 LastVelocity;
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
    private readonly BoxCollider clipped; // the board copy's, standing in for its shape (BoardVariant.Clip)
    private readonly List<Stand> stands = new List<Stand>();
    private readonly BoardShape shape;    // the board
    private readonly BoardVariant ground; // the board: where its top is, under each point
    // The closing circle (ZoneRule): its middle, the one closing as this
    // round ends (a piece left outside it is as good as gone) and the next
    // (one outside it has to come in); infinite, none.
    private readonly Vector2 zoneCentre;
    private readonly float safe = float.PositiveInfinity; // closed: outside it, a piece goes as the shot ends
    private readonly float closing = float.PositiveInfinity;
    private readonly float nextCircle = float.PositiveInfinity;
    private readonly BothOutRule bothOutRule;
    private readonly IRuleset ruleset;
    private readonly IReadOnlyList<Side> sides;
    private readonly Func<int, int> teamOf; // a teammate's pieces are as its own

    // zone: the match's closing circle, or null to leave it out (Foresight).
    public AIPlanner(BoardSetup board, IEnumerable<GamePieceDragAndReleaseForce> pieces, IRuleset ruleset, IReadOnlyList<Side> sides, BothOutRule rule, ZoneRule zone = null, Func<int, int> teamOf = null)
    {
        this.ruleset = ruleset;
        this.sides = sides;
        this.teamOf = teamOf ?? (id => id);
        scene = SceneManager.CreateScene("AIPlanner " + Time.frameCount, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        physics = scene.GetPhysicsScene();
        shape = board.Playable;
        if (zone != null && zone.Enabled)
        {
            zoneCentre = zone.Centre;
            safe = zone.Safe;
            nextCircle = zone.Next;
            if (zone.Warned) closing = zone.Next;
        }
        bothOutRule = rule;

        // A box standing in for the board's shape stands in here too.
        var cover = Copy(board.Active.gameObject).transform.Find(BoardVariant.SlabName);
        clipped = cover != null && board.Playable.Parts.Count > 1 ? cover.GetComponent<BoxCollider>() : null;
        if (clipped != null) BoardVariant.Clip(clipped, board.Playable);
        ground = board.Active;
        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            var manager = piece.Manager;
            var copy = Copy(piece.gameObject);
            stands.Add(new Stand
            {
                Source = piece,
                Body = copy.GetComponent<Rigidbody>(),
                Solid = Array.Find(copy.GetComponentsInChildren<Collider>(), c => !c.isTrigger),
                Owner = manager.playerIndex,
                Value = ruleset.Value(manager),
                Radius = manager.radius,
                MaxRise = piece.maxRiseSpeed,
                Health = manager.health,
            });
        }
    }

    public void Dispose()
    {
        BoardVariant.Unclip(clipped);
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
    }

    // Colliders and bodies only: no scripts, nothing drawn, no joints (an
    // item's glue, tied to the real board). Simulated even where the real
    // pieces only follow the host's word (a network guest's foresight).
    private GameObject Copy(GameObject original)
    {
        var copy = Object.Instantiate(original, original.transform.position, original.transform.rotation);
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
        foreach (var joint in copy.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = false;
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
        if (ground.IsRelief) tries *= 2; // the knocks and the approaches both
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

    // Foresight (ItemId.Foresight): where this shot would send what it
    // moves, stepped on the copy as a candidate is: a line for each piece
    // that moved (the shot's own first), a point every other step.
    // SureUntil: the point on every line from which it's a guess - once a
    // third piece is set moving, a knock passed on from one already hit,
    // where small differences grow.
    public int SureUntil { get; private set; }

    public IEnumerator Trace(GamePieceDragAndReleaseForce piece, Vector3 direction, float power, List<(GamePieceDragAndReleaseForce piece, List<Vector3> path)> paths)
    {
        var shooter = stands.Find(s => s.Source == piece);
        if (shooter == null) yield break;
        var tracks = new Dictionary<Stand, List<Vector3>>();
        var started = new HashSet<Stand>();
        SureUntil = int.MaxValue;
        void Record(int step)
        {
            if (step % 2 != 0) return;
            foreach (var stand in stands)
            {
                if (stand.Source == null || !stand.Body.gameObject.activeSelf) continue;
                if (!tracks.TryGetValue(stand, out var track)) tracks[stand] = track = new List<Vector3> { stand.Source.Body.position };
                track.Add(stand.Body.position);
                if ((stand.Body.position - track[0]).sqrMagnitude > 0.004f * 0.004f) started.Add(stand);
            }
            if (started.Count >= 3 && SureUntil == int.MaxValue) SureUntil = step / 2;
        }
        var frame = Stopwatch.StartNew();
        var run = Run(piece.Manager.playerIndex, new Candidate { Shooter = shooter, Direction = direction, Power = power }, _ => { }, frame, Record);
        while (run.MoveNext())
        {
            yield return null;
            frame.Restart();
        }
        paths.Clear();
        foreach (var (stand, track) in tracks.OrderBy(t => t.Key == shooter ? 0 : 1))
            if ((track[track.Count - 1] - track[0]).sqrMagnitude > 0.02f * 0.02f) paths.Add((stand.Source, track));
    }

    // One candidate, from the board as it is now, stepping until everything
    // stops. Yields (hands the frame back) whenever the frame budget is spent.
    private IEnumerator Run(int player, Candidate candidate, Action<float> score, Stopwatch frame, Action<int> onStep = null)
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
            stand.Damage = 0;
            stand.LastVelocity = Vector3.zero;
        }

        var shooter = candidate.Shooter;
        var impacts = ruleset is HealthRuleset; // the flicked piece takes none
        var flick = candidate.Direction * (candidate.Power * shooter.Source.maxForce);
        shooter.Body.linearVelocity = flick / shooter.Source.referenceMass * Mathf.Pow(shooter.Source.referenceMass / shooter.Source.LaunchMass, shooter.Source.massExponent);

        var still = 0;
        for (var step = 0; step < MaxSteps && still < 3; step++)
        {
            var moving = false;
            foreach (var stand in stands)
            {
                if (stand.Out) continue;
                var velocity = stand.Body.linearVelocity;
                if (velocity.y > stand.MaxRise) stand.Body.linearVelocity = new Vector3(velocity.x, stand.MaxRise, velocity.z);
                GamePieceDragAndReleaseForce.Slow(stand.Body, stand.Solid, stand.Source.extraSlowing, ground, shape);
                if (velocity.sqrMagnitude > 0.0025f || stand.Body.angularVelocity.sqrMagnitude > 0.0025f) moving = true;
            }
            physics.Simulate(Time.fixedDeltaTime);
            onStep?.Invoke(step);
            foreach (var stand in stands)
            {
                if (stand.Out) continue;
                // A battle of health: a knock is a sudden change of speed
                // across the board (sliding only takes 0.18 a step off),
                // its impulse the piece's mass times that.
                var velocity = stand.Body.linearVelocity;
                var change = velocity - stand.LastVelocity;
                change.y = 0;
                if (impacts && stand != shooter && change.magnitude > 0.5f) stand.Damage += GameManager.manager.DamageFor(stand.Body.mass * change.magnitude);
                stand.LastVelocity = velocity;
                if (stand.Body.position.y > OutHeight) continue;
                stand.Out = true;
                stand.Body.gameObject.SetActive(false);
            }
            still = step > 5 && !moving ? still + 1 : 0;
            if (frame.Elapsed.TotalMilliseconds > FrameBudgetMs) yield return null;
        }
        score(Score(player, shooter));
    }

    // On the landscape, a shot that brings its piece nearer the nearest
    // piece it can knock counts for a little: out of reach of everything,
    // the next shot can't score.
    private const float Approach = 1.5f;
    private const float OutsideNext = 3f; // a piece of its own outside the next circle (an opponent's: half for it)

    private bool Outside(Vector2 point, float radius) => !float.IsInfinity(radius) && (point - zoneCentre).sqrMagnitude > radius * radius;

    private float Score(int player, Stand shooter)
    {
        var lost = new (int pieces, int value)[sides.Count];
        float mineOut = 0, theirsOut = 0;
        var position = 0f;
        var perPiece = ruleset is HealthRuleset health && health.Rule == HealthRule.PerPiece;
        foreach (var stand in stands)
        {
            if (stand.Source == null) continue; // gone before this turn
            if (!sides[stand.Owner].Standing) continue; // out: in the way, but worth nothing to anyone
            var mine = teamOf(stand.Owner) == teamOf(player);
            // A battle of health: knocks cost too, and (A) one that leaves a
            // piece nothing breaks it.
            var damage = Mathf.RoundToInt(stand.Damage);
            if (perPiece && !stand.Out && damage >= stand.Health) stand.Out = true;
            if (stand.Out)
            {
                if (mine) mineOut += stand.Value;
                else theirsOut += stand.Value;
                lost[stand.Owner].pieces++;
                lost[stand.Owner].value += stand.Value;
                continue;
            }
            if (damage > 0)
            {
                if (mine) mineOut += damage;
                else theirsOut += damage;
                lost[stand.Owner].value += damage;
            }
            // Near the edge is where a piece gets knocked out next; outside
            // the circle, or the one closing as this round ends, it's out.
            var flat = new Vector2(stand.Body.position.x, stand.Body.position.z);
            if (!shape.Contains(flat) || Outside(flat, safe) || Outside(flat, closing))
            {
                if (mine) mineOut += stand.Value;
                else theirsOut += stand.Value;
                lost[stand.Owner].pieces++;
                lost[stand.Owner].value += stand.Value;
                continue;
            }
            var margin = shape.Margin(flat);
            if (!float.IsInfinity(safe)) margin = Mathf.Min(margin, safe - (flat - zoneCentre).magnitude);
            var nearEdge = 1f - Mathf.Clamp01(margin / 0.5f);
            position += mine ? -0.8f * nearEdge : 0.8f * nearEdge;
            // Outside the next circle: gone when it closes, unless brought in.
            if (Outside(flat, nextCircle)) position += mine ? -OutsideNext : OutsideNext * 0.5f;
            // A pond's rim is as near as an edge, and the slope into it reaches further:
            // its own piece left by one is as good as in it at the next miss.
            if (ground.IsRelief)
            {
                var nearPond = 1f - Mathf.Clamp01(BoardTerrain.PondMargin(flat - new Vector2(ground.transform.position.x, ground.transform.position.z)) / 1f);
                position += mine ? -2.5f * nearPond : 0.8f * nearPond;
            }
        }
        if (ground.IsRelief && !shooter.Out)
        {
            var before = NearestTarget(player, shooter, shooter.Source.Body.position);
            var after = NearestTarget(player, shooter, shooter.Body.position);
            if (before < float.MaxValue && after < float.MaxValue) position += Approach * Mathf.Clamp((before - after) / 4f, -1f, 1f);
        }

        var score = 10f * theirsOut - 12f * mineOut + position;
        // The match above all, whatever the pieces are worth (health runs to hundreds).
        const float match = 100000f;
        bool Gone(Side side) => ruleset.WouldBeKnockedOut(side, lost[side.Id].pieces, lost[side.Id].value);
        // With teams, a team is out when all of it is.
        var mineGone = sides.Where(s => teamOf(s.Id) == teamOf(player) && s.Standing).All(Gone);
        var theirsGone = sides.Where(s => teamOf(s.Id) != teamOf(player) && s.Standing).All(Gone);
        if (theirsGone && !mineGone) score += match;
        else if (mineGone && !theirsGone) score -= match;
        else if (mineGone)
            score += bothOutRule == BothOutRule.ShooterWins ? match : bothOutRule == BothOutRule.Draw ? -match / 20 : -match;
        return score;
    }

    // How far from a point the nearest piece the player could knock is, as
    // the copy has it now.
    private float NearestTarget(int player, Stand shooter, Vector3 from)
    {
        var nearest = float.MaxValue;
        foreach (var stand in stands)
        {
            if (stand == shooter || stand.Out || stand.Source == null || teamOf(stand.Owner) == teamOf(player) || !sides[stand.Owner].Standing) continue;
            nearest = Mathf.Min(nearest, (Flat(stand.Body.position) - Flat(from)).magnitude);
        }
        return nearest;
    }

    private IEnumerable<Candidate> Candidates(int player)
    {
        var mine = stands.Where(s => s.Owner == player && s.Source != null && (PieceSelector.MayMove == null || PieceSelector.MayMove(s.Source))).ToList();
        var theirs = stands.Where(s => teamOf(s.Owner) != teamOf(player) && sides[s.Owner].Standing && s.Source != null).ToList();
        foreach (var shooter in mine)
        foreach (var target in theirs)
        {
            var from = Flat(shooter.Source.transform.position);
            var to = Flat(target.Source.transform.position);
            var toTarget = to - from;
            var distance = toTarget.magnitude;
            if (distance < 1e-3f) continue;
            if (ground.IsRelief && distance > Reach(shooter) + shooter.Radius + target.Radius) continue; // out of reach: an approach (below) instead
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
        // Its own pieces outside the next circle: in again, straight for the
        // middle or a little either side, past the line by a piece and more.
        // Before all else when it closes as this round ends.
        foreach (var shooter in mine)
        {
            var from = Flat(shooter.Source.transform.position);
            var flat = new Vector2(from.x, from.z);
            if (!Outside(flat, nextCircle)) continue;
            var middle = new Vector3(zoneCentre.x, from.y, zoneCentre.y);
            var travel = (middle - from).magnitude - nextCircle + shooter.Radius + 0.3f;
            var priority = Outside(flat, closing) ? 2f : -1.5f;
            foreach (var turn in new[] { 0f, 15f, -15f, 30f, -30f })
            {
                var way = Quaternion.Euler(0, turn, 0) * (middle - from).normalized;
                var stop = from + way * travel;
                if (PathBlocked(shooter, from, stop)) continue;
                var power = Mathf.Clamp(Mathf.Sqrt(2 * Deceleration * travel) / FullSpeed(shooter), 0.12f, 1f);
                yield return new Candidate { Shooter = shooter, Direction = way, Power = power, Priority = priority - Mathf.Abs(turn) / 30f };
            }
        }
        if (!ground.IsRelief) yield break;
        // The landscape: most of the other sides are out of reach. Every
        // way round (a long slide and a shorter one), not past a pond (a
        // little off, and it's in) nor off the board; first those that end
        // nearest the nearest piece it could knock, a striking distance short.
        var offset = new Vector2(ground.transform.position.x, ground.transform.position.z);
        foreach (var shooter in mine)
        {
            var from = Flat(shooter.Source.transform.position);
            if (theirs.Count == 0) yield break;
            var nearest = theirs.Select(t => Flat(t.Source.transform.position)).OrderBy(p => (p - from).sqrMagnitude).First();
            var away = (nearest - from).magnitude;
            if (away < Reach(shooter) * 0.6f) continue; // in reach: the shots above
            var fullSpeed = FullSpeed(shooter);
            for (var k = 0; k < 16; k++)
            {
                var a = k * Mathf.PI / 8;
                var way = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                foreach (var share in new[] { 1f, 0.6f })
                {
                    var travel = Mathf.Min(Reach(shooter) * share, Mathf.Max(0.3f, away - StrikingDistance));
                    var stop = from + way * travel;
                    if (EdgeDistance(from, way) < travel + 0.4f) continue;
                    if (BoardTerrain.PassesPond(new Vector2(from.x, from.z) - offset, new Vector2(stop.x, stop.z) - offset, 0.45f)) continue;
                    if (PathBlocked(shooter, from, stop)) continue; // its own front row, say
                    var gain = away - (nearest - stop).magnitude;
                    if (gain < 0.3f) continue;
                    var power = Mathf.Clamp(Mathf.Sqrt(2 * Deceleration * travel) / fullSpeed, 0.12f, 1f);
                    yield return new Candidate { Shooter = shooter, Direction = way, Power = power, Priority = gain - 6f };
                }
            }
        }
    }

    private const float StrikingDistance = 1.2f; // short of a far target, to strike it from next turn

    private static float FullSpeed(Stand shooter)
    {
        var source = shooter.Source;
        return source.maxForce / source.referenceMass * Mathf.Pow(source.referenceMass / source.LaunchMass, source.massExponent);
    }

    // How far a full shot slides on level ground.
    private static float Reach(Stand shooter)
    {
        var speed = FullSpeed(shooter);
        return speed * speed / (2 * Deceleration);
    }

    // The pull that should send target just over the edge when shooter hits
    // it square: friction on the way in, a near-elastic hit, friction out.
    private float PowerToClear(Stand shooter, Stand target, Vector3 direction, float distance)
    {
        var toEdge = EdgeDistance(Flat(target.Source.transform.position), direction) + 0.1f;
        var targetSpeed = Mathf.Sqrt(2 * Deceleration * toEdge);
        var m1 = shooter.Body.mass;
        var m2 = target.Body.mass;
        var impact = targetSpeed * (m1 + m2) / ((1 + Restitution(shooter)) * m1);
        var gap = Mathf.Max(0, distance - shooter.Radius - target.Radius);
        var launch = Mathf.Sqrt(impact * impact + 2 * Deceleration * gap);
        var source = shooter.Source;
        var fullSpeed = source.maxForce / source.referenceMass * Mathf.Pow(source.referenceMass / source.LaunchMass, source.massExponent);
        return Mathf.Clamp(launch / fullSpeed, 0.12f, 1f);
    }

    // Something stands in the way from one point to another (any piece but the shooter).
    private bool PathBlocked(Stand shooter, Vector3 from, Vector3 to)
    {
        var line = to - from;
        foreach (var other in stands)
        {
            if (other == shooter || other.Source == null) continue;
            var point = Flat(other.Source.transform.position);
            var t = Mathf.Clamp01(Vector3.Dot(point - from, line) / line.sqrMagnitude);
            if ((from + line * t - point).magnitude < shooter.Radius + other.Radius) return true;
        }
        return false;
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
    // How far from a point, going one way, the board ends - or the closed
    // circle, which is its edge now.
    private float EdgeDistance(Vector3 point, Vector3 direction)
    {
        var from = new Vector2(point.x, point.z);
        var way = new Vector2(direction.x, direction.z);
        var exit = shape.Exit(from, way);
        return way.sqrMagnitude < 1e-8f ? exit : Mathf.Min(exit, ZoneRule.Exit(from, way.normalized, zoneCentre, safe));
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
}
