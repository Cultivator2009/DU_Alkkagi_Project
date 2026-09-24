using UnityEngine;

// How fast a match plays. It is Time.timeScale while a match is on, so every
// shot flies, knocks and falls exactly as it would at full speed, only
// slower: distances, hinge hops and the AI's predictions all stay as they
// were. The clocks the players read (turn and placement time) keep to real
// seconds through ClockDelta.
public static class GamePace
{
    public const float Speed = 0.8f;

    // Real seconds since the last frame, and none while the game is paused.
    public static float ClockDelta => Time.timeScale > 0 ? Time.unscaledDeltaTime : 0f;
}
