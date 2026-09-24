using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager manager;
    public enum GameState
    {
        Mainmenu,
        GameReadyProcess,
        WaitingForPlayers, // online: stones spawned, waiting for the guest to load in
        Placement,         // SpawnMode.Placement: sides placing their stones
        WaitingForInput,
        WaitingForEndTurn,
        ProcessingTurn,
        TurnChanging,
        MatchOver
    }

    public GameState gameState;
    public List<GamePieceDragAndReleaseForce> gamePieceScripts = new List<GamePieceDragAndReleaseForce>();
    public List<PlayersManager> playersList = new List<PlayersManager>();

    public int totalPlayerCnt = 2; // get input from UI future TODO

    public GameObject[] vcams = null;

    public IRuleset Ruleset { get; private set; }
    public TurnController TurnController { get; private set; }
    public BoardSetup Board { get; private set; }
    public PlacementPhase Placement { get; private set; }
    // The side the AI plays in a local game against it (LocalOpponent), or -1.
    public int AIPlayerId { get; private set; } = -1;
    public bool VersusAI => AIPlayerId >= 0;

    // Set by NetworkMatchBridge on a network guest: the authoritative turn
    // state machine only ever runs on the host, so a guest's local
    // TurnController must not process input on its own.
    public bool SkipLocalTurnProcessing;

    private void Awake()
    {
        if (manager == null)
        {
            manager = this;
            DontDestroyOnLoad(manager);
        }
        else if (manager != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        gameState = GameState.Mainmenu;
    }

    public void OnGameStart()
    {
        gameState = GameState.GameReadyProcess;
    }

    private void Update()
    {
        // Held: the other camera angle (Settings > Controls, Left Ctrl by default).
        if (KeyBindings.Down(GameAction.CameraView))
            if (vcams != null)
                vcams[0].SetActive(false);
        if (KeyBindings.Up(GameAction.CameraView))
            if (vcams != null)
                vcams[0].SetActive(true);

        if (gameState == GameState.GameReadyProcess)
        {
            GamePreparation();
            return;
        }
        if (gameState == GameState.Placement)
        {
            Placement.Tick(Time.deltaTime); // a guest's mirror only counts down; the host decides
            return;
        }

        if (TurnController == null || !TurnController.HasStarted) return;
        if (SkipLocalTurnProcessing) return; // a network guest's state is driven by NetworkMatchBridge instead
        TurnController.Tick();
        gameState = TurnController.State;
    }

    private void GamePreparation()
    {
        var settings = MatchSettings.Current;
        Ruleset = new ClassicRuleset(settings.BothOutRule);

        var playersParentOb = new GameObject("Players");
        for (var playerIndex = 0; playerIndex < totalPlayerCnt; playerIndex++)
        {
            var playersOb = new GameObject("P" + (playerIndex + 1));
            playersOb.transform.SetParent(playersParentOb.transform);
            var playersObComp = playersOb.AddComponent<PlayersManager>();
            playersObComp.ID = playerIndex;
            playersList.Add(playersObComp);
        }

        // Host and guest spawn from the same settings, so both boards get the
        // same stones with the same ids.
        Board = FindObjectOfType<BoardSetup>();
        foreach (var gamePieceScript in Board.Spawn(settings))
        {
            gamePieceScripts.Add(gamePieceScript);
            var pieceManager = gamePieceScript.GetComponent<GamePieceManager>();
            var owner = playersList.Find(p => p.ID == pieceManager.playerIndex);
            if (owner != null) owner.totalPieceCnt++;
        }

        vcams = GameObject.FindGameObjectsWithTag("vcam");
        // Lives in GameScene, so it goes with the match.
        new GameObject("BoardSounds").AddComponent<BoardSounds>().Init(settings.PieceType == PieceType.JanggiPieces);
        foreach (var piece in gamePieceScripts) piece.gameObject.AddComponent<PieceSounds>();

        TurnController = new TurnController(Ruleset, playersList, gamePieceScripts, new PieceSelector(gamePieceScripts), settings.TurnSeconds);
        gameState = GameState.WaitingForPlayers;
        if (!IsOnlineMatch && LocalOpponent.IsAI)
        {
            // White; this screen's mouse only ever moves black.
            AIPlayerId = 1;
            TurnController.PieceSelector.LocalPlayerId = 0;
            var ai = new GameObject("AI").AddComponent<AIOpponent>();
            ai.playerId = AIPlayerId;
            ai.level = LocalOpponent.Level;
        }
        // Online, NetworkMatchBridge calls BeginMatch once the guest is in.
        // Against the AI both sides can place at once, as online.
        if (!IsOnlineMatch) BeginMatch(hotSeat: !VersusAI);
    }

    private static bool IsOnlineMatch => SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.CurrentLobby.HasValue;

    // Placement first if the rules ask for it, then the first turn. Runs on
    // the authority only: the local game, or the network host.
    public void BeginMatch(bool hotSeat)
    {
        if (MatchSettings.Current.SpawnMode != SpawnMode.Placement)
        {
            StartTurns();
            return;
        }
        StartPlacement(hotSeat, authority: true);
        Placement.OnFinished += () =>
        {
            Board.EndPlacement(Placement, gamePieceScripts, restorePhysics: true);
            StartTurns();
        };
    }

    // A network guest's copy of the host's placement phase.
    public void BeginGuestPlacement()
    {
        StartPlacement(hotSeat: false, authority: false);
        Placement.OnFinished += () =>
        {
            Board.EndPlacement(Placement, gamePieceScripts, restorePhysics: false);
            gameState = GameState.WaitingForInput; // the host's opening TurnResult follows
        };
    }

    private void StartPlacement(bool hotSeat, bool authority)
    {
        var pieces = gamePieceScripts.Select(p => p.GetComponent<GamePieceManager>());
        Placement = new PlacementPhase(Board, pieces, playersList.Count, MatchSettings.Current, hotSeat, authority);
        Board.BeginPlacement(gamePieceScripts);
        gameState = GameState.Placement;
    }

    private void StartTurns()
    {
        TurnController.StartMatch();
        gameState = TurnController.State;
    }

    public void RemovePiece(GamePieceDragAndReleaseForce piece)
    {
        gamePieceScripts.Remove(piece);
    }

    // Call before leaving GameScene. This manager outlives the scene
    // (DontDestroyOnLoad) and GamePreparation only ever appends, so without a
    // reset the next match would inherit the previous one's players, pieces
    // and TurnController - and the HUD would bind to the stale controller.
    public void EndMatch()
    {
        TurnController = null;
        Placement = null;
        Board = null;
        playersList.Clear();
        gamePieceScripts.Clear();
        vcams = null;
        AIPlayerId = -1;
        SkipLocalTurnProcessing = false;
        gameState = GameState.Mainmenu;
    }
}
