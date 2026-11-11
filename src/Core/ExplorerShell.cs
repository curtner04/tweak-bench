using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        // ---- native -------------------------------------------------------------------------------------------

        const uint ProcessQueryLimited = 0x1000;
        const uint TokenQuery = 0x0008, TokenDuplicate = 0x0002, TokenAssignPrimary = 0x0001, TokenAdjust = 0x0020;
        const int TokenElevationClass = 20;
        const int TokenPrimary = 1, SecurityImpersonation = 2;
        const uint CreateUnicodeEnvironment = 0x00000400;
        const uint LogonWithProfile = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string application, string commandLine, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation info);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool DestroyEnvironmentBlock(IntPtr environment);

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

        static bool IsElevatedProcess()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        /// <summary>Starts explorer.exe with the user's normal rights. Returns false (with a short note) when no safe route worked.</summary>
        static bool StartNotElevated(int session, out string how)
        {
            how = "";
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (!IsElevatedProcess())
            {
                try { Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = false })?.Dispose(); return true; }
                catch (Exception ex) { how = ex.Message; return false; }
            }

            // Elevated: borrow the token of a normal (not elevated) process of this user and session.
            try
            {
                if (StartWithNormalToken(session, explorer)) return true;
                how = "no normal-rights process was available to borrow";
            }
            catch (Exception ex) { how = ex.Message; }

            // Last route: Windows' own "basic user" trust level, which is also not elevated.
            try
            {
                string runas = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "runas.exe");
                ProcessResult r = ProcessRunner.Run(runas, "/trustlevel:0x20000 \"" + explorer + "\"", 15000);
                if (r.Ok) return true;
            }
            catch (Exception) { }
            return false;
        }

        static bool IsTokenElevated(IntPtr token)
        {
            int elevated, returned;
            return GetTokenInformation(token, TokenElevationClass, out elevated, 4, out returned) && elevated != 0;
        }

        static bool StartWithNormalToken(int session, string explorerPath)
        {
            string[] donors = { "sihost", "ctfmon", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost", "SearchHost", "ShellExperienceHost" };
            foreach (string name in donors)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try
                        {
                            if (p.SessionId != session) continue;
                            if (StartWithTokenOf(p.Id, explorerPath)) return true;
                        }
                        catch (Exception) { }
                    }
                }
            }
            return false;
        }

        static bool StartWithTokenOf(int pid, string explorerPath)
        {
            IntPtr process = OpenProcess(ProcessQueryLimited, false, pid);
            if (process == IntPtr.Zero) return false;
            IntPtr token = IntPtr.Zero, primary = IntPtr.Zero, environment = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(process, TokenQuery | TokenDuplicate | TokenAssignPrimary | TokenAdjust, out token)) return false;
                if (IsTokenElevated(token)) return false;
                if (!DuplicateTokenEx(token, 0xF01FF /* TOKEN_ALL_ACCESS */, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary)) return false;
                CreateEnvironmentBlock(out environment, primary, false);

                var startup = new StartupInfo { lpDesktop = @"winsta0\default" };
                startup.cb = Marshal.SizeOf(typeof(StartupInfo));
                ProcessInformation info;
                bool ok = CreateProcessWithTokenW(primary, LogonWithProfile, explorerPath, "\"" + explorerPath + "\"", CreateUnicodeEnvironment, environment, Path.GetDirectoryName(explorerPath), ref startup, out info);
                if (!ok) return false;
                CloseHandle(info.hProcess); CloseHandle(info.hThread);
                return true;
            }
            finally
            {
                if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
                if (primary != IntPtr.Zero) CloseHandle(primary);
                if (token != IntPtr.Zero) CloseHandle(token);
                CloseHandle(process);
            }
        }
    }
}
