using System.Collections.Generic;
using UnityEngine;

// A piece knocked off the board is out the moment it drops past the board
// (DeathTrigger), well before it reaches the table: it used to vanish in
// mid air. This lets a look-alike fall on - onto the floor, a bounce, a
// roll - and fade a few seconds later. Only for show: a copy with no
// scripts, in a physics layer of its own that meets nothing but the floor
// and the other fallen (on every screen alike, a guest's from how its
// piece was last seen moving).
public class FallenPieces : MonoBehaviour
{
    public const int Layer = 30;          // unnamed, kept for these
    private const int Most = 12;          // on the table at once; the oldest goes first
    private const float Stay = 3.5f;      // seconds before one fades
    private const float Fade = 0.5f;

    private static FallenPieces instance;
    private readonly List<(GamePieceDragAndReleaseForce piece, Vector3 velocity)> waiting = new List<(GamePieceDragAndReleaseForce, Vector3)>();
    private readonly List<(GameObject copy, float at, Vector3 scale)> fallen = new List<(GameObject, float, Vector3)>();

    // A piece out below the board's top: its look-alike falls on from here.
    public static void Add(GamePieceDragAndReleaseForce piece, Vector3 velocity)
    {
        if (!Application.isPlaying || HitEffects.Instance == null) return;
        if (instance == null)
        {
            instance = new GameObject("Fallen pieces").AddComponent<FallenPieces>();
            for (var i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layer, i, i != Layer);
            Floor();
        }
        // Copied once the frame's done with it (it's being switched off now).
        instance.waiting.Add((piece, velocity));
    }

    // The floor, for them alone: a slab just under its top.
    private static void Floor()
    {
        var floor = new GameObject("Fallen floor") { layer = Layer };
        floor.transform.SetParent(instance.transform, false);
        var box = floor.AddComponent<BoxCollider>();
        box.size = new Vector3(40, 0.2f, 40);
        box.center = new Vector3(0, PieceShatter.TableTop - 0.1f, 0);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        foreach (var (piece, velocity) in waiting)
            if (piece != null) Copy(piece, velocity);
        waiting.Clear();

        var now = Time.time;
        for (var i = fallen.Count - 1; i >= 0; i--)
        {
            var (copy, at, scale) = fallen[i];
            var age = now - at;
            if (copy == null || age > Stay + Fade || fallen.Count - i > Most)
            {
                if (copy != null) Destroy(copy);
                fallen.RemoveAt(i);
                continue;
            }
            if (age > Stay) copy.transform.localScale = scale * (1 - (age - Stay) / Fade);
        }
    }

    private void Copy(GamePieceDragAndReleaseForce piece, Vector3 velocity)
    {
        // Broken already (a battle of health's last knock): nothing whole to fall.
        var drawn = false;
        foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>(true)) drawn |= renderer.enabled && !renderer.forceRenderingOff;
        if (!drawn) return;

        var copy = Instantiate(piece.gameObject, piece.transform.position, piece.transform.rotation, transform);
        copy.name = "Fallen " + piece.name;
        // The game's own scripts off; what draws it (a janggi piece's letter) stays.
        var ours = typeof(GamePieceDragAndReleaseForce).Assembly;
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour.GetType().Assembly == ours) DestroyImmediate(behaviour);
        foreach (var line in copy.GetComponentsInChildren<LineRenderer>(true)) line.enabled = false;
        foreach (var joint in copy.GetComponentsInChildren<Joint>(true)) DestroyImmediate(joint); // an item's glue, tied to the board's pieces
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        foreach (var solid in copy.GetComponentsInChildren<Collider>(true))
            if (solid.isTrigger) Destroy(solid);
        copy.SetActive(true);
        var body = copy.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 4f;
        }
        fallen.Add((copy, Time.time, copy.transform.localScale));
    }
}
