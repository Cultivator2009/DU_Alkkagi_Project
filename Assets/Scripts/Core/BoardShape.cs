using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// One convex piece of a board's surface: its corners in (x, z), seen from
// above, anticlockwise.
[Serializable]
public struct BoardPart
{
    public Vector2[] corners;

    public static BoardPart Rect(Rect rect) => new BoardPart
    {
        corners = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) },
    };

    // A regular polygon of n corners round the centre, the first at angle
    // (degrees, anticlockwise from +x).
    public static BoardPart Regular(int n, float circumradius, float firstAngle) => new BoardPart
    {
        corners = Enumerable.Range(0, n).Select(i =>
        {
            var a = (firstAngle + 360f * i / n) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * circumradius;
        }).ToArray(),
    };
}

// A board's playing surface seen from above: the union of convex parts (a
// plain board one rectangle, the hexagon one, the cross two overlapping
// bars). The questions the game asks of an edge - where a shot leaves the
// board, how far a piece is from falling, what's in reach - go through
// here, so nothing assumes a rectangle. Inset gives the board as it
// crumbles in from the edge (BoardZone).
public sealed class BoardShape
{
    private readonly Vector2[][] parts;

    public BoardShape(IEnumerable<BoardPart> parts) : this(parts.Select(p => p.corners)) { }

    private BoardShape(IEnumerable<Vector2[]> parts)
    {
        this.parts = parts.Where(p => p != null && p.Length >= 3).ToArray();
    }

    // Convex polygons, anticlockwise, as they are.
    public static BoardShape Of(IEnumerable<Vector2[]> polygons) => new BoardShape(polygons);

    public IReadOnlyList<Vector2[]> Parts => parts;
    public bool IsEmpty => parts.Length == 0;

    public Rect Bounds
    {
        get
        {
            if (IsEmpty) return new Rect();
            var all = parts.SelectMany(p => p).ToArray();
            return Rect.MinMaxRect(all.Min(p => p.x), all.Min(p => p.y), all.Max(p => p.x), all.Max(p => p.y));
        }
    }

    // Every edge moved in by distance (a part too small for it drops out).
    public BoardShape Inset(float distance)
    {
        var inset = new List<Vector2[]>();
        foreach (var part in parts)
        {
            var n = part.Length;
            var corners = new Vector2[n];
            var ok = true;
            for (var i = 0; i < n && ok; i++)
            {
                // Where the edges either side of corner i meet, each moved in.
                var a0 = part[(i + n - 1) % n];
                var a1 = part[i];
                var b1 = part[(i + 1) % n];
                var na = Inward(a0, a1);
                var nb = Inward(a1, b1);
                ok = Intersect(a0 + na * distance, a1 + na * distance, a1 + nb * distance, b1 + nb * distance, out corners[i]);
            }
            // Every edge still runs the way it did and the part still has an
            // area: the inset didn't eat it (past that, the corners cross
            // over into a smaller copy turned inside out).
            for (var i = 0; i < n && ok; i++) ok = Vector2.Dot(corners[(i + 1) % n] - corners[i], part[(i + 1) % n] - part[i]) > 0;
            if (ok && Area(corners) > 1e-4f) inset.Add(corners);
        }
        return new BoardShape(inset);
    }

    // What lies on one side of a line: where Dot(point, normal) is at least
    // offset. Each part is cut along it; one wholly past it goes.
    public BoardShape Clip(Vector2 normal, float offset)
    {
        var kept = new List<Vector2[]>();
        foreach (var part in parts)
        {
            var cut = new List<Vector2>();
            for (var i = 0; i < part.Length; i++)
            {
                var a = part[i];
                var b = part[(i + 1) % part.Length];
                var da = Vector2.Dot(a, normal) - offset;
                var db = Vector2.Dot(b, normal) - offset;
                if (da >= 0) cut.Add(a);
                if ((da >= 0) != (db >= 0)) cut.Add(a + (b - a) * (da / (da - db)));
            }
            if (cut.Count >= 3 && Area(cut.ToArray()) > 1e-6f) kept.Add(cut.ToArray());
        }
        return new BoardShape(kept);
    }

    // Inside some part, at least margin from its edges.
    public bool Contains(Vector2 point, float margin = 0f) => parts.Any(part => Depth(part, point) >= margin);

