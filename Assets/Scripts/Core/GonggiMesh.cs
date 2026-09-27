using System.Collections.Generic;
using UnityEngine;

// A gonggi stone, the plastic kind sold in stationery shops: a rounded,
// squat three-sided pyramid, 19 mm across and 13.5 mm tall, hollow and
// filled with steel shot. Built in code like the other pieces. The shape
// is four balls - three at the foot, one at the top - wrapped in one smooth
// skin: each direction out from the middle finds its point on the skin
// from the balls it faces most (a soft maximum), so the faces stay nearly
// flat and the corners and edges round off. Origin at the bottom centre,
// a corner pointing +z.
public static class GonggiMesh
{
    public const float WidthCm = 1.9f;  // at the widest, corner to corner
    public const float HeightCm = 1.35f;
    private const float CornerCm = 0.4f; // the balls' radius: how round it is
    private const float Sharpness = 25f; // per cm: higher, flatter faces and tighter edges

    private static Mesh cached;
    private static Mesh collider;
    private static float radius;

    public static float Height => HeightCm * ChessPieceMesh.UnitsPerCm;

    // From the middle out to a corner.
    public static float Radius
    {
        get
        {
            Get();
            return radius;
        }
    }

    public static Mesh Get()
    {
        if (cached != null) return cached;
        var balls = Balls();
        Sphere(3, out var directions, out var triangles);
        var vertices = new Vector3[directions.Count];
        var weights = new float[balls.Length];
        for (var v = 0; v < directions.Count; v++)
        {
            var d = directions[v];
            var top = float.MinValue;
            foreach (var ball in balls) top = Mathf.Max(top, Vector3.Dot(ball, d));
            var sum = 0f;
            for (var b = 0; b < balls.Length; b++) sum += weights[b] = Mathf.Exp(Sharpness * (Vector3.Dot(balls[b], d) - top));
            var point = d * CornerCm;
            for (var b = 0; b < balls.Length; b++) point += balls[b] * (weights[b] / sum);
            vertices[v] = point;
        }
        // The blend pulls the corners in a little: stretch back to the size.
        radius = Fit(vertices);

        cached = new Mesh { name = "Gonggi" };
        cached.SetVertices(vertices);
        cached.SetTriangles(triangles, 0);
        cached.RecalculateNormals();
        cached.RecalculateBounds();
        return cached;
    }

    // For the convex collider, which can't take the drawn skin's faces
    // (256 at most): the balls themselves, coarse, whose hull PhysX takes.
    // Each is turned to have a point straight down, so the foot is flat
    // and level.
    public static Mesh Collider()
    {
        if (collider != null) return collider;
        var balls = Balls();
        Sphere(1, out var directions, out var sphere);
        var turn = Quaternion.FromToRotation(directions[2], Vector3.down);
        for (var i = 0; i < directions.Count; i++) directions[i] = turn * directions[i];
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (var ball in balls)
        {
            foreach (var t in sphere) triangles.Add(vertices.Count + t);
            foreach (var d in directions) vertices.Add(ball + d * CornerCm);
        }
        var points = vertices.ToArray();
        Fit(points);
        collider = new Mesh { name = "Gonggi collider" };
        collider.SetVertices(points);
        collider.SetTriangles(triangles, 0);
        collider.RecalculateBounds();
        return collider;
    }

    private static Vector3[] Balls()
    {
        var balls = new Vector3[4];
        var foot = (WidthCm - 2 * CornerCm) / Mathf.Sqrt(3); // out to the foot balls' centres
        for (var i = 0; i < 3; i++)
        {
            var angle = Mathf.PI / 2 + i * Mathf.PI * 2 / 3;
            balls[i] = new Vector3(Mathf.Cos(angle) * foot, CornerCm, Mathf.Sin(angle) * foot);
        }
        balls[3] = new Vector3(0, HeightCm - CornerCm, 0);
        return balls;
    }

    // Scales cm points to exactly the size, bottom at 0, in game units.
    // Returns how far they reach out from the middle.
    private static float Fit(Vector3[] points)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        var reach = 0f;
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            reach = Mathf.Max(reach, new Vector2(p.x, p.z).magnitude);
        }
        var across = WidthCm / (max.x - min.x);
        var up = HeightCm / (max.y - min.y);
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            points[i] = new Vector3(p.x * across, (p.y - min.y) * up, p.z * across) * ChessPieceMesh.UnitsPerCm;
        }
        return reach * across * ChessPieceMesh.UnitsPerCm;
    }

    // A unit icosphere: evenly spread directions, sharing vertices, wound
    // clockwise seen from outside.
    private static void Sphere(int subdivisions, out List<Vector3> vertices, out List<int> triangles)
    {
        var t = (1 + Mathf.Sqrt(5)) / 2;
        vertices = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (var i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized;
        triangles = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        for (var s = 0; s < subdivisions; s++)
        {
            var middles = new Dictionary<long, int>();
            var list = vertices;
            int Middle(int a, int b)
            {
                var key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                if (middles.TryGetValue(key, out var index)) return index;
                list.Add(((list[a] + list[b]) / 2).normalized);
                return middles[key] = list.Count - 1;
            }
            var finer = new List<int>();
            for (var i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                finer.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            triangles = finer;
        }
    }
}
