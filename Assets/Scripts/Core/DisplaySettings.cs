using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Fullscreen or a window, and the window's size (Settings > Display).
// Unity itself remembers the last mode and size between launches, so
// nothing is saved here. Fullscreen is borderless at the display's own
// resolution; only a window has a size to choose.
public static class DisplaySettings
{
    // 16:9 window sizes, offered while they fit on the display.
    private static readonly Vector2Int[] Sizes =
    {
        new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440), new Vector2Int(3200, 1800), new Vector2Int(3840, 2160),
    };

    public static bool Fullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

    public static Vector2Int Current => new Vector2Int(Screen.width, Screen.height);

    // Smaller than the display both ways, so the title bar fits too; the
    // smallest is always offered.
    public static List<Vector2Int> WindowSizes()
    {
        var display = Screen.currentResolution;
        var fitting = Sizes.Where(s => s.x < display.width && s.y < display.height).ToList();
        return fitting.Count > 0 ? fitting : new List<Vector2Int> { Sizes[0] };
    }

    public static void SetFullscreen(bool fullscreen)
    {
        if (fullscreen)
        {
            var display = Screen.currentResolution;
            Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            // The largest window that leaves some desktop around it.
            var sizes = WindowSizes();
            var display = Screen.currentResolution;
            var size = sizes.LastOrDefault(s => s.x <= display.width * 0.85f && s.y <= display.height * 0.85f);
            if (size == default) size = sizes[0];
            SetWindowSize(size);
        }
    }

    public static void SetWindowSize(Vector2Int size) => Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);

    // The next listed size above (step 1) or below (-1) the window's width
    // now; null at either end. The window may have been dragged to any size.
    public static Vector2Int? StepFrom(Vector2Int current, int step)
    {
        var sizes = WindowSizes();
        return step > 0 ? sizes.Cast<Vector2Int?>().FirstOrDefault(s => s.Value.x > current.x)
            : sizes.Cast<Vector2Int?>().LastOrDefault(s => s.Value.x < current.x);
    }
}
