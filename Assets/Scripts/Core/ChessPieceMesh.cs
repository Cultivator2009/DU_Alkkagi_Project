using System.Collections.Generic;
using UnityEngine;

public enum ChessKind : byte
{
    King,
    Queen,
    Rook,
    Bishop,
    Knight,
    Pawn
}

// Staunton chess pieces, built in code like the janggi pieces so they need
// no model assets. Sizes are FIDE's (king 9.5 cm tall down to the pawn's 5,
// bases 40-57% of that) at the game's scale, where the go board's 45.45 cm
// is three units - the go stones already sat at their real 22.5 mm there.
// Each piece is its profile turned round the upright axis, plus what isn't
// round: the rook's battlements, the king's cross and the knight's head (a
// silhouette given thickness, facing +z). Origin at the bottom centre.
public static class ChessPieceMesh
{
    public const float UnitsPerCm = 3f / 45.45f;
    private const int Segments = 24;
    private const float CreaseDegrees = 40f; // sharper profile corners than this stay sharp

    private static readonly Dictionary<ChessKind, Mesh> Cache = new Dictionary<ChessKind, Mesh>();
    private static readonly Dictionary<ChessKind, float> Volumes = new Dictionary<ChessKind, float>();

    public static float HeightCm(ChessKind kind) => kind switch
    {
        ChessKind.King => 9.5f,
        ChessKind.Queen => 8.5f,
        ChessKind.Bishop => 7f,
        ChessKind.Knight => 6f,
        ChessKind.Rook => 5.5f,
        _ => 5f,
    };

    // A pawn's base is half a 5.7 cm square, as FIDE has it.
    public static float BaseRadiusCm(ChessKind kind) => kind switch
    {
        ChessKind.King => 2.14f,
        ChessKind.Queen => 1.9f,
        ChessKind.Pawn => 1.425f,
        _ => 1.65f,
    };

    public static float Height(ChessKind kind) => HeightCm(kind) * UnitsPerCm;
    public static float BaseRadius(ChessKind kind) => BaseRadiusCm(kind) * UnitsPerCm;

    // In cubic units, of the mesh as built (so its mass can go with it).
    public static float Volume(ChessKind kind)
    {
        Get(kind);
        return Volumes[kind];
    }

    public static Mesh Get(ChessKind kind)
    {
        if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;
        var builder = new Builder();
        switch (kind)
        {
            case ChessKind.Pawn: BuildPawn(builder); break;
            case ChessKind.Rook: BuildRook(builder); break;
            case ChessKind.Bishop: BuildBishop(builder); break;
            case ChessKind.Knight: BuildKnight(builder); break;
            case ChessKind.Queen: BuildQueen(builder); break;
            default: BuildKing(builder); break;
        }
        var mesh = builder.ToMesh("Chess " + kind);
        Cache[kind] = mesh;
        Volumes[kind] = builder.Volume();
        return mesh;
    }

    // ---- The pieces, in cm: (radius, height) from the bottom centre up ----

    // Base, bead and collar every piece stands on, up to where the stem starts.
    private static List<Vector2> Foot(float r, float scale)
    {
        return new List<Vector2>
        {
            new Vector2(0, 0), new Vector2(r, 0), new Vector2(r, 0.22f * scale), new Vector2(r * 0.91f, 0.32f * scale),
            new Vector2(r * 0.91f, 0.46f * scale), new Vector2(r * 0.7f, 0.64f * scale), new Vector2(r * 0.6f, 0.76f * scale),
            new Vector2(r * 0.67f, 0.9f * scale), new Vector2(r * 0.67f, 1.0f * scale), new Vector2(r * 0.52f, 1.1f * scale),
        };
    }

    private static void Arc(List<Vector2> profile, Vector2 center, float radiusR, float radiusY, float fromDegrees, float toDegrees, int steps)
    {
        for (var i = 0; i <= steps; i++)
        {
            var a = Mathf.Lerp(fromDegrees, toDegrees, (float)i / steps) * Mathf.Deg2Rad;
            profile.Add(new Vector2(Mathf.Max(0, center.x + radiusR * Mathf.Cos(a)), center.y + radiusY * Mathf.Sin(a)));
        }
    }

    private static void BuildPawn(Builder b)
    {
        var p = Foot(1.425f, 1.45f);
        p.AddRange(new[] { new Vector2(0.5f, 2.5f), new Vector2(0.85f, 2.65f), new Vector2(0.85f, 2.8f), new Vector2(0.5f, 2.95f) });
        Arc(p, new Vector2(0, 3.9f), 1.05f, 1.05f, -60, 90, 10);
        b.Lathe(p);
    }

    private static void BuildRook(Builder b)
    {
        var p = Foot(1.65f, 1.5f);
        p.AddRange(new[]
        {
            new Vector2(0.85f, 3.7f), new Vector2(1.15f, 3.9f), new Vector2(1.25f, 4.2f), new Vector2(1.25f, 4.6f),
            new Vector2(0.95f, 4.6f), new Vector2(0.95f, 4.4f), new Vector2(0, 4.4f),
        });
        b.Lathe(p);
        // Six merlons round the rim, gaps between.
        for (var i = 0; i < 6; i++) b.Box(new Vector3(0, 5.05f, 1.1f), new Vector3(0.7f, 0.9f, 0.3f), i * 60f);
    }

