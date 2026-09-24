using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the Lobby_UI prefab (built by Tools > Alkkagi UI > 4. Build lobby).
// Two views: idle (create a lobby, quick match, join by code, or pick one
// from the public lobby browser) and in-lobby (code to share, both seats,
// who can join, invite, start). Everything renders from SteamLobbyManager's
// state, so arriving here mid-join - e.g. from an accepted Steam invite -
// shows the right view without extra wiring.
public class LobbySceneUI : MonoBehaviour
{
    [Header("Idle")]
    public GameObject idleView;
    public Button createButton;
    public Button quickButton;
    public TMP_InputField joinCodeInput;
    public Button joinButton;
    public Button backButton;
    public Button refreshButton;
    public LobbyListRow[] browserRows;
    public TMP_Text browserEmptyText;

    public RectTransform card;
    public float lobbyCardShift; // moves the main card left, making room for the browser or rules card

    [Header("In lobby")]
    public GameObject lobbyView;
    public TMP_Text lobbyCodeText;
    public Button copyButton;
    public TMP_Text copyLabel;
    public LobbyPlayerSlot hostSlot;
    public LobbyPlayerSlot guestSlot;
    public MatchSettingsPanel rulesPanel; // host edits, the guest sees it read-only
    public TMP_Text rulesCaption;
    public SegmentedToggle visibilityToggle; // LobbyVisibility order; the host's to change
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
    private string pendingStatusKey; // creating / badCode / failed / version / kicked, until the lobby state takes over
    private float copiedUntil;
    private Steamworks.Data.Lobby[] openLobbies = Array.Empty<Steamworks.Data.Lobby>();
    private bool browsing; // a browser search in flight

    private void Awake()
    {
        NetworkServices.EnsureCreated();
        EnsureEventSystem();

        createButton.onClick.AddListener(OnClickCreate);
        quickButton.onClick.AddListener(OnClickQuick);
        joinButton.onClick.AddListener(OnClickJoin);
        refreshButton.onClick.AddListener(RefreshBrowser);
        foreach (var row in browserRows)
        {
            var target = row;
            row.joinButton.onClick.AddListener(() => JoinLobby(target.LobbyId));
        }
        joinCodeInput.onSubmit.AddListener(_ => OnClickJoin());
        backButton.onClick.AddListener(() => SceneManager.LoadScene("MainMenuScene"));
        copyButton.onClick.AddListener(OnClickCopy);
        guestSlot.inviteButton.onClick.AddListener(() => lobbyManager.InviteFriends());
        leaveButton.onClick.AddListener(OnClickLeave);
        startButton.onClick.AddListener(OnClickStartMatch);
        rulesPanel.OnChanged += OnRulesChanged;
        visibilityToggle.OnSelected += index =>
        {
            lobbyManager.SetVisibility((LobbyVisibility)index);
            Render();
        };
        guestSlot.kickButton.onClick.AddListener(OnClickKick);
    }

    private void Start()
    {
        lobbyManager = SteamLobbyManager.Instance;
        lobbyManager.OnLobbyReady += HandleLobbyReady;
        lobbyManager.OnMemberJoined += HandleMemberChanged;
        lobbyManager.OnMemberLeft += HandleMemberChanged;
        lobbyManager.OnLobbyFailed += HandleLobbyFailed;
        lobbyManager.OnLobbyDataChanged += Render;
        SteamTransport.Instance.OnMessageReceived += HandleNetworkMessage;
        Loc.OnLanguageChanged += Render;
        // Back from a match: open the lobby to new players again.
        if (lobbyManager.IsHost) lobbyManager.SetMatchInProgress(false);
        Render();
        if (!lobbyManager.CurrentLobby.HasValue) RefreshBrowser();
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
            lobbyManager.OnLobbyDataChanged -= Render;
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
        lobbyManager.CreateLobby(SteamLobbyManager.PreferredVisibility);
        Render();
    }

    private void OnClickQuick()
    {
        pendingStatusKey = null;
        lobbyManager.QuickMatch();
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
        JoinLobby(lobbyId);
    }

    private void JoinLobby(ulong lobbyId)
    {
        pendingStatusKey = null;
        lobbyManager.JoinLobby(lobbyId);
        Render();
    }

    private async void RefreshBrowser()
    {
        if (browsing || SteamTransport.Instance == null || !SteamTransport.Instance.IsReady) return;
        browsing = true;
        Render();
        var found = await lobbyManager.FindOpenLobbies();
        if (this == null) return; // left the scene meanwhile
        openLobbies = found;
        browsing = false;
        Render();
    }

    private void OnClickKick()
    {
        var lobby = lobbyManager.CurrentLobby.Value;
        var guest = lobby.Members.FirstOrDefault(m => m.Id.Value != lobby.Owner.Id.Value);
        if (guest.Id.Value != 0) lobbyManager.Kick(guest.Id.Value);
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
        RefreshBrowser();
    }

    private void OnRulesChanged(MatchSettings settings)
    {
        lobbyManager.SetLobbySettings(settings);
        settings.SavePrefs(); // the next lobby this player hosts starts from these
    }

