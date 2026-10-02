using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A piece's outline seen from above, as it stands: the convex hull of its
// colliders, flattened onto the board, round its position. Placing by hand
// keeps pieces this far apart rather than a circle round each: an
// octagonal janggi piece goes flat to flat with its neighbour, a gonggi
// stone's corner into the notch beside another's.
public static class Footprint
{
    // Taken where the piece stands now (it only ever moves sideways while
    // being placed).
    public static Vector2[] Of(GameObject piece)
    {
        var origin = piece.transform.position;
        var points = new List<Vector2>();
        foreach (var collider in piece.GetComponentsInChildren<MeshCollider>(true))
        {
            if (collider.isTrigger || collider.sharedMesh == null) continue;
            foreach (var vertex in collider.sharedMesh.vertices)
            {
                var world = collider.transform.TransformPoint(vertex) - origin;
                points.Add(new Vector2(world.x, world.z));
            }
        }
        return Hull(points);
    }

    // Whether the two outlines, at those places, are at least gap apart:
    // some line between them (an edge's normal, for convex outlines) leaves
    // that much room.
    public static bool Apart(Vector2[] a, Vector2 atA, Vector2[] b, Vector2 atB, float gap)
    {
        return HasGap(a, atA, b, atB, gap) || HasGap(b, atB, a, atA, gap);
    }

    private static bool HasGap(Vector2[] edges, Vector2 atEdges, Vector2[] other, Vector2 atOther, float gap)
    {
        for (var i = 0; i < edges.Length; i++)
        {
            var edge = edges[(i + 1) % edges.Length] - edges[i];
            var normal = new Vector2(edge.y, -edge.x).normalized; // outward, for an anticlockwise outline
            var (minA, maxA) = Project(edges, atEdges, normal);
            var (minB, maxB) = Project(other, atOther, normal);
            if (minB - maxA >= gap || minA - maxB >= gap) return true;
        }
        return false;
    }

    private static (float min, float max) Project(Vector2[] outline, Vector2 at, Vector2 axis)
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var p in outline)
        {
            var d = Vector2.Dot(p + at, axis);
            min = Mathf.Min(min, d);
            max = Mathf.Max(max, d);
        }
        return (min, max);
    }

    // Anticlockwise convex hull (monotone chain).
    private static Vector2[] Hull(List<Vector2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        if (sorted.Count < 3) return sorted.ToArray();
        var hull = new List<Vector2>();
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull.ToArray();
    }
}
