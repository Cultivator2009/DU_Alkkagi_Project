using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The physics matches are played with, in one place, with the sets they
// were played with before: going back to one is a line (Current). All of it
// is at full speed, in game time. A unit is 15 cm (a go stone's 22 mm is
// 0.147), so 65.4 units/s² is the real 9.81 m/s². GamePace then slows the
// whole world alike (Time.timeScale 0.7): every physics step is the same,
// so a shot ends where it would at full speed, only later.
//
// Gravity and the bounce threshold are set as the game starts, the pieces'
// and boards' physics materials (found by their assets' names) as each
// scene loads. What a piece is flicked and slowed with is read at spawn
// (BoardSetup.Spawn), and by the AI, the sounds, the quake and the menu's demo.
// Both machines of an online match must play the same set (the same build).
public sealed class PhysicsTuning
{
    // A physics material's values.
    public struct Surface
    {
        public float Dynamic, Static, Bounce;
        public PhysicsMaterialCombine FrictionCombine, BounceCombine;

        public Surface(float dynamic, float stat, float bounce, PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Average)
        {
            Dynamic = dynamic;
            Static = stat;
            Bounce = bounce;
            FrictionCombine = PhysicsMaterialCombine.Average;
            BounceCombine = bounceCombine;
        }

        // Only what differs: in the editor a material set at all is saved
        // with the project, and the committed ones are the Current set's.
        public void Apply(PhysicsMaterial material)
        {
            if (material.dynamicFriction != Dynamic) material.dynamicFriction = Dynamic;
            if (material.staticFriction != Static) material.staticFriction = Static;
            if (material.bounciness != Bounce) material.bounciness = Bounce;
            if (material.frictionCombine != FrictionCombine) material.frictionCombine = FrictionCombine;
            if (material.bounceCombine != BounceCombine) material.bounceCombine = BounceCombine;
        }
    }

    public string Name;
    public float Gravity;          // units/s², down
    public float BounceThreshold;  // units/s: a slower knock doesn't bounce
    public float MaxRiseSpeed;     // units/s: a piece pushed up by what it leans on rises no faster (GamePieceDragAndReleaseForce.LimitRise)
    // PhysicMaterial_Board (every board, the landscape's ground and rocks),
    // PhysicMaterial_GO (go stones), Wood (janggi and chess pieces), Gonggi.
    // Friction combines by the average: a piece on the board slides with
    // the mean of the two.
    public Surface Board, Stone, Wood, Gonggi;
    // How a piece sliding on the board slows from its friction alone
    // (units/s², measured), and for each kind of piece: the impulse a
    // full-power flick gives one of mass 3 (a go stone; heavier ones leave
    // a little slower, GamePieceDragAndReleaseForce.ApplyFlick), and any
    // slowing added on top through its centre of mass (GamePieceDragAndReleaseForce.Slow).
    public float Sliding;
    public Dictionary<PieceType, (float Flick, float Extra)> Pieces;

    public float Flick(PieceType type) => Of(type).Flick;
    public float Extra(PieceType type) => Of(type).Extra;
    public float Slowing(PieceType type) => Sliding + Extra(type); // a sliding piece's, in all

    private (float Flick, float Extra) Of(PieceType type) => Pieces.TryGetValue(type, out var piece) ? piece : Pieces[PieceType.GoStones];

    // Until 2026-10-28 the world fell at a sixth of real gravity (9.81
    // units/s², 1.47 m/s²): pieces dropping off the edge and chess pieces
    // toppling floated. For a stone to stop as quickly as it should, friction
    // was far above any real (0.875), and go stones and janggi pieces were
    // slowed as much again through their centre (2026-10-04, "very
    // slippery"; more friction at their foot tipped them), with a flick √2
    // as hard so a shot went as far. Janggi and chess pieces were of the go
    // stones' material.
    private static readonly Dictionary<PieceType, (float, float)> Gripped = new Dictionary<PieceType, (float, float)>
    {
        { PieceType.GoStones, (50f, 8.8f) },
        { PieceType.JanggiPieces, (50f, 8.8f) },
        { PieceType.ChessPieces, (35.36f, 0f) },
        { PieceType.GonggiStones, (35.36f, 0f) },
    };

