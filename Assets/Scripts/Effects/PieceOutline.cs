using UnityEngine;
using UnityEngine.Rendering;

// A line round the pieces the aim marks: the one a click would pick up (under
// the cursor), the one being aimed - deepening from white to seal red with
// the pull's power - and the one the shot would meet. It follows each
// piece's silhouette on screen, so it fits a chess piece lying down or a
// stone half under another, where the rings drawn round a piece's foot
// didn't. Drawn after the scene, before the HUD (Shaders/PieceOutline).
// Lives on the main camera: For finds or adds it.
[RequireComponent(typeof(Camera))]
public class PieceOutline : MonoBehaviour
{
    public enum Mark
    {
        Hover,
        Aim,
        Target
    }

    public float width = 10f; // pixels at 1080 lines, line and fringe together
    public Color hoverColor = new Color(1f, 0.97f, 0.9f, 0.95f);
    public Color aimColor = new Color(0.70f, 0.19f, 0.16f, 1f); // at full power
    public Color targetColor = new Color(0.93f, 0.72f, 0.26f, 0.95f);
    public Color fringeColor = new Color(0.10f, 0.07f, 0.05f, 0.5f);

    private static readonly int MaskId = Shader.PropertyToID("_PieceOutlineMask");
    private readonly GamePieceDragAndReleaseForce[] marked = new GamePieceDragAndReleaseForce[3];
    private Material[] maskMaterials;
    private Material lineMaterial;
    private CommandBuffer buffer;
    private Camera view;

    // 0..1: how far the aimed piece's line has gone from white to aimColor.
    public float AimPower { get; set; }

    public static PieceOutline For(Camera camera)
    {
        var outline = camera.GetComponent<PieceOutline>();
        return outline != null ? outline : camera.gameObject.AddComponent<PieceOutline>();
    }

    public void Set(Mark mark, GamePieceDragAndReleaseForce piece) => marked[(int)mark] = piece;

    private void Awake()
    {
        view = GetComponent<Camera>();
        var template = Resources.Load<Material>("PieceOutline");
        lineMaterial = new Material(template);
        maskMaterials = new Material[marked.Length];
        for (var i = 0; i < marked.Length; i++)
        {
            maskMaterials[i] = new Material(template);
            maskMaterials[i].SetColor("_MaskChannel", new Color(i == 0 ? 1 : 0, i == 1 ? 1 : 0, i == 2 ? 1 : 0, 1));
        }
        buffer = new CommandBuffer { name = "Piece outline" };
    }

    private void OnEnable() => view.AddCommandBuffer(CameraEvent.AfterForwardAlpha, buffer);

    private void OnDisable() => view.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, buffer);

    private void OnDestroy()
    {
        buffer.Release();
        Destroy(lineMaterial);
        foreach (var material in maskMaterials) Destroy(material);
    }

    // Rebuilt every frame, from where the marked pieces are now.
    private void OnPreRender()
    {
        buffer.Clear();
        var any = false;
        foreach (var piece in marked) any |= piece != null && piece.gameObject.activeInHierarchy;
        if (!any) return;

        buffer.GetTemporaryRT(MaskId, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
        buffer.SetRenderTarget(MaskId);
        buffer.ClearRenderTarget(false, true, Color.clear);
        for (var mark = 0; mark < marked.Length; mark++)
        {
            var piece = marked[mark];
            if (piece == null || !piece.gameObject.activeInHierarchy) continue;
            var body = piece.GetComponent<MeshRenderer>(); // not a janggi piece's letter
            if (body == null || !body.enabled) continue;
            for (var submesh = 0; submesh < body.sharedMaterials.Length; submesh++) buffer.DrawRenderer(body, maskMaterials[mark], submesh, 0);
        }

        lineMaterial.SetFloat("_Width", width * view.pixelHeight / 1080f);
        lineMaterial.SetColor("_HoverColor", hoverColor);
        lineMaterial.SetColor("_AimColor", Color.Lerp(hoverColor, aimColor, Mathf.Sqrt(AimPower))); // red soon, not only near full
        lineMaterial.SetColor("_TargetColor", targetColor);
        lineMaterial.SetColor("_FringeColor", fringeColor);
        buffer.Blit(MaskId, BuiltinRenderTextureType.CameraTarget, lineMaterial, 1);
        buffer.ReleaseTemporaryRT(MaskId);
    }
}
