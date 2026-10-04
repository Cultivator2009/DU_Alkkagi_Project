using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// One playable board in GameScene, under Board_GO (Tools > Alkkagi > Build
// boards and piece templates). BoardSetup turns on the one the match rules
// pick and turns the others off.
//
// The board itself is its shape (parts, seen from above) made solid at run
// time: a top with its texture, sides, and a collider - a box for a
// rectangle, a convex prism for another single part, and for several (the
// cross's bars) one solid of their outline, so the top has no seams. Built
// again smaller as the edge crumbles (Build with an inset), the texture
// staying where it was.
public class BoardVariant : MonoBehaviour
{
    public BoardType type;
    public BoardPart[] parts;
    public float thickness = 0.5f;
    public Material topMaterial;
    public Material sideMaterial;
    public Rect uvRect;          // where the top's texture lies, in (x, z)
    public PhysicsMaterial physics;

    // Three or four sides' starting rows: pieces this far apart, a front row
    // and one behind it, this far in from the edge.
    public float multiSpacing = 0.32f;
    public float multiFront = 0.5f;
    public float multiBack = 0.2f;
    // Two sides start on BoardSetup.layouts (drawn for the go board) rather
    // than on rows of their own.
    public bool sceneLayouts = true;
    // Where each side sits, as it plays with two, three or four: degrees
    // anticlockwise from the south edge, seen from above, in turn order.
    public float[] seats2 = { 0, 180 };
    public float[] seats3 = { 0, 270, 90 };
    public float[] seats4 = { 0, 270, 180, 90 };

    private BoardShape shape;
    private BoardShape playable;

    public BoardShape Shape => shape ??= new BoardShape(parts);
    // What's left to play on: the shape, crumbled in by Build's inset.
    public BoardShape Playable => playable ?? Shape;
    public float Inset { get; private set; }

    public float[] Seats(int players) => players <= 2 ? seats2 : players == 3 ? seats3 : seats4;

    // The whole board's bounds (not the crumbled one's): the camera frames this.
    public Bounds Bounds
    {
        get
        {
            var rect = Shape.Bounds;
            var center = transform.position;
            return new Bounds(new Vector3(center.x + rect.center.x, center.y - thickness / 2, center.z + rect.center.y), new Vector3(rect.width, thickness, rect.height));
        }
    }

    // The board, inset from its edge by inset. Replaces whatever it built before.
    public void Build(float inset = 0f)
    {
        Inset = inset;
        playable = inset > 0 ? Shape.Inset(inset) : Shape;
        var old = transform.Find(SlabName);
        if (old != null)
        {
            // Its meshes are its own (made below), not assets: they go with it.
            var meshes = old.GetComponents<MeshCollider>().Select(c => c.sharedMesh).Append(old.GetComponent<MeshFilter>()?.sharedMesh);
            foreach (var mesh in meshes.Where(m => m != null).ToList()) Discard(mesh);
            Discard(old.gameObject);
        }
        var oldWalls = transform.Find(WallsName);
        if (oldWalls != null) Discard(oldWalls.gameObject);
        var slab = new GameObject(SlabName).transform;
        slab.SetParent(transform, false);
        slab.gameObject.layer = gameObject.layer;

        var filter = slab.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = Mesh(playable);
        var renderer = slab.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { topMaterial, sideMaterial };
        renderer.shadowCastingMode = ShadowCastingMode.On;

        // Where two parts' colliders met, a piece sliding from one onto the
        // other caught the other's edge and now and then came back.
        var outline = playable.Parts.Count > 1 ? Loop(playable.Outline()) : null;
        if (outline != null)
        {
            var solid = slab.gameObject.AddComponent<MeshCollider>();
            solid.sharedMesh = Solid(outline);
            solid.sharedMaterial = physics;
            return;
        }
        foreach (var part in playable.Parts)
        {
            if (BoardShape.IsRect(part, out var rect))
            {
                var box = slab.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(rect.center.x, -thickness / 2, rect.center.y);
                box.size = new Vector3(rect.width, thickness, rect.height);
                box.sharedMaterial = physics;
                continue;
            }
            var prism = slab.gameObject.AddComponent<MeshCollider>();
            prism.sharedMesh = Prism(part);
            prism.convex = true;
            prism.sharedMaterial = physics;
        }
    }

