using UnityEngine;

// This player's own preferences, kept on this machine only. Match rules are
// MatchSettings; rebindable keys are KeyBindings.
public static class GameSettings
{
    private const string MasterKey = "settings.masterVolume";
    private const string InterfaceKey = "settings.interfaceVolume";
    private const string MusicKey = "settings.musicVolume";
    private const string EffectsKey = "settings.effectsVolume";
    private const string JanggiHanjaKey = "settings.janggiHanja";
    private const string ScreenShakeKey = "settings.screenShake";
    private const string ColorAssistKey = "settings.colorAssist";
    private const string QualityKey = "settings.quality";
    private const string VSyncKey = "settings.vsync";
    private const string FrameCapKey = "settings.frameCap";
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

    // 0..1, for the music (MusicPlayer).
    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(MusicKey, DefaultVolume);
        set => PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
    }

    // 0..1, for the board's sounds (knocks, falls, flicks).
    public static float EffectsVolume
    {
        get => PlayerPrefs.GetFloat(EffectsKey, DefaultVolume);
        set => PlayerPrefs.SetFloat(EffectsKey, Mathf.Clamp01(value));
    }

    // Janggi piece letters in Hanja (楚 車 包...) rather than Hangul. Display
    // only, and each player's own: both sides play the same pieces.
    public static bool JanggiHanja
    {
        get => PlayerPrefs.GetInt(JanggiHanjaKey, 0) == 1;
        set => PlayerPrefs.SetInt(JanggiHanjaKey, value ? 1 : 0);
    }

    // The view shakes a moment on a hard knock (CameraRig.Shake). On unless
    // the player turns it off.
    public static bool ScreenShake
    {
        get => PlayerPrefs.GetInt(ScreenShakeKey, 1) == 1;
        set => PlayerPrefs.SetInt(ScreenShakeKey, value ? 1 : 0);
    }

    // Colourblind mode: the sides in colours told apart without red and
    // green (PieceSet.Of). A match already on keeps its pieces' colours.
    public static bool ColorAssist
    {
        get => PlayerPrefs.GetInt(ColorAssistKey, 0) == 1;
        set => PlayerPrefs.SetInt(ColorAssistKey, value ? 1 : 0);
    }

    // Graphics (DisplaySettings.ApplyGraphics): an index into
    // DisplaySettings.QualityLevels, the highest at first; vertical sync, on
    // at first; and without it an index into DisplaySettings.FrameCaps.
    public static int Quality
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, DisplaySettings.QualityLevels.Length - 1), 0, DisplaySettings.QualityLevels.Length - 1);
        set => PlayerPrefs.SetInt(QualityKey, value);
    }

    public static bool VSync
    {
        get => PlayerPrefs.GetInt(VSyncKey, 1) == 1;
        set => PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
    }

    public static int FrameCap
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(FrameCapKey, 1), 0, DisplaySettings.FrameCaps.Length - 1);
        set => PlayerPrefs.SetInt(FrameCapKey, value);
    }

    public static void ResetVolumes()
    {
        MasterVolume = DefaultVolume;
        InterfaceVolume = DefaultVolume;
        MusicVolume = DefaultVolume;
        EffectsVolume = DefaultVolume;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnLaunch()
    {
        AudioListener.volume = MasterVolume;
        DisplaySettings.ApplyGraphics();
    }
}
