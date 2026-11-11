using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using TweakBench.Core;

namespace TweakBench.Notify
{
    /// <summary>
    /// The engine of the background helper "Tweak Bench Notifications.exe": watches the Windows shell for the notification pop-up window
    /// and, every time Windows places it, moves it to the spot chosen in Tweak Bench. Nothing in Windows is changed: when the helper quits,
    /// Windows places the next pop-up itself again. No WPF; runs without administrator rights.
    /// </summary>
    public sealed class NotificationMover : IDisposable
    {
        // ---- native -------------------------------------------------------------------------------------------

        delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)]
        struct Msg { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint milliseconds, uint wakeMask, uint flags);
        [DllImport("user32.dll")] static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref Msg msg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Msg msg);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        const uint EventObjectShow = 0x8002, EventObjectLocationChange = 0x800B, EventObjectCloaked = 0x8017, EventObjectUncloaked = 0x8018;
        const uint WinEventOutOfContext = 0, WinEventSkipOwnProcess = 2;
        const uint QsAllInput = 0x04FF, MwmoInputAvailable = 0x0004, PmRemove = 1, WmQuit = 0x0012;
        const int ObjidWindow = 0;

        // ---- tuning -------------------------------------------------------------------------------------------

        const int MaxMovesPerAppearance = 12;
        const int MinToastHeight = 40;
        /// <summary>A pop-up in the shell's corner is "placed" (fully grown) once it is taller than this.</summary>
        const int SettledToastHeight = 100;
        /// <summary>How long to wait for the shell's own placement before measuring a pop-up wherever it is.</summary>
        const int SettleMs = 400;
        const int TrimWatchMs = 300;
        const int MonitorRefreshSeconds = 30;
        const int StatusIntervalMs = 1000;
        const int TickIntervalMs = 900;
        /// <summary>Move budget: at most this many moves in <see cref="MoveWindowSeconds"/> (a fight with the shell stops quickly,
        /// while a run of notifications shown one after another keeps being moved).</summary>
        const int MoveWindowSeconds = 3;
        const long MaxLogBytes = 256 * 1024;

        // ---- state --------------------------------------------------------------------------------------------

        Mutex mutex;
        bool ownsMutex;
        EventWaitHandle reloadEvent, quitEvent;
        AppSettings settings;
        DateTime settingsFileStamp = DateTime.MinValue, settingsChecked = DateTime.MinValue;
        List<string> titles = new List<string>();
        List<MonitorInfo> monitors = new List<MonitorInfo>();
        DateTime monitorsAt = DateTime.MinValue;
        volatile bool monitorsDirty = true;
        readonly List<IntPtr> hooks = new List<IntPtr>();
        HashSet<int> hookedPids = new HashSet<int>();
        DateTime lastPidCheck = DateTime.MinValue;
        bool hookFailureLogged;
        WinEventProc callback;
        IntPtr toast = IntPtr.Zero;
        readonly HashSet<IntPtr> notToast = new HashSet<IntPtr>();
        bool appearanceActive, countedMoved, capLogged, movedAny;
        int appearanceMoves;
        DateTime appearanceStart = DateTime.MinValue, lastTick = DateTime.MinValue;
        readonly Queue<DateTime> recentMoves = new Queue<DateTime>();
        // The pop-up as the shell placed it in this appearance: card size in DIPs, window size in pixels and the DPI it was sized at.
        bool measured;
        double cardWidthDip, cardHeightDip;
        int shellWidth, shellHeight, shellDpi = 96;
        RectPx lastTarget;
        // Width trimming on a monitor with another DPI (see NotificationPlacement.TargetWindowRect); off for good if the shell resists.
        bool trimDisabled;
        DateTime trimAt = DateTime.MinValue;
        NotificationStatus status = new NotificationStatus();
        bool statusDirty;
        DateTime statusWritten = DateTime.MinValue;
        static readonly object LogLock = new object();

        NotificationMover() { }

        /// <summary>The helper's version: the informational version of the assembly it was built with (Tweak Bench's release version).</summary>
        public static string Version
        {
            get
            {
                try
                {
                    Assembly a = typeof(NotificationMover).Assembly;
                    var info = a.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
                    if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion)) return info.InformationalVersion.Trim();
                    return a.GetName().Version.ToString(3);
                }
                catch (Exception) { return "0.0.0"; }
            }
        }

        /// <summary>
        /// Runs the helper until it is asked to quit. Single instance: exits with 0 at once when another one runs. Returns 0 when it ended
        /// normally (including "the option is off"), 1 or 2 after an error that is also written to the status file.
        /// </summary>
        public static int Run()
        {
            NotificationMover mover = null;
            try
            {
                mover = new NotificationMover();
                return mover.Execute();
            }
            catch (Exception ex)
            {
                Log("Stopped by an error: " + ex);
                try
                {
                    if (mover != null) { mover.status.State = NotificationStatus.StateError; mover.status.LastError = ex.Message; mover.Flush(true); }
                }
                catch (Exception) { }
                return 1;
            }
            finally { if (mover != null) mover.Dispose(); }
        }

        int Execute()
        {
            bool created;
            mutex = CreateMutex(out created);
            if (!created) { mutex.Dispose(); mutex = null; return 0; }
            ownsMutex = true;
            reloadEvent = CreateEvent(NotificationHelper.ReloadEventName);
            quitEvent = CreateEvent(NotificationHelper.QuitEventName);

            status.Pid = Process.GetCurrentProcess().Id;
            status.Version = Version;
            status.ExePath = SafeExePath();
            status.StartedUtc = NotificationStatus.Stamp(DateTime.UtcNow);
            status.State = NotificationStatus.StateWaiting;
            Log("Started " + Version + " (" + status.ExePath + ")");

            IntPtr previousContext = IntPtr.Zero;
            try { previousContext = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (Exception) { }

            string error;
            bool loaded = LoadSettings(true, out error);
            for (int attempt = 1; !loaded && attempt < 6; attempt++)
            {
                // Another program (antivirus, a sync tool, a save in progress) can hold the file for a moment, often right at sign-in.
                Thread.Sleep(500);
                loaded = LoadSettings(true, out error);
            }
            if (!loaded)
            {
                status.State = NotificationStatus.StateError; status.LastError = error; Flush(true);
                Log("Settings could not be read: " + error);
                return 2;
            }
            if (!IsOn())
            {
                status.State = NotificationStatus.StateOff; Flush(true);
                Log("The option is off; exiting.");
                return 0;
            }

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
            callback = OnWinEvent;
            Flush(true);

            try
            {
                MessageLoop();
            }
            finally
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
                UnhookAll();
                try { if (movedAny) RestoreToast(); } catch (Exception ex) { Log("Putting the pop-up back failed: " + ex.Message); }
                if (!IsOn() && status.State != NotificationStatus.StateError) status.State = NotificationStatus.StateOff;
                else if (status.State != NotificationStatus.StateError) status.State = NotificationStatus.StateStopped;
                status.HostFound = false;
                Flush(true);
                Log("Stopped (" + status.State + "); moved " + status.Moved + " pop-ups.");
                try { if (previousContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousContext); } catch (Exception) { }
            }
            return 0;
        }

        public void Dispose()
        {
            try { if (ownsMutex && mutex != null) mutex.ReleaseMutex(); } catch (Exception) { }
            ownsMutex = false;
            if (mutex != null) { mutex.Dispose(); mutex = null; }
            if (reloadEvent != null) { reloadEvent.Dispose(); reloadEvent = null; }
            if (quitEvent != null) { quitEvent.Dispose(); quitEvent = null; }
        }

        // ---- single instance and signals -----------------------------------------------------------------------

        static SecurityIdentifier CurrentUser()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent()) return id.User;
        }

        static Mutex CreateMutex(out bool created)
        {
            try
            {
                var security = new MutexSecurity();
                security.AddAccessRule(new MutexAccessRule(CurrentUser(), MutexRights.Synchronize | MutexRights.Modify | MutexRights.ReadPermissions, AccessControlType.Allow));
                return new Mutex(true, NotificationHelper.MutexName, out created, security);
            }
            catch (Exception)
            {
                return new Mutex(true, NotificationHelper.MutexName, out created);
            }
        }

        /// <summary>An auto-reset event the same user can signal, also from an elevated Tweak Bench.</summary>
        static EventWaitHandle CreateEvent(string name)
        {
            bool created;
            try
            {
                var security = new EventWaitHandleSecurity();
                security.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser(), EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize | EventWaitHandleRights.ReadPermissions, AccessControlType.Allow));
                return new EventWaitHandle(false, EventResetMode.AutoReset, name, out created, security);
            }
            catch (Exception)
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, name, out created);
            }
        }

        static string SafeExePath()
        {
            try { return Process.GetCurrentProcess().MainModule.FileName; }
            catch (Exception) { return Assembly.GetEntryAssembly() == null ? "" : Assembly.GetEntryAssembly().Location; }
        }

        // ---- settings and monitors -----------------------------------------------------------------------------

        bool IsOn() { return settings != null && settings.MoveNotifications && settings.NotificationSpot != null; }

        /// <summary>Reads settings.json. A failed read keeps the last good copy (and is an error only at start, when there is none).</summary>
        bool LoadSettings(bool first, out string error)
        {
            error = "";
            try
            {
                // Take the file time first: a save that lands during the read then still looks new at the next check.
                DateTime stamp = DateTime.MinValue;
                try { stamp = File.Exists(Paths.SettingsFile) ? File.GetLastWriteTimeUtc(Paths.SettingsFile) : DateTime.MinValue; } catch (Exception) { }
                AppSettings s = AppSettings.Load();
                settings = s;
                settingsFileStamp = stamp;
                titles = new List<string> { ToastWindow.DefaultTitle };
                titles.AddRange(s.NotificationTitles ?? new List<string>());
                toast = IntPtr.Zero; notToast.Clear();
                monitorsDirty = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (!first) Log("Settings could not be read; keeping the last good copy. " + ex.Message);
                return !first && settings != null;
            }
        }

        void RefreshMonitors()
        {
            monitorsDirty = false;
            monitorsAt = DateTime.UtcNow;
            List<MonitorInfo> fresh = Monitors.All();
            if (fresh.Count > 0) monitors = fresh;
        }

        void OnDisplayChanged(object sender, EventArgs e) { monitorsDirty = true; }
        void OnPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) { monitorsDirty = true; }

        // ---- loop ----------------------------------------------------------------------------------------------

        void MessageLoop()
        {
            IntPtr[] handles = { reloadEvent.SafeWaitHandle.DangerousGetHandle(), quitEvent.SafeWaitHandle.DangerousGetHandle() };
            while (true)
            {
                uint r = MsgWaitForMultipleObjectsEx(2, handles, 1000, QsAllInput, MwmoInputAvailable);
                if (r == 1) return; // quit event
                if (r == 0)
                {
                    string error;
                    Log("Reload requested.");
                    LoadSettings(false, out error);
                    if (!IsOn()) return;
                    appearanceActive = false;
                }
                Msg msg;
                while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PmRemove))
                {
                    if (msg.message == WmQuit) return;
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                // Every hooked shell event wakes this loop; the housekeeping (and its window search) runs about once a second.
                if (r != 0 && (DateTime.UtcNow - lastTick).TotalMilliseconds < TickIntervalMs) continue;
                lastTick = DateTime.UtcNow;
                try { if (!Tick()) return; }
                catch (Exception ex) { Log("Tick failed: " + ex.Message); }
            }
        }

        /// <summary>One housekeeping pass (about once a second). False when the option was turned off and the helper should end.</summary>
        bool Tick()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - settingsChecked).TotalSeconds >= 5)
            {
                settingsChecked = now;
                try
                {
                    DateTime stamp = File.Exists(Paths.SettingsFile) ? File.GetLastWriteTimeUtc(Paths.SettingsFile) : DateTime.MinValue;
                    if (stamp != settingsFileStamp)
                    {
                        // LoadSettings records the stamp only after a successful read, so a failed read is retried at the next check.
                        string error; LoadSettings(false, out error);
                    }
                }
                catch (Exception) { }
            }
            if (!IsOn()) return false;

            if (monitorsDirty || (now - monitorsAt).TotalSeconds >= MonitorRefreshSeconds) RefreshMonitors();
            EnsureHooks(now);

            // Safety net for events that were missed: look at the window itself once a second.
            IntPtr hwnd = ToastWindow.Find(titles);
            if (hwnd != IntPtr.Zero)
            {
                toast = hwnd;
                if (!ToastWindow.IsCloaked(hwnd)) HandleToast(hwnd);
                else if (appearanceActive) appearanceActive = false;
            }
            Flush(false);
            return true;
        }

        // ---- hooks ---------------------------------------------------------------------------------------------

        static HashSet<int> ShellHostPids()
        {
            var pids = new HashSet<int>();
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcessesByName("ShellExperienceHost"))
            {
                using (p)
                {
                    try { if (p.SessionId == session) pids.Add(p.Id); } catch (Exception) { }
                }
            }
            return pids;
        }

        void EnsureHooks(DateTime now)
        {
            double wait = hookedPids.Count > 0 ? 5 : 1;
            if ((now - lastPidCheck).TotalSeconds < wait) return;
            lastPidCheck = now;

            HashSet<int> current = ShellHostPids();
            if (current.SetEquals(hookedPids)) return;

            UnhookAll();
            toast = IntPtr.Zero; notToast.Clear(); appearanceActive = false;
            foreach (int pid in current)
            {
                // Show..LocationChange (show, hide, reorder, focus, ..., state change, location change), then cloaked / uncloaked.
                IntPtr a = SetWinEventHook(EventObjectShow, EventObjectLocationChange, IntPtr.Zero, callback, (uint)pid, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
                IntPtr b = SetWinEventHook(EventObjectCloaked, EventObjectUncloaked, IntPtr.Zero, callback, (uint)pid, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
                if (a != IntPtr.Zero) hooks.Add(a);
                if (b != IntPtr.Zero) hooks.Add(b);
                if (a != IntPtr.Zero && b != IntPtr.Zero) hookedPids.Add(pid);
                else if (!hookFailureLogged) { hookFailureLogged = true; Log("Watching process " + pid + " failed."); }
            }
            status.HostFound = hookedPids.Count > 0;
            status.State = status.HostFound ? NotificationStatus.StateMoving : NotificationStatus.StateWaiting;
            statusDirty = true;
            Log(status.HostFound ? "Watching ShellExperienceHost " + string.Join(", ", hookedPids.Select(x => x.ToString())) + "." : "ShellExperienceHost is not running; waiting.");
        }

        void UnhookAll()
        {
            foreach (IntPtr h in hooks) { try { UnhookWinEvent(h); } catch (Exception) { } }
            hooks.Clear();
            hookedPids = new HashSet<int>();
        }

        // ---- events --------------------------------------------------------------------------------------------

        void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != ObjidWindow || hwnd == IntPtr.Zero) return; // cheap checks first
            try
            {
                if (!IsOurToast(hwnd)) return;
                if (eventType == EventObjectCloaked) { appearanceActive = false; return; }
                if (eventType == EventObjectUncloaked) BeginAppearance();
                HandleToast(hwnd);
            }
            catch (Exception ex) { Log("Event failed: " + ex.Message); }
        }

        bool IsOurToast(IntPtr hwnd)
        {
            if (hwnd == toast) return true;
            if (notToast.Contains(hwnd)) return false;
            if (ToastWindow.IsToast(hwnd, titles)) { toast = hwnd; return true; }
            if (notToast.Count > 256) notToast.Clear();
            notToast.Add(hwnd);
            return false;
        }

        void BeginAppearance()
        {
            appearanceActive = true;
            appearanceStart = DateTime.UtcNow;
            appearanceMoves = 0;
            countedMoved = false;
            capLogged = false;
            measured = false;
            lastTarget = new RectPx();
            trimAt = DateTime.MinValue;
        }

        /// <summary>True when <paramref name="rect"/> is where the shell itself puts a fully grown pop-up: against the right edge of the
        /// main screen's work area, at its bottom (or top) edge.</summary>
        static bool IsShellPlacement(RectPx rect, MonitorInfo primary)
        {
            if (primary == null || rect.Height <= SettledToastHeight) return false;
            return Math.Abs(rect.Right - primary.Work.Right) <= 2
                && (Math.Abs(rect.Bottom - primary.Work.Bottom) <= 2 || Math.Abs(rect.Top - primary.Work.Top) <= 2);
        }

        MonitorInfo MonitorAt(RectPx rect)
        {
            int x = rect.Left + rect.Width / 2, y = rect.Top + rect.Height / 2;
            return monitors.FirstOrDefault(m => x >= m.Bounds.Left && x < m.Bounds.Right && y >= m.Bounds.Top && y < m.Bounds.Bottom);
        }

        static bool Near(RectPx a, RectPx b, int slack)
        {
            return Math.Abs(a.Left - b.Left) <= slack && Math.Abs(a.Top - b.Top) <= slack && Math.Abs(a.Right - b.Right) <= slack && Math.Abs(a.Bottom - b.Bottom) <= slack;
        }

        /// <summary>Moves the pop-up to the chosen spot when it is showing and not there yet.</summary>
        void HandleToast(IntPtr hwnd)
        {
            if (!IsOn() || ToastWindow.IsCloaked(hwnd)) return;
            RectPx rect = ToastWindow.GetRect(hwnd);
            if (rect.Height <= MinToastHeight) return;
            if (!appearanceActive) BeginAppearance();
            if (!lastTarget.IsEmpty && Near(rect, lastTarget, 1)) return; // our own move arriving

            if (monitors.Count == 0) RefreshMonitors();
            MonitorInfo primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
            MonitorInfo dest = NotificationPlacement.Resolve(settings.NotificationSpot, monitors);
            if (dest == null) return;
            bool shellPlaced = IsShellPlacement(rect, primary);

            if (trimAt != DateTime.MinValue)
            {
                // The shell setting the width back right after we trimmed it means it does not allow it: stop trimming.
                if (!shellPlaced && (DateTime.UtcNow - trimAt).TotalMilliseconds <= TrimWatchMs && Math.Abs(rect.Width - lastTarget.Width) > 2)
                {
                    trimDisabled = true;
                    Log("The shell set the pop-up width back after a trim; keeping its width from now on.");
                }
                trimAt = DateTime.MinValue;
            }

            DateTime nowUtc = DateTime.UtcNow;
            while (recentMoves.Count > 0 && (nowUtc - recentMoves.Peek()).TotalSeconds > MoveWindowSeconds) recentMoves.Dequeue();
            if (recentMoves.Count >= MaxMovesPerAppearance)
            {
                if (!capLogged) { capLogged = true; Log("Too many moves in a few seconds; leaving the pop-up where it is for now."); }
                return;
            }
            capLogged = false;

            if (shellPlaced || !measured)
            {
                // Measure the card where the shell put it. A pop-up that is still growing (not in the corner yet) is left alone for a
                // moment; if the shell's own placement never shows up, use whatever is there.
                if (!shellPlaced && (DateTime.UtcNow - appearanceStart).TotalMilliseconds < SettleMs) return;
                MonitorInfo at = MonitorAt(rect) ?? primary;
                shellDpi = at != null ? at.Dpi : ToastWindow.DpiOf(hwnd);
                NotificationPlacement.CardSizeDip(rect, shellDpi, out cardWidthDip, out cardHeightDip);
                shellWidth = rect.Width;
                shellHeight = rect.Height;
                measured = true;
                status.LastCardWidthDip = Math.Round(cardWidthDip, 1);
                status.LastCardHeightDip = Math.Round(cardHeightDip, 1);
            }
            else if (rect.Height != shellHeight)
            {
                // The shell grew or shrank the pop-up where it is: the card changed by the same amount (in the shell's own DPI).
                cardHeightDip = Math.Max(1, cardHeightDip + (rect.Height - shellHeight) * 96.0 / Math.Max(1, shellDpi));
                shellHeight = rect.Height;
            }

            bool trim = !trimDisabled;
            RectPx target = NotificationPlacement.TargetWindowRect(settings.NotificationSpot, dest, cardWidthDip, cardHeightDip, shellWidth, shellHeight, shellDpi, trim);
            bool resize = Math.Abs(target.Width - rect.Width) > 1 || Math.Abs(target.Height - rect.Height) > 1;

            status.LastToastTitle = ToastWindow.TitleOf(hwnd);
            status.LastToastRect = rect.ToString();
            status.LastTargetRect = target.ToString();
            statusDirty = true;

            if (Near(rect, target, 1)) { lastTarget = target; return; }
            if (!ToastWindow.Move(hwnd, target, resize)) { Log("SetWindowPos failed for " + target + "."); return; }
            lastTarget = target;
            appearanceMoves++;
            recentMoves.Enqueue(DateTime.UtcNow);
            movedAny = true;
            if (resize && target.Width != shellWidth) trimAt = DateTime.UtcNow;
            if (!countedMoved)
            {
                countedMoved = true;
                status.Moved++;
                status.LastMovedUtc = NotificationStatus.Stamp(DateTime.UtcNow);
                Log("Moved pop-up " + rect + " -> " + target + (target.Width != shellWidth ? " (width trimmed for " + dest.Dpi + " dpi)" : "") + " on " + dest.Device + ".");
            }
        }

        // ---- quit ----------------------------------------------------------------------------------------------

        /// <summary>Puts a pop-up that is showing back where Windows places it: the main screen's work-area bottom-right corner.</summary>
        void RestoreToast()
        {
            IntPtr hwnd = ToastWindow.Find(titles);
            if (hwnd == IntPtr.Zero || ToastWindow.IsCloaked(hwnd)) return;
            RectPx rect = ToastWindow.GetRect(hwnd);
            if (rect.Height <= MinToastHeight) return;
            List<MonitorInfo> current = Monitors.All();
            MonitorInfo primary = current.FirstOrDefault(m => m.Primary) ?? current.FirstOrDefault();
            if (primary == null) return;
            // Give a width-trimmed pop-up its own width back: on the main screen its card needs the shell's full width.
            int width = measured && shellWidth > 0 ? shellWidth : rect.Width;
            var target = RectPx.FromSize(primary.Work.Right - width, primary.Work.Bottom - rect.Height, width, rect.Height);
            ToastWindow.Move(hwnd, target, width != rect.Width);
            Thread.Sleep(150); // the move is asynchronous; let the shell's thread take it before this process ends
            Log("Put the visible pop-up back at the main screen corner.");
        }

        // ---- status and log ------------------------------------------------------------------------------------

        void Flush(bool force)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && (!statusDirty || (now - statusWritten).TotalMilliseconds < StatusIntervalMs)) return;
            statusWritten = now;
            // A write can fail while Tweak Bench is reading the file; keep it pending so it is written again.
            bool written = NotificationStatusFile.Write(status);
            for (int attempt = 1; !written && force && attempt < 4; attempt++) { Thread.Sleep(100); written = NotificationStatusFile.Write(status); }
            statusDirty = !written;
        }

        static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    string file = Path.Combine(Paths.Logs, "notifications.log");
                    Directory.CreateDirectory(Paths.Logs);
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Exists && info.Length > MaxLogBytes)
                        {
                            string old = file + ".previous";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(file, old);
                        }
                    }
                    catch (Exception) { }
                    File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch (Exception) { }
        }
    }
}
