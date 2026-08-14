using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BorderlessApp
{
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        // A custom, system-wide-unique message ID (guaranteed unique by
        // Windows for this exact string) used to tell an already-running
        // instance "someone tried to launch you again - show yourself".
        public static readonly uint ShowExistingInstanceMessage =
            RegisterWindowMessage("BorderlessGameApp-ShowExistingInstance-8F3E2C11");

        // Called by a second launch that's about to exit (mutex already
        // held by another instance) to ask the running instance to restore
        // its window instead of leaving the user stuck with no way back in.
        public static void RequestExistingInstanceToShow()
        {
            PostMessage(HWND_BROADCAST, ShowExistingInstanceMessage, IntPtr.Zero, IntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;   // title bar
        private const int WS_THICKFRAME = 0x00040000; // resizable border
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        // True if the window still has its title bar/border (i.e. hasn't
        // been made borderless yet, or reset itself back to windowed).
        public static bool HasBorder(IntPtr hWnd)
        {
            int style = GetWindowLong(hWnd, GWL_STYLE);
            return (style & WS_CAPTION) != 0;
        }

        // Current position/size, so callers can remember it before
        // stripping the border and put it back later.
        public static Rectangle GetWindowBounds(IntPtr hWnd)
        {
            GetWindowRect(hWnd, out RECT rect);
            return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public static void MakeBorderless(IntPtr hWnd)
        {
            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~WS_CAPTION;
            style &= ~WS_THICKFRAME;
            SetWindowLong(hWnd, GWL_STYLE, style);

            // Size to whichever monitor the window is CURRENTLY on, not
            // always the primary display - matters for multi-monitor setups
            // where the game might be running on a second/ultrawide screen.
            // Screen.Bounds is in virtual-desktop coordinates, so a monitor
            // to the left/above the primary one (negative X/Y) is handled
            // correctly too.
            var screenBounds = Screen.FromHandle(hWnd).Bounds;

            SetWindowPos(hWnd, IntPtr.Zero, screenBounds.X, screenBounds.Y,
                screenBounds.Width, screenBounds.Height,
                SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        // Puts the title bar/border back. If originalBounds is supplied,
        // also puts the window back to its pre-borderless size/position;
        // otherwise it just restores the frame at the current size.
        public static void RestoreBorder(IntPtr hWnd, Rectangle? originalBounds)
        {
            int style = GetWindowLong(hWnd, GWL_STYLE);
            style |= WS_CAPTION;
            style |= WS_THICKFRAME;
            SetWindowLong(hWnd, GWL_STYLE, style);

            if (originalBounds.HasValue)
            {
                var b = originalBounds.Value;
                SetWindowPos(hWnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height,
                    SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            else
            {
                // No remembered size - just reapply the frame so the
                // border/title bar reappears at the current size.
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
            }
        }
    }
}
