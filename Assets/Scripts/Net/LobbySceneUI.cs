using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the Lobby_UI prefab (built by Tools > Alkkagi UI > 4. Build lobby).
// Two views: idle (create a lobby, or join one by code) and in-lobby (code
// to share, both seats, invite, start). Everything renders from
// SteamLobbyManager's state, so arriving here mid-join - e.g. from an
// accepted Steam invite - shows the right view without extra wiring.
public class LobbySceneUI : MonoBehaviour
{
    [Header("Idle")]
    public GameObject idleView;
    public Button createButton;
    public TMP_InputField joinCodeInput;
    public Button joinButton;
    public Button backButton;

    [Header("In lobby")]
    public GameObject lobbyView;
    public TMP_Text lobbyCodeText;
    public Button copyButton;
    public TMP_Text copyLabel;
    public LobbyPlayerSlot hostSlot;
    public LobbyPlayerSlot guestSlot;
    public Button leaveButton;
    public Button startButton;

    [Header("Status")]
    public TMP_Text statusText;
    public Image statusDot;
    public Color okColor = Color.green;
    public Color busyColor = Color.yellow;
    public Color errorColor = Color.red;

    private const float CopiedFeedbackSeconds = 1.5f;

    private SteamLobbyManager lobbyManager;
    private string pendingStatusKey; // creating / badCode / failed, until the lobby state takes over
    private float copiedUntil;

    private void Awake()
    {
        NetworkServices.EnsureCreated();
        EnsureEventSystem();

        createButton.onClick.AddListener(OnClickCreate);
        joinButton.onClick.AddListener(OnClickJoin);
        joinCodeInput.onSubmit.AddListener(_ => OnClickJoin());
        backButton.onClick.AddListener(() => SceneManager.LoadScene("MainMenuScene"));
        copyButton.onClick.AddListener(OnClickCopy);
        guestSlot.inviteButton.onClick.AddListener(() => lobbyManager.InviteFriends());
        leaveButton.onClick.AddListener(OnClickLeave);
        startButton.onClick.AddListener(OnClickStartMatch);
    }

    private void Start()
    {
        lobbyManager = SteamLobbyManager.Instance;
        lobbyManager.OnLobbyReady += HandleLobbyReady;
        lobbyManager.OnMemberJoined += HandleMemberChanged;
        lobbyManager.OnMemberLeft += HandleMemberChanged;
        lobbyManager.OnLobbyFailed += HandleLobbyFailed;
        SteamTransport.Instance.OnMessageReceived += HandleNetworkMessage;
        Loc.OnLanguageChanged += Render;
        Render();
    }

    // SteamLobbyManager outlives this scene (DontDestroyOnLoad), so every
    // handler must come off again or re-entering the lobby stacks stale ones.
    private void OnDestroy()
    {
        if (lobbyManager != null)
        {
            lobbyManager.OnLobbyReady -= HandleLobbyReady;
            lobbyManager.OnMemberJoined -= HandleMemberChanged;
            lobbyManager.OnMemberLeft -= HandleMemberChanged;
            lobbyManager.OnLobbyFailed -= HandleLobbyFailed;
        }
        if (SteamTransport.Instance != null) SteamTransport.Instance.OnMessageReceived -= HandleNetworkMessage;
        Loc.OnLanguageChanged -= Render;
    }