    private static void BuildBishop(Builder b)
    {
        var p = Foot(1.65f, 1.6f);
        p.AddRange(new[] { new Vector2(0.55f, 3.7f), new Vector2(0.95f, 3.9f), new Vector2(0.95f, 4.1f), new Vector2(0.6f, 4.25f), new Vector2(0.62f, 4.33f) });
        Arc(p, new Vector2(0, 5.3f), 0.95f, 1.1f, -62, 88, 10); // the mitre
        p.Add(new Vector2(0.2f, 6.45f));
        Arc(p, new Vector2(0, 6.72f), 0.28f, 0.28f, -45, 90, 5);
        b.Lathe(p);
    }

    private static void BuildKnight(Builder b)
    {
        var p = Foot(1.65f, 1.55f);
        p.Add(new Vector2(0, 1.7f));
        b.Lathe(p);
        // The head and neck from the side, looking along +z: (z, height).
        var head = new[]
        {
            new Vector2(-1.05f, 1.65f), new Vector2(1.05f, 1.65f), new Vector2(1.15f, 2.3f), new Vector2(0.95f, 3.0f),
            new Vector2(0.75f, 3.45f), new Vector2(1.15f, 3.65f), new Vector2(1.7f, 3.95f), new Vector2(1.75f, 4.35f),
            new Vector2(1.35f, 4.75f), new Vector2(0.7f, 5.25f), new Vector2(0.35f, 5.75f), new Vector2(0.1f, 6.0f),
            new Vector2(-0.15f, 5.6f), new Vector2(-0.55f, 5.5f), new Vector2(-0.9f, 5.0f), new Vector2(-1.2f, 4.2f),
            new Vector2(-1.25f, 3.2f), new Vector2(-1.15f, 2.4f),
        };
        b.Extrude(head, 0.7f);
    }

    private static void BuildQueen(Builder b)
    {
        var p = Foot(1.9f, 1.75f);
        p.AddRange(new[]
        {
            new Vector2(0.7f, 4.9f), new Vector2(1.15f, 5.1f), new Vector2(1.15f, 5.35f), new Vector2(0.75f, 5.5f), new Vector2(0.85f, 5.65f),
            new Vector2(1.35f, 7.0f), new Vector2(1.4f, 7.15f), new Vector2(1.2f, 7.3f), new Vector2(0.85f, 7.45f), new Vector2(0.5f, 7.6f),
            new Vector2(0.3f, 7.75f),
        });
        Arc(p, new Vector2(0, 8.1f), 0.4f, 0.4f, -48, 90, 6);
        b.Lathe(p);
    }

    private static void BuildKing(Builder b)
    {
        var p = Foot(2.14f, 1.9f);
        p.AddRange(new[]
        {
            new Vector2(0.8f, 5.6f), new Vector2(1.25f, 5.8f), new Vector2(1.25f, 6.05f), new Vector2(0.85f, 6.2f), new Vector2(0.95f, 6.35f),
            new Vector2(1.35f, 7.6f), new Vector2(1.35f, 7.8f), new Vector2(1.05f, 7.95f), new Vector2(0.75f, 8.1f), new Vector2(0.45f, 8.2f),
            new Vector2(0, 8.2f),
        });
        b.Lathe(p);
        b.Box(new Vector3(0, 8.85f, 0), new Vector3(0.38f, 1.3f, 0.38f), 0); // the cross
        b.Box(new Vector3(0, 9.0f, 0), new Vector3(1.05f, 0.34f, 0.38f), 0);
    }

    // ---- Geometry ----

    private class Builder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        // Turns a (radius, height) profile round the y axis. Each profile
        // segment gets rings of its own, so the normals are smooth round the
        // piece and across gentle bends of the profile, sharp at hard ones.
        public void Lathe(List<Vector2> profile)
        {
            var count = profile.Count;
            // The outward normal of each segment in the (radius, height) plane.
            var segment = new Vector2[count - 1];
            for (var i = 0; i < count - 1; i++)
            {
                var d = profile[i + 1] - profile[i];
                segment[i] = d.sqrMagnitude > 1e-8f ? new Vector2(d.y, -d.x).normalized : Vector2.up;
            }
            for (var i = 0; i < count - 1; i++)
            {
                var n0 = segment[i];
                var n1 = segment[i];
                if (i > 0 && Vector2.Angle(segment[i - 1], segment[i]) < CreaseDegrees) n0 = (segment[i - 1] + segment[i]).normalized;
                if (i < count - 2 && Vector2.Angle(segment[i], segment[i + 1]) < CreaseDegrees) n1 = (segment[i] + segment[i + 1]).normalized;
                var first = vertices.Count;
                for (var s = 0; s <= Segments; s++)
                {
                    var angle = s * Mathf.PI * 2 / Segments;
                    var around = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Add(profile[i], n0, around);
                    Add(profile[i + 1], n1, around);
                }
                for (var s = 0; s < Segments; s++)
                {
                    var a = first + s * 2;
                    // Up the profile, then round: clockwise seen from outside.
                    triangles.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
                }
            }
        }

