using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// How the crumbling edge (ZoneRule) looks. Announced, the ground it will
// take glows seal red, pulsing, just above the board; as it gives way that
// ground breaks into chunks that tip outward off the edge and shrink away.
// The board itself shrinks at once (BoardVariant.Build); the chunks are
// only a picture of it, touching nothing. Every screen draws it from the
// same events, a guest's mirror included.
public class BoardZoneView : MonoBehaviour
{
    public float chunkLength = 0.3f;   // along the edge
    public float chunkSeconds = 0.8f;
    public float warnAlpha = 0.65f;    // at the pulse's height; half of it at the low
    public float warnPulse = 1f;       // pulses a second

    private static readonly Color Seal = new Color32(0xB3, 0x31, 0x2A, 0xFF);

    private struct Chunk
    {
        public Transform Piece;
        public Vector3 Base;   // where it broke off
        public Vector3 Out;    // level, off the board
        public Vector3 Axis;   // it tips about this
        public float Delay;
    }

    private BoardSetup board;
    private TurnController turns;
    private MeshRenderer warning;
    private Material warningMaterial;
    private readonly List<Chunk> chunks = new List<Chunk>();
    private float crumbledAt = -1;
    private BoardShape before;

    public void Init(BoardSetup setup, TurnController controller)
    {
        board = setup;
        turns = controller;
        turns.OnZoneChanged += Refresh;
        warningMaterial = new Material(Shader.Find("Sprites/Default"));
        var go = new GameObject("ZoneWarning");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>();
        warning = go.AddComponent<MeshRenderer>();
        warning.sharedMaterial = warningMaterial;
        warning.shadowCastingMode = ShadowCastingMode.Off;
        warning.receiveShadows = false;
        warning.enabled = false;
        before = board.Playable;
    }

    private void OnDestroy()
    {
        if (turns != null) turns.OnZoneChanged -= Refresh;
        if (warningMaterial != null) Destroy(warningMaterial);
        ClearChunks();
    }

    private void Refresh()
    {
        var now = board.Playable;
        if (turns.Zone.Stage > 0 && now != before && before != null) Break(before, now);
        before = now;
        warning.enabled = turns.Zone.Warned;
        if (!warning.enabled) return;
        var filter = warning.GetComponent<MeshFilter>();
        if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
        filter.sharedMesh = FlatMesh(Band(now, now.Inset(ZoneRule.Step)), 0.004f);
    }

