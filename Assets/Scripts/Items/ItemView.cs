using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// How the items look on the board, drawn from ItemSystem.State on every
// screen alike: the item boxes (gold, turning and bobbing), the posts and
// bumpers (a collider of their own, under the board so the AI's copy has
// them too), the patches - ice, tar, a pad's arrow, a vortex, a hole, a
// fog that hides the pieces in it - and a ring under each piece an item is
// on, its colour the item's. What an item does as it happens goes off as
// the board's other effects do (HitEffects).
public class ItemView : MonoBehaviour
{
    public const string PillarName = "Pillar";
    public const string BumperName = "Bumper";

    private ItemSystem items;
    private GameManager game;
    private readonly Dictionary<int, Transform> boxes = new Dictionary<int, Transform>();
    private readonly Dictionary<int, GameObject> objects = new Dictionary<int, GameObject>();
    private readonly Dictionary<(char piece, ItemId kind), Transform> rings = new Dictionary<(char, ItemId), Transform>();
    private readonly List<Object> made = new List<Object>();
    private Material boxMaterial, pillarMaterial, bumperMaterial;
    private readonly Dictionary<ItemId, Material> patchMaterials = new Dictionary<ItemId, Material>();
    private readonly HashSet<Renderer> fogged = new HashSet<Renderer>();
    private readonly List<(Transform spin, float speed)> spinning = new List<(Transform, float)>();
    private readonly Dictionary<ItemId, Material> ringMaterials = new Dictionary<ItemId, Material>();
    private Mesh ringMesh, discMesh;

    private static readonly Dictionary<ItemId, Color> RingColors = new Dictionary<ItemId, Color>
    {
        { ItemId.Anchor, new Color32(0x2E, 0x24, 0x1A, 0xD0) },
        { ItemId.Shield, new Color32(0xE3, 0xB3, 0x41, 0xE0) },
        { ItemId.Glue, new Color32(0x3E, 0x8E, 0x5A, 0xD0) },
        { ItemId.Grease, new Color32(0xD9, 0xA1, 0x3B, 0xC0) },
        { ItemId.Freeze, new Color32(0x7F, 0xC8, 0xE8, 0xE0) },
        { ItemId.Grow, new Color32(0x6E, 0x3F, 0x8E, 0x90) },
        { ItemId.Shrink, new Color32(0x2F, 0x5D, 0xA8, 0x90) },
    };

    private float BoardTop => game.Board.Active.transform.position.y;

    public void Init(ItemSystem itemSystem, GameManager gameManager)
    {
        items = itemSystem;
        game = gameManager;
        var lit = game.Board.blackTemplate.GetComponentInChildren<Renderer>().sharedMaterial;
        boxMaterial = Keep(new Material(lit) { color = new Color32(0xE3, 0xB3, 0x41, 0xFF) });
        pillarMaterial = Keep(new Material(lit) { color = new Color32(0x5A, 0x46, 0x32, 0xFF) });
        bumperMaterial = Keep(new Material(lit) { color = new Color32(0xB3, 0x31, 0x2A, 0xFF) });
        foreach (var (kind, color) in new[]
                 {
                     (ItemId.Ice, new Color(0.62f, 0.86f, 1f, 0.42f)),
                     (ItemId.Tar, new Color(0.16f, 0.12f, 0.08f, 0.72f)),
                     (ItemId.Boost, new Color(0.93f, 0.72f, 0.26f, 0.6f)),
                     (ItemId.Vortex, new Color(0.43f, 0.25f, 0.56f, 0.45f)),
                     (ItemId.Sinkhole, new Color(0.04f, 0.03f, 0.02f, 0.95f)),
                     (ItemId.Fog, new Color(0.9f, 0.9f, 0.88f, 0.93f)),
                 })
            patchMaterials[kind] = Keep(new Material(Shader.Find("Sprites/Default")) { color = color });
        foreach (var kv in RingColors) ringMaterials[kv.Key] = Keep(new Material(Shader.Find("Sprites/Default")) { color = kv.Value });
        ringMesh = Keep(Annulus(0.72f, 1f));
        discMesh = Keep(Annulus(0f, 1f));
        items.OnChanged += Sync;
        items.OnEvent += Play;
        Sync();
    }