    public static readonly PhysicsTuning SixthGravity = new PhysicsTuning
    {
        Name = "2026-10-28 이전 (중력 1/6.7)",
        Gravity = 9.81f,
        BounceThreshold = 1.4f,
        MaxRiseSpeed = 0.4f,
        Board = new Surface(0.95f, 0.95f, 0.1f),
        Stone = new Surface(0.8f, 0.8f, 0.6f),
        Wood = new Surface(0.8f, 0.8f, 0.6f),
        Gonggi = new Surface(0.8f, 0.8f, 0.4f),
        Sliding = 8.8f,
        Pieces = Gripped,
    };

    // 2026-10-28 (4722532): real gravity, and friction lowered to keep the
    // slowing as it was - a go stone 0.27 g in all, half of it still pushed
    // through its centre, chess pieces and gonggi stones 0.135 g. Stones
    // knock as glass or slate does (0.85), wood less (0.55); nothing bounces
    // on the board (the board's Minimum). The rise limit up with gravity so a
    // hop stays as low.
    public static readonly PhysicsTuning RealGravity = new PhysicsTuning
    {
        Name = "2026-10-28 (실제 중력)",
        Gravity = 65.4f,
        BounceThreshold = 2f,
        MaxRiseSpeed = 1f,
        Board = new Surface(0.15f, 0.55f, 0.1f, PhysicsMaterialCombine.Minimum),
        Stone = new Surface(0.12f, 0.45f, 0.85f),
        Wood = new Surface(0.12f, 0.45f, 0.55f),
        Gonggi = new Surface(0.12f, 0.45f, 0.4f),
        Sliding = 8.8f,
        Pieces = Gripped,
    };

    // 2026-10-30: worked out at full speed, from what real pieces do. Under
    // real gravity friction alone slows a piece as it should, so nothing is
    // pushed through its centre any more: every piece slides on the board
    // at 0.27 g (glass or slate on wood 0.2-0.3, wood on wood 0.3, felt and
    // plastic about the same), and a chess piece's foot grips as felt does
    // while one lying down still rolls. Pieces rub on each other as glass
    // and wood do (0.4; they were 0.12), so a glancing knock throws and
    // spins as it would. Every piece is flicked as a go stone was, so each
    // goes as far as before, chess pieces and gonggi stones a little faster
    // and stopping sooner.
    public static readonly PhysicsTuning RealFriction = new PhysicsTuning
    {
        Name = "2026-10-30 (실제 마찰)",
        Gravity = 65.4f,
        BounceThreshold = 2f,
        MaxRiseSpeed = 1f,
        Board = new Surface(0.14f, 0.3f, 0.1f, PhysicsMaterialCombine.Minimum),
        Stone = new Surface(0.4f, 0.6f, 0.85f),
        Wood = new Surface(0.4f, 0.6f, 0.55f),
        Gonggi = new Surface(0.4f, 0.6f, 0.4f),
        Sliding = 17.6f,
        Pieces = new Dictionary<PieceType, (float, float)>
        {
            { PieceType.GoStones, (50f, 0f) },
            { PieceType.JanggiPieces, (50f, 0f) },
            { PieceType.ChessPieces, (50f, 0f) },
            { PieceType.GonggiStones, (50f, 0f) },
        },
    };

    // The set played. To go back: RealGravity (2026-10-28) or SixthGravity (before it).
    public static PhysicsTuning Current = RealFriction;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Begin()
    {
        Apply();
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
    }

    private static void Loaded(Scene scene, LoadSceneMode mode) => Dress();

    // The world's, and the materials loaded now.
    public static void Apply()
    {
        var gravity = new Vector3(0, -Current.Gravity, 0);
        if (Physics.gravity != gravity) Physics.gravity = gravity;
        if (Physics.bounceThreshold != Current.BounceThreshold) Physics.bounceThreshold = Current.BounceThreshold;
        Dress();
    }

    private static void Dress()
    {
        foreach (var material in Resources.FindObjectsOfTypeAll<PhysicsMaterial>())
        {
            switch (material.name)
            {
                case "PhysicMaterial_Board": Current.Board.Apply(material); break;
                case "PhysicMaterial_GO": Current.Stone.Apply(material); break;
                case "Wood": Current.Wood.Apply(material); break;
                case "Gonggi": Current.Gonggi.Apply(material); break;
            }
        }
    }
}
