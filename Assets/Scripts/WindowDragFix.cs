using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Text;
using AOT;
#endif

// Windows only. Dragging a window by its title bar makes Windows run its own
// move loop on the thread that owns the window - Unity's main thread - so the
// game stood still for as long as the window was held (online, the host's
// board with it). This takes the title-bar drag over: the press that would
// start that loop is swallowed, and the window follows the cursor from
// Update while the button is held, so the game keeps running.
// Left to Windows: double-click to maximise, dragging a maximised window,
// resizing by the edges (that still pauses), and snapping to a screen edge
// (lost while this is on). The -nodragfix command-line argument turns it off.
public class WindowDragFix : MonoBehaviour
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern IntPtr SetWindowLong32(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;
    private const int SM_SWAPBUTTON = 23;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    // Static and kept alive for as long as Windows may call them.
    private static readonly WndProc Hook = OnMessage;
    private static readonly EnumWindowsProc Finder = OnWindow;
    private static IntPtr window;
    private static IntPtr previous;
    private static volatile bool dragging;
    private static int grabX, grabY; // the cursor's place in the window when the drag began

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nodragfix") >= 0) return;
        EnumThreadWindows(GetCurrentThreadId(), Finder, IntPtr.Zero);
        if (window == IntPtr.Zero) return; // not found: Windows keeps the drag, as before
        previous = SetWindowProc(Marshal.GetFunctionPointerForDelegate(Hook));
        if (previous == IntPtr.Zero) return;
        var host = new GameObject("WindowDragFix");
        DontDestroyOnLoad(host);
        host.AddComponent<WindowDragFix>();
    }

    [MonoPInvokeCallback(typeof(EnumWindowsProc))]
    private static bool OnWindow(IntPtr candidate, IntPtr lParam)
    {
        var name = new StringBuilder(64);
        GetClassName(candidate, name, name.Capacity);
        if (name.ToString() != "UnityWndClass") return true;
        window = candidate;
        return false; // found: stop
    }

    [MonoPInvokeCallback(typeof(WndProc))]
    private static IntPtr OnMessage(IntPtr target, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_NCLBUTTONDOWN && wParam.ToInt64() == HTCAPTION && !IsZoomed(target)
            && GetCursorPos(out var cursor) && GetWindowRect(target, out var rect))
        {
            grabX = cursor.X - rect.Left;
            grabY = cursor.Y - rect.Top;
            dragging = true;
            return IntPtr.Zero; // handled: no move loop
        }
        return CallWindowProc(previous, target, message, wParam, lParam);
    }

    private static IntPtr SetWindowProc(IntPtr proc) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(window, GWLP_WNDPROC, proc) : SetWindowLong32(window, GWLP_WNDPROC, proc);

    private void Update()
    {
        if (!dragging) return;
        // The physical button that is the primary one (swapped for some left-handed setups).
        var button = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        if ((GetAsyncKeyState(button) & 0x8000) == 0)
        {
            dragging = false;
            return;
        }
        if (GetCursorPos(out var cursor))
            SetWindowPos(window, IntPtr.Zero, cursor.X - grabX, cursor.Y - grabY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnApplicationQuit()
    {
        // Windows must not call into the game once it's going away. The hook
        // keeps forwarding to the original in case a message is already on
        // its way.
        if (restored) return;
        restored = true;
        SetWindowProc(previous);
    }

    private static bool restored;
#endif
}
