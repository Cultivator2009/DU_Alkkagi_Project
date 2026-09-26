using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The board's cartoon effects, in one mesh rebuilt each frame. All of it
// lies flat on the board and is drawn under the pieces (the shader tests
// depth), so however many go off at once no piece is ever hidden - the old
// knock, a big yellow star drawn over everything, covered the very pieces
// the shot was moving.
// - a knock: ink strokes shooting out over the board from where it
//   happened, a ring spreading and a glow gone in a blink. How hard it was
//   sets the size, the number of strokes and how long it lasts (0.14 to
//   0.4 s); a tap gets only the ring.
// - a piece going over the edge: an ink blot where it left the board, ink
//   drops thrown off it and a seal-red ring - the knockout, at once, before
//   the shot is scored.
// - a flick: a small shock ring round the piece, wider the harder it goes.
// - a turn starting: a ring swelling out of each of that side's pieces.
// Knocks, falls and flicks go off with their sounds (BoardSounds), so a
// network guest sees them where and when it hears them.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HitEffects : MonoBehaviour
{
    public static HitEffects Instance { get; private set; }

    public float smallSize = 0.05f; // a knock's reach at its softest, in world units (a go stone's radius is about 0.075)
    public float largeSize = 0.16f; // and at full strength
    public float strokesFrom = 0.1f; // knocks softer than this get only the ring
    public float fallLife = 0.65f;
    public float flickLife = 0.3f;
    public float turnLife = 0.7f;
    public float turnStagger = 0.05f; // between one piece's turn ring and the next
    public int maxBursts = 48;

    private const float Lift = 0.002f; // above the board's surface, under any piece on it

    // The HUD's ink, hanji white and the seal's red (sRGB).
    private static readonly Color Ink = new Color32(0x2E, 0x24, 0x1A, 0xFF);
    private static readonly Color Core = new Color32(0xFF, 0xFB, 0xEE, 0xFF);
    private static readonly Color Seal = new Color32(0xB3, 0x31, 0x2A, 0xFF);

    // Shapes lie in the board's plane.
    private static readonly Vector3 Across = Vector3.right;
    private static readonly Vector3 Along = Vector3.forward;

    private enum Kind : byte { Hit, Fall, Flick, Turn }

    private struct Burst
    {
        public Kind Kind;
        public Vector3 At;     // on the board's surface
        public Vector3 Out;    // a fall: level, off the board
        public float Strength; // 0..1, with the knock's (or flick's) speed
        public float Size;     // a turn ring: the piece's radius
        public float Age;      // below zero until it starts
        public float Spin;     // radians
        public int Seed;       // its own stroke angles, drop sizes and blot spikes
    }

    private readonly List<Burst> bursts = new List<Burst>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();
    private Mesh mesh;
    private MeshRenderer meshRenderer;

    private void Awake()
    {
        Instance = this;
        mesh = new Mesh { name = "Hit effects" };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = Resources.Load<Material>("HitEffect");
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Destroy(mesh);
    }

    // volume: the knock's loudness from BoardSounds, 0.15 for the softest.
    public void Play(Vector3 at, float volume)
    {
        // Loudness goes as speed^0.6 (BoardSounds.Loudness); undone here so
        // a knock twice as fast makes a burst twice as strong.
        Add(new Burst { Kind = Kind.Hit, At = OnBoard(at), Strength = Mathf.Pow(Mathf.InverseLerp(0.15f, 1f, volume), 1.6f) });
    }

    // at: where the piece was when it dropped below the board, just past an
    // edge. The blot goes on the edge it went over.
    public void PlayFall(Vector3 at)
    {
        var board = GameManager.manager != null ? GameManager.manager.Board : null;
        if (board == null) return;
        var surface = board.SurfaceBounds;
        var edge = new Vector3(Mathf.Clamp(at.x, surface.min.x, surface.max.x), surface.max.y + Lift, Mathf.Clamp(at.z, surface.min.z, surface.max.z));
        var away = at - surface.center;
        away.y = 0;
        Add(new Burst { Kind = Kind.Fall, At = edge, Out = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward });
    }

    // volume: the flick's loudness from BoardSounds, 0.35 for the softest.
    public void PlayFlick(Vector3 at, float volume)
    {
        Add(new Burst { Kind = Kind.Flick, At = OnBoard(at), Strength = Mathf.InverseLerp(0.35f, 1f, volume) });
    }

    // A side's turn has come: a ring out of each of its pieces, one after
    // another (index: the piece's place in that order).
    public void PlayTurn(Vector3 at, float radius, int index)
    {
        Add(new Burst { Kind = Kind.Turn, At = OnBoard(at), Size = radius, Age = -index * turnStagger });
    }

    private static Vector3 OnBoard(Vector3 at)
    {
        var board = GameManager.manager != null ? GameManager.manager.Board : null;
        at.y = (board != null ? board.SurfaceBounds.max.y : 0f) + Lift;
        return at;
    }

    private void Add(Burst burst)
    {
        if (bursts.Count >= maxBursts) bursts.RemoveAt(0);
        burst.Spin = Random.value * Mathf.PI * 2;
        burst.Seed = Random.Range(0, 1 << 20);
        bursts.Add(burst);
    }

    private static float StrokeLife(float strength) => Mathf.Lerp(0.1f, 0.3f, strength);
    private static float RingLife(float strength) => Mathf.Lerp(0.14f, 0.4f, strength);
    private static float GlowLife(float strength) => Mathf.Lerp(0.06f, 0.16f, strength);

    private float Life(Burst burst) => burst.Kind switch
    {
        Kind.Fall => fallLife,
        Kind.Flick => flickLife,
        Kind.Turn => turnLife,
        _ => RingLife(burst.Strength), // the longest of a knock's parts
    };

    // Game time: the bursts keep the match's pace and hold still while it's paused.
    private void LateUpdate()
    {
        for (var i = bursts.Count - 1; i >= 0; i--)
        {
            var burst = bursts[i];
            burst.Age += Time.deltaTime;
            if (burst.Age >= Life(burst)) bursts.RemoveAt(i);
            else bursts[i] = burst;
        }
        meshRenderer.enabled = bursts.Count > 0;
        if (bursts.Count == 0) return;

        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        // Front to back: the shader draws each pixel once (stencil), so what
        // goes in first is on top. The newest burst over older ones.
        for (var i = bursts.Count - 1; i >= 0; i--)
        {
            var burst = bursts[i];
            if (burst.Age < 0) continue; // not started
            switch (burst.Kind)
            {
                case Kind.Fall: DrawFall(burst); break;
                case Kind.Flick: DrawFlick(burst); break;
                case Kind.Turn: DrawTurn(burst); break;
                default: DrawHit(burst); break;
            }
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000); // never culled
    }

    // The strokes, then the ring under them, then the glow under both (see
    // LateUpdate). Everything fades out rather than just vanishing.
    private void DrawHit(Burst burst)
    {
        var k = burst.Strength;
        var age = burst.Age;
        var size = Mathf.Lerp(smallSize, largeSize, k);

        var u = age / StrokeLife(k);
        if (k >= strokesFrom && u < 1)
        {
            var count = 4 + Mathf.RoundToInt(4 * k);
            var head = size * (0.9f + 1.4f * EaseOut(u));
            var length = size * 0.75f * (1 - 0.6f * u);
            var width = size * 0.14f * (1 - 0.5f * u);
            var ink = WithAlpha(Ink, 0.85f * (1 - Square(u)));
            for (var i = 0; i < count; i++)
            {
                var angle = burst.Spin + (i + 0.5f + (Hash(burst.Seed, 100 + i) - 0.5f) * 0.5f) * Mathf.PI * 2 / count;
                var direction = Across * Mathf.Cos(angle) + Along * Mathf.Sin(angle);
                var side = Along * Mathf.Cos(angle) - Across * Mathf.Sin(angle);
                Stroke(burst.At + direction * (head - length), burst.At + direction * head, side, width, width * 0.3f, ink);
            }
        }

        u = age / RingLife(k);
        if (u < 1) Ring(burst.At, size * (0.6f + 1.3f * EaseOut(u)), size * 0.08f * (1 - u) + 0.003f, WithAlpha(Ink, 0.45f * (1 - u)));

        u = age / GlowLife(k);
        if (k >= strokesFrom && u < 1) Glow(burst.At, size * (0.8f + 0.6f * EaseOut(u)), WithAlpha(Core, 0.6f * (1 - u)));
    }

    // Drops thrown off the edge (over the blot), the blot, and the seal-red
    // ring under both. The drops fly out in a fan away from the board and
    // shrink as they go.
    private void DrawFall(Burst burst)
    {
        var u = burst.Age / fallLife;
        var side = Vector3.Cross(Vector3.up, burst.Out);
        var ink = WithAlpha(Ink, 1 - Square(u));
        for (var i = 0; i < 7; i++)
        {
            var angle = (Hash(burst.Seed, i) - 0.5f) * 2.4f;
            var direction = burst.Out * Mathf.Cos(angle) + side * Mathf.Sin(angle);
            var reach = Mathf.Lerp(0.15f, 0.45f, Hash(burst.Seed, 20 + i)) * EaseOut(Mathf.Min(1, u * 1.6f));
            var radius = Mathf.Lerp(0.016f, 0.04f, Hash(burst.Seed, 40 + i)) * (1 - 0.6f * u);
            Disc(burst.At + direction * (0.05f + reach), radius, ink);
        }
        var swell = EaseOut(Mathf.Min(1, u * 3));
        Star(burst.At, 0.1f * (0.6f + 0.5f * swell), 0.72f, burst.Spin, burst.Seed, WithAlpha(Ink, 0.9f * Mathf.Pow(1 - u, 1.5f)));
        Ring(burst.At, 0.08f + 0.42f * EaseOut(u), 0.034f * (1 - u) + 0.004f, WithAlpha(Seal, 0.85f * (1 - u)));
    }

    private void DrawFlick(Burst burst)
    {
        var k = burst.Strength;
        var u = burst.Age / flickLife;
        Ring(burst.At, 0.1f + (0.06f + 0.18f * k) * EaseOut(u), (0.01f + 0.012f * k) * (1 - u) + 0.002f, WithAlpha(Ink, 0.5f * (1 - u)));
    }

    private void DrawTurn(Burst burst)
    {
        var u = burst.Age / turnLife;
        Ring(burst.At, burst.Size * (1.1f + 0.6f * EaseOut(u)), burst.Size * 0.14f * (1 - u) + 0.002f, WithAlpha(Ink, 0.55f * (1 - Square(u))));
    }

    // Eight spikes of radius (each a little longer or shorter) and notches
    // between them at inner times radius.
    private void Star(Vector3 at, float radius, float inner, float spin, int seed, Color color)
    {
        const int points = 8;
        var center = vertices.Count;
        Vertex(at, color);
        for (var i = 0; i < points * 2; i++)
        {
            var angle = spin + i * Mathf.PI / points;
            var r = i % 2 == 0 ? radius * Mathf.Lerp(0.78f, 1.18f, Hash(seed, i)) : radius * inner;
            Vertex(at + (Across * Mathf.Cos(angle) + Along * Mathf.Sin(angle)) * r, color);
        }
        for (var i = 0; i < points * 2; i++) Triangle(center, center + 1 + i, center + 1 + (i + 1) % (points * 2));
    }

    private void Disc(Vector3 at, float radius, Color color)
    {
        const int segments = 10;
        var center = vertices.Count;
        Vertex(at, color);
        for (var i = 0; i < segments; i++)
        {
            var angle = i * Mathf.PI * 2 / segments;
            Vertex(at + (Across * Mathf.Cos(angle) + Along * Mathf.Sin(angle)) * radius, color);
        }
        for (var i = 0; i < segments; i++) Triangle(center, center + 1 + i, center + 1 + (i + 1) % segments);
    }

    // A disc that fades from color at its middle to nothing at its rim.
    private void Glow(Vector3 at, float radius, Color color)
    {
        const int segments = 20;
        var center = vertices.Count;
        Vertex(at, color);
        var rim = WithAlpha(color, 0);
        for (var i = 0; i < segments; i++)
        {
            var angle = i * Mathf.PI * 2 / segments;
            Vertex(at + (Across * Mathf.Cos(angle) + Along * Mathf.Sin(angle)) * radius, rim);
        }
        for (var i = 0; i < segments; i++) Triangle(center, center + 1 + i, center + 1 + (i + 1) % segments);
    }

    // A tapering quad from one end to the other.
    private void Stroke(Vector3 from, Vector3 to, Vector3 across, float widthFrom, float widthTo, Color color)
    {
        var first = vertices.Count;
        Vertex(from - across * (widthFrom * 0.5f), color);
        Vertex(from + across * (widthFrom * 0.5f), color);
        Vertex(to + across * (widthTo * 0.5f), color);
        Vertex(to - across * (widthTo * 0.5f), color);
        Triangle(first, first + 1, first + 2);
        Triangle(first, first + 2, first + 3);
    }

    private void Ring(Vector3 at, float radius, float width, Color color)
    {
        const int segments = 40;
        var first = vertices.Count;
        for (var i = 0; i < segments; i++)
        {
            var angle = i * Mathf.PI * 2 / segments;
            var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vertex(at + direction * (radius - width * 0.5f), color);
            Vertex(at + direction * (radius + width * 0.5f), color);
        }
        for (var i = 0; i < segments; i++)
        {
            var a = first + i * 2;
            var b = first + (i + 1) % segments * 2;
            Triangle(a, a + 1, b + 1);
            Triangle(a, b + 1, b);
        }
    }

    // The colours are sRGB; vertex colours go to the shader as they are, and
    // the project renders in linear space.
    private void Vertex(Vector3 position, Color color)
    {
        vertices.Add(position);
        colors.Add(color.linear);
    }

    private void Triangle(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
    private static float EaseOut(float t) => 1 - Square(1 - t);
    private static float Square(float t) => t * t;

    // 0..1, the same every frame for the same seed and index.
    private static float Hash(int seed, int index)
    {
        unchecked
        {
            var h = (uint)(seed * 374761393 + index * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
