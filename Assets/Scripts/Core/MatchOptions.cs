using UnityEngine;

// Per-match options. Locally they come from the player's own settings;
// online the host decides and publishes them as Steam lobby data, so both
// sides play under the same rules.
public static class MatchOptions
{
    public const string AimGuideLobbyKey = "aimGuide";
    private const string AimGuidePrefsKey = "aimGuide";

    // The first-contact guide line while aiming. On by default.
    public static bool LocalAimGuide
    {
        get => PlayerPrefs.GetInt(AimGuidePrefsKey, 1) == 1;
        set => PlayerPrefs.SetInt(AimGuidePrefsKey, value ? 1 : 0);
    }

    public static bool AimGuide
    {
        get
        {
            var lobby = SteamLobbyManager.Instance;
            if (lobby != null && lobby.CurrentLobby.HasValue) return lobby.GetLobbyOption(AimGuideLobbyKey, true);
            return LocalAimGuide;
        }
    }
}