    private void Update()
    {
        if (warning != null && warning.enabled)
        {
            var a = warnAlpha * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * warnPulse * Mathf.PI * 2));
            warningMaterial.color = new Color(Seal.r, Seal.g, Seal.b, a);
        }
        if (crumbledAt < 0) return;
        var age = Time.time - crumbledAt;
        foreach (var chunk in chunks)
        {
            var t = Mathf.Clamp01((age - chunk.Delay) / chunkSeconds);
            var eased = t * t;
            chunk.Piece.localPosition = chunk.Base + chunk.Out * (0.35f * t) + Vector3.down * (0.45f * eased);
            chunk.Piece.localRotation = Quaternion.AngleAxis(55f * eased, chunk.Axis);
            chunk.Piece.localScale = Vector3.one * (1f - 0.9f * eased);
        }
        if (age < chunkSeconds + 0.4f) return;
        ClearChunks();
        crumbledAt = -1;
    }

    private void ClearChunks()
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Piece == null) continue;
            foreach (var filter in chunk.Piece.GetComponentsInChildren<MeshFilter>()) Destroy(filter.sharedMesh);
            Destroy(chunk.Piece.gameObject);
        }
        chunks.Clear();
    }

    // The ground lost from outer to inner, in chunks along the edge that tip off it.
    private void Break(BoardShape outer, BoardShape inner)
    {
        ClearChunks();
        var variant = board.Active;
        foreach (var polygon in Band(outer, inner).SelectMany(Split))
        {
            var centre = polygon.Aggregate(Vector2.zero, (sum, p) => sum + p) / polygon.Length;
            // Pivot at its middle, so it tips and shrinks about itself; the
            // mesh is in the board's coordinates, moved back under it.
            var pivot = new GameObject("Chunk").transform;
            pivot.SetParent(transform, false);
            pivot.position = variant.transform.position + new Vector3(centre.x, 0, centre.y);
            var holder = new GameObject("Mesh").transform;
            holder.SetParent(pivot, false);
            holder.localPosition = new Vector3(-centre.x, 0, -centre.y);
            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = ChunkMesh(polygon, variant);
            var renderer = holder.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { variant.topMaterial, variant.sideMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            // Off the board: away from what's left of it.
            var away2 = inner.IsEmpty ? centre : centre - inner.Closest(centre);
            var away = new Vector3(away2.x, 0, away2.y);
            away = away.sqrMagnitude > 1e-8f ? away.normalized : Vector3.forward;
            chunks.Add(new Chunk
            {
                Piece = pivot,
                Base = pivot.localPosition,
                Out = away,
                Axis = Vector3.Cross(Vector3.up, away),
                Delay = Random.Range(0f, 0.25f),
            });
        }
        crumbledAt = Time.time;
    }

    // What's in outer but not in inner, as convex pieces: for a rectangular
    // part the four strips round its inset less what the other parts still
    // hold; for any other the strip under each edge.
    private static List<Vector2[]> Band(BoardShape outer, BoardShape inner)
    {
        var band = new List<Vector2[]>();
        var kept = inner.Parts.Select(p => BoardShape.IsRect(p, out var r) ? (Rect?)r : null).ToList();
        for (var k = 0; k < outer.Parts.Count; k++)
        {
            var part = outer.Parts[k];
            var shrunk = new BoardShape(new[] { new BoardPart { corners = part } }).Inset(ZoneRule.Step);
            if (BoardShape.IsRect(part, out var rect))
            {
                var strips = shrunk.IsEmpty ? new List<Rect> { rect } : Subtract(rect, RectOf(shrunk.Parts[0])).ToList();
                foreach (var hold in kept.Where(h => h.HasValue))
                    strips = strips.SelectMany(s => Subtract(s, hold.Value)).ToList();
                band.AddRange(strips.Where(s => s.width > 1e-3f && s.height > 1e-3f).Select(s => BoardPart.Rect(s).corners));
                continue;
            }
            if (shrunk.IsEmpty)
            {
                band.Add(part);
                continue;
            }
            var core = shrunk.Parts[0];
            for (var i = 0; i < part.Length; i++)
            {
                var j = (i + 1) % part.Length;
                band.Add(new[] { part[i], part[j], core[j], core[i] });
            }
        }
        return band;
    }

    private static Rect RectOf(Vector2[] part)
    {
        BoardShape.IsRect(part, out var rect);
        return rect;
    }

    private static IEnumerable<Rect> Subtract(Rect a, Rect b)
    {
        if (!a.Overlaps(b))
        {
            yield return a;
            yield break;
        }
        if (b.yMin > a.yMin) yield return Rect.MinMaxRect(a.xMin, a.yMin, a.xMax, b.yMin);
        if (b.yMax < a.yMax) yield return Rect.MinMaxRect(a.xMin, b.yMax, a.xMax, a.yMax);
        var yMin = Mathf.Max(a.yMin, b.yMin);
        var yMax = Mathf.Min(a.yMax, b.yMax);
        if (b.xMin > a.xMin) yield return Rect.MinMaxRect(a.xMin, yMin, b.xMin, yMax);
        if (b.xMax < a.xMax) yield return Rect.MinMaxRect(b.xMax, yMin, a.xMax, yMax);
    }

    // A strip cut into chunks about chunkLength long, along its longer way
    // (a quad's first and third edges run along the board's edge).
    private IEnumerable<Vector2[]> Split(Vector2[] polygon)
    {
        if (polygon.Length != 4)
        {
            yield return polygon;
            yield break;
        }
        var a = polygon[1] - polygon[0];
        var b = polygon[2] - polygon[1];
        var alongFirst = a.magnitude >= b.magnitude;
        var p0 = alongFirst ? polygon[0] : polygon[1];
        var p1 = alongFirst ? polygon[1] : polygon[2];
        var p2 = alongFirst ? polygon[2] : polygon[3];
        var p3 = alongFirst ? polygon[3] : polygon[0];
        var n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max((p1 - p0).magnitude, (p2 - p3).magnitude) / chunkLength));
        for (var i = 0; i < n; i++)
        {
            float t0 = (float)i / n, t1 = (float)(i + 1) / n;
            yield return new[] { Vector2.Lerp(p0, p1, t0), Vector2.Lerp(p0, p1, t1), Vector2.Lerp(p3, p2, t1), Vector2.Lerp(p3, p2, t0) };
        }
    }

    private static Mesh FlatMesh(List<Vector2[]> polygons, float height)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (var polygon in polygons)
        {
            var first = vertices.Count;
            vertices.AddRange(polygon.Select(p => new Vector3(p.x, height, p.y)));
            for (var i = 1; i < polygon.Length - 1; i++) triangles.AddRange(new[] { first, first + i + 1, first + i });
        }
        var mesh = new Mesh { name = "Zone warning" };
        mesh.SetVertices(vertices);
        mesh.SetColors(Enumerable.Repeat(Color.white, vertices.Count).ToList()); // the tint comes from the material
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // A chunk of board: its top with the board's texture where it was, and its sides.
    private static Mesh ChunkMesh(Vector2[] polygon, BoardVariant variant)
    {
        var uv = variant.uvRect;
        var thickness = variant.thickness;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tops = new List<int>();
        var sides = new List<int>();
        foreach (var p in polygon)
        {
            vertices.Add(new Vector3(p.x, 0, p.y));
            uvs.Add(new Vector2((p.x - uv.xMin) / uv.width, (p.y - uv.yMin) / uv.height));
        }
        for (var i = 1; i < polygon.Length - 1; i++) tops.AddRange(new[] { 0, i + 1, i });
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var at = vertices.Count;
            vertices.Add(new Vector3(a.x, 0, a.y));
            vertices.Add(new Vector3(b.x, 0, b.y));
            vertices.Add(new Vector3(b.x, -thickness, b.y));
            vertices.Add(new Vector3(a.x, -thickness, a.y));
            uvs.AddRange(new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) });
            sides.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
        }
        var mesh = new Mesh { name = "Board chunk" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(tops, 0);
        mesh.SetTriangles(sides, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
