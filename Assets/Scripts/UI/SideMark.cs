using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A side's stone icon and/or name in the UI. The builders put one on every
// black/white icon; whoever owns the screen calls Show with the pieces in
// play, and the icon repaints as black/white or Cho/Han.
public class SideMark : MonoBehaviour
{
    public int playerId;
    public Graphic fill;
    public Graphic ring;
    public TMP_Text label;

    private PieceType pieces;

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Repaint;
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Repaint;
    }

    public void Show(PieceType value)
    {
        pieces = value;
        Repaint();
    }

    // Every mark under root, e.g. a whole screen.
    public static void ShowAll(Component root, PieceType pieces)
    {
        foreach (var mark in root.GetComponentsInChildren<SideMark>(true)) mark.Show(pieces);
    }

    private void Repaint()
    {
        if (fill != null) fill.color = SideStyle.Fill(playerId, pieces);
        if (ring != null) ring.color = SideStyle.Ring(playerId, pieces);
        if (label != null) label.text = SideStyle.Name(playerId, pieces);
    }
}
