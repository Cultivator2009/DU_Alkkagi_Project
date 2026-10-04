using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One achievement on the records card: a seal stamped once it's earned,
// its name and what it takes, and how far along a counted one is. Not yet
// earned, it's faded.
public class AchievementTile : MonoBehaviour
{
    public Image seal;
    public Image ring;           // the seal's place, before it's stamped
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public TMP_Text progressText;
    public CanvasGroup group;
    [Range(0f, 1f)] public float lockedAlpha = 0.55f;

    public void Show(Achievements.Def def, PlayerRecords records)
    {
        var earned = records.Has(def.Id);
        seal.enabled = earned;
        ring.enabled = !earned;
        titleText.text = def.Title;
        descriptionText.text = def.Description;
        progressText.text = def.Goal > 0 && !earned ? $"{Mathf.Min(records.Progress(def.Id), def.Goal)}/{def.Goal}" : string.Empty;
        group.alpha = earned ? 1f : lockedAlpha;
    }
}
