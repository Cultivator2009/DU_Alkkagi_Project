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
}
