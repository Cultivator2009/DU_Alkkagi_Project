using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The board's cartoon effects, all drawn over everything like a comic's,
// in one mesh rebuilt each frame:
// - a knock: an ink-outlined star that pops out, ink strokes flying off it
//   and a ring spreading over the board. How hard the knock was sets all of
//   it: a tap is a small star gone in a blink, a full-power hit a big one
//   that holds for a moment and then fades out, with more strokes and a
//   wider ring.
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

    public float smallSize = 0.05f; // star radius of the softest knock, in world units (a go stone is 0.1)
    public float largeSize = 0.2f;  // and of a full-strength one
    public float shortLife = 0.12f; // how long the star stays for the softest knock, in game seconds
    public float longLife = 0.75f;  // and for a full-strength one, its fade included
    public float starFrom = 0.1f;   // knocks softer than this get only the ring
    public float fallLife = 0.65f;
    public float flickLife = 0.3f;
    public float turnLife = 0.7f;
    public float turnStagger = 0.05f; // between one piece's turn ring and the next
    public int maxBursts = 48;

    // The HUD's ink, a warm yellow and hanji white (sRGB).
    private static readonly Color Ink = new Color32(0x2E, 0x24, 0x1A, 0xFF);
    private static readonly Color Fill = new Color32(0xFF, 0xD2, 0x3F, 0xFF);
    private static readonly Color Core = new Color32(0xFF, 0xFB, 0xEE, 0xFF);
    private static readonly Color Seal = new Color32(0xB3, 0x31, 0x2A, 0xFF);

    private enum Kind : byte { Hit, Fall, Flick, Turn }

    private struct Burst
    {
        public Kind Kind;
        public Vector3 At;
        public Vector3 Out;    // a fall: level, off the board
        public float Strength; // 0..1, with the knock's (or flick's) speed
        public float Size;     // a turn ring: the piece's radius
        public float Age;      // below zero until it starts
        public float Spin;     // radians
        public int Seed;       // the star's own spike lengths and stroke angles
    }

    private readonly List<Burst> bursts = new List<Burst>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private Camera view;

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
        Add(new Burst { Kind = Kind.Hit, At = at, Strength = Mathf.Pow(Mathf.InverseLerp(0.15f, 1f, volume), 1.6f) });
    }

    // at: where the piece was when it dropped below the board, just past an
    // edge. The blot goes on the edge it went over.
    public void PlayFall(Vector3 at)
    {
        var board = GameManager.manager != null ? GameManager.manager.Board : null;
        if (board == null) return;
        var surface = board.SurfaceBounds;
        var edge = new Vector3(Mathf.Clamp(at.x, surface.min.x, surface.max.x), surface.max.y, Mathf.Clamp(at.z, surface.min.z, surface.max.z));
        var away = at - surface.center;
        away.y = 0;
        Add(new Burst { Kind = Kind.Fall, At = edge, Out = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward });
    }

    // volume: the flick's loudness from BoardSounds, 0.35 for the softest.
    public void PlayFlick(Vector3 at, float volume)
    {
        Add(new Burst { Kind = Kind.Flick, At = at, Strength = Mathf.InverseLerp(0.35f, 1f, volume) });
    }

    // A side's turn has come: a ring out of each of its pieces, one after
    // another (index: the piece's place in that order).
    public void PlayTurn(Vector3 at, float radius, int index)
    {
        Add(new Burst { Kind = Kind.Turn, At = at, Size = radius, Age = -index * turnStagger });
    }

    private void Add(Burst burst)
    {
        if (bursts.Count >= maxBursts) bursts.RemoveAt(0);
        burst.Spin = Random.value * Mathf.PI * 2;
        burst.Seed = Random.Range(0, 1 << 20);
        bursts.Add(burst);
    }

    private float StarLife(float strength) => Mathf.Lerp(shortLife, longLife, strength);
    private static float StarFade(float strength) => Mathf.Lerp(0.07f, 0.35f, strength); // the end of StarLife
    private static float StrokeLife(float strength) => Mathf.Lerp(0.1f, 0.45f, strength);
    private static float RingLife(float strength) => Mathf.Lerp(0.16f, 0.6f, strength);

    private float Life(Burst burst) => burst.Kind switch
    {
        Kind.Fall => fallLife,
        Kind.Flick => flickLife,
        Kind.Turn => turnLife,
        _ => Mathf.Max(StarLife(burst.Strength), RingLife(burst.Strength)),
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
        if (view == null) view = Camera.main;
        if (view == null) return;

        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        var right = view.transform.right;
        var up = view.transform.up;
        // Front to back: the shader draws each pixel once (stencil), so what
        // goes in first is on top. The newest burst over older ones.
        for (var i = bursts.Count - 1; i >= 0; i--)
        {
            var burst = bursts[i];
            if (burst.Age < 0) continue; // not started
            switch (burst.Kind)
            {
                case Kind.Fall: DrawFall(burst, right, up); break;
                case Kind.Flick: DrawFlick(burst); break;
                case Kind.Turn: DrawTurn(burst); break;
                default: Draw(burst, right, up); break;
            }
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000); // never culled
    }

    // The star, then the strokes under it, then the ring under those (see
    // LateUpdate). The star and strokes face the camera; the ring lies on
    // the board. Everything fades out rather than just vanishing.
    private void Draw(Burst burst, Vector3 right, Vector3 up)
    {
        var k = burst.Strength;
        var age = burst.Age;
        var size = Mathf.Lerp(smallSize, largeSize, k);

        var life = StarLife(k);
        if (k >= starFrom && age < life)
        {
            // Pops out past full size and settles, holds, then fades while it
            // shrinks - a soft knock most of the way to nothing, a hard one
            // only a little.
            const float pop = 0.06f;
            var scale = age < pop ? Mathf.Lerp(0.5f, 1.12f, age / pop) : Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((age - pop) / pop));
            var fade = StarFade(k);
            var f = Mathf.Clamp01((age - (life - fade)) / fade);
            var radius = size * scale * Mathf.Lerp(1f, Mathf.Lerp(0.35f, 0.9f, k), f);
            var alpha = 1 - f;
            Star(burst.At, right, up, radius * 0.52f, 0.55f, burst.Spin, burst.Seed, WithAlpha(Core, alpha));
            Star(burst.At, right, up, radius, 0.5f, burst.Spin, burst.Seed, WithAlpha(Fill, alpha));
            Star(burst.At, right, up, radius * 1.25f, 0.5f, burst.Spin, burst.Seed, WithAlpha(Ink, alpha));
        }

        var u = age / StrokeLife(k);
        if (k >= starFrom && u < 1)
        {
            var count = 4 + Mathf.RoundToInt(4 * k);
            var head = size * (1.3f + 1.5f * EaseOut(u));
            var length = size * 0.8f * (1 - 0.7f * u);
            var width = size * 0.16f * (1 - 0.6f * u);
            var ink = WithAlpha(Ink, 1 - Square(u));
            for (var i = 0; i < count; i++)
            {
                var angle = burst.Spin + (i + 0.5f + (Hash(burst.Seed, 100 + i) - 0.5f) * 0.5f) * Mathf.PI * 2 / count;
                var direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var across = up * Mathf.Cos(angle) - right * Mathf.Sin(angle);
                Stroke(burst.At + direction * (head - length), burst.At + direction * head, across, width, width * 0.3f, ink);
            }
        }

        u = age / RingLife(k);
        if (u < 1) Ring(burst.At, size * (0.8f + 1.4f * EaseOut(u)), size * 0.1f * (1 - u) + 0.003f, WithAlpha(Ink, 0.5f * (1 - u)));
    }

    // Drops thrown off the edge (over the blot), the blot, and the seal-red
    // ring under both. The drops fly out in a fan away from the board and
    // shrink as they go.
    private void DrawFall(Burst burst, Vector3 right, Vector3 up)
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
            Disc(burst.At + direction * (0.05f + reach), right, up, radius, ink);
        }
        var swell = EaseOut(Mathf.Min(1, u * 3));
        Star(burst.At, right, up, 0.1f * (0.6f + 0.5f * swell), 0.72f, burst.Spin, burst.Seed, WithAlpha(Ink, 0.9f * Mathf.Pow(1 - u, 1.5f)));
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

    private void Disc(Vector3 at, Vector3 right, Vector3 up, float radius, Color color)
    {
        const int segments = 10;
        var center = vertices.Count;
        Vertex(at, color);
        for (var i = 0; i < segments; i++)
        {
            var angle = i * Mathf.PI * 2 / segments;
            Vertex(at + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius, color);
        }
        for (var i = 0; i < segments; i++) Triangle(center, center + 1 + i, center + 1 + (i + 1) % segments);
    }

    // Eight spikes of radius (each a little longer or shorter) and notches
    // between them at inner times radius.
    private void Star(Vector3 at, Vector3 right, Vector3 up, float radius, float inner, float spin, int seed, Color color)
    {
        const int points = 8;
        var center = vertices.Count;
        Vertex(at, color);
        for (var i = 0; i < points * 2; i++)
        {
            var angle = spin + i * Mathf.PI / points;
            var r = i % 2 == 0 ? radius * Mathf.Lerp(0.78f, 1.18f, Hash(seed, i)) : radius * inner;
            Vertex(at + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * r, color);
        }
        for (var i = 0; i < points * 2; i++) Triangle(center, center + 1 + i, center + 1 + (i + 1) % (points * 2));
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
