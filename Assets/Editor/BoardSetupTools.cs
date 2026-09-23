using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Regenerates BoardSetup's preset layouts (one child per stone count, one
// point per stone) from BoardSetup.DefaultLayout. After that the points are
// ordinary scene transforms: drag them in the Scene view to tune a layout.
internal static class BoardSetupTools
{
    [MenuItem("Tools/Alkkagi/Reset spawn layouts")]
    private static void ResetSpawnLayouts()
    {
        var board = Object.FindObjectOfType<BoardSetup>();
        if (board == null)
        {
            Debug.LogError("[Alkkagi] No BoardSetup in the open scene - open GameScene first.");
            return;
        }

        if (board.layouts == null)
        {
            board.layouts = new GameObject("SpawnLayouts").transform;
            board.layouts.SetParent(board.transform, false);
        }
        for (var i = board.layouts.childCount - 1; i >= 0; i--) Object.DestroyImmediate(board.layouts.GetChild(i).gameObject);

        for (var count = 1; count <= BoardSetup.MaxStones; count++)
        {
            var layout = new GameObject(count.ToString()).transform;
            layout.SetParent(board.layouts, false);
            var points = BoardSetup.DefaultLayout(count);
            for (var i = 0; i < points.Length; i++)
            {
                var point = new GameObject($"Stone {i + 1}").transform;
                point.SetParent(layout, false);
                point.position = new Vector3(points[i].x, board.layouts.position.y, points[i].y);
            }
        }
        EditorUtility.SetDirty(board);
        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        Debug.Log("[Alkkagi] Spawn layouts reset to the defaults.");
    }
}
