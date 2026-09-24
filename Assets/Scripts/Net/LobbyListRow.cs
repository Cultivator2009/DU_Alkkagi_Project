using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One open public lobby in the lobby browser: host, rules, and Join.
public class LobbyListRow : MonoBehaviour
{
    public TMP_Text hostText;
    public TMP_Text rulesText;
    public Button joinButton;

    public ulong LobbyId { get; private set; }

    public void Show(ulong lobbyId, string host, string rules)
    {
        LobbyId = lobbyId;
        hostText.text = host;
        rulesText.text = rules;
        gameObject.SetActive(true);
    }
}
