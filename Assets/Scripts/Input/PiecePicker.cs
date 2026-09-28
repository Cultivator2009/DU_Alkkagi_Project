using System.Collections.Generic;
using UnityEngine;

// Which pieces are under the cursor. Unity's own OnMouseDown only reaches
// the first collider along the ray, triggers included: a go stone's
// trigger box (wider than the stone) or any piece lying over another took
// the click meant for the one underneath, and if it wasn't the clicker's
// own piece the click did nothing.
public static class PiecePicker
{
    private static readonly RaycastHit[] Hits = new RaycastHit[32];

    // Nearest first, by the pieces' solid colliders. Only if none is under
    // the cursor, the pieces whose trigger box is: a click just off a go
    // stone still takes it, as it always has.
    public static List<GamePieceDragAndReleaseForce> UnderCursor(Camera camera)
    {
        if (camera == null || !Pointer.OnScreen) return new List<GamePieceDragAndReleaseForce>();
        return Along(camera.ScreenPointToRay(Input.mousePosition));
    }

    public static List<GamePieceDragAndReleaseForce> Along(Ray ray)
    {
        var pieces = new List<GamePieceDragAndReleaseForce>();
        foreach (var solid in new[] { true, false })
        {
            var count = Physics.RaycastNonAlloc(ray, Hits, 100f, Physics.DefaultRaycastLayers, solid ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide);
            System.Array.Sort(Hits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            for (var i = 0; i < count; i++)
            {
                var piece = Hits[i].collider.GetComponentInParent<GamePieceDragAndReleaseForce>();
                if (piece != null && !pieces.Contains(piece)) pieces.Add(piece);
            }
            if (pieces.Count > 0) break;
        }
        return pieces;
    }
}
