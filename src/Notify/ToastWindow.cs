using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TweakBench.Notify
{
    /// <summary>
    /// Finds and moves the Windows notification pop-up window. On Windows 11 every pop-up is one persistent top-level
    /// "Windows.UI.Core.CoreWindow" owned by ShellExperienceHost.exe; it sits in a shell z-order band, so EnumWindows does not list it
    /// and FindWindowEx is used instead. It is cloaked (hidden by DWM) while no pop-up is showing. Call the rectangle and DPI functions
    /// from a Per-Monitor-V2 thread to get physical pixels.
    /// </summary>
    public static class ToastWindow
    {
        /// <summary>Title of the pop-up window in an English display language.</summary>
        public const string DefaultTitle = "New notification";
        public const string WindowClass = "Windows.UI.Core.CoreWindow";
        public const string HostProcess = "ShellExperienceHost.exe";

        const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpAsyncWindowPos = 0x4000;
        const int DwmaCloaked = 14;
        const uint ProcessQueryLimited = 0x1000;

        [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string windowClass, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

        /// <summary>The pop-up window, or IntPtr.Zero when it does not exist right now (also while Windows is not showing pop-ups).</summary>
        public static IntPtr Find(ICollection<string> titles)
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, WindowClass, null)) != IntPtr.Zero)
                if (IsToast(hwnd, titles)) return hwnd;
            return IntPtr.Zero;
        }

        /// <summary>True when <paramref name="hwnd"/> is a ShellExperienceHost CoreWindow whose title is <see cref="DefaultTitle"/> or one of <paramref name="titles"/>.</summary>
        public static bool IsToast(IntPtr hwnd, ICollection<string> titles)
        {
            try
            {
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
                if (ClassOf(hwnd) != WindowClass) return false;
                if (!IsShellHost(hwnd)) return false;
                string title = TitleOf(hwnd);
                if (title.Length == 0) return false;
                if (string.Equals(title, DefaultTitle, StringComparison.OrdinalIgnoreCase)) return true;
                if (titles != null)
                    foreach (string known in titles)
                        if (!string.IsNullOrWhiteSpace(known) && string.Equals(title, known.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch (Exception) { return false; }
        }

        /// <summary>True when DWM hides the window (it is cloaked while no pop-up is showing).</summary>
        public static bool IsCloaked(IntPtr hwnd)
        {
            try
            {
                int cloaked;
                return DwmGetWindowAttribute(hwnd, DwmaCloaked, out cloaked, 4) == 0 && cloaked != 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The window rectangle in screen pixels (physical when called from a Per-Monitor-V2 thread); empty when the window is gone.</summary>
        public static RectPx GetRect(IntPtr hwnd)
        {
            Rect r;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out r)) return new RectPx();
            return new RectPx(r.L, r.T, r.R, r.B);
        }

        /// <summary>The window title, or an empty string.</summary>
        public static string TitleOf(IntPtr hwnd)
        {
            try
            {
                var text = new StringBuilder(256);
                return GetWindowText(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>DPI the window is drawn at (96 when it cannot be read).</summary>
        public static int DpiOf(IntPtr hwnd)
        {
            try { uint dpi = GetDpiForWindow(hwnd); return dpi > 0 ? (int)dpi : 96; }
            catch (Exception) { return 96; }
        }

        /// <summary>
        /// Any visible pop-up-like shell window on the main screen: an uncloaked ShellExperienceHost CoreWindow taller than 60 pixels whose
        /// right and bottom edges are within 4 pixels of the main screen's work area. Used to learn the window title of a test pop-up.
        /// </summary>
        public static IntPtr FindVisibleShellPopup(MonitorInfo primary)
        {
            if (primary == null) return IntPtr.Zero;
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, WindowClass, null)) != IntPtr.Zero)
            {
                try
                {
                    if (!IsShellHost(hwnd) || IsCloaked(hwnd) || !IsWindowVisible(hwnd)) continue;
                    RectPx r = GetRect(hwnd);
                    if (r.Height > 60 && Math.Abs(r.Right - primary.Work.Right) <= 4 && Math.Abs(r.Bottom - primary.Work.Bottom) <= 4) return hwnd;
                }
                catch (Exception) { }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Moves the window (not activating it, not changing its z-order). With <paramref name="resize"/> the window also takes the size of
        /// <paramref name="target"/>; otherwise only its top-left corner is placed.
        /// </summary>
        public static bool Move(IntPtr hwnd, RectPx target, bool resize)
        {
            try
            {
                uint flags = SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos | (resize ? 0u : SwpNoSize);
                return SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, flags);
            }
            catch (Exception) { return false; }
        }

        static string ClassOf(IntPtr hwnd)
        {
            var text = new StringBuilder(256);
            return GetClassName(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
        }

        static bool IsShellHost(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;
            IntPtr process = OpenProcess(ProcessQueryLimited, false, pid);
            if (process == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(520);
                int size = name.Capacity;
                if (!QueryFullProcessImageName(process, 0, name, ref size)) return false;
                return string.Equals(Path.GetFileName(name.ToString()), HostProcess, StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(process); }
        }
    }
}
