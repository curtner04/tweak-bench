using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using AppShared;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>Tiny file log (machine-local, next to the data folder). Never throws.</summary>
    public static class AppLog
    {
        static readonly object gate = new object();

        public static string FilePath { get { return Path.Combine(Paths.Logs, "tweakbench.log"); } }

        public static void Write(string level, string message, Exception ex = null)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Paths.Logs);
                    string file = FilePath;
                    try { if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024) File.Move(file, file + ".old"); } catch (Exception) { try { File.Delete(file + ".old"); File.Move(file, file + ".old"); } catch (Exception) { } }
                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(' ').Append(level).Append(' ').AppendLine(message);
                    if (ex != null) sb.AppendLine(ex.ToString());
                    File.AppendAllText(file, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception) { }
        }

        public static void Info(string message) { Write("INFO ", message); }
        public static void Warn(string message) { Write("WARN ", message); }
        public static void Error(string message, Exception ex) { Write("ERROR", message, ex); }
    }

    public static class Program
    {
        const string MutexName = @"Local\TweakBench.App";
        const string ShowEventName = @"Local\TweakBench.App.Show";

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int state);
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll")] static extern bool SetStdHandle(int handle, IntPtr value);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--version") { WriteOut(Release.Version); return 0; }
                if (args.Length > 0 && args[0] == "--changelog") { WriteOut(ReleaseNotes.Read()); return 0; }
                if (args.Length > 0 && args[0] == "--audit") return RunAudit(args);
                if (args.Length > 0 && args[0] == "--render") return RunRender(args);

                bool fromRestart = args.Length == 3 && args[0] == AppRestart.Argument;
                bool fromElevation = args.Length == 3 && args[0] == "--elevated-after";
                if (fromRestart || fromElevation)
                {
                    // The old instance is closing: wait for it to finish before taking the instance mutex.
                    try { AppRestart.WaitForParent(args[1], args[2]); }
                    catch (ArgumentException) { /* the old process is already gone */ }
                    catch (InvalidOperationException ex) { AppLog.Warn("Restart handoff: " + ex.Message); }
                }
                bool noElevate = args.Contains("--no-elevate");

                Mutex mutex;
                try { mutex = new Mutex(false, MutexName); }
                catch (UnauthorizedAccessException)
                {
                    // An elevated copy owns the mutex: Tweak Bench is already running.
                    TellAlreadyRunning(true);
                    return 0;
                }

                using (mutex)
                {
                    if (!SingleInstance.TryAcquire(mutex, fromRestart || fromElevation ? 10000 : 0)) { TellAlreadyRunning(false); return 0; }
                    try { return RunApp(args, fromRestart || fromElevation, noElevate); }
                    finally { try { mutex.ReleaseMutex(); } catch (Exception) { } }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not start", ex);
                MessageBox.Show(ex.Message, "Tweak Bench could not start", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        // ------------------------------------------------------------------ normal start

        static int RunApp(string[] args, bool handedOver, bool noElevate)
        {
            if (SingleInstance.TransferWindowOpen())
                throw new IOException("Finish and close Export / import configuration before opening Tweak Bench.");

            AppSettings settings = LoadSettings();

            // Ask for administrator rights once, at the start (not when this copy was started by a restart or by the elevation itself).
            if (!handedOver && !noElevate && settings.AskForAdmin && !IsElevated())
            {
                string error;
                if (TryStartElevated(out error)) return 0;      // the elevated copy waits for this one to exit
                if (error != null) AppLog.Info("Continuing without administrator rights: " + error);
            }

            AppLog.Info("Tweak Bench " + Release.Version + " starting" + (IsElevated() ? " (administrator)" : ""));
            var app = new Application();
            bool showingError = false;
            app.DispatcherUnhandledException += (s, e) =>
            {
                AppLog.Error("Unhandled UI error", e.Exception);
                e.Handled = true;
                if (showingError) return;
                showingError = true;
                try { MessageBox.Show("Something unexpected went wrong:\n\n" + e.Exception.Message + "\n\nDetails were saved to " + AppLog.FilePath, "Tweak Bench", MessageBoxButton.OK, MessageBoxImage.Warning); }
                finally { showingError = false; }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => AppLog.Error("Unhandled error", e.ExceptionObject as Exception);

            var window = new AppWindow(new RealSystem(), settings, false);
            EventWaitHandle showRequest = null;
            RegisteredWaitHandle showWait = null;
            try
            {
                // A second launch sets this event so the running window comes forward.
                // Allow the same user's normal (not elevated) copy to set it: the default access of an elevated process is administrators only.
                var security = new EventWaitHandleSecurity();
                using (WindowsIdentity me = WindowsIdentity.GetCurrent())
                    security.AddAccessRule(new EventWaitHandleAccessRule(me.User, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
                bool created;
                showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out created, security);
                showWait = ThreadPool.RegisterWaitForSingleObject(showRequest, (state, timedOut) => window.BringForward(), null, Timeout.Infinite, false);
            }
            catch (Exception ex) { AppLog.Warn("Single-instance signal unavailable: " + ex.Message); }
            try { return app.Run(window.Window); }
            finally
            {
                if (showWait != null) showWait.Unregister(null);
                if (showRequest != null) showRequest.Dispose();
            }
        }

        static AppSettings LoadSettings()
        {
            try { return AppSettings.Load(); }
            catch (Exception ex)
            {
                // Never overwrite an unreadable settings file: keep a copy, start with defaults.
                AppLog.Error("Settings could not be read", ex);
                string kept = null;
                try
                {
                    kept = Paths.SettingsFile + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    // Move it aside (as the history store does) so the elevated copy that starts next does not meet the same file and show this message again.
                    try { File.Move(Paths.SettingsFile, kept); }
                    catch (Exception) { File.Copy(Paths.SettingsFile, kept, true); }
                }
                catch (Exception) { kept = null; }
                MessageBox.Show("Your Tweak Bench settings could not be read, so it starts with the default settings.\n\n" + ex.Message + (kept != null ? "\n\nThe unreadable file was kept as:\n" + kept : ""), "Tweak Bench", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new AppSettings();
            }
        }

        // ------------------------------------------------------------------ elevation

        public static bool IsElevated()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Starts a copy of Tweak Bench with administrator rights (Windows asks the user). The copy waits for this process to exit before
        /// it takes the instance mutex. Returns true when it started; false when it was declined (error is null) or could not start.
        /// </summary>
        public static bool TryStartElevated(out string error)
        {
            error = null;
            try
            {
                using (var current = Process.GetCurrentProcess())
                {
                    string exe = current.MainModule.FileName;
                    var start = new ProcessStartInfo(exe, "--elevated-after " + current.Id.ToString(CultureInfo.InvariantCulture) + " " + current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture))
                    {
                        UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(exe)
                    };
                    Process.Start(start).Dispose();
                    return true;
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) error = ex.Message;    // 1223: the user declined the Windows prompt
                return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ------------------------------------------------------------------ already running

        static void TellAlreadyRunning(bool elevatedOwner)
        {
            try
            {
                using (var request = EventWaitHandle.OpenExisting(ShowEventName)) { request.Set(); return; }
            }
            catch (Exception) { /* the running copy is elevated, or older: fall back to its window */ }
            foreach (var p in Process.GetProcessesByName("Tweak Bench"))
                using (p)
                    if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); if (!elevatedOwner) return; }
            // Windows does not let a normal program bring an administrator program's window forward, so say where it is.
            if (elevatedOwner)
                MessageBox.Show("Tweak Bench is already open (running as administrator). Switch to its window from the taskbar.", "Tweak Bench", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ------------------------------------------------------------------ command-line modes

        static int RunAudit(string[] args)
        {
            string file = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
            if (file == null)
            {
                var sw = new StringWriter();
                Audit.Write(new RealSystem(), sw);
                WriteOut(sw.ToString().TrimEnd());
                return 0;
            }
            string folder = Path.GetDirectoryName(Path.GetFullPath(file));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(false)))
                Audit.Write(new RealSystem(), writer);
            return 0;
        }

        static int RunRender(string[] args)
        {
            int at = Array.IndexOf(args, "--render");
            string folder = at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : Path.Combine(Path.GetTempPath(), "TweakBench-shots");
            string theme = "dark";
            int t = Array.IndexOf(args, "--theme");
            if (t >= 0 && t + 1 < args.Length && (args[t + 1] == "light" || args[t + 1] == "dark")) theme = args[t + 1];
            try { return Renderer.Run(folder, theme, WriteOut); }
            catch (Exception ex)
            {
                WriteOut("Rendering failed: " + ex);
                return 1;
            }
        }

        /// <summary>
        /// This is a GUI-subsystem exe, so --version, --changelog and --audit attach to the parent terminal's console
        /// (unless the output is already redirected to a file or pipe) to print where the user can see it.
        /// </summary>
        public static void WriteOut(string text)
        {
            IntPtr handle = GetStdHandle(-11);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                if (AttachConsole(-1))
                {
                    // GENERIC_WRITE, FILE_SHARE_WRITE, OPEN_EXISTING
                    IntPtr console = CreateFile("CONOUT$", 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (console != new IntPtr(-1)) SetStdHandle(-11, console);
                }
            }
            Console.Out.WriteLine(text);
            Console.Out.Flush();
        }
    }
}
