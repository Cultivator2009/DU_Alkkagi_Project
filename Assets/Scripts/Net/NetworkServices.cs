using UnityEngine;

// Creates the Steam transport + lobby singletons (both DontDestroyOnLoad)
// on first use. The main menu calls this at boot so Steam is initialized -
// and a friend's lobby invite can be accepted - before the player ever
// opens the lobby. If Steam isn't running, SteamTransport logs and stays
// not-ready; local play doesn't depend on it.
public static class NetworkServices
{
    public static void EnsureCreated()
    {
        if (SteamTransport.Instance == null) new GameObject("SteamTransport").AddComponent<SteamTransport>();
        if (SteamLobbyManager.Instance == null) new GameObject("SteamLobbyManager").AddComponent<SteamLobbyManager>();
    }
}
