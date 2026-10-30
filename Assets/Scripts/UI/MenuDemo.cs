using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The main menu's board (built by Tools > Alkkagi > Build menu demo): a few
// sides of go stones knocking each other about on the walnut table, slowly,
// on one board after another - go, janggi, hexagon, cross. Only for show:
// no rules, no scripts on the pieces, the menu's camera looking on from
// the side the menu leaves free. The stones are flicked as the game's are
// (launch speed from the same numbers) and slowed the same way.
public class MenuDemo : MonoBehaviour
{
    [System.Serializable]
    public class Stage
    {
        public GameObject board;
        public int sides = 2;
        public float spawnDistance = 1.1f; // centre to a side's row
        public float view = 3.2f;          // how far the camera stands back
    }

    public Stage[] stages;
    public GameObject blackStone;
    public GameObject whiteStone;
    private const float StoneMass = 3f; // a go stone's (GamePieceDragAndReleaseForce.referenceMass): a full flick leaves at PhysicsTuning.Flick / 3
    public int stonesPerSide = 4;
    public float shotEvery = 2.4f;     // seconds from everything stopping to the next shot
    public int shotsPerStage = 14;

    private readonly List<(Rigidbody body, int side)> stones = new List<(Rigidbody, int)>();
    private readonly Dictionary<int, Material> dyed = new Dictionary<int, Material>();
    private Camera view;
    private Vector3 viewFrom, viewTo;
    private int stage = -1;

    private void Start()
    {
        view = Camera.main;
        blackStone.SetActive(false);
        whiteStone.SetActive(false);
        foreach (var s in stages) s.board.SetActive(false);
        StartCoroutine(Play());
    }

    private void OnDestroy()
    {
        foreach (var material in dyed.Values)
            if (material != null) Destroy(material);
    }

    private IEnumerator Play()
    {
        while (true)
        {
            Next();
            yield return new WaitForSeconds(1.6f);
            var turn = Random.Range(0, stages[stage].sides);
            for (var shot = 0; shot < shotsPerStage && SidesLeft() > 1; shot++)
            {
                Shoot(turn);
                turn = (turn + 1) % stages[stage].sides;
                yield return new WaitForSeconds(0.4f);
                var until = Time.time + 8f;
                while (Time.time < until && stones.Any(s => s.body.gameObject.activeSelf && s.body.linearVelocity.sqrMagnitude > 0.003f)) yield return null;
                yield return new WaitForSeconds(shotEvery);
            }
            yield return new WaitForSeconds(1.5f);
        }
    }

