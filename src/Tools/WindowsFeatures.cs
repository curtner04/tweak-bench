using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TweakBench.Tools
{
    public enum FeatureState
    {
        /// <summary>Not yet read, or the read failed.</summary>
        Unknown,
        On,
        Off,
        /// <summary>Windows does not list this feature on this PC (older build, other edition).</summary>
        NotOnThisPc,
        /// <summary>Listed, but its files are removed from the PC (Windows would download them when turned on).</summary>
        Removed,
        /// <summary>Turned on or off, waiting for a restart.</summary>
        Pending
    }

    public enum FeatureAdvice { None, KeepOff, KeepOn }

    public sealed class WindowsFeature
    {
        /// <summary>Exact feature name for DISM.</summary>
        public string Name;
        public string Title;
        public string Description;
        /// <summary>Plain edition or download note, may be empty.</summary>
        public string Note;
        /// <summary>Only on Windows Pro, Enterprise and Education (not Home).</summary>
        public bool NeedsPro;
        /// <summary>Also turn on the features this one depends on (DISM /all) when enabling.</summary>
        public bool WithParents;
        public FeatureAdvice Advice;
        /// <summary>Why <see cref="Advice"/> is given.</summary>
        public string AdviceText;
        public string Group;
        /// <summary>What stops working when this feature is turned off, for the confirmation. Empty when nothing common depends on it.</summary>
        public string BreaksIfOff = "";
    }

    /// <summary>The curated optional Windows features, how to read their state and how to switch them. No UI in here.</summary>
    public static class WindowsFeatures
    {
        public const string GroupVirtual = "Virtual machines and Linux";
        public const string GroupDeveloper = "Developer and legacy runtimes";
        public const string GroupOld = "Old, risky or rarely needed";
        public const string GroupMedia = "Media and printing";

        public static readonly string[] Groups = { GroupVirtual, GroupDeveloper, GroupMedia, GroupOld };

        static WindowsFeature F(string group, string name, string title, string description, string note = "", bool pro = false, bool parents = false,
            FeatureAdvice advice = FeatureAdvice.None, string adviceText = "", string breaks = "")
        {
            return new WindowsFeature { Group = group, Name = name, Title = title, Description = description, Note = note, NeedsPro = pro, WithParents = parents, Advice = advice, AdviceText = adviceText, BreaksIfOff = breaks };
        }

        // Feature names are the Win32_OptionalFeature / DISM names (dism /online /get-features) on Windows 11 26H2.
        public static readonly List<WindowsFeature> All = new List<WindowsFeature>
        {
            F(GroupVirtual, "Containers-DisposableClientVM", "Windows Sandbox", "A throwaway Windows you can open to try a risky file or app. Everything inside vanishes when you close it.",
                "Needs Windows Pro, Enterprise or Education, and virtualisation turned on in the PC's firmware.", pro: true, parents: true),
            F(GroupVirtual, "Microsoft-Hyper-V-All", "Hyper-V", "Microsoft's virtual machine platform with Hyper-V Manager.",
                "Needs Windows Pro, Enterprise or Education. Some games with anti-cheat and older virtual machine programs do not like Hyper-V being on.", pro: true, parents: true, breaks: "Virtual machines you made in Hyper-V are kept but cannot run while it is off."),
            F(GroupVirtual, "VirtualMachinePlatform", "Virtual Machine Platform", "The base layer WSL 2, Windows Sandbox and Android apps use. Works on Windows Home too.", parents: true, breaks: "WSL 2, Windows Sandbox, Docker Desktop and Android emulators that use it stop working."),
            F(GroupVirtual, "HypervisorPlatform", "Windows Hypervisor Platform", "Lets other virtual machine programs (VirtualBox, VMware, Android emulators) share Hyper-V's acceleration.", parents: true, breaks: "VirtualBox, VMware and Android emulators that share Hyper-V acceleration fall back to slower modes or stop working."),
            F(GroupVirtual, "Microsoft-Windows-Subsystem-Linux", "Windows Subsystem for Linux", "Runs Linux command-line tools and distributions such as Ubuntu inside Windows.",
                "Install a distribution afterwards with 'wsl --install'.", parents: true, breaks: "Linux distributions in WSL stop starting, and Docker Desktop loses its WSL backend. Your Linux files stay on disk."),
            F(GroupVirtual, "Containers", "Windows containers", "Lets Docker run Windows containers.", "Most people only need Linux containers, which use WSL.", parents: true, breaks: "Docker Desktop cannot run Windows containers."),

            F(GroupDeveloper, "NetFx3", ".NET Framework 3.5", "Needed by older apps and games built for .NET 2.0 to 3.5.",
                "Windows downloads the files from Windows Update, so the PC must be online.", parents: true),
            F(GroupDeveloper, "IIS-WebServerRole", "Internet Information Services (IIS)", "Microsoft's web server, for testing websites on your own PC.",
                "Leave it off if you do not host websites: it opens network ports.", parents: true),
            F(GroupDeveloper, "DirectPlay", "DirectPlay (old games)", "Lets games from the 1990s and early 2000s run their multiplayer code.", parents: true),

            F(GroupMedia, "WindowsMediaPlayer", "Windows Media Player (legacy)", "The classic Windows Media Player, not the newer Media Player app.", "Removing it does not remove the newer Media Player."),
            F(GroupMedia, "Printing-PrintToPDFServices-Features", "Microsoft Print to PDF", "Adds a printer that saves any document as a PDF.", "Keep it on unless you use another PDF printer.",
                advice: FeatureAdvice.KeepOn, adviceText: "Keep on: it is how most apps save PDFs."),
            F(GroupMedia, "Printing-XPSServices-Features", "XPS printing", "Lets Windows print to and view XPS files.", "Rarely needed since PDF replaced XPS."),

            F(GroupOld, "SMB1Protocol", "SMB 1.0 file sharing", "A 1980s way of sharing files that very old NAS drives and printers still use.",
                "It has serious security holes that ransomware has used. Turn it on only for a device that cannot be updated.", advice: FeatureAdvice.KeepOff,
                adviceText: "Recommended off. Windows already keeps it off on new installs."),
            F(GroupOld, "MicrosoftWindowsPowerShellV2Root", "Windows PowerShell 2.0", "An old PowerShell version kept for ancient scripts.",
                "Attackers use it to dodge modern PowerShell logging. Newer Windows builds remove it.", advice: FeatureAdvice.KeepOff, adviceText: "Recommended off."),
            F(GroupOld, "TelnetClient", "Telnet client", "The telnet command for plain-text connections to old equipment.", "Sends passwords unencrypted, so use SSH where you can."),
            F(GroupOld, "TFTP", "TFTP client", "The tftp command for sending files to routers and network devices.", "Plain text and without a password. Turn it on only when you need it."),
            F(GroupOld, "SimpleTCP", "Simple TCP/IP services", "Tiny echo and quote servers from the 1990s.", "No modern use. Leave it off.", advice: FeatureAdvice.KeepOff, adviceText: "Recommended off."),
            F(GroupOld, "ServicesForNFS-ClientOnly", "NFS client", "Lets Windows open shared folders from Linux and NAS devices that use NFS.", "Needs Windows Pro, Enterprise or Education.", pro: true, parents: true),
            F(GroupOld, "WorkFolders-Client", "Work Folders client", "Syncs files with a company Work Folders server.", "Only useful if your workplace runs Work Folders."),
        };

        public static WindowsFeature Find(string name)
        {
            return All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Windows Home editions (EditionID "Core", "CoreN", "CoreSingleLanguage", "CoreCountrySpecific").</summary>
        public static bool IsHome(string edition)
        {
            return edition != null && edition.StartsWith("Core", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A short reason the feature cannot be switched on this edition, or null.</summary>
        public static string EditionProblem(WindowsFeature f, string edition)
        {
            if (f.NeedsPro && IsHome(edition)) return "Needs Windows Pro. This PC runs Windows Home.";
            return null;
        }

        // ---- reading state ----

        /// <summary>PowerShell that prints every optional feature as a JSON array of {Name, InstallState}. Works without administrator rights.</summary>
        public static string ReadScript
        {
            get { return "ConvertTo-Json -Compress -InputObject @(Get-CimInstance -ClassName Win32_OptionalFeature | Select-Object Name,InstallState)"; }
        }

        /// <summary>Win32_OptionalFeature.InstallState: 1 enabled, 2 disabled, 3 absent (files removed), 4 unknown.</summary>
        public static FeatureState FromInstallState(int state)
        {
            switch (state)
            {
                case 1: return FeatureState.On;
                case 2: return FeatureState.Off;
                case 3: return FeatureState.Removed;
                default: return FeatureState.Unknown;
            }
        }

        /// <summary>Parses <see cref="ReadScript"/> output into feature name → state (case-insensitive).</summary>
        public static Dictionary<string, FeatureState> ParseCim(string json)
        {
            var map = new Dictionary<string, FeatureState>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Cmd.JsonObjects(json))
            {
                string name = Cmd.Str(o, "Name");
                if (name.Length > 0) map[name] = FromInstallState(Cmd.Int(o, "InstallState", 4));
            }
            return map;
        }

        /// <summary>Parses "dism /online /get-features /format:table" (needs administrator rights).</summary>
        public static Dictionary<string, FeatureState> ParseDismTable(string output)
        {
            var map = new Dictionary<string, FeatureState>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in Cmd.Lines(output))
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                string name = line.Substring(0, bar).Trim();
                string state = line.Substring(bar + 1).Trim();
                if (name.Length == 0 || name.StartsWith("-", StringComparison.Ordinal) || name.StartsWith("Feature Name", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOf(' ') >= 0) continue;
                map[name] = ParseDismState(state);
            }
            return map;
        }

        public static FeatureState ParseDismState(string state)
        {
            state = (state ?? "").Trim();
            if (state.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0) return FeatureState.Pending;
            if (state.IndexOf("Payload Removed", StringComparison.OrdinalIgnoreCase) >= 0) return FeatureState.Removed;
            if (state.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) return FeatureState.On;
            if (state.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return FeatureState.Off;
            return FeatureState.Unknown;
        }

        /// <summary>The state of <paramref name="name"/> in a parsed map, or <see cref="FeatureState.NotOnThisPc"/> when Windows does not list it.</summary>
        public static FeatureState Lookup(Dictionary<string, FeatureState> map, string name)
        {
            FeatureState s;
            return map != null && map.TryGetValue(name, out s) ? s : FeatureState.NotOnThisPc;
        }

        // ---- switching ----

        static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,127}$", RegexOptions.Compiled);

        public static string EnableArgs(WindowsFeature f) { return "/online /enable-feature /featurename:" + Safe(f.Name) + (f.WithParents ? " /all" : "") + " /norestart"; }
        public static string DisableArgs(WindowsFeature f) { return "/online /disable-feature /featurename:" + Safe(f.Name) + " /norestart"; }

        static string Safe(string name)
        {
            if (!SafeName.IsMatch(name ?? "")) throw new ArgumentException("Not a valid feature name: " + name);
            return name;
        }

        /// <summary>DISM exit codes: 0 done, 3010 done but a restart is needed. Anything else failed.</summary>
        public static bool IsSuccess(int exitCode) { return exitCode == 0 || exitCode == 3010; }
        public static bool NeedsRestart(int exitCode) { return exitCode == 3010; }

        public static string DescribeExit(int exitCode)
        {
            switch (exitCode)
            {
                case 0: return "Done.";
                case 3010: return "Done. Restart the PC when convenient to finish.";
                case 740: return "Needs administrator rights.";
                case 50: return "Windows does not support this feature on this PC.";
                case -2146498529: // 0x800F081F: source files not found
                case -2146498530: // 0x800F081E
                    return "Windows could not find the files. Check your internet connection and try again.";
                case 87: return "DISM did not understand the request.";
                default: return "DISM stopped with code " + exitCode + ".";
            }
        }
    }
}
