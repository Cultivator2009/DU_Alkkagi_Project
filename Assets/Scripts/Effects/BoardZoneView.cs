using UnityEngine;
using UnityEngine.Rendering;

// How the closing circle (ZoneRule) looks: cast down from above on the
// board and on whatever stands on it (a projector with Alkkagi/Zone Field) -
// a haze outside the circle, its edge a bright line, and the next circle
// dashed, seal red and pulsing in the round it closes at the end of - and a
// wall of light standing on the circle (Alkkagi/Zone Wall). Closing, it
// sweeps in over ZoneRule.CloseSeconds as GameManager takes what it passes.
// Every screen draws it from the same events, a guest's mirror included.
public class BoardZoneView : MonoBehaviour
{
    private const float Above = 2f;        // the projector over the board's top
    private const float Under = 0.35f;     // and how far under it it reaches: the landscape's banks, not the floor
    private const float WallBelow = 0.02f; // the wall from just under the board's top
    private const float WallHeight = 0.32f; // and this tall: low, so from above it's a line, not a band
    private const int WallSides = 128;

    private TurnController turns;
    private Material field;
    private Material wallMaterial;
    private Transform wall;
    private Mesh wallMesh;
    private float shown;           // the circle as drawn now (infinite: none yet)
    private float sweepFrom, sweepTo;
    private float sweptAt = -1;    // game time it began closing in
    private int stage;

    public void Init(BoardSetup board, TurnController controller)
    {
        turns = controller;
        var zone = turns.Zone;
        if (!zone.Enabled)
        {
            enabled = false;
            return;
        }
        turns.OnZoneChanged += Refresh;
        stage = zone.Stage;
        shown = zone.Safe;

        var bounds = board.SurfaceBounds;
        var top = bounds.max.y;
        field = new Material(Resources.Load<Material>("ZoneField"));
        field.SetFloat("_Below", top - Under);
        var caster = new GameObject("ZoneProjector");
        caster.transform.SetParent(transform, false);
        caster.transform.SetPositionAndRotation(new Vector3(bounds.center.x, top + Above, bounds.center.z), Quaternion.LookRotation(Vector3.down, Vector3.forward));
        var projector = caster.AddComponent<Projector>();
        projector.orthographic = true;
        projector.orthographicSize = bounds.extents.z + 0.5f;
        projector.aspectRatio = (bounds.extents.x + 0.5f) / (bounds.extents.z + 0.5f);
        projector.nearClipPlane = 0.1f;
        projector.farClipPlane = Above + Under;
        projector.material = field;

        wallMaterial = new Material(Resources.Load<Material>("ZoneWall"));
        wallMaterial.SetFloat("_Top", top);
        wallMaterial.SetFloat("_Height", WallHeight - WallBelow);
        wallMesh = WallMesh();
        var standing = new GameObject("ZoneWall");
        standing.transform.SetParent(transform, false);
        standing.transform.position = new Vector3(zone.Centre.x, top, zone.Centre.y);
        standing.AddComponent<MeshFilter>().sharedMesh = wallMesh;
        var renderer = standing.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = wallMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        wall = standing.transform;
        Draw();
    }

    private void OnDestroy()
    {
        if (turns != null) turns.OnZoneChanged -= Refresh;
        if (field != null) Destroy(field);
        if (wallMaterial != null) Destroy(wallMaterial);
        if (wallMesh != null) Destroy(wallMesh);
    }

    // Closed in (not just announced): sweep from where it was drawn.
    private void Refresh()
    {
        var zone = turns.Zone;
        if (zone.Stage == stage) return;
        stage = zone.Stage;
        sweepFrom = float.IsInfinity(shown) ? zone.Reach : shown;
        sweepTo = zone.Safe;
        sweptAt = Time.time;
    }

    private void Update()
    {
        if (sweptAt >= 0)
        {
            var t = Mathf.Clamp01((Time.time - sweptAt) / ZoneRule.CloseSeconds);
            shown = float.IsInfinity(sweepTo) ? sweepTo : Mathf.Lerp(sweepFrom, sweepTo, GameManager.ZoneSweep(t));
            if (t >= 1) sweptAt = -1;
        }
        Draw();
    }

    private void Draw()
    {
        var zone = turns.Zone;
        var next = zone.Next;
        var none = float.IsInfinity(shown);
        field.SetVector("_Circle", new Vector4(zone.Centre.x, zone.Centre.y, none ? 1e4f : shown, float.IsInfinity(next) ? 0 : next));
        field.SetFloat("_Warned", zone.Warned ? 1 : 0);
        wall.gameObject.SetActive(!none);
        if (!none) wall.localScale = new Vector3(shown, 1, shown);
    }

    // A ring of unit radius, open top and bottom, its uv x once round.
    private static Mesh WallMesh()
    {
        var vertices = new Vector3[(WallSides + 1) * 2];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[WallSides * 6];
        for (var i = 0; i <= WallSides; i++)
        {
            var a = i * Mathf.PI * 2 / WallSides;
            var way = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            vertices[2 * i] = way + Vector3.down * WallBelow;
            vertices[2 * i + 1] = way + Vector3.up * (WallHeight - WallBelow);
            uvs[2 * i] = uvs[2 * i + 1] = new Vector2(i / (float)WallSides, 0);
            if (i == WallSides) break;
            var k = i * 6;
            triangles[k] = 2 * i;
            triangles[k + 1] = 2 * i + 1;
            triangles[k + 2] = 2 * i + 2;
            triangles[k + 3] = 2 * i + 1;
            triangles[k + 4] = 2 * i + 3;
            triangles[k + 5] = 2 * i + 2;
        }
        var mesh = new Mesh { name = "Zone wall", vertices = vertices, uv = uvs, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }
}
