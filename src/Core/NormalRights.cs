using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TweakBench.Core
{
    /// <summary>
    /// Starts a program with the signed-in user's normal (not elevated) rights, even when the caller runs as administrator.
    /// Used for File Explorer and for the notification helper, which must not run elevated. When the caller is not elevated this is
    /// a plain Process.Start. When it is, the token of a normal process of the same user and session is borrowed; the Windows
    /// "basic user" trust level (runas /trustlevel) is the last route.
    /// </summary>
    public static class NormalRights
    {
        const uint ProcessQueryLimited = 0x1000;
        const uint TokenQuery = 0x0008, TokenDuplicate = 0x0002, TokenAssignPrimary = 0x0001, TokenAdjust = 0x0020;
        const int TokenUserClass = 1, TokenElevationClass = 20;
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
        [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "GetTokenInformation")] static extern bool GetTokenInformationBuffer(IntPtr token, int infoClass, IntPtr buffer, int length, out int returned);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string application, string commandLine, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation info);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool DestroyEnvironmentBlock(IntPtr environment);

        /// <summary>True when this process runs with an elevated (administrator) token.</summary>
        public static bool IsElevated()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Starts <paramref name="exePath"/> with normal rights. <paramref name="how"/> is empty on success; otherwise it is a short note
        /// on why no safe route worked. Returns true when a process was started.
        /// </summary>
        public static bool Start(string exePath, string arguments, out string how)
        {
            how = "";
            arguments = arguments ?? "";
            if (string.IsNullOrWhiteSpace(exePath)) { how = "no program was given"; return false; }
            if (!IsElevated())
            {
                try { Process.Start(new ProcessStartInfo(exePath, arguments) { UseShellExecute = false })?.Dispose(); return true; }
                catch (Exception ex) { how = ex.Message; return false; }
            }

            // Elevated: borrow the token of a normal (not elevated) process of this user and session.
            try
            {
                int session = Process.GetCurrentProcess().SessionId;
                if (StartWithNormalToken(session, exePath, arguments)) return true;
                how = "no normal-rights process was available to borrow";
            }
            catch (Exception ex) { how = ex.Message; }

            // Last route: the Windows "basic user" trust level, which is also not elevated.
            try
            {
                string runas = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "runas.exe");
                // runas hands the text inside its quotes to CreateProcess, so the program path needs its own (escaped) quotes:
                // an unquoted path with spaces would start the first program that matches a prefix of it.
                string command = ("\"" + exePath + "\"" + (arguments.Length > 0 ? " " + arguments : "")).Replace("\"", "\\\"");
                string runasArguments = "/trustlevel:0x20000 \"" + command +"\"";
                ProcessResult r = ProcessRunner.Run(runas, runasArguments, 15000);
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

        /// <summary>The user a token belongs to, or null.</summary>
        static SecurityIdentifier TokenUser(IntPtr token)
        {
            int length;
            GetTokenInformationBuffer(token, TokenUserClass, IntPtr.Zero, 0, out length);
            if (length <= 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformationBuffer(token, TokenUserClass, buffer, length, out length)) return null;
                return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)); // TOKEN_USER starts with the SID pointer
            }
            catch (Exception) { return null; }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        static SecurityIdentifier CurrentUser()
        {
            try { using (WindowsIdentity me = WindowsIdentity.GetCurrent()) return me.User; }
            catch (Exception) { return null; }
        }

        static bool StartWithNormalToken(int session, string exePath, string arguments)
        {
            // Only borrow from this user's own processes: when an administrator approved the elevation prompt for a standard user,
            // the signed-in user's processes belong to someone else, and the program must not start as that other user.
            SecurityIdentifier me = CurrentUser();
            if (me == null) return false;
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
                            if (StartWithTokenOf(p.Id, me, exePath, arguments)) return true;
                        }
                        catch (Exception) { }
                    }
                }
            }
            return false;
        }

        static bool StartWithTokenOf(int pid, SecurityIdentifier user, string exePath, string arguments)
        {
            IntPtr process = OpenProcess(ProcessQueryLimited, false, pid);
            if (process == IntPtr.Zero) return false;
            IntPtr token = IntPtr.Zero, primary = IntPtr.Zero, environment = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(process, TokenQuery | TokenDuplicate | TokenAssignPrimary | TokenAdjust, out token)) return false;
                if (IsTokenElevated(token)) return false;
                SecurityIdentifier owner = TokenUser(token);
                if (owner == null || owner != user) return false;
                if (!DuplicateTokenEx(token, 0xF01FF /* TOKEN_ALL_ACCESS */, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary)) return false;
                CreateEnvironmentBlock(out environment, primary, false);

                var startup = new StartupInfo { lpDesktop = @"winsta0\default" };
                startup.cb = Marshal.SizeOf(typeof(StartupInfo));
                ProcessInformation info;
                string commandLine = "\"" + exePath + "\"" + (arguments.Length > 0 ? " " + arguments : "");
                bool ok = CreateProcessWithTokenW(primary, LogonWithProfile, exePath, commandLine, CreateUnicodeEnvironment, environment, Path.GetDirectoryName(exePath), ref startup, out info);
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
