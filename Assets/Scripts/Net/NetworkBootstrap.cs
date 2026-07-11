using UnityEngine;
using UnityEngine.SceneManagement;

// Auto-spawns NetworkMatchBridge when GameScene loads into an active Steam
// lobby, so nothing needs to be hand-placed in GameScene.unity. Local
// single-player play (no lobby) never triggers this - GameManager and the
// rest of the local core stay completely unaware of networking.
public static class NetworkBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    private static void Init()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "GameScene") return;
        if (SteamLobbyManager.Instance == null || !SteamLobbyManager.Instance.CurrentLobby.HasValue) return;
        if (Object.FindObjectOfType<NetworkMatchBridge>() != null) return;

        new GameObject("NetworkMatchBridge").AddComponent<NetworkMatchBridge>();
    }
}
