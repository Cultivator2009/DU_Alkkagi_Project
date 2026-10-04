using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameAction
{
    CameraView, // hold for the other camera angle
    CancelAim,
    PanView,    // hold and drag: move the main view across the board
    ResetView,
    FineAim,    // hold while pulling: the pull follows the mouse slower
    HideHud     // photo mode: the HUD out of the way, back with it again (or Esc)
}

// Rebindable keys (Settings > Controls), saved on this machine. Any KeyCode
// works, mouse buttons included, except the left button (clicking and
// aiming need it) and Escape (it cancels a rebind).
public static class KeyBindings
{
    private const string PrefsPrefix = "key.";

    private static readonly Dictionary<GameAction, KeyCode> Defaults = new Dictionary<GameAction, KeyCode>
    {
        { GameAction.CameraView, KeyCode.LeftControl },
        { GameAction.CancelAim, KeyCode.Mouse1 },
        { GameAction.PanView, KeyCode.Mouse2 },
        { GameAction.ResetView, KeyCode.R },
        { GameAction.FineAim, KeyCode.LeftShift },
        { GameAction.HideHud, KeyCode.H },
    };

    public static event Action OnChanged;

    // Read from PlayerPrefs once: the keys are polled every frame, the
    // cancel key once per piece. Set and ResetAll drop it.
    private static KeyCode[] cache;

    // For an action with nothing saved whose default another one already has
    // (an action added after the player rebound a key to its default): the
    // first of these still free.
    private static readonly KeyCode[] Spares = { KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.LeftAlt, KeyCode.Space, KeyCode.Q, KeyCode.E, KeyCode.F };

    public static KeyCode Get(GameAction action)
    {
        if (cache == null)
        {
            cache = new KeyCode[Defaults.Count];
            var used = new HashSet<KeyCode>();
            var unsaved = new List<GameAction>();
            foreach (var pair in Defaults)
            {
                if (!PlayerPrefs.HasKey(PrefsPrefix + pair.Key))
                {
                    unsaved.Add(pair.Key);
                    continue;
                }
                cache[(int)pair.Key] = (KeyCode)PlayerPrefs.GetInt(PrefsPrefix + pair.Key);
                used.Add(cache[(int)pair.Key]);
            }
            foreach (var free in unsaved)
            {
                var key = Defaults[free];
                if (used.Contains(key)) key = Array.Find(Spares, k => !used.Contains(k));
                cache[(int)free] = key;
                used.Add(key);
            }
        }
        return cache[(int)action];
    }

    public static bool Down(GameAction action) => Input.GetKeyDown(Get(action));
    public static bool Up(GameAction action) => Input.GetKeyUp(Get(action));
    public static bool Held(GameAction action) => Input.GetKey(Get(action));

    public static bool IsBindable(KeyCode key) => key != KeyCode.None && key != KeyCode.Mouse0 && key != KeyCode.Escape;

    // Taking a key another action already uses swaps the two, so no key is
    // ever bound twice and no action is left without one.
    public static void Set(GameAction action, KeyCode key)
    {
        var previous = Get(action);
        foreach (var other in Defaults.Keys)
            if (other != action && Get(other) == key) PlayerPrefs.SetInt(PrefsPrefix + other, (int)previous);
        PlayerPrefs.SetInt(PrefsPrefix + action, (int)key);
        cache = null;
        OnChanged?.Invoke();
    }

    public static void ResetAll()
    {
        foreach (var action in Defaults.Keys) PlayerPrefs.DeleteKey(PrefsPrefix + action);
        cache = null;
        OnChanged?.Invoke();
    }

    public static string DisplayName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Mouse1: return Loc.Get("key.mouseRight");
            case KeyCode.Mouse2: return Loc.Get("key.mouseMiddle");
            case KeyCode.Mouse3:
            case KeyCode.Mouse4:
            case KeyCode.Mouse5:
            case KeyCode.Mouse6: return Loc.Get("key.mouseN", key - KeyCode.Mouse0 + 1);
            case KeyCode.LeftControl: return Loc.Get("key.left", "Ctrl");
            case KeyCode.RightControl: return Loc.Get("key.right", "Ctrl");
            case KeyCode.LeftShift: return Loc.Get("key.left", "Shift");
            case KeyCode.RightShift: return Loc.Get("key.right", "Shift");
            case KeyCode.LeftAlt: return Loc.Get("key.left", "Alt");
            case KeyCode.RightAlt: return Loc.Get("key.right", "Alt");
            case KeyCode.LeftCommand: return Loc.Get("key.left", "Cmd");
            case KeyCode.RightCommand: return Loc.Get("key.right", "Cmd");
            case KeyCode.Space: return Loc.Get("key.space");
        }
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
        if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num " + (int)(key - KeyCode.Keypad0);
        return key.ToString();
    }
}
