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
    private Material glowMaterial, ghostMaterial, arrowMaterial;
    // A ring going out from where something happened and fading: (ring, its material, when, how long, how far).
    private readonly List<(Transform ring, Material material, float at, float seconds, float radius)> pulses = new List<(Transform, Material, float, float, float)>();
    private Transform shotRing;     // under the piece this turn's shot item rides on
    private GameObject ghost;       // a ghost shot's pale double, the piece itself unseen
    private char ghostOf;
    private readonly Dictionary<char, Transform> shells = new Dictionary<char, Transform>(); // a frozen piece's ice
    private readonly Dictionary<ItemId, Material> ringMaterials = new Dictionary<ItemId, Material>();
    private Mesh ringMesh, discMesh, pulseMesh;

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

    // The ground's height under a point: the board's top, or the landscape's rise there.
    private float Top(float x, float z) => game.Board.Active.transform.position.y + Mathf.Max(0, game.Board.Active.RiseAt(new Vector2(x, z)));

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
        glowMaterial = Keep(new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.82f, 0.35f, 0.75f) });
        ghostMaterial = Keep(new Material(Shader.Find("Sprites/Default")) { color = new Color(0.92f, 0.95f, 1f, 0.38f) });
        arrowMaterial = Keep(new Material(Shader.Find("Sprites/Default")) { color = new Color(0.35f, 0.22f, 0.06f, 0.9f) });
        ringMesh = Keep(Annulus(0.72f, 1f));
        pulseMesh = Keep(Annulus(0.84f, 1f));
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
        foreach (var pulse in pulses)
            if (pulse.material != null) Destroy(pulse.material);
        RestoreGhosted();
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

    // A gold box tied with a red ribbon, turning and bobbing over the board
    // (seen from above as much as from the side), and a gold ring beating
    // round its foot as far as a piece must come.
    private Transform MakeBox(Vector2 at)
    {
        var root = new GameObject("Item box").transform;
        root.SetParent(transform, false);
        root.position = new Vector3(at.x, Top(at.x, at.y), at.y);
        var box = new GameObject("Box").transform;
        box.SetParent(root, false);
        var size = ItemSystem.BoxRadius * 1.8f;
        Part(box, Vector3.zero, Vector3.one * size, boxMaterial);
        Part(box, Vector3.zero, new Vector3(size * 1.04f, size * 1.04f, size * 0.26f), bumperMaterial);
        Part(box, Vector3.zero, new Vector3(size * 0.26f, size * 1.04f, size * 1.04f), bumperMaterial);
        // The bow on top: two loops leaning apart.
        Part(box, new Vector3(-size * 0.16f, size * 0.62f, 0), new Vector3(size * 0.3f, size * 0.22f, size * 0.18f), bumperMaterial, Quaternion.Euler(0, 0, 30));
        Part(box, new Vector3(size * 0.16f, size * 0.62f, 0), new Vector3(size * 0.3f, size * 0.22f, size * 0.18f), bumperMaterial, Quaternion.Euler(0, 0, -30));
        var foot = Flat("Foot", ringMesh, glowMaterial, root);
        foot.localPosition = new Vector3(0, 0.002f, 0);
        foot.localScale = Vector3.one * (ItemSystem.BoxRadius + 0.05f);
        Pulse(at, new Color(1f, 0.82f, 0.35f, 0.9f), 0.35f, 0.6f);
        return root;
    }

    private static void Part(Transform parent, Vector3 at, Vector3 size, Material material, Quaternion? turn = null)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localRotation = turn ?? Quaternion.identity;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    // A ring going out from at to radius over seconds, fading as it goes.
    private void Pulse(Vector2 at, Color color, float radius, float seconds)
    {
        var material = new Material(Shader.Find("Sprites/Default")) { color = color };
        var ring = Flat("Pulse", pulseMesh, material, transform);
        ring.position = new Vector3(at.x, Top(at.x, at.y) + 0.004f, at.y);
        ring.localScale = Vector3.one * 0.01f;
        pulses.Add((ring, material, Time.time, seconds, radius));
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
        post.transform.position = new Vector3(o.Position.x, Top(o.Position.x, o.Position.y) + height / 2, o.Position.y);
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
        patch.position = new Vector3(o.Position.x, Top(o.Position.x, o.Position.y) + 0.0015f, o.Position.y);
        patch.localScale = Vector3.one * radius;
        switch (o.Kind)
        {
            case ItemId.Boost:
                var arrow = Flat("Arrow", Keep(Arrow()), arrowMaterial, patch);
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
            var box = kv.Value.GetChild(0);
            box.rotation = Quaternion.Euler(18f, t * 70f, 12f);
            box.localPosition = new Vector3(0, 0.12f + Mathf.Sin(t * 3f + kv.Key) * 0.025f, 0);
            kv.Value.GetChild(1).localScale = Vector3.one * (ItemSystem.BoxRadius + 0.05f) * (1f + 0.12f * Mathf.Sin(t * 4f));
        }
        for (var i = pulses.Count - 1; i >= 0; i--)
        {
            var (ring, material, at, seconds, radius) = pulses[i];
            var u = (t - at) / seconds;
            if (u >= 1 || ring == null)
            {
                if (ring != null) Destroy(ring.gameObject);
                Destroy(material);
                pulses.RemoveAt(i);
                continue;
            }
            ring.localScale = Vector3.one * Mathf.Max(0.01f, radius * (1 - (1 - u) * (1 - u)));
            var c = material.color;
            material.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(1.4f * (1 - u)) * 0.9f);
        }
        ShotPiece(t);
        Shells();
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
            kv.Value.position = new Vector3(at.x, Top(at.x, at.z) + 0.003f, at.z);
            kv.Value.localScale = Vector3.one * size;
            if (kv.Key.kind == ItemId.Shield) kv.Value.localScale *= 1f + 0.06f * Mathf.Sin(t * 4f);
        }
    }

    // The piece this turn's shot item rides on - flicked, or being aimed
    // here - has a ring in the shot's colour, beating; a ghost shot's is a
    // pale see-through double of it instead.
    private void ShotPiece(float t)
    {
        var state = items.State;
        GamePieceDragAndReleaseForce piece = null;
        if (state.ShotItem != ItemState.NoShot)
        {
            piece = items.PieceOf(state.ShotPiece);
            var turns = game.TurnController;
            if (piece == null && turns != null)
                piece = game.gamePieceScripts.FirstOrDefault(p => p != null && p.isDragging && p.Manager.playerIndex == turns.CurrentPlayerID);
        }
        var ghostly = piece != null && state.Shot(ItemId.Ghost);
        if (!ghostly || piece.Manager.pieceID != ghostOf) RestoreGhosted();
        if (ghostly && ghost == null) MakeGhost(piece);
        if (ghost != null)
        {
            ghost.transform.SetPositionAndRotation(piece.transform.position, piece.transform.rotation);
            ghost.transform.localScale = piece.transform.lossyScale;
            foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>()) renderer.forceRenderingOff = true;
        }

        if (piece == null || ghostly)
        {
            if (shotRing != null) shotRing.gameObject.SetActive(false);
            return;
        }
        if (shotRing == null)
        {
            var material = Keep(new Material(Shader.Find("Sprites/Default")));
            shotRing = Flat("Shot ring", ringMesh, material, transform);
        }
        shotRing.gameObject.SetActive(true);
        var color = ItemDefs.ColorOf(ItemCategory.Shot);
        shotRing.GetComponent<Renderer>().sharedMaterial.color = new Color(color.r, color.g, color.b, 0.55f + 0.35f * Mathf.Sin(t * 8f));
        var at = piece.Body.position;
        shotRing.position = new Vector3(at.x, Top(at.x, at.z) + 0.0035f, at.z);
        shotRing.localScale = Vector3.one * piece.Manager.radius * (1.45f + 0.08f * Mathf.Sin(t * 8f));
    }

    private void MakeGhost(GamePieceDragAndReleaseForce piece)
    {
        ghostOf = piece.Manager.pieceID;
        ghost = new GameObject("Ghost");
        ghost.transform.SetParent(transform, true);
        var scale = piece.transform.lossyScale;
        foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>())
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            // Placed in the piece's own space, so the double follows it as the piece moves and grows.
            var part = new GameObject("Part");
            part.transform.SetParent(ghost.transform, false);
            part.transform.localPosition = piece.transform.InverseTransformPoint(renderer.transform.position);
            part.transform.localRotation = Quaternion.Inverse(piece.transform.rotation) * renderer.transform.rotation;
            var own = renderer.transform.lossyScale;
            part.transform.localScale = new Vector3(own.x / scale.x, own.y / scale.y, own.z / scale.z);
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var drawn = part.AddComponent<MeshRenderer>();
            drawn.sharedMaterial = ghostMaterial;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void RestoreGhosted()
    {
        if (ghost == null) return;
        Destroy(ghost);
        ghost = null;
        var piece = game != null ? game.gamePieceScripts.Find(p => p != null && p.Manager.pieceID == ghostOf) : null;
        ghostOf = '\0';
        if (piece == null) return;
        foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>()) renderer.forceRenderingOff = false;
    }

    // A frozen piece stands in a shell of ice till it may move again.
    private void Shells()
    {
        var frozen = items.State.Effects.Where(e => e.Kind == ItemId.Freeze).Select(e => e.Piece).ToHashSet();
        foreach (var id in shells.Keys.Where(id => !frozen.Contains(id)).ToList())
        {
            if (shells[id] != null) Destroy(shells[id].gameObject);
            shells.Remove(id);
        }
        foreach (var id in frozen)
        {
            var piece = items.PieceOf(id);
            if (!shells.TryGetValue(id, out var shell) || shell == null)
            {
                if (piece == null) continue;
                var made = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(made.GetComponent<Collider>());
                made.name = "Ice shell";
                made.transform.SetParent(transform, false);
                var renderer = made.GetComponent<Renderer>();
                renderer.sharedMaterial = ringMaterials[ItemId.Freeze];
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shells[id] = shell = made.transform;
            }
            shell.gameObject.SetActive(piece != null && !items.InFog(piece));
            if (piece == null) continue;
            var bounds = piece.GetComponentInChildren<Renderer>().bounds;
            var radius = piece.Manager.radius * 1.25f;
            shell.position = new Vector3(piece.Body.position.x, Top(piece.Body.position.x, piece.Body.position.z) + bounds.size.y * 0.6f, piece.Body.position.z);
            shell.localScale = new Vector3(radius * 2, bounds.size.y * 0.6f + 0.01f, radius * 2);
        }
    }

    // A piece in the fog isn't drawn at all, on any screen (its outline
    // neither: PieceOutline draws only what's drawn).
    private void HideInFog()
    {
        foreach (var renderer in fogged)
            if (renderer != null) renderer.forceRenderingOff = false;
        fogged.Clear();
        if (ghost != null) ghost.SetActive(true);
        if (!items.State.Objects.Any(o => o.Kind == ItemId.Fog)) return;
        foreach (var piece in game.gamePieceScripts)
        {
            if (piece == null || !piece.gameObject.activeSelf || !items.InFog(piece)) continue;
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>())
            {
                renderer.forceRenderingOff = true;
                fogged.Add(renderer);
            }
            if (ghost != null && piece.Manager.pieceID == ghostOf) ghost.SetActive(false);
            if (shotRing != null && piece.Manager.pieceID == items.State.ShotPiece) shotRing.gameObject.SetActive(false);
        }
        foreach (var kv in rings)
        {
            var piece = items.PieceOf(kv.Key.piece);
            if (kv.Value != null && piece != null && items.InFog(piece)) kv.Value.gameObject.SetActive(false);
        }
    }

    // What happened, where it happened: the board's own effects, and a
    // ring going out in the item's colour.
    private void Play(ItemEvent e)
    {
        var effects = HitEffects.Instance;
        var at = new Vector3(e.Position.x, 0, e.Position.y);
        var placed = e.Position != Vector2.zero;
        var gold = new Color(1f, 0.82f, 0.35f, 0.9f);
        var colour = ItemDefs.IsValid((byte)e.Item) ? ItemDefs.ColorOf(ItemDefs.Of(e.Item).Category) : gold;
        switch (e.Kind)
        {
            case ItemEventKind.Used:
                if (placed)
                {
                    if (effects != null) effects.PlayTurn(at, 0.2f, 0);
                    Pulse(e.Position, colour, 0.4f, 0.55f);
                }
                var bank = GameAudio.Bank;
                var sound = e.Item switch
                {
                    ItemId.Freeze => bank.itemFreeze,
                    ItemId.Shield => bank.itemShield,
                    ItemId.Glue => bank.itemGlue,
                    ItemId.Quake => bank.itemQuake,
                    ItemId.Fog => bank.itemFog,
                    _ => bank.itemUse,
                };
                GameAudio.PlayBoard(sound, 0.75f);
                break;
            case ItemEventKind.Gained:
            case ItemEventKind.CatchUp:
            case ItemEventKind.RoundGift:
                if (placed)
                {
                    if (effects != null) effects.PlayTurn(at, 0.15f, 0);
                    Pulse(e.Position, gold, 0.35f, 0.5f);
                    Pulse(e.Position, gold, 0.6f, 0.8f);
                }
                GameAudio.PlayInterface(GameAudio.Bank.itemGain, 0.8f);
                break;
            case ItemEventKind.BoxAppeared:
                GameAudio.PlayInterface(GameAudio.Bank.itemBox, 0.6f);
                break;
            case ItemEventKind.Blast:
                Pulse(e.Position, new Color(1f, 0.55f, 0.2f, 0.95f), ItemSystem.BlastRadius * 1.05f, 0.45f);
                Pulse(e.Position, new Color(1f, 0.85f, 0.5f, 0.9f), ItemSystem.BlastRadius * 0.6f, 0.3f);
                GameAudio.PlayBoard(GameAudio.Bank.itemBlast, 1f);
                if (effects == null) break;
                effects.Play(at, 1f);
                for (var i = 0; i < 3; i++) effects.PlayTurn(at, ItemSystem.BlastRadius * (0.35f + 0.3f * i), i * 2);
                break;
            case ItemEventKind.Shielded:
                if (effects != null) effects.PlayTurn(at, 0.18f, 0);
                Pulse(e.Position, gold, 0.3f, 0.45f);
                GameAudio.PlayBoard(GameAudio.Bank.itemShield, 0.8f);
                break;
            case ItemEventKind.Stuck:
                if (effects != null) effects.PlayTurn(at, 0.14f, 0);
                Pulse(e.Position, ItemDefs.ColorOf(ItemCategory.Piece), 0.2f, 0.4f);
                GameAudio.PlayBoard(GameAudio.Bank.itemGlue, 0.7f);
                break;
            case ItemEventKind.Sunk:
                if (effects != null) effects.PlayTurn(at, ItemSystem.SinkholeRadius, 0);
                Pulse(e.Position, new Color(0.1f, 0.07f, 0.05f, 0.9f), 0.3f, 0.5f);
                GameAudio.PlayBoard(GameAudio.Bank.itemSink, 0.85f);
                break;
            case ItemEventKind.Boosted:
                if (effects != null) effects.PlayTurn(at, 0.1f, 0);
                Pulse(e.Position, gold, 0.18f, 0.3f);
                GameAudio.PlayBoard(GameAudio.Bank.itemBoost, 0.8f);
                break;
            case ItemEventKind.Bumped:
                if (effects != null) effects.Play(at, 0.5f);
                Pulse(e.Position, ItemDefs.ColorOf(ItemCategory.Shot), 0.25f, 0.35f);
                GameAudio.PlayBoard(GameAudio.Bank.itemBumper, 0.85f);
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
