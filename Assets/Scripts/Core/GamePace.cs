using UnityEngine;

// How fast a match plays. It is Time.timeScale while a match is on, so every
// shot flies, knocks and falls exactly as it would at full speed, only
// slower: distances, hinge hops and the AI's predictions all stay as they
// were. The clocks the players read (turn and placement time) keep to real
// seconds through ClockDelta.
//
// A shot that goes on rolling (chess pieces toppling round the board) can
// be fast-forwarded once it has run FastForwardAfter real seconds, by the
// screen that runs the physics: a local game, or the online host. Only
// time goes faster; every step is the same, so it ends exactly as it would
// have. It stops with the shot (GameManager).
//
// Two moments are held, on the same screen: the hardest knocks stop time
// for a blink (HitStop), and the piece a match turns on goes over the edge
// slowly (SlowMotion). Only later, never different. Tick, from GameManager,
// lets time go again.
public static class GamePace
{
    public const float Speed = 0.7f;
    public const float FastForward = 3f;       // times Speed
    public const float FastForwardAfter = 5f;  // real seconds into a shot

    public static bool FastForwarding { get; private set; }

    // Time.timeScale while a match is on.
    public static float Current => FastForwarding ? Speed * FastForward : Speed;

    // A paused game stays paused, and resumes at the new pace.
    public static void SetFastForward(bool on)
    {
        FastForwarding = on;
        if (Time.timeScale > 0) Time.timeScale = Current;
    }

    // Real seconds since the last frame, and none while the game is paused.
    public static float ClockDelta => Time.timeScale > 0 ? Time.unscaledDeltaTime : 0f;

    public const float HitStopScale = 0.04f;   // of Speed, a blink's worth of motion
    public const float SlowMotionScale = 0.3f; // of Speed

    private static float heldUntil = -1; // real time
    private static float heldScale;

    // Time nearly stops for seconds (real), unless something holds it
    // slower already or the game is paused.
    public static void HitStop(float seconds) => Hold(HitStopScale, seconds);

    public static void SlowMotion(float seconds) => Hold(SlowMotionScale, seconds);

    private static void Hold(float scale, float seconds)
    {
        if (Time.timeScale <= 0) return;
        var until = Time.realtimeSinceStartup + seconds;
        if (heldUntil > Time.realtimeSinceStartup && heldScale <= scale && heldUntil >= until) return;
        heldScale = heldUntil > Time.realtimeSinceStartup ? Mathf.Min(heldScale, scale) : scale;
        heldUntil = Mathf.Max(heldUntil, until);
        Time.timeScale = Speed * heldScale;
    }

    // Each frame: a hold over, the pace it was. Paused, it waits.
    public static void Tick()
    {
        if (heldUntil < 0 || Time.realtimeSinceStartup < heldUntil || Time.timeScale <= 0) return;
        heldUntil = -1;
        Time.timeScale = Current;
    }

    // Real seconds the hold has left; none, nothing held.
    public static float HeldFor => Mathf.Max(0, heldUntil - Time.realtimeSinceStartup);

    // A match over or left: nothing held.
    public static void Release()
    {
        heldUntil = -1;
    }
}
