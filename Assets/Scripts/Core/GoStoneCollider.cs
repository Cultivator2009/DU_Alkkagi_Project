using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

// A go stone's collider: the stone's own outline - flat underneath, the rim
// rounded, the top a low dome - turned round its axis, coarse enough for a
// convex mesh collider (255 faces at most). The stones used to collide as
// eight boxes making a flat-topped disc as thick at the rim as at the
// middle: a stone could rest on another's rim a few millimetres above it,
// or sit on its flat top where a real one slides off the dome.
public static class GoStoneCollider
{
    private const int Segments = 16;

    // (radius, height) of the stone mesh ("go") from its centre, measured:
    // bottom 0.0175 below, top 0.0293 above, and a straight band at the
    // widest (0.0735). The band matters: two stones meet on it square, as
    // round ones do; meeting corner to corner on a slope instead, one was
    // pressed into the board and the other lifted, and a hit lost half its
    // push.
    private static readonly Vector2[] Rim =
    {
        new Vector2(0.0613f, -0.0175f), new Vector2(0.0690f, -0.0140f), new Vector2(0.0735f, -0.0080f), new Vector2(0.0735f, 0.0080f),
        new Vector2(0.0690f, 0.0150f), new Vector2(0.0580f, 0.0200f), new Vector2(0.0350f, 0.0265f),
    };
    private const float Bottom = -0.0175f;
    private const float Top = 0.0293f;

    private static Mesh cached;

    public static Mesh Get()
    {
        if (cached != null) return cached;
        // Out to the corners, so the flats between them meet the round stone.
        var outward = 1 / Mathf.Cos(Mathf.PI / Segments);
        var vertices = new Vector3[Rim.Length * Segments + 2];
        for (var s = 0; s < Segments; s++)
        {
            var angle = s * Mathf.PI * 2 / Segments;
            var around = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            for (var i = 0; i < Rim.Length; i++) vertices[s * Rim.Length + i] = around * (Rim[i].x * outward) + Vector3.up * Rim[i].y;
        }
        var bottom = vertices.Length - 2;
        var top = vertices.Length - 1;
        vertices[bottom] = Vector3.up * Bottom;
        vertices[top] = Vector3.up * Top;

        // Bands between the rings, and a fan at each pole. PhysX takes the
        // hull of the points; the triangles only have to be there.
        var triangles = new System.Collections.Generic.List<int>();
        for (var s = 0; s < Segments; s++)
        {
            var a = s * Rim.Length;
            var b = (s + 1) % Segments * Rim.Length;
            for (var i = 0; i < Rim.Length - 1; i++) triangles.AddRange(new[] { a + i, a + i + 1, b + i + 1, a + i, b + i + 1, b + i });
            triangles.AddRange(new[] { bottom, a, b, top, b + Rim.Length - 1, a + Rim.Length - 1 });
        }

        cached = new Mesh { name = "Go stone collider" };
        cached.SetVertices(vertices);
        cached.SetTriangles(triangles, 0);
        cached.RecalculateBounds();
        return cached;
    }

    // Two stones knock as round ones do. Between two of these hulls PhysX
    // pushes square to the faces that meet, so a square knock, corner on
    // corner, went off 11° to the side and a cut 3-4° (2026-10-30). A touch
    // on their sides is turned about the upright to lie on the line between
    // their middles, and moved onto it, where a round stone's is: where they
    // touch on the way through the step, from where each began it and how
    // fast it goes - a fast knock is met there (swept, CCD), and one from
    // where they began went that far off toward the shooter's way (6° in a
    // 30° cut). A knock slower than PhysX bounces at all (the bounce
    // threshold, kept over what a piece resting on the board gains falling
    // for a step) bounces all the same: on their sides nothing rests, and
    // dead, a gentle tap sent the shooter on behind the stone it hit at half
    // the speed, a 60° cut at 30% gave a third of what a slightly different
    // one did. The middles are marked before each step (Mark: RoundKnock,
    // and the AI for its copies); the touches are read during it, on the
    // physics threads, where nothing of Unity's may be asked.
    private static readonly Dictionary<EntityId, (Vector2 middle, float radius)> stones = new Dictionary<EntityId, (Vector2, float)>();
    private const float Widest = 0.0735f; // the band's radius (Rim)
    private const float SideTouch = 0.5f; // a touch whose normal is steeper (on the dome, under a stone resting on it) is left as it is
    private static readonly float Aligned = Mathf.Cos(30 * Mathf.Deg2Rad); // as is one further off the line than a face can turn it
    private const float Still = 0.2f;     // units/s: slower, it's two stones at rest side by side (or going on together), not a knock
    private static float step = 0.02f;    // Time.fixedDeltaTime, read where Unity may be asked

