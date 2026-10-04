using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// The practice match (the main menu's Practice, offered on the first
// start): black against an easy AI on the go board, taught a step at a
// time on the HUD's card (TutorialCard). Each lesson ends when the player
// has done it, or skips it; white sits out (its turns passed) until the
// last, which is a real game to the end. The keys named are this machine's.
public class Tutorial : MonoBehaviour
{
    private const string DoneKey = "tutorial.done";
    private const string OfferedKey = "tutorial.offered";

    private enum Step
    {
        Pull,    // press a stone and drag back
        Release, // let go: it flies
        Knock,   // knock a white stone off
        Fine,    // pull with FineAim held
        View,    // move the view, then back
        Play     // a real game
    }

    private static bool pending;

    // Finished once (or played to the end): the menu stops offering it.
    public static bool Done
    {
        get => PlayerPrefs.GetInt(DoneKey, 0) == 1;
        private set => PlayerPrefs.SetInt(DoneKey, value ? 1 : 0);
    }

    // The main menu offers it once, on the first start.
    public static bool Offered
    {
        get => PlayerPrefs.GetInt(OfferedKey, 0) == 1;
        set => PlayerPrefs.SetInt(OfferedKey, value ? 1 : 0);
    }

    // While white sits out, its passed turns aren't announced.
    public static bool QuietPasses => instance != null && instance.step < Step.Play;

    private static Tutorial instance;

    private Step step;
    private TutorialCard card;
    private AIOpponent white;
    private TurnController turns;
    private int blackKills;
    private float fineHeld;
    private bool viewMoved;

    [RuntimeInitializeOnLoadMethod]
    private static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (scene.name != "GameScene" || !pending) return;
            pending = false;
            new GameObject("Tutorial").AddComponent<Tutorial>();
        };
    }

    // Into GameScene for the practice match; the seats as it wants them for
    // this match only (LocalOpponent.Override).
    public static void Begin()
    {
        var rules = new MatchSettings();
        rules.Set(MatchSettingId.BoardType, (int)BoardType.Go);
        rules.Set(MatchSettingId.PieceType, (int)PieceType.GoStones);
        rules.Set(MatchSettingId.Seats, 2);
        rules.Set(MatchSettingId.BlackStones, 4);
        rules.Set(MatchSettingId.WhiteStones, 3);
        rules.Set(MatchSettingId.AimGuide, 1);
        rules.Set(MatchSettingId.Zone, 0);
        rules.Set(MatchSettingId.RoundLimit, 0);
        rules.Set(MatchSettingId.TurnSeconds, 0);
        MatchSettings.Current = rules;
        MatchRoster.Current = null;
        LocalOpponent.Override = new[] { Opponent.Human, Opponent.AIEasy };
        Offered = true;
        pending = true;
        SceneManager.LoadScene("GameScene");
    }

    private void Awake() => instance = this;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        LocalOpponent.Override = null;
        if (turns != null)
        {
            turns.OnShotEnded -= ShotEnded;
            turns.Kills.OnShotResolved -= Resolved;
            turns.OnMatchEnded -= MatchEnded;
        }
        if (card != null) card.OnSkip -= Advance;
    }

    private void Update()
    {
        var gameManager = GameManager.manager;
        if (gameManager == null || gameManager.TurnController == null || !gameManager.TurnController.HasStarted) return;
        if (turns == null) Attach(gameManager);

        // White sits out the lessons.
        if (step < Step.Play && turns.CurrentPlayerID == 1 && turns.IsAwaitingShot) turns.TryPassTurn(1);

        var pulling = gameManager.gamePieceScripts.FirstOrDefault(p => p != null && p.isDragging && p.Manager.playerIndex == 0);
        switch (step)
        {
            case Step.Pull:
                if (pulling != null && pulling.AimPower >= 0.3f) Advance();
                break;
            case Step.Knock:
                if (blackKills > 0) Advance();
                break;
            case Step.Fine:
                if (pulling != null && KeyBindings.Held(GameAction.FineAim)) fineHeld += Time.unscaledDeltaTime;
                if (fineHeld >= 0.6f) Advance();
                break;
            case Step.View:
                var rig = CameraRig.Instance;
                if (rig == null) break;
                if (rig.IsMoved && !viewMoved)
                {
                    viewMoved = true;
                    Show();
                }
                else if (viewMoved && !rig.IsMoved) Advance();
                break;
        }
    }

    private void Attach(GameManager gameManager)
    {
        turns = gameManager.TurnController;
        turns.OnShotEnded += ShotEnded;
        turns.Kills.OnShotResolved += Resolved;
        turns.OnMatchEnded += MatchEnded;
        white = FindObjectsByType<AIOpponent>(FindObjectsSortMode.None).FirstOrDefault(ai => ai.playerId == 1);
        if (white != null) white.paused = true;
        card = FindAnyObjectByType<TutorialCard>(FindObjectsInactive.Include);
        if (card != null) card.OnSkip += Advance;
        Show();
    }

    private void ShotEnded(int shooter)
    {
        if (shooter == 0 && step == Step.Release) Advance();
    }

    private void Resolved(System.Collections.Generic.IReadOnlyList<KillEvent> kills)
    {
        blackKills += kills.Count(k => k.ShooterId == 0 && (k.Kind == KillKind.Kill || k.Kind == KillKind.Nongae));
    }

    // Played to the end: done, whoever won.
    private void MatchEnded(int winner, MatchEndReason reason)
    {
        Done = true;
        if (card != null) card.Hide();
    }

    private void Advance()
    {
        if (step == Step.Play) return;
        step++;
        if (step == Step.Play && white != null) white.paused = false;
        Show();
    }

    private void Show()
    {
        if (card == null) return;
        var fine = KeyBindings.DisplayName(KeyBindings.Get(GameAction.FineAim));
        var pan = KeyBindings.DisplayName(KeyBindings.Get(GameAction.PanView));
        var reset = KeyBindings.DisplayName(KeyBindings.Get(GameAction.ResetView));
        var (title, body) = step switch
        {
            Step.Pull => (Loc.Get("tutorial.pull"), Loc.Get("tutorial.pullBody")),
            Step.Release => (Loc.Get("tutorial.release"), Loc.Get("tutorial.releaseBody")),
            Step.Knock => (Loc.Get("tutorial.knock"), Loc.Get("tutorial.knockBody")),
            Step.Fine => (Loc.Get("tutorial.fine"), Loc.Get("tutorial.fineBody", fine)),
            Step.View => (Loc.Get("tutorial.view"), viewMoved ? Loc.Get("tutorial.viewBack", reset) : Loc.Get("tutorial.viewBody", pan)),
            _ => (Loc.Get("tutorial.play"), Loc.Get("tutorial.playBody")),
        };
        card.Show((int)step + 1, (int)Step.Play + 1, title, body, step < Step.Play);
    }
}
