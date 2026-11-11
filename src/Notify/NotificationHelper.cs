using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using TweakBench.Core;

namespace TweakBench.Notify
{
    /// <summary>
    /// How Tweak Bench controls the background helper "Tweak Bench Notifications.exe": start it, ask it to reload settings or quit, and
    /// register it to start at sign-in (HKCU Run). No WPF. Works from an elevated caller too.
    /// </summary>
    public static class NotificationHelper
    {
        public const string ExeName = "Tweak Bench Notifications.exe", RunValueName = "Tweak Bench Notifications";
        public const string MutexName = @"Local\TweakBench.Notifications", ReloadEventName = @"Local\TweakBench.Notifications.Reload",
            QuitEventName = @"Local\TweakBench.Notifications.Quit";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>The helper's path for a given Tweak Bench exe path (the same folder).</summary>
        public static string ExePathNextTo(string appExePath)
        {
            string folder = string.IsNullOrEmpty(appExePath) ? "" : Path.GetDirectoryName(appExePath);
            return Path.Combine(folder ?? "", ExeName);
        }

        /// <summary>True while a helper process owns the single-instance mutex.</summary>
        public static bool IsRunning()
        {
            try
            {
                using (Mutex.OpenExisting(MutexName, System.Security.AccessControl.MutexRights.Synchronize)) return true;
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return true; } // it exists but belongs to someone else
            catch (Exception) { return false; }
        }

        /// <summary>Asks the running helper to read the settings again. False when no helper is running.</summary>
        public static bool Reload() { return Signal(ReloadEventName); }

        /// <summary>Asks the running helper to put any visible pop-up back and exit, then waits up to <paramref name="waitMs"/> for it. True when no helper is running afterwards.</summary>
        public static bool Quit(int waitMs)
        {
            if (!IsRunning()) return true;
            Signal(QuitEventName);
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < waitMs)
            {
                if (!IsRunning()) return true;
                Thread.Sleep(50);
            }
            return !IsRunning();
        }

        static bool Signal(string name)
        {
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(name, System.Security.AccessControl.EventWaitHandleRights.Modify))
                    return handle.Set();
            }
            catch (Exception) { return false; }
        }

        /// <summary>Makes Windows start the helper at sign-in (HKCU Run, for this user only; no administrator rights needed).</summary>
        public static void RegisterStartup(string helperExe)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                key.SetValue(RunValueName, "\"" + helperExe + "\"", RegistryValueKind.String);
        }

        public static void UnregisterStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (key != null && key.GetValue(RunValueName) != null) key.DeleteValue(RunValueName, false);
        }

        /// <summary>The registered start-up command (a quoted exe path), or null when the helper does not start at sign-in.</summary>
        public static string StartupCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key == null ? null : key.GetValue(RunValueName) as string;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Starts the helper without elevation (from an elevated Tweak Bench a normal-rights token is borrowed). <paramref name="error"/> is empty on success.</summary>
        public static bool Start(string helperExe, out string error)
        {
            error = "";
            try
            {
                if (string.IsNullOrWhiteSpace(helperExe) || !File.Exists(helperExe))
                {
                    error = "The notification helper was not found next to Tweak Bench (" + ExeName + ").";
                    return false;
                }
                if (NormalRights.IsElevated())
                {
                    string how;
                    if (NormalRights.Start(helperExe, "", out how)) return true;
                    error = "The notification helper could not be started without administrator rights" + (how.Length > 0 ? " (" + how + ")" : "") + ".";
                    return false;
                }
                var info = new ProcessStartInfo(helperExe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helperExe) };
                Process.Start(info)?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = "The notification helper could not be started: " + ex.Message;
                return false;
            }
        }
    }
}
