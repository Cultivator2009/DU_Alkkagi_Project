using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One lobby seat. The host always has the first (black); the others follow
// in the order Steam lists the members (SteamLobbyManager.SeatOrder).
public class LobbyPlayerSlot : MonoBehaviour
{
    public GameObject filledView;
    public TMP_Text nameText;
    public TMP_Text roleText;
    public GameObject ratingChip;
    public TMP_Text ratingText;
    public GameObject emptyView;
    public Button inviteButton;
    public Button kickButton; // guests' seats only; LobbySceneUI shows it to the host

    // rating: null until the player's game has shared it.
    public void ShowPlayer(string playerName, string role, int? rating)
    {
        filledView.SetActive(true);
        emptyView.SetActive(false);
        nameText.text = playerName;
        roleText.text = role;
        ratingChip.SetActive(rating.HasValue);
        if (rating.HasValue) ratingText.text = rating.Value.ToString();
    }

    public void ShowEmpty(bool canInvite)
    {
        filledView.SetActive(false);
        emptyView.SetActive(true);
        if (inviteButton != null) inviteButton.gameObject.SetActive(canInvite);
    }
}
