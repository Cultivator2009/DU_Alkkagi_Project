using UnityEngine;

// Creates the Steam transport + lobby singletons (both DontDestroyOnLoad)
// on first use. The main menu calls this at boot so Steam is initialized -
// and a friend's lobby invite can be accepted - before the player ever
// opens the lobby. If Steam isn't running, SteamTransport logs and stays
// not-ready; local play doesn't depend on it. With Steam up, the player's
// rating loads here too.
public static class NetworkServices
{
    public static void EnsureCreated()
    {
        if (SteamTransport.Instance == null)
        {
            var transport = new GameObject("SteamTransport").AddComponent<SteamTransport>();
            if (transport.IsReady) PlayerRating.Load(transport.LocalId);
        }
        if (SteamLobbyManager.Instance == null) new GameObject("SteamLobbyManager").AddComponent<SteamLobbyManager>();
    }
}