    public static void Round(Collider collider)
    {
        if (stones.Count == 0)
        {
            Physics.ContactModifyEvent += Turn;
            Physics.ContactModifyEventCCD += Turn;
        }
        Mark(collider.GetEntityId(), collider.transform.position, collider.transform.lossyScale.x);
        collider.hasModifiableContacts = true;
        step = Time.fixedDeltaTime;
    }

    public static void Mark(EntityId collider, Vector3 middle, float scale) => stones[collider] = (new Vector2(middle.x, middle.z), Widest * scale);

    public static void Forget(EntityId collider)
    {
        if (!stones.Remove(collider) || stones.Count > 0) return;
        Physics.ContactModifyEvent -= Turn;
        Physics.ContactModifyEventCCD -= Turn;
    }

    private static void Turn(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
    {
        for (var p = 0; p < pairs.Length; p++)
        {
            var pair = pairs[p];
            if (!stones.TryGetValue(pair.colliderEntityId, out var a) || !stones.TryGetValue(pair.otherColliderEntityId, out var b)) continue;
            var va = new Vector2(pair.bodyVelocity.x, pair.bodyVelocity.z);
            var vb = new Vector2(pair.otherBodyVelocity.x, pair.otherBodyVelocity.z);
            // Two at rest against each other (or going on together) are left
            // to the shapes they are: turned, a stone leaning on another on the
            // landscape's slopes rocked on and never settled.
            if ((va - vb).sqrMagnitude < Still * Still) continue;
            var t = Meet(a.middle - b.middle, va - vb, a.radius + b.radius);
            var atB = b.middle + vb * t;
            var line = a.middle + va * t - atB;
            if (line.sqrMagnitude < 1e-8f) continue;
            line.Normalize();
            var touch = atB + line * b.radius;
            var closing = -Vector2.Dot(va - vb, line);
            var dead = closing > Still && closing < PhysicsTuning.Current.BounceThreshold;
            var missed = false;
            for (var i = 0; i < pair.contactCount; i++)
            {
                var normal = pair.GetNormal(i);
                if (Mathf.Abs(normal.y) > SideTouch) continue;
                var flat = new Vector2(normal.x, normal.z);
                var across = flat.magnitude;
                var along = Vector2.Dot(flat, line) / across;
                if (Mathf.Abs(along) < Aligned) continue;
                var way = along > 0 ? line : -line;
                var turned = new Vector3(way.x * across, normal.y, way.y * across);
                pair.SetNormal(i, turned);
                pair.SetPoint(i, new Vector3(touch.x, pair.GetPoint(i).y, touch.y));
                // Along the normal only: across it the solver drives the two
                // to slide past each other (as a belt does) and spins them.
                if (dead) pair.SetTargetVelocity(i, turned * (Vector2.Dot(line, way) * across * pair.GetBounciness(i) * closing));
                // A knock missed, found a step on: met as they touched, not
                // pushed out of each other over the steps after, and not
                // rubbing (a clean knock throws nothing; held, this one drew
                // the stone 24° toward the shooter's way).
                if (t < 0)
                {
                    missed = true;
                    pair.SetSeparation(i, 0);
                    pair.SetDynamicFriction(i, 0);
                    pair.SetStaticFriction(i, 0);
                }
            }
            // And turning neither: what they'd turn round is where they are
            // now, past the touch, and it spun them.
            if (missed) pair.massProperties = new ModifiableMassProperties { inverseMassScale = 1, otherInverseMassScale = 1 };
        }
    }

    // When two circles, apart (from b's middle to a's) and closing (a's
    // velocity less b's), touch, within a step either way: ahead, or behind
    // if they're already into each other (a glancing knock in a cut at 30%
    // came on from just outside the touching distance, 0.02, and was found
    // only a step later, 2 mm into the stone, then shoved off 11° wrong and
    // spinning), or when nearest if they'd pass. None if they're drawing
    // apart or hardly moving.
    private static float Meet(Vector2 apart, Vector2 closing, float reach)
    {
        var a = closing.sqrMagnitude;
        var b = Vector2.Dot(apart, closing);
        if (a < Still * Still || b >= 0) return 0;
        var d = b * b - a * (apart.sqrMagnitude - reach * reach);
        var t = d < 0 ? -b / a : (-b - Mathf.Sqrt(d)) / a;
        return Mathf.Clamp(t, -step, step);
    }
}

// A go stone in play (BoardSetup, the menu's demo): its middle marked before
// each step, for its knocks on the others (GoStoneCollider.Round).
public class RoundKnock : MonoBehaviour
{
    private Rigidbody body;
    private EntityId solid;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        var collider = GetComponent<MeshCollider>();
        solid = collider.GetEntityId();
        GoStoneCollider.Round(collider);
    }

    private void FixedUpdate() => GoStoneCollider.Mark(solid, body.position, transform.lossyScale.x);

    private void OnDestroy() => GoStoneCollider.Forget(solid);
}
