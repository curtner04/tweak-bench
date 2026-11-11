using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TweakBench.Core;

namespace TweakBench.Tools
{
    /// <summary>What a repair task does when run.</summary>
    public enum RepairKind
    {
        Sfc,
        DismRestoreHealth,
        ComponentCleanup,
        ResetWindowsUpdate,
        ResetNetwork,
        FlushDns,
        ClearTemp,
        RestorePoint,
        RestartExplorer,
        OpenWindowsUpdate
    }

    public sealed class RepairTask
    {
        public RepairKind Kind;
        public string Title;
        /// <summary>What it does and when to use it, in plain words.</summary>
        public string Description;
        public bool NeedsAdmin;
        /// <summary>Runs for minutes and streams output (shown in the console with a Stop button).</summary>
        public bool Long;
        /// <summary>The confirmation text (what exactly will happen). Empty when no confirmation is needed.</summary>
        public string Confirm;
        public string Button;
        public string Glyph;
        public string Group;
    }

    public sealed class ClassicTool
    {
        public string Id;
        public string Name;
        public string Purpose;
        /// <summary>Segoe Fluent Icons code point.</summary>
        public string Glyph;
        /// <summary>File passed to ShellExecute.</summary>
        public string File;
        public string Arguments = "";
    }

    public sealed class TempCleanResult
    {
        public long Bytes;
        public int Files;
        public int Skipped;
    }

    /// <summary>The repair tasks, classic tool shortcuts, and the code that runs them. No WPF in here.</summary>
    public static class RepairTools
    {
        public const string GroupScan = "Check and repair Windows";
        public const string GroupReset = "Reset parts of Windows";
        public const string GroupQuick = "Quick actions";

        public static readonly string[] Groups = { GroupScan, GroupReset, GroupQuick };

        public static readonly List<RepairTask> Tasks = new List<RepairTask>
        {
            new RepairTask { Kind = RepairKind.Sfc, Group = GroupScan, Title = "Check system files", Button = "Run check", Glyph = "\uE9D9", NeedsAdmin = true, Long = true,
                Description = "System File Checker scans Windows' protected files and repairs damaged ones from a stored copy. Takes about 5 to 15 minutes.",
                Confirm = "Run System File Checker (sfc /scannow)? It scans every protected Windows file and replaces damaged ones. Your own files are not touched." },
            new RepairTask { Kind = RepairKind.DismRestoreHealth, Group = GroupScan, Title = "Repair the Windows image", Button = "Start repair", Glyph = "\uE90F", NeedsAdmin = true, Long = true,
                Description = "DISM downloads healthy copies of damaged Windows components from Windows Update. Run it first if the system file check cannot fix everything.",
                Confirm = "Repair the Windows component store with DISM (/Online /Cleanup-Image /RestoreHealth)? It needs an internet connection and can take 10 to 30 minutes. Nothing is restarted." },
            new RepairTask { Kind = RepairKind.ComponentCleanup, Group = GroupScan, Title = "Clean up old update files", Button = "Clean up", Glyph = "\uE74D", NeedsAdmin = true, Long = true,
                Description = "Removes superseded copies of Windows components to free disk space. After this you cannot uninstall the updates installed so far.",
                Confirm = "Clean up the component store (DISM /StartComponentCleanup)? Old versions of Windows components are deleted, so you cannot uninstall the updates installed so far." },

            new RepairTask { Kind = RepairKind.ResetWindowsUpdate, Group = GroupReset, Title = "Reset Windows Update", Button = "Reset", Glyph = "\uE895", NeedsAdmin = true, Long = true,
                Description = "Fixes updates that fail or get stuck. Stops the update services, renames the update download and catalog folders (nothing is deleted), then starts the services again.",
                Confirm = "Reset Windows Update? These services stop for a moment: Windows Update, Background Intelligent Transfer and Cryptographic Services. The folders C:\\Windows\\SoftwareDistribution and C:\\Windows\\System32\\catroot2 are renamed to *.old-<date and time> (not deleted) and Windows rebuilds them. The update history list starts empty." },
            new RepairTask { Kind = RepairKind.ResetNetwork, Group = GroupReset, Title = "Reset the network stack", Button = "Reset", Glyph = "\uE839", NeedsAdmin = true, Long = true,
                Description = "Fixes odd connection problems by resetting Winsock and TCP/IP to their defaults and clearing the DNS cache. Needs a restart afterwards.",
                Confirm = "Reset the network stack? This runs netsh winsock reset, netsh int ip reset and ipconfig /flushdns. Static IP addresses, DNS servers set by hand and some VPN or firewall add-ons may need to be set up again. Restart the PC afterwards to finish. Tweak Bench does not restart it for you." },

            new RepairTask { Kind = RepairKind.FlushDns, Group = GroupQuick, Title = "Flush the DNS cache", Button = "Flush", Glyph = "\uE774", Long = false,
                Description = "Forgets stored website addresses so Windows looks them up again. Helps after a site moves or a DNS change.", Confirm = "" },
            new RepairTask { Kind = RepairKind.ClearTemp, Group = GroupQuick, Title = "Clear temporary files", Button = "Check and clear", Glyph = "\uE74D", Long = false,
                Description = "Deletes files older than 24 hours from your Temp folder and older than 7 days from C:\\Windows\\Temp. Files in use are skipped. Shows how much space it frees.",
                Confirm = "Clear temporary files?" },
            new RepairTask { Kind = RepairKind.RestorePoint, Group = GroupQuick, Title = "Create a restore point", Button = "Create", Glyph = "\uE777", NeedsAdmin = true, Long = false,
                Description = "Saves a snapshot of system settings you can go back to in System Restore. Windows allows one per 24 hours unless that limit is changed.",
                Confirm = "Create a System Restore point called \"Tweak Bench\" now?" },
            new RepairTask { Kind = RepairKind.RestartExplorer, Group = GroupQuick, Title = "Restart File Explorer", Button = "Restart Explorer", Glyph = "\uE72C", Long = false,
                Description = "Reloads the taskbar, Start button and desktop. Fixes a frozen taskbar and applies Explorer tweaks. Open Explorer windows close.",
                Confirm = "Restart File Explorer? The taskbar and desktop flicker and open File Explorer windows close. Other programs keep running." },
            new RepairTask { Kind = RepairKind.OpenWindowsUpdate, Group = GroupQuick, Title = "Open Windows Update settings", Button = "Open", Glyph = "\uE895", Long = false,
                Description = "Opens the Windows Update page in Settings.", Confirm = "" },
        };

