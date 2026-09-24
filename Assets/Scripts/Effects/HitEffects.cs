using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The cartoon burst where two pieces knock together: an ink-outlined star
// that pops and shrinks, ink strokes flying out of it and a ring spreading
// over the board. A harder knock makes it bigger, with more strokes. It goes
// off with the knock's sound (BoardSounds), so a network guest sees it where
// and when it hears it. Like a comic effect it's drawn over everything, all
// bursts in one mesh rebuilt each frame.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HitEffects : MonoBehaviour
{
    public static HitEffects Instance { get; private set; }

    public float smallSize = 0.07f; // star radius of the softest knock, in world units (a go stone is 0.1)
    public float largeSize = 0.18f; // and of a full-strength one
    public float starFrom = 0.3f;   // knocks softer than this get only the ring
    public int maxBursts = 24;

    // The HUD's ink, a warm yellow and hanji white (sRGB).
    private static readonly Color Ink = new Color32(0x2E, 0x24, 0x1A, 0xFF);
    private static readonly Color Fill = new Color32(0xFF, 0xD2, 0x3F, 0xFF);
    private static readonly Color Core = new Color32(0xFF, 0xFB, 0xEE, 0xFF);

    private struct Burst
    {
        public Vector3 At;
        public float Strength; // 0..1
        public float Age;
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
        if (bursts.Count >= maxBursts) bursts.RemoveAt(0);
        bursts.Add(new Burst
        {
            At = at,
            Strength = Mathf.InverseLerp(0.15f, 1f, volume),
            Spin = Random.value * Mathf.PI * 2,
            Seed = Random.Range(0, 1 << 20),
        });
    }

    private static float RingLife(float strength) => 0.28f + 0.08f * strength;
    private static float StrokeLife(float strength) => 0.2f + 0.06f * strength;
    private static float StarLife(float strength) => 0.16f + 0.06f * strength;

    // Game time: the bursts keep the match's pace and hold still while it's paused.
    private void LateUpdate()
    {
        for (var i = bursts.Count - 1; i >= 0; i--)
        {
            var burst = bursts[i];
            burst.Age += Time.deltaTime;
            if (burst.Age >= RingLife(burst.Strength)) bursts.RemoveAt(i);
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
        foreach (var burst in bursts) Draw(burst, right, up);
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000); // never culled
    }

    // Ring first, then the strokes, then the star over them. The star and
    // strokes face the camera; the ring lies on the board.
    private void Draw(Burst burst, Vector3 right, Vector3 up)
    {
        var k = burst.Strength;
        var size = Mathf.Lerp(smallSize, largeSize, k);

        var u = burst.Age / RingLife(k);
        Ring(burst.At, size * (0.8f + 1.2f * EaseOut(u)), size * 0.1f * (1 - u) + 0.003f, WithAlpha(Ink, 0.5f * (1 - u)));
        if (k < starFrom) return;

        u = burst.Age / StrokeLife(k);
        if (u < 1)
        {
            var count = 5 + Mathf.RoundToInt(3 * k);
            var head = size * (1.3f + 1.3f * EaseOut(u));
            var length = size * 0.8f * (1 - u);
            var width = size * 0.16f * (1 - 0.8f * u);
            for (var i = 0; i < count; i++)
            {
                var angle = burst.Spin + (i + 0.5f + (Hash(burst.Seed, 100 + i) - 0.5f) * 0.5f) * Mathf.PI * 2 / count;
                var direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var across = up * Mathf.Cos(angle) - right * Mathf.Sin(angle);
                Stroke(burst.At + direction * (head - length), burst.At + direction * head, across, width, width * 0.3f, Ink);
            }
        }

        u = burst.Age / StarLife(k);
        if (u < 1)
        {
            // Pops out past full size, then shrinks away.
            var pop = u < 0.2f ? Mathf.Lerp(0.55f, 1.1f, u / 0.2f) : 1.1f * (1 - Square((u - 0.2f) / 0.8f));
            var radius = size * pop;
            Star(burst.At, right, up, radius * 1.25f, 0.5f, burst.Spin, burst.Seed, Ink);
            Star(burst.At, right, up, radius, 0.5f, burst.Spin, burst.Seed, Fill);
            Star(burst.At, right, up, radius * 0.52f, 0.55f, burst.Spin, burst.Seed, Core);
        }
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
