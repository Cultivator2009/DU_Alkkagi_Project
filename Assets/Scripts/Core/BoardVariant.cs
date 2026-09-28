using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// One playable board in GameScene, under Board_GO (Tools > Alkkagi > Build
// boards and piece templates). BoardSetup turns on the one the match rules
// pick and turns the others off.
//
// The board itself is its shape (parts, seen from above) made solid at run
// time: a top with its texture, sides, and a collider per part - a box for
// a rectangle, a convex prism otherwise. Built again smaller as the edge
// crumbles (Build with an inset), the texture staying where it was.
public class BoardVariant : MonoBehaviour
{
    public BoardType type;
    public BoardPart[] parts;
    public float thickness = 0.5f;
    public Material topMaterial;
    public Material sideMaterial;
    public Rect uvRect;          // where the top's texture lies, in (x, z)
    public PhysicsMaterial physics;

    // The south side's zone in (x, z) for a go-stone-sized piece (radius
    // 0.1) when two play; larger pieces keep further in. Every other side's
    // is the same turned to face its own edge.
    public Rect blackZone = new Rect(-1.35f, -1.35f, 2.7f, 1.05f);
    // Three or four sides: across the side's edge (x), and in from it (y).
    public Rect multiZone = Rect.MinMaxRect(-0.7f, 0.15f, 0.7f, 0.7f);
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
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }
        var slab = new GameObject(SlabName).transform;
        slab.SetParent(transform, false);
        slab.gameObject.layer = gameObject.layer;

        var filter = slab.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = Mesh(playable);
        var renderer = slab.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { topMaterial, sideMaterial };
        renderer.shadowCastingMode = ShadowCastingMode.On;

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

    // Tops (submesh 0) and sides (submesh 1). Where rectangular parts
    // overlap, a later one's top leaves out what an earlier one covers, so
    // no bit of the surface is drawn twice. A side inside another part is
    // under its top, out of sight.
    private Mesh Mesh(BoardShape board)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tops = new List<int>();
        var sides = new List<int>();
        var covered = new List<Rect>();
        foreach (var part in board.Parts)
        {
            var pieces = new List<Vector2[]> { part };
            if (BoardShape.IsRect(part, out var rect))
            {
                var left = new List<Rect> { rect };
                foreach (var earlier in covered) left = left.SelectMany(r => Subtract(r, earlier)).ToList();
                covered.Add(rect);
                pieces = left.Select(r => BoardPart.Rect(r).corners).ToList();
            }
            foreach (var piece in pieces)
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
