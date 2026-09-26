using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the Lobby_UI prefab (built by Tools > Alkkagi UI > 4. Build lobby).
// Two views: idle (create a lobby, quick match, join by code, or pick one
// from the public lobby browser) and in-lobby (code to share, up to four
// seats, who can join, invite, start). Everything renders from SteamLobbyManager's
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
    public LobbyPlayerSlot[] seats; // SteamLobbyManager.SeatOrder: the host first
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
        for (var i = 0; i < seats.Length; i++)
        {
            var seat = i;
            seats[i].inviteButton.onClick.AddListener(() => lobbyManager.InviteFriends());
            if (seats[i].kickButton != null) seats[i].kickButton.onClick.AddListener(() => OnClickKick(seat));
        }
        leaveButton.onClick.AddListener(OnClickLeave);
        startButton.onClick.AddListener(OnClickStartMatch);
        rulesPanel.OnChanged += OnRulesChanged;
        visibilityToggle.OnSelected += index =>
        {
            lobbyManager.SetVisibility((LobbyVisibility)index);
            Render();
        };
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
        PlayerRating.SettlePending(); // a rated match left before its result
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
        if (FindAnyObjectByType<EventSystem>() != null) return;
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

    private void OnClickKick(int seat)
    {
        var members = SteamLobbyManager.SeatOrder(lobbyManager.CurrentLobby.Value);
        if (seat > 0 && seat < members.Count) lobbyManager.Kick(members[seat].Id.Value);
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
        // Seats can't go below the players already in.
        var members = lobbyManager.CurrentLobby.Value.MemberCount;
        if (settings.Seats < members)
        {
            settings.Set(MatchSettingId.Seats, members);
            rulesPanel.Show(settings, true);
        }
        lobbyManager.SetLobbySettings(settings);
        settings.SavePrefs(lobby: true); // the next lobby this player hosts starts from these
    }

    // The rules and seats go out with the scene change, Random rolled;
    // everyone plays exactly these.
    private void OnClickStartMatch()
    {
        lobbyManager.SetMatchInProgress(true);
        MatchSettings.Picked = lobbyManager.ReadLobbySettings();
        MatchSettings.Current = MatchSettings.Picked.Resolve();
        MatchRoster.Current = lobbyManager.BuildRoster();
        SteamTransport.Instance.Broadcast(NetMessage.WriteLoadGameScene(MatchSettings.Current, MatchRoster.Current));
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
                var (settings, roster) = NetMessage.ReadLoadGameScene(data);
                if (roster.PlayerOf(SteamTransport.Instance.LocalId) < 0) break; // joined too late for this one
                MatchSettings.Current = settings;
                MatchRoster.Current = roster;
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

        var members = SteamLobbyManager.SeatOrder(lobby);
        // Seats read black/white/blue/red or Cho/Han/..., whichever pieces
        // the rules pick. As many seats as the rules allow; the host's
        // invite on the first open one.
        var rules = lobbyManager.ReadLobbySettings();
        var seatCount = Mathf.Clamp(Mathf.Max(rules.Seats, members.Count), 2, seats.Length);
        for (var i = 0; i < seats.Length; i++)
        {
            var slot = seats[i];
            slot.gameObject.SetActive(i < seatCount);
            if (i < members.Count)
                slot.ShowPlayer(members[i].Name, $"{Loc.Get(i == 0 ? "lobby.host" : "lobby.guest")} · {SideStyle.Name(i, rules.PieceType)}",
                    lobbyManager.RatingOf(members[i].Id.Value));
            else
                slot.ShowEmpty(lobbyManager.IsHost && i == members.Count);
            if (slot.kickButton != null) slot.kickButton.gameObject.SetActive(lobbyManager.IsHost && i > 0 && i < members.Count);
        }
        SideMark.ShowAll(lobbyView.transform, rules.PieceType);

        rulesPanel.Show(rules, lobbyManager.IsHost);
        rulesCaption.text = Loc.Get(rules.RankedMode ? "lobby.rulesRanked" : "lobby.rulesCustom");
        visibilityToggle.Show((int)lobbyManager.Visibility, lobbyManager.IsHost);

        var canStart = members.Count >= 2;
        startButton.gameObject.SetActive(lobbyManager.IsHost);
        SetInteractable(startButton, canStart);
        if (!lobbyManager.IsHost) SetStatus("lobby.status.waitingHost", busyColor);
        else if (!canStart) SetStatus("lobby.status.waitingOpponent", busyColor);
        else if (seatCount > 2) SetStatusText(Loc.Get("lobby.status.readyCount", members.Count, seatCount), okColor);
        else SetStatus("lobby.status.ready", okColor);
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
                MatchSettings.Defs[(int)MatchSettingId.Mode].Format((int)rules.Mode),
                MatchSettings.Defs[(int)MatchSettingId.BoardType].Format((int)rules.BoardType),
                MatchSettings.Defs[(int)MatchSettingId.PieceType].Format((int)rules.PieceType),
                lobby.MemberCount, lobby.MaxMembers);
            row.Show(lobby.Id.Value, SteamLobbyManager.HostName(lobby), summary);
            SetInteractable(row.joinButton, canJoin);
        }
        browserEmptyText.gameObject.SetActive(openLobbies.Length == 0);
        browserEmptyText.text = Loc.Get(browsing ? "lobby.browserSearching" : "lobby.browserEmpty");
    }

    private void SetStatus(string key, Color dot) => SetStatusText(Loc.Get(key), dot);

    private void SetStatusText(string text, Color dot)
    {
        statusText.text = text;
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
