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
    private bool teams;         // two teams of two (MatchSettings.Teams, four playing)

    // Which team a side plays for: its own, or with teams sides 0 and 2
    // against 1 and 3. Every rule asks this rather than comparing sides.
    public int TeamOf(int playerId) => teams ? playerId % 2 : playerId;
    public bool Teams => teams;
    public BoardSetup Board { get; private set; }
    public PlacementPhase Placement { get; private set; }
    // The sides the AI plays in a local game (LocalOpponent, seat by seat).
    public HashSet<int> AIPlayers { get; } = new HashSet<int>();
    public bool VersusAI => AIPlayers.Count > 0;
    public bool IsAI(int playerId) => AIPlayers.Contains(playerId);
    // The one side played at this screen in a local game against the AI,
    // or -1 (several at this screen take turns at it, or none do).
    public int SoleHuman { get; private set; } = -1;

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
        Time.timeScale = GamePace.Speed;
        // Online, whoever the roster lists; locally, the seats picked.
        totalPlayerCnt = Mathf.Clamp(MatchRoster.Current != null ? MatchRoster.Current.Count : IsOnlineMatch ? 2 : settings.Seats, 2, MatchRoster.MaxPlayers);
        teams = settings.Teams && totalPlayerCnt == 4;
        Ruleset = settings.Variant == GameVariant.Health
            ? new HealthRuleset(settings.BothOutRule, settings.HealthRule, settings.PieceHealth, settings.SideHealth, TeamOf)
            : new ClassicRuleset(settings.BothOutRule, TeamOf);

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
        TurnController = new TurnController(Ruleset, Sides, gamePieceScripts, new PieceSelector(gamePieceScripts), settings.TurnSeconds, zone, settings.RoundLimit, TeamOf)
        {
            Crumble = stage => Board.Crumble(stage, gamePieceScripts),
        };
        TurnController.OnPlayerOut += RetirePieces;
        new GameObject("BoardZoneView").AddComponent<BoardZoneView>().Init(Board, TurnController);
        gameState = GameState.WaitingForPlayers;
        var humans = Enumerable.Range(0, totalPlayerCnt).ToList();
        if (!IsOnlineMatch)
        {
            for (var seat = 0; seat < totalPlayerCnt; seat++)
            {
                if (!LocalOpponent.IsAI(seat)) continue;
                AIPlayers.Add(seat);
                var ai = new GameObject("AI " + seat).AddComponent<AIOpponent>();
                ai.playerId = seat;
                ai.level = LocalOpponent.LevelOf(seat);
            }
            // This screen's mouse only ever moves the sides played at it.
            humans.RemoveAll(AIPlayers.Contains);
            if (VersusAI) TurnController.PieceSelector.Players = new HashSet<int>(humans);
            SoleHuman = VersusAI && humans.Count == 1 ? humans[0] : -1;
        }
        // Online, NetworkMatchBridge calls BeginMatch once the guest is in.
        // With one at this screen, everyone places at once, as online; with
        // several, they take turns at it.
        if (!IsOnlineMatch) BeginMatch(hotSeat: humans.Count > 1);
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

    // A side knocked out with pieces still on the board (a battle of
    // health's B, its health gone) has them break and go, counting for no
    // one; a side that conceded or left keeps its pieces there, in the way.
    // On the authority: a guest sees them break (BoardSounds) and go with
    // the next turn's result.
    private void RetirePieces(int playerId, MatchEndReason reason)
    {
        if (reason != MatchEndReason.Knockout || TurnController.IsMirror) return;
        foreach (var piece in gamePieceScripts.Where(p => p != null && p.Manager.playerIndex == playerId && !p.Manager.isDestroyed).ToList())
        {
            piece.Manager.isDestroyed = true;
            if (BoardSounds.Instance != null) BoardSounds.Instance.Emit(BoardSound.Shatter, 0f, piece.transform.position, piece: piece.Manager.pieceID);
            RemovePiece(piece);
            Destroy(piece.gameObject);
        }
        Sides[playerId].Pieces = 0;
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
        AIPlayers.Clear();
        SoleHuman = -1;
        teams = false;
        SkipLocalTurnProcessing = false;
        gameState = GameState.Mainmenu;
        GamePace.SetFastForward(false);
        Time.timeScale = 1f; // menus run at full speed
    }
}
