using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One lobby seat. The host always has the first (black); the others follow
// in the order Steam lists the members (SteamLobbyManager.SeatOrder), then
// the host's bots. Each shows the side it will play (with teams, where the
// match will seat it).
public class LobbyPlayerSlot : MonoBehaviour
{
    public GameObject filledView;
    public TMP_Text nameText;
    public TMP_Text roleText;
    public GameObject ratingChip;
    public TMP_Text ratingText;
    public SideMark mark;      // the filled seat's stone, in its side's colour
    public GameObject emptyView;
    public SideMark emptyMark; // the open seat's side name
    public Button inviteButton;
    public Button addBotButton; // an open seat, for the host: one of its bots into it
    public Button kickButton; // guests' seats (and the bots'); LobbySceneUI shows it to the host
    public Button blockButton; // anyone else's seat: never matched with them again (BlockList)
    public Button levelButton; // a bot's seat, for the host: how strong it plays (cycles)
    public TMP_Text levelText;
    public Button teamButton;  // with teams: the seat's team, the host's to switch
    public TMP_Text teamText;

    // rating: null until the player's game has shared it.
    public void ShowPlayer(string playerName, string role, int? rating, int side)
    {
        filledView.SetActive(true);
        emptyView.SetActive(false);
        nameText.text = playerName;
        roleText.text = role;
        ratingChip.SetActive(rating.HasValue);
        if (rating.HasValue) ratingText.text = rating.Value.ToString();
        mark.playerId = side;
        levelButton.gameObject.SetActive(false);
    }

    // level: the strength's name, the host's to change (canChange).
    public void ShowBot(string botName, string role, string level, int side, bool canChange)
    {
        ShowPlayer(botName, role, null, side);
        levelButton.gameObject.SetActive(canChange);
        levelText.text = level;
    }

    public void ShowEmpty(bool canInvite, bool canAddBot, int side)
    {
        filledView.SetActive(false);
        emptyView.SetActive(true);
        emptyMark.playerId = side;
        if (inviteButton != null) inviteButton.gameObject.SetActive(canInvite);
        addBotButton.gameObject.SetActive(canAddBot);
    }

    // team: null without teams. The rating chip gives way to it.
    public void ShowTeam(int? team, bool canChange)
    {
        teamButton.gameObject.SetActive(team.HasValue && filledView.activeSelf);
        if (!team.HasValue) return;
        teamText.text = Loc.Get("team.number", team.Value + 1);
        teamButton.interactable = canChange;
        ratingChip.SetActive(false);
    }
}
