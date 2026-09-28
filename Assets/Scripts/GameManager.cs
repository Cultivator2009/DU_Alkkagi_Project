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
    // Index-aligned with player ids: 0 is black (or Cho, or chess's white).
    public List<Side> Sides { get; } = new List<Side>();

    public int totalPlayerCnt = 2; // two locally; online, whoever the host's roster lists (MatchRoster)

    public GameObject[] vcams = null;

    public IRuleset Ruleset { get; private set; }
    private readonly List<GamePieceDragAndReleaseForce> parked = new List<GamePieceDragAndReleaseForce>(); // off the board, coming back
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
        if (gameState == GameState.GameReadyProcess)
        {
            GamePreparation();
            return;
        }
        if (gameState == GameState.Placement)
        {
            Placement.Tick(GamePace.ClockDelta); // a guest's mirror only counts down; the host decides
            return;
        }

        if (TurnController == null || !TurnController.HasStarted) return;
        TurnController.Tick(); // a network guest's only runs its clock
        if (SkipLocalTurnProcessing) return; // and its game state follows the host (NetworkMatchBridge)
        gameState = TurnController.State;
        if (GamePace.FastForwarding && gameState != GameState.ProcessingTurn) GamePace.SetFastForward(false); // the shot is over
    }

    private void GamePreparation()
    {
        var settings = MatchSettings.Current;
        Ruleset = new ClassicRuleset(settings.BothOutRule);
        Time.timeScale = GamePace.Speed;
        totalPlayerCnt = MatchRoster.Current != null ? Mathf.Clamp(MatchRoster.Current.Count, 2, MatchRoster.MaxPlayers) : 2;

        for (var playerIndex = 0; playerIndex < totalPlayerCnt; playerIndex++) Sides.Add(new Side(playerIndex));

        // Host and guest spawn from the same settings, so both boards get the
        // same stones with the same ids.
        Board = FindAnyObjectByType<BoardSetup>();
        foreach (var gamePieceScript in Board.Spawn(settings, totalPlayerCnt))
        {
            gamePieceScripts.Add(gamePieceScript);
            Sides[gamePieceScript.Manager.playerIndex].Pieces++;
        }
        Ruleset.Begin(Sides);

        vcams = GameObject.FindGameObjectsWithTag("vcam");
        // Lives in GameScene, so it goes with the match.
        new GameObject("CameraRig").AddComponent<CameraRig>().Init(vcams, Board.SurfaceBounds, settings.PieceType == PieceType.ChessPieces);
        new GameObject("BoardSounds").AddComponent<BoardSounds>().Init(settings.PieceType);
        new GameObject("HitEffects").AddComponent<HitEffects>();
        var knocksBoard = settings.PieceType == PieceType.ChessPieces || settings.PieceType == PieceType.GonggiStones;
        foreach (var piece in gamePieceScripts) piece.gameObject.AddComponent<PieceSounds>().knocksBoard = knocksBoard;

        TurnController = new TurnController(Ruleset, Sides, gamePieceScripts, new PieceSelector(gamePieceScripts), settings.TurnSeconds)
        {
            ResolveParked = ResolveParked,
        };
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

    // NetworkBootstrap adds the bridge as GameScene loads into a lobby, before
    // the match is prepared. (A test harness can add one of its own.)
    private static bool IsOnlineMatch => FindAnyObjectByType<NetworkMatchBridge>() != null;

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
        Placement = new PlacementPhase(Board, pieces, Sides.Count, MatchSettings.Current, hotSeat, authority);
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

    // A piece went off the board (DeathTrigger), on the authority: the
    // ruleset says whether it's gone or comes back once the shot is over.
    public void PieceOut(GamePieceDragAndReleaseForce piece)
    {
        var manager = piece.Manager;
        if (manager.isDestroyed || TurnController == null || TurnController.IsMirror) return;
        manager.isDestroyed = true;
        if (TurnController.PieceOut(manager))
        {
            RemovePiece(piece);
            Destroy(piece.gameObject);
            return;
        }
        piece.Park();
        parked.Add(piece);
    }

    // The shot is over and the ruleset has said who's out: the pieces of the
    // sides still in come back into their zones, the others' are gone.
    private void ResolveParked()
    {
        foreach (var piece in parked)
        {
            var side = Sides[piece.Manager.playerIndex];
            if (!side.Standing)
            {
                side.Pieces = Mathf.Max(0, side.Pieces - 1);
                RemovePiece(piece);
                Destroy(piece.gameObject);
                continue;
            }
            Board.Respawn(piece, gamePieceScripts);
            piece.Manager.isDestroyed = false;
        }
        parked.Clear();
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
        Sides.Clear();
        gamePieceScripts.Clear();
        parked.Clear();
        vcams = null;
        AIPlayerId = -1;
        SkipLocalTurnProcessing = false;
        gameState = GameState.Mainmenu;
        GamePace.SetFastForward(false);
        Time.timeScale = 1f; // menus run at full speed
    }
}