        public static RepairTask Task(RepairKind kind) { return Tasks.First(t => t.Kind == kind); }

        static ClassicTool T(string id, string name, string purpose, string glyph, string file, string args = "")
        {
            return new ClassicTool { Id = id, Name = name, Purpose = purpose, Glyph = glyph, File = file, Arguments = args };
        }

        public static readonly List<ClassicTool> Classic = new List<ClassicTool>
        {
            T("controlpanel", "Control Panel", "The classic settings hub.", "\uE713", "control.exe"),
            T("network", "Network Connections", "Adapters, Wi-Fi and Ethernet settings.", "\uE839", "control.exe", "ncpa.cpl"),
            T("power", "Power Options", "Power plans and what the buttons do.", "\uE7E8", "control.exe", "powercfg.cpl"),
            T("sound", "Sound", "Playback and recording devices.", "\uE767", "control.exe", "mmsys.cpl"),
            T("sysprops", "System Properties", "Computer name, remote access, restore.", "\uE770", "control.exe", "sysdm.cpl"),
            T("advanced", "Advanced system settings", "Performance, startup and recovery options.", "\uE713", "SystemPropertiesAdvanced.exe"),
            T("envvars", "Environment Variables", "PATH and other variables.", "\uE943", "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"),
            T("programs", "Programs and Features", "Uninstall programs, turn features on or off.", "\uE71D", "control.exe", "appwiz.cpl"),
            T("devmgmt", "Device Manager", "Hardware and drivers.", "\uE772", "mmc.exe", "devmgmt.msc"),
            T("diskmgmt", "Disk Management", "Partitions and drive letters.", "\uEDA2", "mmc.exe", "diskmgmt.msc"),
            T("compmgmt", "Computer Management", "Users, shares, events and disks in one place.", "\uE7F4", "mmc.exe", "compmgmt.msc"),
            T("services", "Services", "Start, stop and configure background services.", "\uE90F", "mmc.exe", "services.msc"),
            T("eventvwr", "Event Viewer", "Windows logs and error details.", "\uE81C", "mmc.exe", "eventvwr.msc"),
            T("taskschd", "Task Scheduler", "Programs that run on a schedule.", "\uE787", "mmc.exe", "taskschd.msc"),
            T("regedit", "Registry Editor", "Edit the registry. Be careful here.", "\uE943", "regedit.exe"),
            T("netplwiz", "User Accounts", "Manage accounts and automatic sign-in.", "\uE77B", "netplwiz.exe"),
            T("region", "Region", "Formats for dates, numbers and currency.", "\uE774", "control.exe", "intl.cpl"),
            T("mouse", "Mouse", "Buttons, pointer speed and double-click.", "\uE962", "control.exe", "main.cpl"),
            T("datetime", "Date and time", "Clock, time zone and internet time.", "\uE121", "control.exe", "timedate.cpl"),
            T("firewall", "Windows Defender Firewall", "Allow apps and see firewall status.", "\uEA18", "control.exe", "firewall.cpl"),
            T("msconfig", "System Configuration", "Startup choices and boot options.", "\uE713", "msconfig.exe"),
            T("resmon", "Resource Monitor", "Live CPU, memory, disk and network use per program.", "\uE9D9", "resmon.exe"),
            T("reliability", "Reliability Monitor", "A timeline of crashes and failures.", "\uE9D9", "perfmon.exe", "/rel"),
            T("taskmgr", "Task Manager", "Running programs and startup apps.", "\uE9D9", "taskmgr.exe"),
            T("sysinfo", "System Information", "Full list of hardware and software details.", "\uE946", "msinfo32.exe"),
            T("cleanmgr", "Disk Cleanup", "Free space by removing system leftovers.", "\uE74D", "cleanmgr.exe"),
            T("winupdate", "Windows Update settings", "Check for and pause updates.", "\uE895", "ms-settings:windowsupdate"),
        };

