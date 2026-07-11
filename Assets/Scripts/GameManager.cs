using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager manager;
    public enum GameState
    {
        Mainmenu,
        GameReadyProcess,
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

    public IRuleset Ruleset { get; private set; } = new ClassicRuleset();
    public TurnController TurnController { get; private set; }

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
        if (Input.GetKeyDown(KeyCode.LeftAlt))
            if (vcams != null)
                vcams[0].SetActive(false);
        if (Input.GetKeyUp(KeyCode.LeftAlt))
            if (vcams != null)
                vcams[0].SetActive(true);

        if (gameState == GameState.GameReadyProcess)
        {
            GamePreparation();
            return;
        }

        if (TurnController == null) return;
        if (SkipLocalTurnProcessing) return; // a network guest's state is driven by NetworkMatchBridge instead
        TurnController.Tick();
        gameState = TurnController.State;
    }

    private void GamePreparation()
    {
        var playersParentOb = new GameObject("Players");
        for (var playerIndex = 0; playerIndex < totalPlayerCnt; playerIndex++)
        {
            var playersOb = new GameObject("P" + (playerIndex + 1));
            playersOb.transform.SetParent(playersParentOb.transform);
            var playersObComp = playersOb.AddComponent<PlayersManager>();
            playersObComp.ID = playerIndex;
            playersList.Add(playersObComp);
        }

        var gamePieceGOs = GameObject.FindGameObjectsWithTag("GamePiece_GO");
        foreach (var gamePieceGO in gamePieceGOs)
        {
            var gamePieceScript = gamePieceGO.GetComponent<GamePieceDragAndReleaseForce>();
            if (gamePieceScript == null) continue;
            gamePieceScripts.Add(gamePieceScript);

            var pieceManager = gamePieceGO.GetComponent<GamePieceManager>();
            var owner = playersList.Find(p => p.ID == pieceManager.playerIndex);
            if (owner != null) owner.totalPieceCnt++;
        }

        vcams = GameObject.FindGameObjectsWithTag("vcam");

        TurnController = new TurnController(Ruleset, playersList, gamePieceScripts, new PieceSelector(gamePieceScripts));
        TurnController.StartMatch();
        gameState = TurnController.State;
    }

    public void RemovePiece(GamePieceDragAndReleaseForce piece)
    {
        gamePieceScripts.Remove(piece);
    }
}
