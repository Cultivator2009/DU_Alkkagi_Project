using System.Collections.Generic;
using TMPro;
using UnityEngine;

// A piece breaking as it goes over the edge: its meshes cut into shards -
// every triangle goes with the nearest of a few points scattered over it -
// that burst apart, tumble, skip on the table (or the board) and shrink
// away.
// Drawn only: the shards have no colliders and nothing in the match sees
// them. BoardSounds starts one with the BoardSound.Shatter event, so a
// network guest's goes off with the host's.
public class PieceShatter : MonoBehaviour
{
    private const int Shards = 7;
    private const float Life = 1.4f;       // seconds of game time
    private const float ShrinkFrom = 0.9f; // when they start to shrink away
    private const float TableTop = -0.505f; // the cloth under the board
    private const float BoardTop = 0.002f;

    private struct Shard
    {
        public Transform Transform;
        public Vector3 Velocity;
        public Vector3 Spin; // rad/s
    }

    private readonly List<Shard> shards = new List<Shard>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private float age;
    private float floor;

    public static void Break(GamePieceDragAndReleaseForce piece)
    {
        var shatter = new GameObject("Shatter " + piece.name).AddComponent<PieceShatter>();
        shatter.Build(piece);
    }

    private void Build(GamePieceDragAndReleaseForce piece)
    {
        var body = piece.Body;
        var inherited = body.isKinematic ? Vector3.zero : body.linearVelocity * 0.3f;
        var centre = body.worldCenterOfMass;
        // Still on the board (a battle of health's knockout), or gone over its edge.
        floor = piece.transform.position.y > -0.1f ? BoardTop : TableTop;

        foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.GetComponent<TMP_Text>() != null) continue; // a janggi piece's letter
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            Cut(filter.sharedMesh, renderer, centre, inherited);
        }
    }

    // The mesh's triangles, in the world, shared out among Shards points.
    private void Cut(Mesh mesh, MeshRenderer renderer, Vector3 centre, Vector3 inherited)
    {
        var toWorld = renderer.transform.localToWorldMatrix;
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;
        var world = new Vector3[vertices.Length];
        for (var i = 0; i < vertices.Length; i++) world[i] = toWorld.MultiplyPoint3x4(vertices[i]);

        var submeshes = new int[mesh.subMeshCount][];
        for (var s = 0; s < submeshes.Length; s++) submeshes[s] = mesh.GetTriangles(s);
        var all = new List<(int submesh, int at)>();
        for (var s = 0; s < submeshes.Length; s++)
            for (var t = 0; t < submeshes[s].Length; t += 3) all.Add((s, t));
        if (all.Count == 0) return;

        Vector3 Centroid((int submesh, int at) tri)
        {
            var triangles = submeshes[tri.submesh];
            return (world[triangles[tri.at]] + world[triangles[tri.at + 1]] + world[triangles[tri.at + 2]]) / 3;
        }
        var seeds = new Vector3[Mathf.Min(Shards, all.Count)];
        for (var i = 0; i < seeds.Length; i++) seeds[i] = Centroid(all[Random.Range(0, all.Count)]);

        var groups = new List<(int submesh, int at)>[seeds.Length];
        for (var i = 0; i < groups.Length; i++) groups[i] = new List<(int, int)>();
        foreach (var tri in all)
        {
            var c = Centroid(tri);
            var best = 0;
            for (var i = 1; i < seeds.Length; i++)
                if ((seeds[i] - c).sqrMagnitude < (seeds[best] - c).sqrMagnitude) best = i;
            groups[best].Add(tri);
        }

        foreach (var group in groups)
        {
            if (group.Count == 0) continue;
            var middle = Vector3.zero;
            foreach (var tri in group) middle += Centroid(tri);
            middle /= group.Count;

            var shardVertices = new List<Vector3>();
            var shardNormals = new List<Vector3>();
            var shardUvs = new List<Vector2>();
            var shardTriangles = new List<int>[submeshes.Length];
            for (var s = 0; s < submeshes.Length; s++) shardTriangles[s] = new List<int>();
            foreach (var (submesh, at) in group)
                for (var k = 0; k < 3; k++)
                {
                    var v = submeshes[submesh][at + k];
                    shardTriangles[submesh].Add(shardVertices.Count);
                    shardVertices.Add(world[v] - middle);
                    shardNormals.Add(normals.Length > v ? toWorld.MultiplyVector(normals[v]).normalized : Vector3.up);
                    shardUvs.Add(uvs.Length > v ? uvs[v] : Vector2.zero);
                }
            var shardMesh = new Mesh { name = "Shard" };
            shardMesh.SetVertices(shardVertices);
            shardMesh.SetNormals(shardNormals);
            shardMesh.SetUVs(0, shardUvs);
            shardMesh.subMeshCount = submeshes.Length;
            for (var s = 0; s < submeshes.Length; s++) shardMesh.SetTriangles(shardTriangles[s], s);
            shardMesh.RecalculateBounds();
            meshes.Add(shardMesh);

            var shard = new GameObject("Shard").transform;
            shard.SetParent(transform, false);
            shard.position = middle;
            shard.gameObject.AddComponent<MeshFilter>().sharedMesh = shardMesh;
            var shardRenderer = shard.gameObject.AddComponent<MeshRenderer>();
            shardRenderer.sharedMaterials = renderer.sharedMaterials;
            shardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var outward = middle - centre;
            outward.y = Mathf.Max(0, outward.y);
            shards.Add(new Shard
            {
                Transform = shard,
                Velocity = outward.normalized * Random.Range(1.2f, 2.6f) + Vector3.up * Random.Range(0.8f, 1.6f) + inherited,
                Spin = Random.insideUnitSphere * 25f,
            });
        }
    }

    // Game time, as the other board effects: slowed with the match, still
    // while it's paused.
    private void Update()
    {
        var dt = Time.deltaTime;
        age += dt;
        if (age >= Life)
        {
            Destroy(gameObject);
            return;
        }
        var scale = age < ShrinkFrom ? 1f : 1f - (age - ShrinkFrom) / (Life - ShrinkFrom);
        for (var i = 0; i < shards.Count; i++)
        {
            var shard = shards[i];
            shard.Velocity += Physics.gravity * dt;
            var position = shard.Transform.position + shard.Velocity * dt;
            if (position.y < floor)
            {
                // A skip on the cloth: most of the fall goes, some of the slide.
                position.y = floor;
                shard.Velocity = new Vector3(shard.Velocity.x * 0.5f, Mathf.Abs(shard.Velocity.y) * 0.25f, shard.Velocity.z * 0.5f);
                shard.Spin *= 0.5f;
            }
            shard.Transform.position = position;
            shard.Transform.rotation = Quaternion.Euler(shard.Spin * (dt * Mathf.Rad2Deg)) * shard.Transform.rotation;
            shard.Transform.localScale = Vector3.one * scale;
            shards[i] = shard;
        }
    }

    private void OnDestroy()
    {
        foreach (var mesh in meshes) Destroy(mesh);
    }
}
