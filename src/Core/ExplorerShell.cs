using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace TweakBench.Core
{
    /// <summary>
    /// Restarts File Explorer (taskbar, Start, desktop) for the signed-in user without ending up with an elevated Explorer.
    /// Normally Windows restarts the shell itself (AutoRestartShell); when it does not, Explorer is started again with the
    /// signed-in user's normal, not elevated, rights. Open File Explorer windows close; nothing else is touched.
    /// </summary>
    public static class ExplorerShell
    {
        // ---- public -------------------------------------------------------------------------------------------

        /// <summary>Restarts File Explorer. Returns true when Explorer is running again; <paramref name="message"/> says what happened.</summary>
        public static bool Restart(out string message)
        {
            try
            {
                int session = Process.GetCurrentProcess().SessionId;
                var oldIds = new HashSet<int>();
                foreach (Process p in SessionExplorers(session))
                {
                    using (p)
                    {
                        oldIds.Add(p.Id);
                        try { p.Kill(); } catch (Exception) { }
                    }
                }
                foreach (int id in oldIds.ToList())
                {
                    try { using (Process p = Process.GetProcessById(id)) p.WaitForExit(5000); } catch (Exception) { }
                }

                // Windows restarts the shell by itself (as the signed-in user, not elevated). Give it a few seconds.
                if (WaitForNewExplorer(session, oldIds, 6000))
                {
                    message = "File Explorer was restarted.";
                    return true;
                }

                string how;
                if (StartNotElevated(session, out how) && WaitForNewExplorer(session, oldIds, 8000))
                {
                    message = "File Explorer was restarted.";
                    return true;
                }

                message = "File Explorer did not come back by itself" + (how.Length > 0 ? " (" + how + ")" : "") + ". Press Ctrl+Shift+Esc, choose Run new task, type explorer.exe and press Enter.";
                return false;
            }
            catch (Exception ex)
            {
                message = "File Explorer could not be restarted: " + ex.Message + " Press Ctrl+Shift+Esc, choose Run new task, type explorer.exe and press Enter if the taskbar is missing.";
                return false;
            }
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        static List<Process> SessionExplorers(int session)
        {
            var list = new List<Process>();
            foreach (Process p in Process.GetProcessesByName("explorer"))
            {
                bool keep = false;
                try { keep = p.SessionId == session; } catch (Exception) { }
                if (keep) list.Add(p); else p.Dispose();
            }
            return list;
        }

        static bool WaitForNewExplorer(int session, HashSet<int> oldIds, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                foreach (Process p in SessionExplorers(session))
                {
                    using (p)
                    {
                        if (oldIds.Contains(p.Id)) continue;
                        try { if (!p.HasExited) { Thread.Sleep(500); return true; } } catch (Exception) { }
                    }
                }
                Thread.Sleep(250);
            }
            return false;
        }

        /// <summary>Starts explorer.exe with the user's normal rights. Returns false (with a short note) when no safe route worked.</summary>
        static bool StartNotElevated(int session, out string how)
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            return NormalRights.Start(explorer, "", out how);
        }
    }
}
