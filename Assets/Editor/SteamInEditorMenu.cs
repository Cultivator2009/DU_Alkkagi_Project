using UnityEditor;
using UnityEngine;

// Whether play mode in the editor starts Steam (SteamTransport), kept per
// machine. Off, the game runs as if Steam weren't running: local play,
// no lobby. On, for an online test: from the first play Steam takes the
// editor for the game until Unity quits, and closes it if this account
// logs in on another machine meanwhile.
public static class SteamInEditorMenu
{
    private const string Item = "Tools/Alkkagi/Steam in Play Mode";

    private static bool On => EditorPrefs.GetBool(SteamTransport.EditorPrefKey, false);

    [MenuItem(Item)]
    private static void Toggle()
    {
        EditorPrefs.SetBool(SteamTransport.EditorPrefKey, !On);
        if (On) Debug.LogWarning("Steam in Play Mode is on: from the next play Steam takes the editor for the game until Unity quits, and closes it if this account logs in on another machine.");
        else Debug.Log("Steam in Play Mode is off from the next play (a Steam already started stays until Unity quits).");
    }

    [MenuItem(Item, true)]
    private static bool Check()
    {
        Menu.SetChecked(Item, On);
        return true;
    }
}
