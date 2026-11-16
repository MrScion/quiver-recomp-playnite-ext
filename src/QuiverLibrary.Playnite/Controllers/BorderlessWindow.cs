using System;
using System.Runtime.InteropServices;

namespace QuiverLibrary.Playnite.Controllers
{
    /// <summary>Turns a window into a borderless window covering its whole monitor.</summary>
    internal static class BorderlessWindow
    {
        private const int GwlStyle = -16;
        private const uint WsPopup = 0x80000000;
        private const uint WsVisible = 0x10000000;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const uint MonitorDefaultToNearest = 2;

        // Real pixels whatever the display scaling (Windows 10 1607 and later; older ones keep the thread's own).
        private static readonly IntPtr DpiAwarenessPerMonitorV2 = new IntPtr(-4);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        public static bool Apply(IntPtr window)
        {
            var previous = IntPtr.Zero;
            try
            {
                previous = SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info))
                {
                    return false;
                }

                var screen = info.Monitor;
                SetWindowLong(window, GwlStyle, unchecked((int)(WsPopup | WsVisible)));
                var placed = SetWindowPos(window, IntPtr.Zero, screen.Left, screen.Top,
                    screen.Right - screen.Left, screen.Bottom - screen.Top, SwpFrameChanged | SwpShowWindow);
                SetForegroundWindow(window);
                return placed;
            }
            finally
            {
                if (previous != IntPtr.Zero)
                {
                    SetThreadDpiAwarenessContext(previous);
                }
            }
        }
    }
}
