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
    public TurnController TurnController { get; private set; }
    // A battle of health's knocks as they land (piece, damage, where), on
    // the authority and, from its word, on a guest: for the HUD.
    public static event System.Action<char, int, Vector3> Damaged;
    private float hardestKnock; // the impulse of a full-power hit square on, for this match's pieces (DamageFor)
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
        Ruleset = settings.Variant == GameVariant.Health
            ? new HealthRuleset(settings.BothOutRule, settings.HealthRule, settings.PieceHealth, settings.SideHealth)
            : new ClassicRuleset(settings.BothOutRule);
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
        if (Ruleset is HealthRuleset health)
        {
            foreach (var piece in gamePieceScripts)
            {
                piece.Manager.health = health.PieceHealth;
                piece.gameObject.AddComponent<ImpactDamage>();
            }
            hardestKnock = HardestKnock(gamePieceScripts);
        }
        if (settings.Walled) Board.Active.BuildWalls();

        vcams = GameObject.FindGameObjectsWithTag("vcam");
        // Lives in GameScene, so it goes with the match.
        var pieceSet = PieceSet.Of(settings.PieceType);
        new GameObject("CameraRig").AddComponent<CameraRig>().Init(vcams, Board.SurfaceBounds, pieceSet.Standing);
        new GameObject("BoardSounds").AddComponent<BoardSounds>().Init(pieceSet);
        new GameObject("HitEffects").AddComponent<HitEffects>();
        foreach (var piece in gamePieceScripts) piece.gameObject.AddComponent<PieceSounds>().knocksBoard = pieceSet.KnocksBoard;

        var zone = new ZoneRule { Enabled = settings.Zone, MaxStage = ZoneRule.StagesFor(Board.Active.Shape) };
        TurnController = new TurnController(Ruleset, Sides, gamePieceScripts, new PieceSelector(gamePieceScripts), settings.TurnSeconds, zone, settings.RoundLimit)
        {
            Crumble = stage => Board.Crumble(stage, gamePieceScripts),
        };
        new GameObject("BoardZoneView").AddComponent<BoardZoneView>().Init(Board, TurnController);
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
        Placement = new PlacementPhase(Board, pieces, Sides.Count, MatchSettings.Current, hotSeat, authority, FirstPlayer);
        Board.BeginPlacement(gamePieceScripts);
        gameState = GameState.Placement;
    }

    // Who moves first: black, or in a ranked game whoever the series says.
    private static int FirstPlayer => MatchSettings.Current.RankedMode && RankedSeries.Current != null ? RankedSeries.Current.First : 0;

    private void StartTurns()
    {
        TurnController.StartMatch(FirstPlayer);
        gameState = TurnController.State;
    }

    public void RemovePiece(GamePieceDragAndReleaseForce piece)
    {
        gamePieceScripts.Remove(piece);
    }

    // A piece is out (DeathTrigger, or broken by its knocks), on the authority.
    public void PieceOut(GamePieceDragAndReleaseForce piece)
    {
        var manager = piece.Manager;
        if (manager.isDestroyed || TurnController == null || TurnController.IsMirror) return;
        manager.isDestroyed = true;
        TurnController.PieceOut(manager);
        RemovePiece(piece);
        Destroy(piece.gameObject);
    }

    // ---- A battle of health ----

    // What a knock of this impulse costs: 1 for the faintest that counts up
    // to MaxDamage for a full-power hit square on (and anything harder).
    public int DamageFor(float impulse)
    {
        if (hardestKnock <= 0 || impulse < hardestKnock * 0.02f) return 0;
        return Mathf.Clamp(Mathf.RoundToInt(HealthRuleset.MaxDamage * impulse / hardestKnock), 1, HealthRuleset.MaxDamage);
    }

    // Two of this match's pieces of about the average weight meeting square
    // on, one flicked at full power: each takes m v (1 + e) / 2, a little
    // of the speed gone to the board on the way.
    private static float HardestKnock(List<GamePieceDragAndReleaseForce> pieces)
    {
        if (pieces.Count == 0) return 0;
        var mass = pieces.Average(p => p.Body.mass);
        var piece = pieces[0];
        var speed = piece.maxForce / piece.referenceMass * Mathf.Pow(piece.referenceMass / mass, piece.massExponent);
        return mass * speed * 0.75f;
    }

    // A knock a piece took (ImpactDamage), on the authority: its side's or
    // its own health goes down, and one left with none breaks where it is.
    public void Damage(GamePieceManager piece, int amount, Vector3 at)
    {
        if (!(Ruleset is HealthRuleset health) || piece.isDestroyed || TurnController == null || TurnController.IsMirror) return;
        var broken = health.Damage(piece, amount, Sides);
        Damaged?.Invoke(piece.pieceID, amount, at);
        if (!broken) return;
        var body = gamePieceScripts.Find(p => p.Manager == piece);
        if (BoardSounds.Instance != null) BoardSounds.Instance.Emit(BoardSound.Shatter, 0f, piece.transform.position, piece: piece.pieceID);
        PieceOut(body);
    }

    // A guest hears of a knock from the host (NetworkMatchBridge).
    public static void ShowDamage(char pieceId, int amount, Vector3 at) => Damaged?.Invoke(pieceId, amount, at);

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
        vcams = null;
        hardestKnock = 0;
        AIPlayerId = -1;
        SkipLocalTurnProcessing = false;
        gameState = GameState.Mainmenu;
        GamePace.SetFastForward(false);
        Time.timeScale = 1f; // menus run at full speed
    }
}
