using System.Collections.Generic;
using UnityEngine;

// The octagonal janggi piece, built in code so its three sizes need no
// model assets: straight sides, a small bevel top and bottom, flat faces.
// Flats face the four board edges, as on a real set. Origin at the bottom
// center. The bottom bevel matters for play too: it's what lets a piece
// ride up over a board hinge instead of stopping dead against it.
public static class JanggiPieceMesh
{
    private const float BevelHeight = 0.18f; // of the thickness
    private const float BevelInset = 0.1f;   // of the radius

    private static readonly Dictionary<(float, float), Mesh> Cache = new Dictionary<(float, float), Mesh>();

    // width: across the flats. Circumradius of the result: width / 2 / cos(22.5deg).
    public static Mesh Get(float width, float height)
    {
        if (Cache.TryGetValue((width, height), out var cached) && cached != null) return cached;

        var r = Circumradius(width);
        var bottom = Ring(r * (1 - BevelInset), 0);
        var lower = Ring(r, height * BevelHeight);
        var upper = Ring(r, height * (1 - BevelHeight));
        var top = Ring(r * (1 - BevelInset), height);

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        // a -> b goes up the side, b -> c across it: that winding faces outward.
        // Four vertices of its own per face keep the normals flat.
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var i = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        for (var k = 0; k < 8; k++)
        {
            var n = (k + 1) % 8;
            Quad(bottom[k], lower[k], lower[n], bottom[n]);
            Quad(lower[k], upper[k], upper[n], lower[n]);
            Quad(upper[k], top[k], top[n], upper[n]);
        }
        var topCenter = vertices.Count;
        vertices.Add(new Vector3(0, height, 0));
        vertices.AddRange(top);
        var bottomCenter = vertices.Count;
        vertices.Add(Vector3.zero);
        vertices.AddRange(bottom);
        for (var k = 0; k < 8; k++)
        {
            var n = (k + 1) % 8;
            triangles.AddRange(new[] { topCenter, topCenter + 1 + n, topCenter + 1 + k });
            triangles.AddRange(new[] { bottomCenter, bottomCenter + 1 + k, bottomCenter + 1 + n });
        }

        var mesh = new Mesh { name = $"JanggiPiece {width:0.00}x{height:0.00}" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Cache[(width, height)] = mesh;
        return mesh;
    }

    public static float Circumradius(float width) => width / 2f / Mathf.Cos(Mathf.PI / 8);

    private static Vector3[] Ring(float radius, float y)
    {
        var ring = new Vector3[8];
        for (var k = 0; k < 8; k++)
        {
            // Vertices at 22.5deg + k*45deg, so the flats face +-x and +-z.
            var angle = Mathf.PI / 8 + k * Mathf.PI / 4;
            ring[k] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }
        return ring;
    }
}
