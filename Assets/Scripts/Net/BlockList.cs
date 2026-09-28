using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;
using UnityEngine;

// The players this player never wants to be matched with again, kept on
// this machine (PlayerPrefs). A lobby hosted by one of them doesn't show in
// the browser or quick match; one who joins this player's lobby is sent
// away; a lobby one is in is left (SteamLobbyManager). Their messages are
// dropped too (NetFilters.NotBlocked).
//
// Reporting goes to Steam, the only one who can act on it: its own page
// for the player (in the overlay, or the browser without it), and the
// player is blocked here as well. There's no server of ours to report to;
// the host's word decides a match, so this is how a bad host is dealt with.
public static class BlockList
{
    private const string PrefsKey = "blocked";

    public static event Action OnChanged;

    private static Dictionary<ulong, string> blocked;

    private static Dictionary<ulong, string> Entries
    {
        get
        {
            if (blocked != null) return blocked;
            blocked = new Dictionary<ulong, string>();
            // id=name lines; a name is only for the list in Settings.
            foreach (var line in PlayerPrefs.GetString(PrefsKey, string.Empty).Split('\n'))
            {
                var at = line.IndexOf('=');
                if (at > 0 && ulong.TryParse(line.Substring(0, at), out var id)) blocked[id] = line.Substring(at + 1);
            }
            return blocked;
        }
    }

    public static int Count => Entries.Count;
    public static IEnumerable<(ulong id, string name)> All => Entries.Select(e => (e.Key, e.Value));

    public static bool IsBlocked(ulong steamId) => steamId != 0 && Entries.ContainsKey(steamId);

    public static void Block(ulong steamId, string name)
    {
        if (steamId == 0 || (SteamClient.IsValid && steamId == SteamClient.SteamId.Value)) return;
        Entries[steamId] = (name ?? string.Empty).Replace('\n', ' ');
        Save();
    }

    public static void Unblock(ulong steamId)
    {
        if (Entries.Remove(steamId)) Save();
    }

    public static void Clear()
    {
        Entries.Clear();
        Save();
    }

    // Steam's page for the player, where it can be reported, and blocked here.
    public static void Report(ulong steamId, string name)
    {
        Block(steamId, name);
        if (SteamClient.IsValid && SteamUtils.IsOverlayEnabled) SteamFriends.OpenUserOverlay(steamId, "steamid");
        else Application.OpenURL($"https://steamcommunity.com/profiles/{steamId}");
    }

    private static void Save()
    {
        PlayerPrefs.SetString(PrefsKey, string.Join("\n", Entries.Select(e => $"{e.Key}={e.Value}")));
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }
}
