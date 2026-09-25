using TMPro;
using UnityEngine;

// One line of the rankings card: place, Steam name, rating, record. This
// player's own line is highlighted.
public class LeaderboardRow : MonoBehaviour
{
    public GameObject ownHighlight;
    public TMP_Text placeText;
    public TMP_Text nameText;
    public TMP_Text ratingText;
    public TMP_Text recordText;

    public void Show(RankBoard.Entry entry, bool own)
    {
        gameObject.SetActive(true);
        ownHighlight.SetActive(own);
        placeText.text = entry.Place.ToString();
        nameText.text = entry.Name;
        ratingText.text = entry.Record.Rating.ToString();
        recordText.text = LeaderboardPanel.RecordText(entry.Record);
    }
}
