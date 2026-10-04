using System;
using System.Runtime.InteropServices;
using UnityEngine;

// While a piece is pulled the cursor is locked and hidden: the pull follows
// how the mouse moves (Input.mousePositionDelta), not where the cursor is,
// so it can't run off the window or stop at the screen's edge - and with
// FineAim the hand goes four times as far as the pull. (Confining it to the
// window isn't enough for that, and macOS can't.) Let go, the cursor is
// put back where the pull began, where the platform allows (Windows,
// macOS; elsewhere it shows in the middle of the window).
public static class AimCursor
{
    public static bool Held { get; private set; }
    // The frame it was locked on: locking moves the cursor, which the next
    // frame's delta may still carry.
    public static int HeldFrame { get; private set; } = -1;

    public static void Hold()
    {
        if (Held) return;
        Held = true;
        HeldFrame = Time.frameCount;
        Native.Save();
        Cursor.lockState = CursorLockMode.Locked;
    }

    public static void Release()
    {
        if (!Held) return;
        Held = false;
        Cursor.lockState = CursorLockMode.None;
        Native.Restore();
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X, Y;
        }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

        private static Point saved;
        private static bool has;

        public static void Save() => has = GetCursorPos(out saved);

        public static void Restore()
        {
            if (has) SetCursorPos(saved.X, saved.Y);
        }
    }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    private static class Native
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public double X, Y;
        }

        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CoreGraphics)] private static extern Point CGEventGetLocation(IntPtr evt);
        [DllImport(CoreGraphics)] private static extern int CGWarpMouseCursorPosition(Point point);
        [DllImport(CoreGraphics)] private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

        private static Point saved;
        private static bool has;

        // Where the cursor is on the desktop (points, from the main display's top left).
        public static Point Location()
        {
            var evt = CGEventCreate(IntPtr.Zero);
            if (evt == IntPtr.Zero) return default;
            var at = CGEventGetLocation(evt);
            CFRelease(evt);
            return at;
        }

        public static void Save()
        {
            saved = Location();
            has = true;
        }

        // Warping holds the mouse still for a moment unless it's tied back
        // to the cursor at once.
        public static void Restore()
        {
            if (!has) return;
            CGWarpMouseCursorPosition(saved);
            CGAssociateMouseAndMouseCursorPosition(1);
        }
    }
#else
    private static class Native
    {
        public static void Save() { }
        public static void Restore() { }
    }
#endif
}