    // The next board, its stones dropped in along each side's edge.
    private void Next()
    {
        foreach (var (body, _) in stones) Destroy(body.gameObject);
        stones.Clear();
        if (stage >= 0) stages[stage].board.SetActive(false);
        stage = (stage + 1) % stages.Length;
        var s = stages[stage];
        s.board.SetActive(true);
        for (var side = 0; side < s.sides; side++)
        {
            var toward = Toward(side, s.sides);
            var across = new Vector3(-toward.z, 0, toward.x);
            for (var i = 0; i < stonesPerSide; i++)
            {
                var along = (i - (stonesPerSide - 1) / 2f) * 0.2f + Random.Range(-0.03f, 0.03f);
                var at = toward * (s.spawnDistance + Random.Range(-0.08f, 0.08f)) + across * along + Vector3.up * (0.08f + 0.05f * i);
                var stone = Instantiate(side == 0 ? blackStone : whiteStone, at, blackStone.transform.rotation, transform);
                stone.SetActive(true);
                stone.GetComponent<MeshCollider>().sharedMesh = GoStoneCollider.Get();
                stone.AddComponent<RoundKnock>();
                if (side >= 2)
                    foreach (var renderer in stone.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = Dyed(side, renderer.sharedMaterial);
                stone.AddComponent<MenuDemoKnock>();
                stones.Add((stone.GetComponent<Rigidbody>(), side));
            }
        }
        // Back far enough for the board, the board right of the screen's middle.
        var size = s.view;
        // Wider screens (21:9) set it further right, as the menu stays at the left edge.
        var wide = view != null ? view.aspect / (16f / 9f) : 1f;
        viewTo = new Vector3(-size * 0.4f * wide, 0, -size * 0.06f);
        viewFrom = viewTo + Quaternion.Euler(58, 0, 0) * Vector3.back * size * 2.05f;
    }

    private Material Dyed(int side, Material white)
    {
        if (!dyed.TryGetValue(side, out var material))
        {
            material = new Material(white) { color = SideStyle.StoneColor(side) };
            dyed[side] = material;
        }
        return material;
    }

    // From the middle toward a side's edge: two face each other, three
    // round a hexagon's flat edges, four down the cross's arms.
    private static Vector3 Toward(int side, int sides)
    {
        var degrees = sides == 3 ? 270 - side * 120 : sides == 4 ? 270 - side * 90 : side == 0 ? 270 : 90;
        var a = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
    }

    private int SidesLeft() => stones.Where(s => s.body.gameObject.activeSelf).Select(s => s.side).Distinct().Count();

    // One of the side's stones at another side's, nearest most of the time,
    // a little off and as hard as the way is long.
    private void Shoot(int side)
    {
        var mine = stones.Where(s => s.side == side && s.body.gameObject.activeSelf).Select(s => s.body).ToList();
        var theirs = stones.Where(s => s.side != side && s.body.gameObject.activeSelf).Select(s => s.body).ToList();
        if (mine.Count == 0 || theirs.Count == 0) return;
        var shooter = mine[Random.Range(0, mine.Count)];
        var target = Random.value < 0.7f ? theirs.OrderBy(t => (t.position - shooter.position).sqrMagnitude).First() : theirs[Random.Range(0, theirs.Count)];
        var way = target.position - shooter.position;
        way.y = 0;
        var distance = way.magnitude;
        if (distance < 1e-3f) return;
        way = Quaternion.Euler(0, Random.Range(-7f, 7f), 0) * (way / distance);
        var power = Mathf.Clamp(0.3f + distance * 0.22f + Random.Range(-0.05f, 0.1f), 0.25f, 0.85f);
        shooter.linearVelocity = way * (power * PhysicsTuning.Current.Flick(PieceType.GoStones) / StoneMass);
    }

    private void FixedUpdate()
    {
        var tuning = PhysicsTuning.Current;
        var slowing = tuning.Extra(PieceType.GoStones);
        foreach (var (body, _) in stones)
        {
            if (!body.gameObject.activeSelf) continue;
            // Off the board and falling: gone.
            if (body.position.y < -0.3f)
            {
                body.gameObject.SetActive(false);
                continue;
            }
            var velocity = body.linearVelocity;
            if (velocity.y > tuning.MaxRiseSpeed) body.linearVelocity = new Vector3(velocity.x, tuning.MaxRiseSpeed, velocity.z);
            var flat = new Vector3(velocity.x, 0, velocity.z);
            var speed = flat.magnitude;
            if (slowing <= 0 || speed < 0.05f || body.position.y > 0.08f) continue;
            body.AddForce(-flat * (Mathf.Min(speed, slowing * Time.fixedDeltaTime) / speed), ForceMode.VelocityChange);
        }
    }

    private void LateUpdate()
    {
        if (view == null) return;
        var sharp = 1 - Mathf.Exp(-2.5f * Time.deltaTime);
        view.transform.position = Vector3.Lerp(view.transform.position, viewFrom, sharp);
        var look = Quaternion.LookRotation(viewTo - view.transform.position);
        view.transform.rotation = Quaternion.Slerp(view.transform.rotation, look, sharp);
    }
}

// A demo stone's knock, heard softly under the menu.
public class MenuDemoKnock : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.rigidbody == null) return;
        var speed = collision.relativeVelocity.magnitude;
        if (speed < 0.6f) return;
        GameAudio.PlayBoard(GameAudio.Bank.Get("hit", "go"), 0.22f * Mathf.Clamp01(speed / 6f), Random.Range(0.95f, 1.05f), 0f, Mathf.Clamp01(speed / 8f));
    }
}
