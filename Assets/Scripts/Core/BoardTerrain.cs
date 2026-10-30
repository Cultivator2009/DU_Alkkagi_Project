using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// The landscape board's ground (BoardType.Landscape): a relief carved from
// one block of wood, ten units a side, three and a half times the go
// board's, too big to see whole from above. The sides start on the level
// ground of its corners, near enough the next corner's to reach it in a
// shot. In the middle a raised mesa, ramps up onto it from the edges'
// middles (off its edge a piece drops to the ground below and plays on,
// back up only by a ramp) - the closing circle (ZoneRule) ends round it; a
// low mound before each corner, small ponds by the edges' middles and rocks
// beside them to hide behind. The same at every corner, turned, so no seat
// is better than another. A piece that goes into a pond is out, as one off
// the edge is.
//
// (Twelve a side at first, the sides on raised corners nine apart with a
// pond between each two: the first knock came rounds into a match.)
//
// Every face of it is flat (the mounds are cut, the banks straight down to
// the water), and what the pieces touch is boxes and a few large faces, not
// the fine mesh that's drawn: a stone sliding fast over many small faces
// catches on their edges and jumps.
//
// Built at run time, not saved in the scene (the drawn mesh is large): the
// variant builds it when it's first turned on (BoardVariant.Build calls
// Build). Where it rises is Rise, worked out the same on every machine.
// (On the board's variant; not RequireComponent, so a copy can lose its
// scripts in any order: the AI's.)
public class BoardTerrain : MonoBehaviour
{
    public const float Half = 5f;
    public const float Step = 0.24f;          // the mesa's height over the ground: over any piece's, so none climbs it
    private const float Cell = 0.04f;         // the drawn ground, on a grid this fine: a step's edge lies on its lines
    private const float MesaHalf = 1.1f;
    private const float RampLength = 0.9f;    // 15 degrees up
    private const float RampHalf = 0.5f;      // half a ramp's width
    private const float PondAt = 4.2f;        // a pond's middle from the board's, along x or z: by the edge, off the way between two corners
    private const float PondRadius = 0.35f;
    private const float Bank = 0.12f;         // round a pond the ground slopes down into it
    private const float BankDepth = 0.07f;    // deep enough at the rim that the water hides where the ground ends
    // A mound in every quarter, before its corner: its middle this far along
    // both axes, flat on top, its four faces sloping down to the ground.
    private const float MoundAt = 1.85f;
    private const float MoundHeight = 0.1f;
    private const float MoundTop = 0.2f;      // half the top's side
    private const float MoundFoot = 0.55f;    // half the foot's
    // Rocks in one quarter (x, z, radius, height), mirrored into the others:
    // either side of each pond.
    private static readonly Vector4[] Rocks =
    {
        new Vector4(4.0f, 1.6f, 0.2f, 0.3f),
        new Vector4(1.6f, 4.0f, 0.2f, 0.3f),
    };

    public Material groundMaterial;
    public Material waterMaterial;
    public Material rockMaterial;

    private void Awake()
    {
        // A copy (the AI's) has its ground already.
        if (transform.Find(BoardVariant.SlabName) == null) GetComponent<BoardVariant>().Build();
    }

    // ---- Where the ground is ----

    // How far over the board's top the ground is at point (the board's own
    // x, z); in a pond, far under it.
    public static float Rise(Vector2 point)
    {
        var x = Mathf.Abs(point.x);
        var z = Mathf.Abs(point.y);
        if (PondDistance(point) < PondRadius) return -10f;
        return Ground(x, z, PondDistance(point));
    }

    public static bool InPond(Vector2 point) => PondDistance(point) < PondRadius;

    // Whether the way from a to b (the board's x, z) comes within margin of a pond's water.
    public static bool PassesPond(Vector2 a, Vector2 b, float margin)
    {
        foreach (var pond in PondCentres())
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(pond - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            if ((a + ab * t - pond).magnitude < PondRadius + margin) return true;
        }
        return false;
    }

    // How far a point is from the nearest pond's water (0 in it).
    public static float PondMargin(Vector2 point) => Mathf.Max(0, PondDistance(point) - PondRadius);

    private static float PondDistance(Vector2 point)
    {
        var x = Mathf.Abs(point.x);
        var z = Mathf.Abs(point.y);
        return Mathf.Min(new Vector2(x - PondAt, z).magnitude, new Vector2(x, z - PondAt).magnitude);
    }

