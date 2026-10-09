using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Foresight (ItemId.Foresight), on the screen aiming the shot that carries
// it: while the pull is held, a line on the board for every piece the shot
// would move - the shot's own pale, what it sends gold - from a copy of the
// board stepped as the AI tries its shots (AIPlanner.Trace), again whenever
// the aim moves.
public class ItemForesight : MonoBehaviour
{
    private ItemSystem items;
    private GameManager game;
    private AIPlanner planner;
    private GamePieceDragAndReleaseForce aimed;
    private Vector3 tracedDirection;
    private float tracedPower = -1;
    private bool tracing;
    private List<(GamePieceDragAndReleaseForce piece, List<Vector3> path)> paths = new List<(GamePieceDragAndReleaseForce, List<Vector3>)>();
    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private Material material;

    private static readonly Color ShotColor = new Color(1f, 0.97f, 0.9f, 0.85f);
    private static readonly Color SentColor = new Color(0.93f, 0.72f, 0.26f, 0.9f);

    public void Init(ItemSystem itemSystem, GameManager gameManager)
    {
        items = itemSystem;
        game = gameManager;
        material = new Material(Shader.Find("Sprites/Default"));
    }

    private void OnDestroy()
    {
        Stop();
        if (material != null) Destroy(material);
    }

    private void Update()
    {
        if (game == null || game.Board == null) return;
        var piece = game.gamePieceScripts.FirstOrDefault(p => p != null && p.isDragging);
        if (piece == null || !items.ShotCarries(piece.Manager.playerIndex, ItemId.Foresight) || piece.AimPower < 0.03f || piece.AimDirection == Vector3.zero)
        {
            Stop();
            return;
        }
        if (planner == null || aimed != piece)
        {
            Stop();
            planner = new AIPlanner(game.Board, game.gamePieceScripts, game.Ruleset, game.Sides, MatchSettings.Current.BothOutRule, false, game.TeamOf);
            aimed = piece;
        }
        if (!tracing && (Vector3.Angle(piece.AimDirection, tracedDirection) > 0.8f || Mathf.Abs(piece.AimPower - tracedPower) > 0.015f))
            StartCoroutine(Trace(piece, piece.AimDirection, piece.AimPower));
        Draw();
    }

    private IEnumerator Trace(GamePieceDragAndReleaseForce piece, Vector3 direction, float power)
    {
        tracing = true;
        tracedDirection = direction;
        tracedPower = power;
        var result = new List<(GamePieceDragAndReleaseForce, List<Vector3>)>();
        yield return planner.Trace(piece, direction, power, result);
        paths = result;
        tracing = false;
    }

    private void Draw()
    {
        var top = game.Board.Active.transform.position.y + 0.012f;
        for (var i = 0; i < paths.Count; i++)
        {
            if (i >= lines.Count) lines.Add(MakeLine());
            var line = lines[i];
            var path = paths[i].path;
            line.gameObject.SetActive(true);
            line.positionCount = path.Count;
            for (var j = 0; j < path.Count; j++) line.SetPosition(j, new Vector3(path[j].x, Mathf.Max(path[j].y, top), path[j].z));
            line.startColor = line.endColor = i == 0 ? ShotColor : SentColor;
        }
        for (var i = paths.Count; i < lines.Count; i++) lines[i].gameObject.SetActive(false);
    }

    private LineRenderer MakeLine()
    {
        var line = new GameObject("Foresight").AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.material = material;
        line.widthMultiplier = 0.022f;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }

    private void Stop()
    {
        StopAllCoroutines();
        tracing = false;
        tracedPower = -1;
        aimed = null;
        paths.Clear();
        foreach (var line in lines) line.gameObject.SetActive(false);
        planner?.Dispose();
        planner = null;
    }
}