        // ---- starting classic tools ----

        /// <summary>Opens a classic tool with ShellExecute. Returns null on success or a plain message. Declining the UAC prompt is not an error.</summary>
        public static string Open(ClassicTool tool)
        {
            try
            {
                var psi = new ProcessStartInfo(tool.File, tool.Arguments ?? "") { UseShellExecute = true };
                Process.Start(psi);
                return null;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return null; // the user declined the administrator prompt
                return tool.Name + " could not be opened: " + ex.Message;
            }
            catch (Exception ex) { return tool.Name + " could not be opened: " + ex.Message; }
        }

        public static string OpenWindowsUpdateSettings()
        {
            return Open(Classic.First(t => t.Id == "winupdate"));
        }

        // ---- commands ----

        public static string SfcPath { get { return ProcessRunner.System32("sfc.exe"); } }
        public static string DismPath { get { return ProcessRunner.System32("dism.exe"); } }

        public const string SfcArgs = "/scannow";
        public const string DismRestoreHealthArgs = "/Online /Cleanup-Image /RestoreHealth";
        public const string DismComponentCleanupArgs = "/Online /Cleanup-Image /StartComponentCleanup";

        /// <summary>sfc.exe writes UTF-16 even when redirected; everything else uses the console code page.</summary>
        public static Encoding SfcEncoding { get { return Encoding.Unicode; } }