    private T Keep<T>(T thing) where T : Object
    {
        made.Add(thing);
        return thing;
    }

    private void OnDestroy()
    {
        if (items != null)
        {
            items.OnChanged -= Sync;
            items.OnEvent -= Play;
        }
        foreach (var thing in made)
            if (thing != null) Destroy(thing);
        foreach (var o in objects.Values)
            if (o != null) Destroy(o);
    }

    // Everything there is now, nothing that isn't.
    private void Sync()
    {
        if (game == null || game.Board == null) return;
        var state = items.State;

        foreach (var id in boxes.Keys.Where(id => state.Boxes.All(b => b.Id != id)).ToList())
        {
            if (boxes[id] != null) Destroy(boxes[id].gameObject);
            boxes.Remove(id);
        }
        foreach (var box in state.Boxes.Where(b => !boxes.ContainsKey(b.Id))) boxes[box.Id] = MakeBox(box.Position);

        foreach (var id in objects.Keys.Where(id => state.Objects.All(o => o.Id != id)).ToList())
        {
            if (objects[id] != null) Destroy(objects[id]);
            objects.Remove(id);
        }
        foreach (var o in state.Objects.Where(o => !objects.ContainsKey(o.Id))) objects[o.Id] = ItemSystem.IsPost(o.Kind) ? MakePost(o) : MakePatch(o);

        var wanted = state.Effects.Where(e => RingColors.ContainsKey(e.Kind)).Select(e => (e.Piece, e.Kind)).ToHashSet();
        foreach (var key in rings.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            if (rings[key] != null) Destroy(rings[key].gameObject);
            rings.Remove(key);
        }
        foreach (var key in wanted.Where(k => !rings.ContainsKey(k))) rings[key] = Flat("Ring " + key.Item2, ringMesh, ringMaterials[key.Item2], transform);
    }