    private void Update()
    {
        if (copiedUntil > 0 && Time.unscaledTime >= copiedUntil)
        {
            copiedUntil = 0;
            copyLabel.text = Loc.Get("lobby.copy");
        }
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    // ---- Button handlers ----

    private void OnClickCreate()
    {
        pendingStatusKey = "lobby.status.creating";
        lobbyManager.CreateLobby();
        Render();
    }

    private void OnClickJoin()
    {
        if (!ulong.TryParse(joinCodeInput.text.Trim(), out var lobbyId))
        {
            pendingStatusKey = "lobby.status.badCode";
            Render();
            return;
        }
        pendingStatusKey = null;
        lobbyManager.JoinLobby(lobbyId);
        Render();
    }

    private void OnClickCopy()
    {
        if (!lobbyManager.CurrentLobby.HasValue) return;
        GUIUtility.systemCopyBuffer = lobbyManager.CurrentLobby.Value.Id.Value.ToString();
        copyLabel.text = Loc.Get("lobby.copied");
        copiedUntil = Time.unscaledTime + CopiedFeedbackSeconds;
    }

    private void OnClickLeave()
    {
        lobbyManager.LeaveLobby();
        pendingStatusKey = null;
        Render();
    }

    private void OnClickStartMatch()
    {
        SteamTransport.Instance.Broadcast(NetMessage.WriteLoadGameScene());
        SceneManager.LoadScene("GameScene");
    }

    // ---- Lobby events ----

    private void HandleLobbyReady(Steamworks.Data.Lobby lobby)
    {
        pendingStatusKey = null;
        Render();
    }

    private void HandleMemberChanged(Steamworks.Friend friend)
    {
        Render();
    }

    private void HandleLobbyFailed()
    {
        pendingStatusKey = "lobby.status.failed";
        Render();
    }

    private void HandleNetworkMessage(ulong senderId, byte[] data)
    {
        if (NetMessage.PeekType(data) == NetMessageType.LoadGameScene) SceneManager.LoadScene("GameScene");
    }

    // ---- Rendering ----

    private void Render()
    {
        var steamReady = SteamTransport.Instance != null && SteamTransport.Instance.IsReady;
        var inLobby = lobbyManager != null && lobbyManager.CurrentLobby.HasValue;
        idleView.SetActive(!inLobby);
        lobbyView.SetActive(inLobby);

        if (!steamReady)
        {
            SetStatus("lobby.status.noSteam", errorColor);
            SetInteractable(createButton, false);
            SetInteractable(joinButton, false);
            return;
        }

        if (!inLobby)
        {
            var busy = lobbyManager.IsJoining || pendingStatusKey == "lobby.status.creating";
            SetInteractable(createButton, !busy);
            SetInteractable(joinButton, !busy);
            if (lobbyManager.IsJoining) SetStatus("lobby.status.joining", busyColor);
            else if (pendingStatusKey != null) SetStatus(pendingStatusKey, busy ? busyColor : errorColor);
            else SetStatus("lobby.status.idle", okColor);
            return;
        }

        var lobby = lobbyManager.CurrentLobby.Value;
        lobbyCodeText.text = lobby.Id.Value.ToString();
        if (copiedUntil == 0) copyLabel.text = Loc.Get("lobby.copy");

        var members = lobby.Members.ToList();
        var host = members.FirstOrDefault(m => m.Id.Value == lobby.Owner.Id.Value);
        var guest = members.FirstOrDefault(m => m.Id.Value != lobby.Owner.Id.Value);
        hostSlot.ShowPlayer(host.Name, $"{Loc.Get("lobby.host")} · {Loc.Get("player.black")}");
        if (members.Count > 1) guestSlot.ShowPlayer(guest.Name, $"{Loc.Get("lobby.guest")} · {Loc.Get("player.white")}");
        else guestSlot.ShowEmpty(lobbyManager.IsHost);

        var full = members.Count >= 2;
        startButton.gameObject.SetActive(lobbyManager.IsHost);
        SetInteractable(startButton, full);
        if (!lobbyManager.IsHost) SetStatus("lobby.status.waitingHost", busyColor);
        else if (full) SetStatus("lobby.status.ready", okColor);
        else SetStatus("lobby.status.waitingOpponent", busyColor);
    }

    private void SetStatus(string key, Color dot)
    {
        statusText.text = Loc.Get(key);
        statusDot.color = dot;
    }

    // Dims the whole capsule (fill, ring and label) via its CanvasGroup;
    // Button's own color tint only reaches the fill.
    private static void SetInteractable(Button button, bool interactable)
    {
        button.interactable = interactable;
        var group = button.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = interactable ? 1f : 0.45f;
    }
}