        /// <summary>Services stopped and started, folders renamed (never deleted), one progress line per step.</summary>
        public static string ResetWindowsUpdateScript(string stamp)
        {
            if (stamp == null || stamp.Any(c => !(char.IsLetterOrDigit(c) || c == '-'))) throw new ArgumentException("Bad time stamp.");
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference='Continue'");
            sb.AppendLine("$stamp = " + Cmd.PsQuote(stamp));
            sb.AppendLine("$services = 'wuauserv','bits','cryptsvc'");
            // Remember what was running, including the services that depend on the crypto service, so exactly those come back.
            sb.AppendLine("$wasRunning = @{}");
            sb.AppendLine("foreach ($s in $services) { $o = Get-Service -Name $s -ErrorAction SilentlyContinue; $wasRunning[$s] = ($o -ne $null -and $o.Status -eq 'Running') }");
            sb.AppendLine("$dependents = @(); try { $dependents = @((Get-Service -Name 'cryptsvc' -ErrorAction Stop).DependentServices | Where-Object { $_.Status -eq 'Running' } | ForEach-Object { $_.Name }) } catch {}");
            sb.AppendLine("foreach ($s in $services) { try { Write-Output ('Stopping ' + $s + '...'); Stop-Service -Name $s -Force -ErrorAction Stop } catch { Write-Output ('  could not stop ' + $s + ': ' + $_.Exception.Message) } }");
            sb.AppendLine("$folders = @((Join-Path $env:windir 'SoftwareDistribution'), (Join-Path $env:windir 'System32\\catroot2'))");
            sb.AppendLine("$failed = 0");
            sb.AppendLine("$startFailed = 0");
            sb.AppendLine("try {");
            sb.AppendLine("foreach ($f in $folders) {");
            sb.AppendLine("  if (Test-Path -LiteralPath $f) {");
            sb.AppendLine("    $new = $f + '.old-' + $stamp");
            sb.AppendLine("    try { Rename-Item -LiteralPath $f -NewName (Split-Path -Leaf $new) -ErrorAction Stop; Write-Output ('Renamed ' + $f + ' to ' + $new) }");
            sb.AppendLine("    catch { $failed++; Write-Output ('  could not rename ' + $f + ': ' + $_.Exception.Message) }");
            sb.AppendLine("  } else { Write-Output ('Not found, nothing to rename: ' + $f) }");
            sb.AppendLine("}");
            sb.AppendLine("} finally {");
            sb.AppendLine("foreach ($s in 'cryptsvc','bits','wuauserv') { if ($wasRunning[$s]) { try { Write-Output ('Starting ' + $s + '...'); Start-Service -Name $s -ErrorAction Stop } catch { $startFailed++; Write-Output ('  could not start ' + $s + ': ' + $_.Exception.Message) } } else { Write-Output ($s + ' was not running before, so it stays stopped.') } }");
            sb.AppendLine("foreach ($d in $dependents) { try { Write-Output ('Starting ' + $d + '...'); Start-Service -Name $d -ErrorAction Stop } catch { Write-Output ('  could not start ' + $d + ': ' + $_.Exception.Message) } }");
            sb.AppendLine("}");
            sb.AppendLine("if ($startFailed -gt 0) { Write-Output 'Some services could not be started. Start them from Services, or restart the PC when convenient.'; exit 2 }");
            sb.AppendLine("if ($failed -gt 0) { Write-Output 'Some folders could not be renamed.'; exit 1 } else { Write-Output 'Windows Update was reset. Check for updates again.' }");
            return sb.ToString();
        }

        /// <summary>The network reset steps: file, arguments, title.</summary>
        public static List<string[]> NetworkResetSteps()
        {
            string netsh = ProcessRunner.System32("netsh.exe");
            return new List<string[]>
            {
                new[] { netsh, "winsock reset", "Resetting Winsock" },
                new[] { netsh, "int ip reset", "Resetting TCP/IP" },
                new[] { ProcessRunner.System32("ipconfig.exe"), "/flushdns", "Clearing the DNS cache" },
            };
        }

        public static string FlushDnsPath { get { return ProcessRunner.System32("ipconfig.exe"); } }
        public const string FlushDnsArgs = "/flushdns";

        // ---- temporary files ----