    // The rules go out with the scene change; the guest plays exactly these.
    private void OnClickStartMatch()
    {
        lobbyManager.SetMatchInProgress(true);
        MatchSettings.Current = lobbyManager.ReadLobbySettings();
        SteamTransport.Instance.Broadcast(NetMessage.WriteLoadGameScene(MatchSettings.Current));
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

    private void HandleLobbyFailed(string statusKey)
    {
        pendingStatusKey = statusKey;
        Render();
    }

    private void HandleNetworkMessage(ulong senderId, byte[] data)
    {
        var lobby = lobbyManager.CurrentLobby;
        // Only this lobby's host sends either.
        if (!lobby.HasValue || lobby.Value.Owner.Id.Value != senderId || lobbyManager.IsHost) return;
        switch (NetMessage.PeekType(data))
        {
            case NetMessageType.LoadGameScene:
                MatchSettings.Current = NetMessage.ReadLoadGameScene(data);
                SceneManager.LoadScene("GameScene");
                break;
            case NetMessageType.Kick:
                lobbyManager.LeaveLobby();
                pendingStatusKey = "lobby.status.kicked";
                Render();
                RefreshBrowser();
                break;
        }
    }

    // ---- Rendering ----

    private void Render()
    {
        var steamReady = SteamTransport.Instance != null && SteamTransport.Instance.IsReady;
        var inLobby = lobbyManager != null && lobbyManager.CurrentLobby.HasValue;
        idleView.SetActive(!inLobby);
        lobbyView.SetActive(inLobby);
        card.anchoredPosition = new Vector2(lobbyCardShift, 0);

        if (!steamReady)
        {
            SetStatus("lobby.status.noSteam", errorColor);
            SetInteractable(createButton, false);
            SetInteractable(quickButton, false);
            SetInteractable(joinButton, false);
            SetInteractable(refreshButton, false);
            RenderBrowser(false);
            return;
        }

        if (!inLobby)
        {
            var busy = lobbyManager.IsJoining || lobbyManager.IsSearching || pendingStatusKey == "lobby.status.creating";
            SetInteractable(createButton, !busy);
            SetInteractable(quickButton, !busy);
            SetInteractable(joinButton, !busy);
            SetInteractable(refreshButton, !browsing);
            RenderBrowser(!busy);
            if (lobbyManager.IsSearching) SetStatus("lobby.status.searching", busyColor);
            else if (lobbyManager.IsJoining) SetStatus("lobby.status.joining", busyColor);
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
        // Seats read black/white or Cho/Han, whichever pieces the rules pick.
        var rules = lobbyManager.ReadLobbySettings();
        hostSlot.ShowPlayer(host.Name, $"{Loc.Get("lobby.host")} · {SideStyle.Name(0, rules.PieceType)}");
        if (members.Count > 1) guestSlot.ShowPlayer(guest.Name, $"{Loc.Get("lobby.guest")} · {SideStyle.Name(1, rules.PieceType)}");
        else guestSlot.ShowEmpty(lobbyManager.IsHost);
        SideMark.ShowAll(lobbyView.transform, rules.PieceType);

        rulesPanel.Show(rules, lobbyManager.IsHost);
        rulesCaption.text = Loc.Get(lobbyManager.IsHost ? "lobby.rulesHost" : "lobby.rulesGuest");
        visibilityToggle.Show((int)lobbyManager.Visibility, lobbyManager.IsHost);
        guestSlot.kickButton.gameObject.SetActive(lobbyManager.IsHost && members.Count > 1);

        var full = members.Count >= 2;
        startButton.gameObject.SetActive(lobbyManager.IsHost);
        SetInteractable(startButton, full);
        if (!lobbyManager.IsHost) SetStatus("lobby.status.waitingHost", busyColor);
        else if (full) SetStatus("lobby.status.ready", okColor);
        else SetStatus("lobby.status.waitingOpponent", busyColor);
    }

    // Rows for what the last search found; Join greys out while another
    // join or search is under way.
    private void RenderBrowser(bool canJoin)
    {
        for (var i = 0; i < browserRows.Length; i++)
        {
            var row = browserRows[i];
            if (i >= openLobbies.Length)
            {
                row.gameObject.SetActive(false);
                continue;
            }
            var lobby = openLobbies[i];
            var rules = SteamLobbyManager.RulesOf(lobby);
            var summary = Loc.Get("lobby.rowRules",
                MatchSettings.Defs[(int)MatchSettingId.BoardType].Format((int)rules.BoardType),
                MatchSettings.Defs[(int)MatchSettingId.PieceType].Format((int)rules.PieceType),
                rules.StonesFor(0), rules.StonesFor(1));
            row.Show(lobby.Id.Value, SteamLobbyManager.HostName(lobby), summary);
            SetInteractable(row.joinButton, canJoin);
        }
        browserEmptyText.gameObject.SetActive(openLobbies.Length == 0);
        browserEmptyText.text = Loc.Get(browsing ? "lobby.browserSearching" : "lobby.browserEmpty");
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
