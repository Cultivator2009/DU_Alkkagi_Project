using System.Linq;
using Steamworks.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the entire LobbyScene UI in code instead of hand-authored scene
// YAML, so it's guaranteed to compile and instantiate correctly. Visuals are
// intentionally plain (default uGUI look) - restyle freely in the Editor,
// the button/field wiring below is what matters.
public class LobbySceneUI : MonoBehaviour
{
    private SteamLobbyManager lobbyManager;

    private TMP_Text statusText;
    private TMP_Text lobbyIdText;
    private TMP_Text playerListText;
    private TMP_InputField joinCodeInput;
    private Button startButton;

    private void Awake()
    {
        EnsureNetworkSingletons();
        EnsureEventSystem();
        BuildUI();
    }

    private void Start()
    {
        lobbyManager = SteamLobbyManager.Instance;
        lobbyManager.OnLobbyReady += HandleLobbyReady;
        lobbyManager.OnMemberJoined += _ => RefreshPlayerList();
        lobbyManager.OnMemberLeft += _ => RefreshPlayerList();
        SteamTransport.Instance.OnMessageReceived += HandleNetworkMessage;
    }

    private void OnDestroy()
    {
        if (lobbyManager != null) lobbyManager.OnLobbyReady -= HandleLobbyReady;
        if (SteamTransport.Instance != null) SteamTransport.Instance.OnMessageReceived -= HandleNetworkMessage;
    }

    private void HandleNetworkMessage(ulong senderId, byte[] data)
    {
        if (NetMessage.PeekType(data) == NetMessageType.LoadGameScene) SceneManager.LoadScene("GameScene");
    }

    private static void EnsureNetworkSingletons()
    {
        if (SteamTransport.Instance == null) new GameObject("SteamTransport").AddComponent<SteamTransport>();
        if (SteamLobbyManager.Instance == null) new GameObject("SteamLobbyManager").AddComponent<SteamLobbyManager>();
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    // ---- UI construction ----

    private void BuildUI()
    {
        var canvasGo = new GameObject("LobbyCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasGo.AddComponent<GraphicRaycaster>();

        var root = canvasGo.transform;

        statusText = CreateText(root, "StatusText", "Steam 연결 대기 중...", new Vector2(0, 300), new Vector2(600, 40));
        CreateButton(root, "CreateLobbyButton", "로비 생성 (호스트)", new Vector2(-160, 200), OnClickCreateLobby);

        joinCodeInput = CreateInputField(root, "JoinCodeInput", new Vector2(160, 240));
        CreateButton(root, "JoinLobbyButton", "코드로 참가", new Vector2(160, 180), OnClickJoinLobby);

        lobbyIdText = CreateText(root, "LobbyIdText", string.Empty, new Vector2(-160, 130), new Vector2(500, 40));
        playerListText = CreateText(root, "PlayerListText", string.Empty, new Vector2(0, 40), new Vector2(600, 120));

        startButton = CreateButton(root, "StartMatchButton", "매치 시작", new Vector2(0, -80), OnClickStartMatch).GetComponent<Button>();
        startButton.gameObject.SetActive(false);

        CreateButton(root, "LeaveButton", "나가기", new Vector2(0, -160), OnClickLeave);
    }

    private static TMP_Text CreateText(Transform parent, string name, string content, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPos;
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 24;
        return text;
    }

    private static GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(260, 50);
        rect.anchoredPosition = anchoredPos;
        go.AddComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f);
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(onClick);

        var text = CreateText(go.transform, "Label", label, Vector2.zero, rect.sizeDelta);
        text.color = Color.black;

        return go;
    }

    private static TMP_InputField CreateInputField(Transform parent, string name, Vector2 anchoredPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(260, 50);
        rect.anchoredPosition = anchoredPos;
        go.AddComponent<Image>().color = Color.white;
        var input = go.AddComponent<TMP_InputField>();

        var textArea = new GameObject("Text");
        textArea.transform.SetParent(go.transform, false);
        var textRect = textArea.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8, 4);
        textRect.offsetMax = new Vector2(-8, -4);
        var text = textArea.AddComponent<TextMeshProUGUI>();
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        input.textComponent = text;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        return input;
    }

    // ---- Button handlers ----

    private void OnClickCreateLobby()
    {
        statusText.text = "로비 생성 중...";
        SteamLobbyManager.Instance.CreateLobby();
    }

    private void OnClickJoinLobby()
    {
        if (!ulong.TryParse(joinCodeInput.text, out var lobbyId))
        {
            statusText.text = "올바른 로비 코드를 입력하세요.";
            return;
        }
        statusText.text = "로비 참가 중...";
        SteamLobbyManager.Instance.JoinLobby(lobbyId);
    }

    private void OnClickStartMatch()
    {
        SteamTransport.Instance.Broadcast(NetMessage.WriteLoadGameScene());
        SceneManager.LoadScene("GameScene");
    }

    private void OnClickLeave()
    {
        SteamLobbyManager.Instance.LeaveLobby();
        SceneManager.LoadScene("MainMenuScene");
    }

    // ---- Lobby events ----

    private void HandleLobbyReady(Lobby lobby)
    {
        statusText.text = lobbyManager.IsHost ? "로비 생성됨 - 상대를 기다리는 중" : "로비 참가됨";
        lobbyIdText.text = $"로비 코드: {lobby.Id.Value}";
        startButton.gameObject.SetActive(lobbyManager.IsHost);
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (!lobbyManager.CurrentLobby.HasValue) return;
        var members = lobbyManager.CurrentLobby.Value.Members.Select(m => m.Name);
        playerListText.text = string.Join("\n", members);
        if (startButton != null) startButton.interactable = lobbyManager.CurrentLobby.Value.Members.Count() >= 2;
    }
}
