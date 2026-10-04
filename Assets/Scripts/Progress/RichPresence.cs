using System.Linq;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

// What Steam friends see this player doing: in the menu, waiting in a
// lobby, or in a match - practising, against the AI, at one screen, online
// or ranked. steam_display names one of the tokens in Steam/rich_presence_*.vdf,
// which have to be uploaded to the Steamworks backend; until they are,
// friends see only the game's name. Set as each scene loads; nothing
// without Steam.
public static class RichPresence
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) => Set(scene.name);
        Set(SceneManager.GetActiveScene().name);
    }

    private static void Set(string scene)
    {
        if (!SteamClient.IsValid) return;
        SteamFriends.SetRichPresence("steam_display", Token(scene));
    }

    private static string Token(string scene)
    {
        if (scene == "LobbyScene") return "#Status_Lobby";
        if (scene != "GameScene") return "#Status_Menu";
        var rules = MatchSettings.Current;
        if (MatchRoster.Current != null) return rules.RankedMode ? "#Status_Ranked" : "#Status_Online";
        if (LocalOpponent.Override != null) return "#Status_Practice";
        return Enumerable.Range(0, rules.Seats).Any(LocalOpponent.IsAI) ? "#Status_AI" : "#Status_Local";
    }
}