        /// <summary>The folders Tweak Bench clears: the user's Temp folder and C:\Windows\Temp.</summary>
        public static List<string> TempFolders()
        {
            var list = new List<string>();
            try
            {
                string t = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
                if (Directory.Exists(t) && IsSafeTempRoot(t)) list.Add(t);
            }
            catch (Exception) { }
            try
            {
                string w = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                if (Directory.Exists(w) && IsSafeTempRoot(w) && !list.Contains(w, StringComparer.OrdinalIgnoreCase)) list.Add(w);
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>How long a file in the user's Temp folder must be untouched before it is cleared.</summary>
        public static readonly TimeSpan UserTempAge = TimeSpan.FromHours(24);
        /// <summary>C:\Windows\Temp keeps files longer: installers and servicing may still need them after a restart (Disk Cleanup uses 7 days).</summary>
        public static readonly TimeSpan WindowsTempAge = TimeSpan.FromDays(7);

        /// <summary>True for C:\Windows\Temp.</summary>
        public static bool IsWindowsTemp(string folder)
        {
            try { return string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        /// <summary>The age a file must have in <paramref name="folder"/> before it is cleared.</summary>
        public static TimeSpan AgeFor(string folder) { return IsWindowsTemp(folder) ? WindowsTempAge : UserTempAge; }

        /// <summary>A real folder (not a junction or symbolic link) named Temp or Tmp, or one under %LOCALAPPDATA%. An unusual TEMP setting never points the cleaner at documents.</summary>
        public static bool IsSafeTempRoot(string folder)
        {
            try
            {
                string full = Path.GetFullPath(folder).TrimEnd('\\');
                if ((new DirectoryInfo(full).Attributes & FileAttributes.ReparsePoint) != 0) return false;
                string leaf = Path.GetFileName(full);
                if (string.Equals(leaf, "Temp", StringComparison.OrdinalIgnoreCase) || string.Equals(leaf, "Tmp", StringComparison.OrdinalIgnoreCase)) return true;
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd('\\');
                return local.Length > 0 && full.StartsWith(local + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>Counts or clears every folder with its own age (24 hours; 7 days for C:\Windows\Temp) and adds the results together.</summary>
        public static TempCleanResult CleanTempFolders(IEnumerable<string> folders, DateTime nowUtc, bool delete, CancellationToken cancel)
        {
            var total = new TempCleanResult();
            foreach (string f in folders)
            {
                if (cancel.IsCancellationRequested) break;
                var r = CleanTemp(new[] { f }, nowUtc - AgeFor(f), delete, cancel);
                total.Files += r.Files; total.Bytes += r.Bytes; total.Skipped += r.Skipped;
            }
            return total;
        }

        /// <summary>
        /// Counts (<paramref name="delete"/> false) or deletes files in <paramref name="folders"/> last written before
        /// <paramref name="olderThanUtc"/>. Locked files and folders that cannot be read are skipped. Junctions and symbolic links are never
        /// followed. Empty folders that are old enough are removed after their files. Safe to cancel.
        /// </summary>
        public static TempCleanResult CleanTemp(IEnumerable<string> folders, DateTime olderThanUtc, bool delete, CancellationToken cancel)
        {
            var result = new TempCleanResult();
            foreach (string root in folders)
            {
                if (cancel.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                // Never act on a drive root or a very short path by mistake.
                string full = Path.GetFullPath(root);
                // A root that is itself a junction or symbolic link would send the cleaning somewhere else.
                if ((new DirectoryInfo(full).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (full.Length < 8 || string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase)) continue;
                Walk(new DirectoryInfo(full), olderThanUtc, delete, result, cancel, true);
            }
            return result;
        }

        static bool Walk(DirectoryInfo dir, DateTime olderThanUtc, bool delete, TempCleanResult result, CancellationToken cancel, bool isRoot)
        {
            bool empty = true;
            FileInfo[] files;
            DirectoryInfo[] dirs;
            try { files = dir.GetFiles(); dirs = dir.GetDirectories(); }
            catch (Exception) { result.Skipped++; return false; }

            foreach (FileInfo file in files)
            {
                if (cancel.IsCancellationRequested) return false;
                try
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) { empty = false; continue; }
                    if (file.LastWriteTimeUtc >= olderThanUtc || file.CreationTimeUtc >= olderThanUtc) { empty = false; continue; }
                    long size = file.Length;
                    if (delete)
                    {
                        if ((file.Attributes & FileAttributes.ReadOnly) != 0) file.Attributes &= ~FileAttributes.ReadOnly;
                        file.Delete();
                    }
                    result.Bytes += size;
                    result.Files++;
                }
                catch (Exception) { result.Skipped++; empty = false; }
            }
            foreach (DirectoryInfo sub in dirs)
            {
                if (cancel.IsCancellationRequested) return false;
                try
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) { empty = false; continue; }
                    bool subEmpty = Walk(sub, olderThanUtc, delete, result, cancel, false);
                    if (subEmpty && sub.LastWriteTimeUtc < olderThanUtc)
                    {
                        if (delete) sub.Delete(false);
                    }
                    else empty = false;
                }
                catch (Exception) { result.Skipped++; empty = false; }
            }
            return empty && !isRoot;
        }

        /// <summary>"12 files, 340 MB" and the skip count, for the result message.</summary>
        public static string DescribeTemp(TempCleanResult r, bool deleted)
        {
            string text = Cmd.Plural(r.Files, "file", "files") + ", " + Cmd.FormatSize(r.Bytes);
            if (deleted) text = "Freed " + Cmd.FormatSize(r.Bytes) + " (" + Cmd.Plural(r.Files, "file", "files") + " removed)";
            if (r.Skipped > 0) text += ". " + Cmd.Plural(r.Skipped, "item was", "items were") + " in use or protected and skipped";
            return text + ".";
        }
    }
}