        private void Add(Vector2 point, Vector2 normal, Vector3 around)
        {
            vertices.Add((around * point.x + Vector3.up * point.y) * UnitsPerCm);
            normals.Add((around * normal.x + Vector3.up * normal.y).normalized);
        }

        // An upright box, turned about y by degrees round the piece's axis.
        public void Box(Vector3 center, Vector3 size, float degrees)
        {
            var turn = Quaternion.Euler(0, degrees, 0);
            var h = size * 0.5f;
            Vector3 Corner(float x, float y, float z) => turn * (center + new Vector3(x * h.x, y * h.y, z * h.z)) * UnitsPerCm;
            void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var i = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                var n = turn * normal;
                normals.AddRange(new[] { n, n, n, n });
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Face(Vector3.up, Corner(-1, 1, -1), Corner(-1, 1, 1), Corner(1, 1, 1), Corner(1, 1, -1));
            Face(Vector3.down, Corner(-1, -1, -1), Corner(1, -1, -1), Corner(1, -1, 1), Corner(-1, -1, 1));
            Face(Vector3.forward, Corner(-1, -1, 1), Corner(1, -1, 1), Corner(1, 1, 1), Corner(-1, 1, 1));
            Face(Vector3.back, Corner(-1, -1, -1), Corner(-1, 1, -1), Corner(1, 1, -1), Corner(1, -1, -1));
            Face(Vector3.right, Corner(1, -1, -1), Corner(1, 1, -1), Corner(1, 1, 1), Corner(1, -1, 1));
            Face(Vector3.left, Corner(-1, -1, -1), Corner(-1, -1, 1), Corner(-1, 1, 1), Corner(-1, 1, -1));
        }

        // A (z, height) outline, anticlockwise seen from +x, given thickness
        // along x: two flat faces and the band round them.
        public void Extrude(Vector2[] outline, float halfThickness)
        {
            var cap = Triangulate(outline);
            foreach (var side in new[] { 1f, -1f })
            {
                var first = vertices.Count;
                foreach (var point in outline)
                {
                    vertices.Add(new Vector3(side * halfThickness, point.y, point.x) * UnitsPerCm);
                    normals.Add(new Vector3(side, 0, 0));
                }
                for (var t = 0; t < cap.Count; t += 3)
                {
                    // Seen from +x the outline winds anticlockwise; the -x face flips it.
                    if (side > 0) triangles.AddRange(new[] { first + cap[t], first + cap[t + 2], first + cap[t + 1] });
                    else triangles.AddRange(new[] { first + cap[t], first + cap[t + 1], first + cap[t + 2] });
                }
            }
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Length];
                var edge = b - a;
                var normal = new Vector3(0, -edge.x, edge.y).normalized; // outward, for an anticlockwise outline
                var first = vertices.Count;
                vertices.Add(new Vector3(halfThickness, a.y, a.x) * UnitsPerCm);
                vertices.Add(new Vector3(-halfThickness, a.y, a.x) * UnitsPerCm);
                vertices.Add(new Vector3(-halfThickness, b.y, b.x) * UnitsPerCm);
                vertices.Add(new Vector3(halfThickness, b.y, b.x) * UnitsPerCm);
                normals.AddRange(new[] { normal, normal, normal, normal });
                triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
            }
        }

        // Ear clipping, for a simple anticlockwise polygon.
        private static List<int> Triangulate(Vector2[] polygon)
        {
            var result = new List<int>();
            var remaining = new List<int>();
            for (var i = 0; i < polygon.Length; i++) remaining.Add(i);
            var guard = 0;
            while (remaining.Count > 3 && guard++ < 1000)
            {
                for (var i = 0; i < remaining.Count; i++)
                {
                    var prev = remaining[(i + remaining.Count - 1) % remaining.Count];
                    var curr = remaining[i];
                    var next = remaining[(i + 1) % remaining.Count];
                    var a = polygon[prev];
                    var b = polygon[curr];
                    var c = polygon[next];
                    if (Cross(b - a, c - b) <= 0) continue; // reflex
                    var ear = true;
                    foreach (var other in remaining)
                    {
                        if (other == prev || other == curr || other == next) continue;
                        if (Inside(polygon[other], a, b, c))
                        {
                            ear = false;
                            break;
                        }
                    }
                    if (!ear) continue;
                    result.AddRange(new[] { prev, curr, next });
                    remaining.RemoveAt(i);
                    break;
                }
            }
            if (remaining.Count == 3) result.AddRange(remaining);
            return result;
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0;

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Signed tetrahedra from the origin; overlapping parts (a merlon on
        // the rim) count twice, which is close enough for a mass.
        public float Volume()
        {
            var sum = 0f;
            for (var t = 0; t < triangles.Count; t += 3)
            {
                var a = vertices[triangles[t]];
                var b = vertices[triangles[t + 1]];
                var c = vertices[triangles[t + 2]];
                sum += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
            }
            return Mathf.Abs(sum);
        }
    }
}