    private const string SlabName = "Slab";
    private const string WallsName = "Walls";
    public const string WallName = "Wall"; // a knock against one costs health (ImpactDamage) and is heard (PieceSounds)
    private static PhysicsMaterial wallPhysics;

    // Walls round the board's edge (MatchSettings.Walled): a low wooden rim
    // to see, standing outside the edge so the board keeps its size, and a
    // collider well over the tallest piece's height so nothing gets over.
    // Pieces bounce off them. Gone again with the next Build.
    public void BuildWalls()
    {
        const float thickness = 0.06f, rim = 0.045f, height = 0.8f;
        var walls = new GameObject(WallsName).transform;
        walls.SetParent(transform, false);
        walls.gameObject.layer = gameObject.layer;
        wallPhysics ??= new PhysicsMaterial("Wall") { bounciness = 0.5f, dynamicFriction = 0.3f, staticFriction = 0.3f, bounceCombine = PhysicsMaterialCombine.Average };
        foreach (var (a, b) in Shape.Outline())
        {
            var along = b - a;
            var outward = new Vector2(along.y, -along.x).normalized;
            var middle = (a + b) / 2 + outward * thickness / 2;
            var wall = new GameObject(WallName).transform;
            wall.SetParent(walls, false);
            wall.gameObject.layer = gameObject.layer;
            wall.localPosition = new Vector3(middle.x, 0, middle.y);
            wall.localRotation = Quaternion.LookRotation(new Vector3(outward.x, 0, outward.y));
            var box = wall.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, height / 2, 0);
            box.size = new Vector3(along.magnitude, height, thickness);
            box.sharedMaterial = wallPhysics;

            var look = GameObject.CreatePrimitive(PrimitiveType.Cube);
            look.name = "Rim";
            Discard(look.GetComponent<Collider>());
            look.transform.SetParent(wall, false);
            look.transform.localPosition = new Vector3(0, rim / 2, 0);
            look.transform.localScale = new Vector3(along.magnitude + thickness, rim, thickness);
            look.GetComponent<MeshRenderer>().sharedMaterial = sideMaterial;
        }
    }

    private static void Discard(Object thing)
    {
        if (Application.isPlaying) Destroy(thing);
        else DestroyImmediate(thing);
    }

    // The surface in pieces that don't overlap: where rectangular parts
    // overlap, a later one leaves out what an earlier one covers. For
    // drawing on the board (the top, the placement zones) without any bit
    // drawn twice.
    public static List<Vector2[]> TopPieces(BoardShape board)
    {
        var pieces = new List<Vector2[]>();
        var covered = new List<Rect>();
        foreach (var part in board.Parts)
        {
            if (!BoardShape.IsRect(part, out var rect))
            {
                pieces.Add(part);
                continue;
            }
            var left = new List<Rect> { rect };
            foreach (var earlier in covered) left = left.SelectMany(r => Subtract(r, earlier)).ToList();
            covered.Add(rect);
            pieces.AddRange(left.Select(r => BoardPart.Rect(r).corners));
        }
        return pieces;
    }

    // Tops (submesh 0, TopPieces) and sides (submesh 1). A side inside
    // another part is under its top, out of sight.
    private Mesh Mesh(BoardShape board)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tops = new List<int>();
        var sides = new List<int>();
        foreach (var piece in TopPieces(board))
        {
            var first = vertices.Count;
            foreach (var corner in piece)
            {
                vertices.Add(new Vector3(corner.x, 0, corner.y));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2((corner.x - uvRect.xMin) / uvRect.width, (corner.y - uvRect.yMin) / uvRect.height));
            }
            for (var i = 1; i < piece.Length - 1; i++) tops.AddRange(new[] { first, first + i + 1, first + i });
        }
        foreach (var part in board.Parts)
        {
            for (var i = 0; i < part.Length; i++)
            {
                var a = part[i];
                var b = part[(i + 1) % part.Length];
                var edge = b - a;
                var outward = new Vector3(edge.y, 0, -edge.x).normalized;
                var at = vertices.Count;
                var length = edge.magnitude;
                vertices.Add(new Vector3(a.x, 0, a.y));
                vertices.Add(new Vector3(b.x, 0, b.y));
                vertices.Add(new Vector3(b.x, -thickness, b.y));
                vertices.Add(new Vector3(a.x, -thickness, a.y));
                for (var k = 0; k < 4; k++) normals.Add(outward);
                uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(length / thickness, 1));
                uvs.Add(new Vector2(length / thickness, 0));
                uvs.Add(new Vector2(0, 0));
                sides.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
            }
        }
        var mesh = new Mesh { name = "Board " + type };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(tops, 0);
        mesh.SetTriangles(sides, 1);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // What's left of a when b is cut out of it: up to four rectangles.
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

    // The outline's edges joined into one loop of corners, anticlockwise,
    // straight runs merged. Null unless they make exactly one loop.
    private static List<Vector2> Loop(List<(Vector2 a, Vector2 b)> edges)
    {
        if (edges.Count < 3) return null;
        var left = edges.Skip(1).ToList();
        var loop = new List<Vector2> { edges[0].a };
        var at = edges[0].b;
        while (left.Count > 0)
        {
            var next = left.FindIndex(e => (e.a - at).sqrMagnitude < 1e-8f);
            if (next < 0) return null;
            loop.Add(at);
            at = left[next].b;
            left.RemoveAt(next);
        }
        if ((at - loop[0]).sqrMagnitude > 1e-8f) return null;
        for (var i = 0; i < loop.Count && loop.Count > 3;)
        {
            var before = loop[(i + loop.Count - 1) % loop.Count];
            var after = loop[(i + 1) % loop.Count];
            if (Mathf.Abs(Cross(loop[i] - before, after - loop[i])) < 1e-7f) loop.RemoveAt(i);
            else i++;
        }
        var area = 0f;
        for (var i = 0; i < loop.Count; i++) area += Cross(loop[i], loop[(i + 1) % loop.Count]);
        if (area < 0) loop.Reverse();
        return loop;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    // A loop of corners made solid: its top cut into triangles corner to
    // corner (ear clipping, no corner on another's edge), its bottom, its sides.
    private Mesh Solid(List<Vector2> loop)
    {
        var n = loop.Count;
        var vertices = loop.Select(c => new Vector3(c.x, 0, c.y)).Concat(loop.Select(c => new Vector3(c.x, -thickness, c.y))).ToList();
        var triangles = new List<int>();
        var left = Enumerable.Range(0, n).ToList();
        while (left.Count > 3)
        {
            var clipped = false;
            for (var i = 0; i < left.Count && !clipped; i++)
            {
                var a = left[(i + left.Count - 1) % left.Count];
                var b = left[i];
                var c = left[(i + 1) % left.Count];
                if (Cross(loop[b] - loop[a], loop[c] - loop[b]) <= 1e-7f) continue; // a reflex corner
                if (left.Any(k => k != a && k != b && k != c && InTriangle(loop[k], loop[a], loop[b], loop[c]))) continue;
                triangles.AddRange(new[] { a, c, b, n + a, n + b, n + c });
                left.RemoveAt(i);
                clipped = true;
            }
            if (!clipped) break; // not a simple loop: what's left stays open
        }
        if (left.Count == 3) triangles.AddRange(new[] { left[0], left[2], left[1], n + left[0], n + left[1], n + left[2] });
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            triangles.AddRange(new[] { i, j, n + j, i, n + j, n + i });
        }
        var mesh = new Mesh { name = "Board solid" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // On its edges counts as in.
    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(b - a, p - a) >= -1e-7f && Cross(c - b, p - b) >= -1e-7f && Cross(a - c, p - c) >= -1e-7f;

    // A convex part made solid, for its collider.
    private Mesh Prism(Vector2[] part)
    {
        var vertices = new List<Vector3>();
        foreach (var corner in part) vertices.Add(new Vector3(corner.x, 0, corner.y));
        foreach (var corner in part) vertices.Add(new Vector3(corner.x, -thickness, corner.y));
        var n = part.Length;
        var triangles = new List<int>();
        for (var i = 1; i < n - 1; i++)
        {
            triangles.AddRange(new[] { 0, i + 1, i });
            triangles.AddRange(new[] { n, n + i, n + i + 1 });
        }
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            triangles.AddRange(new[] { i, j, n + j, i, n + j, n + i });
        }
        var mesh = new Mesh { name = "Board part" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
