using UnityEngine;

// This player's own preferences, kept on this machine only. Match rules are
// MatchSettings; rebindable keys are KeyBindings.
public static class GameSettings
{
    private const string MasterKey = "settings.masterVolume";
    private const string InterfaceKey = "settings.interfaceVolume";
    private const string JanggiHanjaKey = "settings.janggiHanja";
    public const float DefaultVolume = 0.8f;

    // 0..1. Scales everything through the AudioListener.
    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterKey, DefaultVolume);
        set
        {
            PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value));
            AudioListener.volume = MasterVolume;
        }
    }

    // 0..1, for interface sounds (clicks, notices) once there are any:
    // whatever plays them scales its volume by this.
    public static float InterfaceVolume
    {
        get => PlayerPrefs.GetFloat(InterfaceKey, DefaultVolume);
        set => PlayerPrefs.SetFloat(InterfaceKey, Mathf.Clamp01(value));
    }

    // Janggi piece letters in Hanja (楚 車 包...) rather than Hangul. Display
    // only, and each player's own: both sides play the same pieces.
    public static bool JanggiHanja
    {
        get => PlayerPrefs.GetInt(JanggiHanjaKey, 0) == 1;
        set => PlayerPrefs.SetInt(JanggiHanjaKey, value ? 1 : 0);
    }

    public static void ResetVolumes()
    {
        MasterVolume = DefaultVolume;
        InterfaceVolume = DefaultVolume;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnLaunch()
    {
        AudioListener.volume = MasterVolume;
    }
}
