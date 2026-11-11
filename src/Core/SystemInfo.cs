using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TweakBench.Core
{
    /// <summary>"This PC" facts shown on the Start page: Windows version, uptime, administrator status and pending restarts.</summary>
    public sealed class SystemInfo
    {
        public string ProductName, DisplayVersion, Edition, ComputerName, UserName;
        public int Build, Ubr;
        public bool IsElevated, ElevatedAsOtherUser;
        public TimeSpan Uptime;
        public DateTime LastBoot;
        public bool RestartPending;
        public List<string> RestartPendingReasons = new List<string>();

        const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        static string Text(ISystem sys, string name)
        {
            try
            {
                RegData d = sys.ReadValue(Hive.Machine, CurrentVersion, name);
                return d == null || d.Data == null ? null : Convert.ToString(d.Data, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Reads everything through <paramref name="sys"/> where it can; the clock and the user come from the running PC.</summary>
        public static SystemInfo Read(ISystem sys)
        {
            var info = new SystemInfo
            {
                Build = sys.Build,
                Edition = sys.Edition,
                IsElevated = sys.IsElevated,
                ComputerName = Environment.MachineName,
                UserName = Environment.UserName,
            };

            string product = Text(sys, "ProductName");
            if (sys.Build >= 22000)
            {
                // Windows 11 still reports "Windows 10 ..." in the registry.
                if (string.IsNullOrWhiteSpace(product)) product = "Windows 11";
                else if (product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)) product = "Windows 11" + product.Substring("Windows 10".Length);
            }
            else if (string.IsNullOrWhiteSpace(product)) product = "Windows build " + sys.Build;
            info.ProductName = product;

            info.DisplayVersion = Text(sys, "DisplayVersion") ?? Text(sys, "ReleaseId") ?? "";
            try
            {
                RegData ubr = sys.ReadValue(Hive.Machine, CurrentVersion, "UBR");
                if (ubr != null && ubr.Data is uint) info.Ubr = (int)(uint)ubr.Data;
            }
            catch (Exception) { }

            try
            {
                info.Uptime = TimeSpan.FromMilliseconds((double)GetTickCount64());
                info.LastBoot = DateTime.Now - info.Uptime;
            }
            catch (Exception) { info.LastBoot = DateTime.Now; }

            ReadRestartPending(sys, info);
            if (sys is RealSystem && sys.IsElevated) info.ElevatedAsOtherUser = ExplorerOwnerDiffers();
            return info;
        }

        static void ReadRestartPending(ISystem sys, SystemInfo info)
        {
            try
            {
                if (sys.KeyExists(Hive.Machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                    info.RestartPendingReasons.Add("Windows Update installed something that needs a restart.");
            }
            catch (Exception) { }
            try
            {
                if (sys.KeyExists(Hive.Machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                    info.RestartPendingReasons.Add("A Windows component is waiting for a restart to finish installing.");
            }
            catch (Exception) { }
            try
            {
                RegData files = sys.ReadValue(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations");
                string[] list = files == null ? null : files.Data as string[];
                if (list != null && System.Linq.Enumerable.Any(list, s => !string.IsNullOrWhiteSpace(s)))
                    info.RestartPendingReasons.Add("Some files will be replaced the next time the PC starts.");
            }
            catch (Exception) { }
            info.RestartPending = info.RestartPendingReasons.Count > 0;
        }

        /// <summary>
        /// True when this (elevated) process runs as a different Windows account than the one signed in at the screen,
        /// which is what happens when someone else's administrator password is typed into the UAC prompt.
        /// Best effort: false when it cannot be told.
        /// </summary>
        internal static bool ExplorerOwnerDiffers()
        {
            try
            {
                SecurityIdentifier me;
                using (WindowsIdentity id = WindowsIdentity.GetCurrent()) me = id.User;
                int session = Process.GetCurrentProcess().SessionId;
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    using (p)
                    {
                        if (p.SessionId != session) continue;
                        SecurityIdentifier owner = OwnerOf(p.Id);
                        if (owner != null && me != null) return !owner.Equals(me);
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        internal static SecurityIdentifier OwnerOf(int processId)
        {
            IntPtr process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
            if (process == IntPtr.Zero) return null;
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out token)) return null;
                try { using (var id = new WindowsIdentity(token)) return id.User; }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(process); }
        }
    }
}
