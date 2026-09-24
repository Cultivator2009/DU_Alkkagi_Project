using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One line of the kill feed: shooter, the piece they shot, and what it
// knocked out, plus a badge for 논개 / suicide / team kill. KillFeed fills it.
public class KillFeedEntry : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public Graphic outline;
    public TMP_Text shooterName;
    public SideMark shotIcon;
    public TMP_Text shotLetter; // janggi pieces only
    public GameObject arrow;
    public SideMark victimIcon;
    public TMP_Text victimLetter;
    public TMP_Text victimName;
    public GameObject badge;
    public TMP_Text badgeText;
}