    // In the first quarter (x, z at least 0); pond: how far from the nearest pond's middle.
    private static float Ground(float x, float z, float pond)
    {
        // The mesa, and its four ramps out toward the edges' middles.
        if (x <= MesaHalf && z <= MesaHalf) return Step;
        var foot = MesaHalf + RampLength;
        if (z <= RampHalf && x > MesaHalf && x <= foot) return Step * (foot - x) / RampLength;
        if (x <= RampHalf && z > MesaHalf && z <= foot) return Step * (foot - z) / RampLength;
        // The low ground: the mounds, and the banks down into the ponds.
        var square = Mathf.Max(Mathf.Abs(x - MoundAt), Mathf.Abs(z - MoundAt));
        var h = MoundHeight * Mathf.Clamp01((MoundFoot - square) / (MoundFoot - MoundTop));
        if (pond < PondRadius + Bank) h -= BankDepth * (1 - (Mathf.Max(pond, PondRadius) - PondRadius) / Bank);
        return h;
    }

    // ---- Building it ----

    // The ground over the board (playable: a square), its sides down to the
    // board's foot, the ponds' water and the rocks, under slab.
    public void Build(Transform slab, BoardShape playable, float thickness, PhysicsMaterial physics, Material side)
    {
        var rect = playable.Bounds;
        var nx = Mathf.Max(1, Mathf.RoundToInt(rect.width / Cell));
        var nz = Mathf.Max(1, Mathf.RoundToInt(rect.height / Cell));
        var dx = rect.width / nx;
        var dz = rect.height / nz;

        var vertices = new List<Vector3>((nx + 1) * (nz + 1));
        for (var j = 0; j <= nz; j++)
        for (var i = 0; i <= nx; i++)
        {
            var p = new Vector2(rect.xMin + i * dx, rect.yMin + j * dz);
            // At a pond's rim the ground stops at the bank's foot.
            var h = Ground(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Max(PondDistance(p), PondRadius));
            vertices.Add(new Vector3(p.x, h, p.y));
        }
        var top = new List<int>(nx * nz * 6);
        for (var j = 0; j < nz; j++)
        for (var i = 0; i < nx; i++)
        {
            var middle = new Vector2(rect.xMin + (i + 0.5f) * dx, rect.yMin + (j + 0.5f) * dz);
            if (InPond(middle)) continue;
            var a = j * (nx + 1) + i;
            var b = a + 1;
            var c = a + nx + 1;
            var d = c + 1;
            top.AddRange(new[] { a, c, b, b, c, d });
        }

        // The sides: down from the ground's edge to the foot, all the way round.
        var sides = new List<int>();
        void Side(int from, int to)
        {
            var v0 = vertices[from];
            var v1 = vertices[to];
            var first = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(new Vector3(v0.x, -thickness, v0.z));
            vertices.Add(new Vector3(v1.x, -thickness, v1.z));
            sides.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
        }
        int At(int i, int j) => j * (nx + 1) + i;
        for (var i = 0; i < nx; i++)
        {
            Side(At(i + 1, 0), At(i, 0));    // south, facing out
            Side(At(i, nz), At(i + 1, nz));  // north
        }
        for (var j = 0; j < nz; j++)
        {
            Side(At(0, j), At(0, j + 1));    // west
            Side(At(nx, j + 1), At(nx, j));  // east
        }

        var mesh = new Mesh { name = "Landscape ground", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(top, 0);
        mesh.SetTriangles(sides, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        slab.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = slab.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { groundMaterial, side };
        renderer.shadowCastingMode = ShadowCastingMode.On;
        Solids(slab, thickness, physics);

        foreach (var pond in PondCentres())
            if (playable.Contains(pond)) Water(slab, pond);
        var n = 0;
        foreach (var rock in RockPlaces())
            if (playable.Contains(new Vector2(rock.x, rock.y), rock.z)) Rock(slab, rock, physics, n++);
    }

    // ---- What the pieces touch ----

    // The mesa as a box, the ramps as tilted boxes, the mounds as cut
    // pyramids, the low ground and the ponds' banks as one mesh of large
    // faces (it runs on under the rest), each with the board's grip.
    private static void Solids(Transform slab, float thickness, PhysicsMaterial physics)
    {
        var solids = new GameObject("Solids").transform;
        solids.gameObject.layer = slab.gameObject.layer;
        solids.SetParent(slab, false);
        T Add<T>(string name) where T : Collider
        {
            var part = new GameObject(name) { layer = slab.gameObject.layer };
            part.transform.SetParent(solids, false);
            var collider = part.AddComponent<T>();
            collider.sharedMaterial = physics;
            return collider;
        }
        void Block(string name, Rect rect)
        {
            var box = Add<BoxCollider>(name);
            box.center = new Vector3(rect.center.x, (Step - thickness) / 2, rect.center.y);
            box.size = new Vector3(rect.width, Step + thickness, rect.height);
        }
        // A ramp from its low end's middle (on the ground) to its high end's
        // (at the step's top), this wide; its body under the ground.
        void Ramp(Vector2 low, Vector2 high, float width)
        {
            var along = new Vector3(high.x - low.x, Step, high.y - low.y);
            var side = Vector3.Cross(Vector3.up, along).normalized;
            var up = Vector3.Cross(along, side).normalized;
            const float body = 0.4f;
            var box = Add<BoxCollider>("Ramp");
            box.transform.localRotation = Quaternion.LookRotation(along, up);
            box.transform.localPosition = new Vector3((low.x + high.x) / 2, Step / 2, (low.y + high.y) / 2) - up * (body / 2);
            box.size = new Vector3(width, body, along.magnitude);
        }

        Block("Mesa", Rect.MinMaxRect(-MesaHalf, -MesaHalf, MesaHalf, MesaHalf));
        var foot = MesaHalf + RampLength;
        foreach (var sx in new[] { 1f, -1f })
        foreach (var sz in new[] { 1f, -1f })
            Mound(Add<MeshCollider>("Mound"), new Vector2(sx * MoundAt, sz * MoundAt));
        foreach (var way in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            Ramp(way * foot, way * MesaHalf, RampHalf * 2);
        var low = Add<MeshCollider>("Ground");
        low.sharedMesh = LowGround();
    }

    // A cut pyramid on a block sunk under the ground. With its foot a hair
    // under (as at first), a stone driven into the slope by a hard shot was
    // pushed out the nearer way, down through the thin foot, and stayed
    // inside, shaking, its shot never over.
    private static void Mound(MeshCollider collider, Vector2 at)
    {
        var points = new List<Vector3>();
        foreach (var (half, y) in new[] { (MoundFoot, -MoundSunk), (MoundFoot, -0.002f), (MoundTop, MoundHeight) })
            foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                points.Add(new Vector3(at.x + dx * half, y, at.y + dz * half));
        var mesh = new Mesh { name = "Mound" };
        mesh.SetVertices(points);
        // Any faces over all the corners: a convex collider is their hull.
        mesh.SetTriangles(Enumerable.Range(0, points.Count).ToArray(), 0);
        collider.sharedMesh = mesh;
        collider.convex = true;
    }

    private const float MoundSunk = 0.3f;

    // The ground at its own level: the board in large squares, but round each
    // pond a square holding the flat down to the bank, the bank straight down
    // to the water, and the water (nothing: a piece falls through).
    private static Mesh LowGround()
    {
        const float around = 0.55f; // half a pond's square: past its bank, inside the board
        const int segments = 32;   // round a pond: a multiple of four, a point at each square side's middle
        var lines = new[] { -Half, -PondAt - around, -PondAt + around, -around, around, PondAt - around, PondAt + around, Half };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int Vertex(Vector3 v)
        {
            vertices.Add(v);
            return vertices.Count - 1;
        }
        // Facing up (Unity's front is clockwise seen from it).
        void Triangle(int a, int b, int c)
        {
            if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y < 0) (b, c) = (c, b);
            triangles.AddRange(new[] { a, b, c });
        }
        var grid = new int[lines.Length, lines.Length];
        for (var j = 0; j < lines.Length; j++)
            for (var i = 0; i < lines.Length; i++)
                grid[i, j] = Vertex(new Vector3(lines[i], 0, lines[j]));
        var ponds = new List<Vector2>(PondCentres());
        for (var j = 0; j + 1 < lines.Length; j++)
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            var middle = new Vector2((lines[i] + lines[i + 1]) / 2, (lines[j] + lines[j + 1]) / 2);
            var pond = ponds.FindIndex(p => (p - middle).sqrMagnitude < 0.01f);
            if (pond < 0)
            {
                Triangle(grid[i, j], grid[i + 1, j], grid[i, j + 1]);
                Triangle(grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                continue;
            }
            // The pond's square: its corners (shared with the squares round
            // it), a ring at the bank's top and one at the water.
            var c = ponds[pond];
            var corners = new[] { grid[i + 1, j + 1], grid[i, j + 1], grid[i, j], grid[i + 1, j] }; // at 45, 135, 225, 315 degrees
            var top = new int[segments];
            var rim = new int[segments];
            for (var k = 0; k < segments; k++)
            {
                var a = k * Mathf.PI * 2 / segments;
                var way = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                top[k] = Vertex(new Vector3(c.x, 0, c.y) + way * (PondRadius + Bank));
                rim[k] = Vertex(new Vector3(c.x, -BankDepth, c.y) + way * PondRadius);
            }
            var quarter = segments / 4;
            for (var k = 0; k < segments; k++)
            {
                var next = (k + 1) % segments;
                // The flat between the square and the bank: each piece of the
                // ring to the corner of its quarter, and at each side's middle
                // the two corners either side of it.
                var corner = corners[k / quarter];
                Triangle(corner, top[k], top[next]);
                if (next % quarter == 0) Triangle(corner, corners[(k / quarter + 1) % 4], top[next]);
                // The bank.
                Triangle(top[k], rim[k], top[next]);
                Triangle(top[next], rim[k], rim[next]);
            }
        }
        var mesh = new Mesh { name = "Low ground" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    private static IEnumerable<Vector2> PondCentres()
    {
        yield return new Vector2(PondAt, 0);
        yield return new Vector2(-PondAt, 0);
        yield return new Vector2(0, PondAt);
        yield return new Vector2(0, -PondAt);
    }

    private static IEnumerable<Vector4> RockPlaces()
    {
        foreach (var rock in Rocks)
            foreach (var sx in new[] { 1f, -1f })
                foreach (var sz in new[] { 1f, -1f })
                    yield return new Vector4(rock.x * sx, rock.y * sz, rock.z, rock.w);
    }

    // A pond's still water, a little under the ground, its rim over the bank.
    private void Water(Transform parent, Vector2 at)
    {
        const int segments = 48;
        var radius = PondRadius + 0.06f;
        var vertices = new List<Vector3> { Vector3.zero };
        var triangles = new List<int>();
        for (var i = 0; i < segments; i++)
        {
            var a = i * Mathf.PI * 2 / segments;
            vertices.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            triangles.AddRange(new[] { 0, 1 + (i + 1) % segments, 1 + i });
        }
        var mesh = new Mesh { name = "Pond" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        var water = new GameObject("Pond");
        water.layer = parent.gameObject.layer;
        water.transform.SetParent(parent, false);
        water.transform.localPosition = new Vector3(at.x, -0.035f, at.y);
        water.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = water.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = waterMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // A dark stone, lumpy, standing on the ground: a sphere pushed about by
    // noise (seeded by which rock it is, so every machine has the same).
    private void Rock(Transform parent, Vector4 place, PhysicsMaterial physics, int index)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var source = sphere.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(sphere);
        else DestroyImmediate(sphere);
        var vertices = source.vertices;
        var seed = index * 7.31f;
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i].normalized;
            vertices[i] = v * (0.5f * Bump(v, seed));
        }
        var mesh = new Mesh { name = "Rock" };
        mesh.vertices = vertices;
        mesh.triangles = source.triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var rock = new GameObject("Rock");
        rock.layer = parent.gameObject.layer;
        rock.transform.SetParent(parent, false);
        var ground = Ground(Mathf.Abs(place.x), Mathf.Abs(place.y), PondDistance(new Vector2(place.x, place.y)));
        // Its middle a little under its height's half, so it sits into the ground.
        rock.transform.localPosition = new Vector3(place.x, ground + place.w * 0.42f, place.y);
        rock.transform.localRotation = Quaternion.Euler(0, index * 47f, 0);
        rock.transform.localScale = new Vector3(place.z * 2, place.w, place.z * 2.2f);
        rock.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = rock.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = rockMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        var solid = rock.AddComponent<MeshCollider>();
        solid.sharedMesh = Hull(seed);
        solid.convex = true;
        solid.sharedMaterial = physics;
    }

    private static float Bump(Vector3 way, float seed) => 0.8f + 0.4f * Mathf.PerlinNoise(way.x * 1.7f + seed, way.z * 1.7f + way.y * 1.3f + seed);

    // A rock's collider: the same lumps on a coarse ball. The hull of the
    // drawn one had more faces than PhysX takes, and it cut it short. From
    // about a stone's height down it goes straight into the ground: the
    // ball's underside curved in over the ground, a stone slid under it, and
    // squeezed there between rock and ground it spun on and never settled
    // (a shot that never ended).
    private static Mesh Hull(float seed)
    {
        const int rings = 6, round = 10;
        const int lowest = 4;      // 120° down from the top: about a stone's height over the ground
        const float under = -0.6f; // the ground is at -0.42 (the rock's middle 0.42 of its height up)
        var points = new List<Vector3> { Vector3.up * (0.5f * Bump(Vector3.up, seed)) };
        for (var ring = 1; ring <= lowest; ring++)
        for (var k = 0; k < round; k++)
        {
            var down = ring * Mathf.PI / rings;
            var about = k * Mathf.PI * 2 / round;
            var way = new Vector3(Mathf.Sin(down) * Mathf.Cos(about), Mathf.Cos(down), Mathf.Sin(down) * Mathf.Sin(about));
            var point = way * (0.5f * Bump(way, seed));
            points.Add(point);
            if (ring == lowest) points.Add(new Vector3(point.x, under, point.z));
        }
        var mesh = new Mesh { name = "Rock hull" };
        mesh.SetVertices(points);
        // Any faces over all the points: a convex collider is their hull.
        mesh.SetTriangles(Enumerable.Range(0, (points.Count + 2) / 3 * 3).Select(i => i % points.Count).ToArray(), 0);
        return mesh;
    }
}
