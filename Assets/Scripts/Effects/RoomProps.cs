using UnityEngine;

// The things on the room's floor by the board (RoomBuilder: the bowls, the
// cushions, the tea table, the lamp). Laid out round the small boards; a
// bigger one (the landscape) would stand on them, so each moves straight
// out from the middle until it's clear of the board (Clear), back again
// for a small one.
public class RoomProps : MonoBehaviour
{
    public float margin = 2.2f; // between the board's edge and a thing's middle

    private Vector3[] home;

    private void Awake()
    {
        home = new Vector3[transform.childCount];
        for (var i = 0; i < home.Length; i++) home[i] = transform.GetChild(i).localPosition;
    }

    public void Clear(Bounds board)
    {
        if (home == null) Awake();
        var half = new Vector2(board.extents.x + margin, board.extents.z + margin);
        for (var i = 0; i < home.Length; i++)
        {
            var at = home[i];
            var flat = new Vector2(at.x - board.center.x, at.z - board.center.z);
            // How far out along its own line it has to go to be outside the box.
            var need = Mathf.Min(Mathf.Abs(flat.x) > 1e-3f ? half.x / Mathf.Abs(flat.x) : float.MaxValue, Mathf.Abs(flat.y) > 1e-3f ? half.y / Mathf.Abs(flat.y) : float.MaxValue);
            var scale = Mathf.Max(1f, need);
            transform.GetChild(i).localPosition = new Vector3(board.center.x + flat.x * scale, at.y, board.center.z + flat.y * scale);
        }
    }
}
