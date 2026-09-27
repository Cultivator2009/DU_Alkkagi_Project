using UnityEngine;

public static class Pointer
{
    // Whether the cursor is over the game's own window. Outside it (another
    // window, or the editor's other panes) Input.mousePosition still reads,
    // and a camera ray through it fails with an error, every frame.
    public static bool OnScreen
    {
        get
        {
            var p = Input.mousePosition;
            return p.x >= 0 && p.y >= 0 && p.x < Screen.width && p.y < Screen.height;
        }
    }
}