    private Transform MakeBox(Vector2 at)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(box.GetComponent<Collider>());
        box.name = "Item box";
        box.transform.SetParent(transform, false);
        box.transform.localScale = Vector3.one * ItemSystem.BoxRadius * 1.3f;
        box.transform.position = new Vector3(at.x, BoardTop + 0.08f, at.y);
        box.GetComponent<Renderer>().sharedMaterial = boxMaterial;
        if (HitEffects.Instance != null) HitEffects.Instance.PlayTurn(new Vector3(at.x, 0, at.y), 0.12f, 0);
        return box.transform;
    }

    // Under the board's own object, so the AI's copy of the board has it.
    // A bumper is a red one, lower, that throws back what knocks into it
    // (ItemBumper, on the authority).
    private GameObject MakePost(BoardObject o)
    {
        var bumper = o.Kind == ItemId.Bumper;
        var radius = ItemSystem.RadiusOf(o.Kind);
        var height = bumper ? ItemSystem.PillarHeight * 0.4f : ItemSystem.PillarHeight;
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(post.GetComponent<Collider>());
        post.name = bumper ? BumperName : PillarName;
        post.transform.SetParent(game.Board.Active.transform, true);
        post.transform.localScale = new Vector3(radius * 2, height / 2, radius * 2);
        post.transform.position = new Vector3(o.Position.x, BoardTop + height / 2, o.Position.y);
        var solid = post.AddComponent<MeshCollider>();
        solid.sharedMesh = post.GetComponent<MeshFilter>().sharedMesh;
        solid.convex = true;
        solid.sharedMaterial = game.Board.Active.physics;
        post.GetComponent<Renderer>().sharedMaterial = bumper ? bumperMaterial : pillarMaterial;
        if (bumper)
        {
            post.AddComponent<ItemBumper>().items = items;
            var ring = Flat("Bumper ring", ringMesh, patchMaterials[ItemId.Boost], transform);
            ring.SetParent(post.transform, false);
            ring.localPosition = new Vector3(0, -0.98f, 0);
            ring.localScale = new Vector3(1.6f, 1, 1.6f);
        }
        return post;
    }

    // A patch flat on the board, its size the item's reach; a pad's arrow
    // its way, a vortex turning, the fog a few soft layers over the pieces.
    private GameObject MakePatch(BoardObject o)
    {
        var radius = ItemSystem.RadiusOf(o.Kind);
        var patch = Flat(o.Kind.ToString(), o.Kind == ItemId.Vortex ? ringMesh : discMesh, patchMaterials[o.Kind], transform);
        patch.position = new Vector3(o.Position.x, BoardTop + 0.0015f, o.Position.y);
        patch.localScale = Vector3.one * radius;
        switch (o.Kind)
        {
            case ItemId.Boost:
                var arrow = Flat("Arrow", Keep(Arrow()), patchMaterials[ItemId.Boost], patch);
                arrow.localPosition = new Vector3(0, 0.001f, 0);
                arrow.localRotation = Quaternion.LookRotation(new Vector3(o.Way.x, 0, o.Way.y));
                break;
            case ItemId.Vortex:
                for (var i = 0; i < 3; i++)
                {
                    var swirl = Flat("Swirl", Keep(Arc(0.25f + 0.25f * i, 0.06f, 200f)), patchMaterials[ItemId.Vortex], patch);
                    swirl.localRotation = Quaternion.Euler(0, i * 120f, 0);
                }
                spinning.Add((patch, 80f));
                break;
            case ItemId.Sinkhole:
                var rim = Flat("Rim", ringMesh, pillarMaterial, patch);
                rim.localScale = new Vector3(1.12f, 1, 1.12f);
                break;
            case ItemId.Fog:
                patch.position += Vector3.up * 0.002f;
                for (var i = 1; i <= 4; i++)
                {
                    var layer = Flat("Fog layer", discMesh, patchMaterials[ItemId.Fog], patch);
                    layer.localPosition = new Vector3(0.08f * Mathf.Sin(i * 2.1f), i * 0.06f / radius, 0.08f * Mathf.Cos(i * 2.1f));
                    layer.localScale = Vector3.one * (1f - 0.12f * i);
                }
                spinning.Add((patch, 6f));
                break;
        }
        return patch.gameObject;
    }

    private static Transform Flat(string name, Mesh mesh, Material material, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go.transform;
    }

    private void LateUpdate()
    {
        if (game == null || game.Board == null) return;
        var t = Time.time;
        spinning.RemoveAll(s => s.spin == null);
        foreach (var (spin, speed) in spinning) spin.Rotate(0, -speed * Time.deltaTime, 0, Space.World);
        HideInFog();
        foreach (var kv in boxes)
        {
            if (kv.Value == null) continue;
            kv.Value.rotation = Quaternion.Euler(25f, t * 90f, 25f);
            var p = kv.Value.position;
            kv.Value.position = new Vector3(p.x, BoardTop + 0.08f + Mathf.Sin(t * 3f + kv.Key) * 0.02f, p.z);
        }
        foreach (var kv in rings)
        {
            if (kv.Value == null) continue;
            var piece = items.PieceOf(kv.Key.piece);
            kv.Value.gameObject.SetActive(piece != null);
            if (piece == null) continue;
            var at = piece.Body.position;
            // Rings of two items on one piece sit one outside the other, in the items' order.
            var inside = rings.Keys.Count(k => k.piece == kv.Key.piece && k.kind < kv.Key.kind);
            var size = piece.Manager.radius * (1.3f + 0.3f * inside);
            kv.Value.position = new Vector3(at.x, BoardTop + 0.003f, at.z);
            kv.Value.localScale = Vector3.one * size;
            if (kv.Key.kind == ItemId.Shield) kv.Value.localScale *= 1f + 0.06f * Mathf.Sin(t * 4f);
        }
    }

    // A piece in the fog isn't drawn at all, on any screen (its outline
    // neither: PieceOutline draws only what's drawn).
    private void HideInFog()
    {
        foreach (var renderer in fogged)
            if (renderer != null) renderer.forceRenderingOff = false;
        fogged.Clear();
        if (!items.State.Objects.Any(o => o.Kind == ItemId.Fog)) return;
        foreach (var piece in game.gamePieceScripts)
        {
            if (piece == null || !piece.gameObject.activeSelf || !items.InFog(piece)) continue;
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>())
            {
                renderer.forceRenderingOff = true;
                fogged.Add(renderer);
            }
        }
        foreach (var kv in rings)
        {
            var piece = items.PieceOf(kv.Key.piece);
            if (kv.Value != null && piece != null && items.InFog(piece)) kv.Value.gameObject.SetActive(false);
        }
    }

    // What happened, where it happened.
    private void Play(ItemEvent e)
    {
        var effects = HitEffects.Instance;
        var at = new Vector3(e.Position.x, 0, e.Position.y);
        switch (e.Kind)
        {
            case ItemEventKind.Used:
                if (effects != null && e.Position != Vector2.zero) effects.PlayTurn(at, 0.2f, 0);
                GameAudio.PlayInterface(GameAudio.Bank.stamp, 0.6f, 1.15f);
                break;
            case ItemEventKind.Gained:
            case ItemEventKind.CatchUp:
            case ItemEventKind.RoundGift:
                if (effects != null && e.Position != Vector2.zero) effects.PlayTurn(at, 0.15f, 0);
                GameAudio.PlayInterface(GameAudio.Bank.open, 0.8f, 1.2f);
                break;
            case ItemEventKind.BoxAppeared:
                GameAudio.PlayInterface(GameAudio.Bank.notch, 0.6f, 1.4f);
                break;
            case ItemEventKind.Blast:
                if (effects == null) break;
                effects.Play(at, 1f);
                for (var i = 0; i < 3; i++) effects.PlayTurn(at, ItemSystem.BlastRadius * (0.35f + 0.3f * i), i * 2);
                break;
            case ItemEventKind.Shielded:
                if (effects != null) effects.PlayTurn(at, 0.18f, 0);
                GameAudio.PlayInterface(GameAudio.Bank.notch, 0.8f, 0.8f);
                break;
            case ItemEventKind.Stuck:
                if (effects != null) effects.PlayTurn(at, 0.14f, 0);
                break;
            case ItemEventKind.Sunk:
                if (effects != null) effects.PlayTurn(at, ItemSystem.SinkholeRadius, 0);
                break;
            case ItemEventKind.Boosted:
                if (effects != null) effects.PlayTurn(at, 0.1f, 0);
                GameAudio.PlayInterface(GameAudio.Bank.notch, 0.7f, 1.6f);
                break;
            case ItemEventKind.Bumped:
                if (effects != null) effects.Play(at, 0.5f);
                break;
        }
    }

    // An arrow along +z on the board's plane, inside the unit circle.
    private static Mesh Arrow()
    {
        var mesh = new Mesh { name = "Item arrow" };
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-0.18f, 0, -0.7f), new Vector3(0.18f, 0, -0.7f), new Vector3(0.18f, 0, 0.1f), new Vector3(-0.18f, 0, 0.1f),
            new Vector3(-0.5f, 0, 0.1f), new Vector3(0.5f, 0, 0.1f), new Vector3(0, 0, 0.75f),
        });
        mesh.SetTriangles(new[] { 0, 3, 2, 0, 2, 1, 4, 6, 5 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // A band of a circle of radius r, width wide, degrees long: a vortex's swirl.
    private static Mesh Arc(float r, float width, float degrees, int segments = 32)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (var i = 0; i <= segments; i++)
        {
            var a = i * degrees * Mathf.Deg2Rad / segments;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var grow = r + 0.3f * i / segments; // a spiral, out as it goes
            vertices.Add(d * (grow - width / 2));
            vertices.Add(d * (grow + width / 2));
            if (i == segments) continue;
            var k = i * 2;
            triangles.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
        }
        var mesh = new Mesh { name = "Item swirl" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // A flat ring (inner 0: a disc) of radius 1 on the board's plane.
    private static Mesh Annulus(float inner, float outer, int segments = 48)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (var i = 0; i <= segments; i++)
        {
            var a = i * Mathf.PI * 2 / segments;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            vertices.Add(d * inner);
            vertices.Add(d * outer);
            if (i == segments) continue;
            var k = i * 2;
            triangles.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
        }
        var mesh = new Mesh { name = inner > 0 ? "Item ring" : "Item disc" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