    // How far inside: the most any part holds it by (0 outside).
    public float Margin(Vector2 point)
    {
        var best = 0f;
        foreach (var part in parts) best = Mathf.Max(best, Depth(part, point));
        return best;
    }

    // How far from point along direction until the board ends (0 if point
    // isn't on it). Parts overlapping along the way count as one surface.
    public float Exit(Vector2 point, Vector2 direction)
    {
        if (direction.sqrMagnitude < 1e-12f) return 0f;
        direction.Normalize();
        var spans = new List<(float from, float to)>();
        foreach (var part in parts)
            if (Clip(part, point, direction, out var from, out var to) && to > 0) spans.Add((from, to));
        var reach = 0f;
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (from, to) in spans)
            {
                if (from > reach + 1e-5f || to <= reach) continue;
                reach = to;
                grew = true;
            }
        }
        return reach;
    }

    // The nearest point of the board to point (point itself when it's on it).
    public Vector2 Closest(Vector2 point)
    {
        if (Contains(point)) return point;
        var best = point;
        var bestDistance = float.MaxValue;
        foreach (var part in parts)
        {
            for (var i = 0; i < part.Length; i++)
            {
                var a = part[i];
                var b = part[(i + 1) % part.Length];
                var ab = b - a;
                var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
                var candidate = a + ab * t;
                var distance = (candidate - point).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    // A convex part is an axis-aligned rectangle (a box collider will do).
    public static bool IsRect(Vector2[] part, out Rect rect)
    {
        rect = Rect.MinMaxRect(part.Min(p => p.x), part.Min(p => p.y), part.Max(p => p.x), part.Max(p => p.y));
        if (part.Length != 4) return false;
        foreach (var p in part)
        {
            var onX = Mathf.Abs(p.x - rect.xMin) < 1e-4f || Mathf.Abs(p.x - rect.xMax) < 1e-4f;
            var onY = Mathf.Abs(p.y - rect.yMin) < 1e-4f || Mathf.Abs(p.y - rect.yMax) < 1e-4f;
            if (!onX || !onY) return false;
        }
        return true;
    }

    // ---- Convex polygon helpers ----

    private static Vector2 Inward(Vector2 a, Vector2 b)
    {
        var edge = (b - a).normalized;
        return new Vector2(-edge.y, edge.x); // left of an anticlockwise edge is inside
    }

    // How far inside the part the point is: its distance to the nearest
    // edge, negative outside.
    private static float Depth(Vector2[] part, Vector2 point)
    {
        var depth = float.MaxValue;
        for (var i = 0; i < part.Length; i++)
        {
            var a = part[i];
            var b = part[(i + 1) % part.Length];
            depth = Mathf.Min(depth, Vector2.Dot(point - a, Inward(a, b)));
        }
        return depth;
    }

    // Where the line point + t direction is inside the part, t from..to.
    private static bool Clip(Vector2[] part, Vector2 point, Vector2 direction, out float from, out float to)
    {
        from = float.MinValue;
        to = float.MaxValue;
        for (var i = 0; i < part.Length; i++)
        {
            var a = part[i];
            var normal = Inward(a, part[(i + 1) % part.Length]);
            var distance = Vector2.Dot(point - a, normal); // > 0 inside
            var rate = Vector2.Dot(direction, normal);
            if (Mathf.Abs(rate) < 1e-9f)
            {
                if (distance < 0) return false;
                continue;
            }
            var t = -distance / rate;
            if (rate > 0) from = Mathf.Max(from, t);
            else to = Mathf.Min(to, t);
        }
        return from <= to;
    }

    private static bool Intersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out Vector2 point)
    {
        var da = a1 - a0;
        var db = b1 - b0;
        var cross = da.x * db.y - da.y * db.x;
        if (Mathf.Abs(cross) < 1e-9f)
        {
            point = a1; // straight on: the edges are one line
            return true;
        }
        var t = ((b0.x - a0.x) * db.y - (b0.y - a0.y) * db.x) / cross;
        point = a0 + da * t;
        return true;
    }

    private static float Area(Vector2[] polygon)
    {
        var area = 0f;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            area += a.x * b.y - b.x * a.y;
        }
        return area / 2;
    }
}
