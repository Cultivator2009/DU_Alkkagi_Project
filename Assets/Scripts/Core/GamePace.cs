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
}
