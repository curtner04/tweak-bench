using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TweakBench.Tools
{
    /// <summary>One removable inbox or Store app that comes with Windows 11 or with a typical PC.</summary>
    public sealed class BuiltInApp
    {
        /// <summary>Exact AppX package name (Get-AppxPackage -Name).</summary>
        public string Package;
        public string Name;
        /// <summary>One line: what the app is.</summary>
        public string Description;
        /// <summary>Completes "Keep it if you ...".</summary>
        public string KeepIf;
        public string Group;
    }

    /// <summary>A package as read from Get-AppxPackage.</summary>
    public sealed class InstalledAppx
    {
        public string Name = "";
        public string PackageFullName = "";
        public string PackageFamilyName = "";
        public bool NonRemovable;
        public bool IsFramework;
    }

    /// <summary>The curated list of apps Tweak Bench offers to remove, how to read what is installed and how to remove it.</summary>
    public static class BuiltInApps
    {
        public const string GroupMicrosoft = "Microsoft extras";
        public const string GroupXbox = "Xbox & games";
        public const string GroupCommunication = "Communication";
        public const string GroupPromo = "Promotional & manufacturer extras";

        public static readonly string[] Groups = { GroupMicrosoft, GroupCommunication, GroupXbox, GroupPromo };

        public static string GroupBlurb(string group)
        {
            switch (group)
            {
                case GroupMicrosoft: return "Small apps Microsoft adds to Windows. Most people use only one or two of them.";
                case GroupCommunication: return "Mail, chat and phone apps. Keep the one you actually use.";
                case GroupXbox: return "Gamers need some of these: the Xbox sign-in pieces are used by Store and Game Pass games.";
                case GroupPromo: return "Pre-installed offers and trial apps. Removing them frees space and reduces clutter.";
                default: return "";
            }
        }

        /// <summary>
        /// Package names (or name prefixes) that are never offered: frameworks, the Store, App Installer (winget), Windows Security,
        /// Settings, Edge and the shell packages Windows needs to show its desktop.
        /// </summary>
        public static readonly string[] ForbiddenPrefixes =
        {
            "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.Services.Store", "Microsoft.DesktopAppInstaller", "Microsoft.Winget",
            "Microsoft.SecHealthUI", "Microsoft.Windows.SecHealthUI", "windows.immersivecontrolpanel", "Microsoft.MicrosoftEdge", "Microsoft.Edge",
            "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.StartExperiencesApp",
            "Microsoft.Windows.CloudExperienceHost", "Microsoft.Windows.ContentDeliveryManager", "Microsoft.Windows.PeopleExperienceHost",
            "Microsoft.Windows.Search", "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl", "Microsoft.LockApp", "Microsoft.BioEnrollment",
            "Microsoft.CredDialogHost", "Microsoft.AsyncTextService", "Microsoft.Win32WebViewHost", "Microsoft.Windows.OOBE", "Microsoft.Windows.Apprep",
            "Microsoft.Windows.PinningConfirmationDialog", "Microsoft.Windows.CapturePicker", "Microsoft.Windows.PrintQueueActionCenter",
            "Microsoft.Windows.SecureAssessmentBrowser", "Microsoft.Windows.AssignedAccessLockApp", "Microsoft.Windows.XGpuEjectDialog",
            "Microsoft.Windows.ParentalControls", "Microsoft.Windows.NarratorQuickStart", "Microsoft.Windows.AugLoop", "Microsoft.ECApp",
            "Microsoft.UI.Xaml", "Microsoft.VCLibs", "Microsoft.NET.Native", "Microsoft.WindowsAppRuntime", "MicrosoftCorporationII.WinAppRuntime",
            "Microsoft.WidgetsPlatformRuntime", "Microsoft.AIFabric", "MicrosoftWindows.Client", "MicrosoftWindows.UndockedDevKit",
            "Windows.CBSPreview", "Windows.PrintDialog", "Microsoft.XboxGameCallableUI", "Microsoft.OneDriveSync", "Microsoft.MicrosoftEdgeDevToolsClient",
            "MicrosoftWindows.CrossDevice", "Microsoft.ApplicationCompatibilityEnhancements", "Microsoft.WindowsTerminal", "Microsoft.WindowsNotepad",
            "Microsoft.WindowsCalculator", "Microsoft.Windows.Photos", "Microsoft.ScreenSketch", "Microsoft.Paint"
        };

        public static bool IsForbidden(string package)
        {
            if (string.IsNullOrWhiteSpace(package)) return true;
            return ForbiddenPrefixes.Any(p => package.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        static BuiltInApp A(string package, string name, string description, string keepIf, string group)
        {
            return new BuiltInApp { Package = package, Name = name, Description = description, KeepIf = keepIf, Group = group };
        }

        // Package names were checked against Get-AppxPackage on a Windows 11 26H2 PC (Clipchamp, Bing Weather, Get Help, Solitaire,
        // Sticky Notes, Outlook, Power Automate, To Do, Alarms, Camera, Feedback Hub, Sound Recorder, Xbox pieces, Phone Link,
        // Media Player, Quick Assist, Dev Home, Bing Search) and against Microsoft's list of inbox apps
        // (https://learn.microsoft.com/windows/application-management/apps-in-windows-10) and the Remove-AppxPackage documentation
        // for the rest.
        public static readonly List<BuiltInApp> All = new List<BuiltInApp>
        {
            // ---- Microsoft extras ----
            A("Clipchamp.Clipchamp", "Clipchamp", "A simple video editor from Microsoft.", "edit videos with it.", GroupMicrosoft),
            A("Microsoft.BingWeather", "Weather", "Weather forecasts, also used by the taskbar weather.", "like the Weather app or the weather on your taskbar.", GroupMicrosoft),
            A("Microsoft.BingNews", "News", "News headlines from Microsoft Start.", "read news in it.", GroupMicrosoft),
            A("Microsoft.BingSearch", "Bing search in Windows", "Adds Bing web results to Windows search.", "want web results in the Start menu search.", GroupMicrosoft),
            A("Microsoft.Copilot", "Copilot", "Microsoft's AI assistant app.", "use Copilot to ask questions or write text.", GroupMicrosoft),
            A("Microsoft.GetHelp", "Get Help", "Opens Microsoft support chats and troubleshooters.", "contact Microsoft support from Windows.", GroupMicrosoft),
            A("Microsoft.Getstarted", "Tips", "Shows tips and a tour of Windows.", "want Windows tips.", GroupMicrosoft),
            A("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", "A launcher that advertises and opens Microsoft 365 apps.", "open your Office documents from it. Word and Excel are not removed.", GroupMicrosoft),
            A("Microsoft.Office.OneNote", "OneNote for Windows 10", "The older Store version of OneNote.", "keep notes in OneNote.", GroupMicrosoft),
            A("Microsoft.MicrosoftStickyNotes", "Sticky Notes", "Notes you stick on the desktop.", "use sticky notes.", GroupMicrosoft),
            A("Microsoft.Todos", "Microsoft To Do", "A to-do list that syncs with your Microsoft account.", "keep your task lists in To Do.", GroupMicrosoft),
            A("Microsoft.PowerAutomateDesktop", "Power Automate", "Automates repeated tasks with flows.", "build or run Power Automate flows.", GroupMicrosoft),
            A("Microsoft.WindowsFeedbackHub", "Feedback Hub", "Sends feedback and bug reports to Microsoft.", "report problems to Microsoft.", GroupMicrosoft),
            A("Microsoft.WindowsMaps", "Maps", "Offline and online maps.", "use Windows for maps or offline maps.", GroupMicrosoft),
            A("Microsoft.WindowsAlarms", "Clock (Alarms & Timers)", "Alarms, timers, stopwatch and world clock.", "use alarms, timers or focus sessions.", GroupMicrosoft),
            A("Microsoft.WindowsSoundRecorder", "Sound Recorder", "Records audio from a microphone.", "record voice memos.", GroupMicrosoft),
            A("Microsoft.WindowsCamera", "Camera", "Takes photos and video with a webcam.", "use a webcam from Windows (Teams and Zoom bring their own).", GroupMicrosoft),
            A("Microsoft.ZuneMusic", "Media Player", "Plays music and video files.", "play music or video with it. VLC is a good replacement.", GroupMicrosoft),
            A("Microsoft.ZuneVideo", "Movies & TV", "The older video player and movie store.", "play videos with it.", GroupMicrosoft),
            A("Microsoft.People", "People", "A contacts list for Mail and Calendar.", "use the older Mail app.", GroupMicrosoft),
            A("Microsoft.Whiteboard", "Whiteboard", "A shared drawing board for meetings.", "sketch together in Whiteboard.", GroupMicrosoft),
            A("Microsoft.Windows.DevHome", "Dev Home", "A dashboard for developers (retired by Microsoft).", "still use it for developer setup.", GroupMicrosoft),
            A("MicrosoftCorporationII.QuickAssist", "Quick Assist", "Lets someone help you remotely, or you help them.", "give or get remote help from family or support.", GroupMicrosoft),
            A("Microsoft.549981C3F5F10", "Cortana", "The retired voice assistant.", "never; Microsoft has retired it.", GroupMicrosoft),
            A("Microsoft.MixedReality.Portal", "Mixed Reality Portal", "Setup app for old Windows Mixed Reality headsets.", "own a Windows Mixed Reality headset.", GroupMicrosoft),
            A("Microsoft.Microsoft3DViewer", "3D Viewer", "Opens 3D model files.", "open 3D models with it.", GroupMicrosoft),
            A("Microsoft.MSPaint", "Paint 3D", "The 3D paint app. Classic Paint is not affected.", "create 3D scenes in Paint 3D.", GroupMicrosoft),
            A("Microsoft.Print3D", "Print 3D", "Sends models to a 3D printer.", "use a 3D printer through Windows.", GroupMicrosoft),

            // ---- Communication ----
            A("Microsoft.OutlookForWindows", "Outlook (new)", "The new Outlook mail app that replaces Mail and Calendar.", "read your mail in it. Outlook from Microsoft 365 is separate.", GroupCommunication),
            A("microsoft.windowscommunicationsapps", "Mail and Calendar", "The older Mail and Calendar apps.", "use these for mail or calendar.", GroupCommunication),
            A("MSTeams", "Microsoft Teams (personal)", "Chat and video calls with the Teams you sign in to with a personal account.", "chat or call with Teams.", GroupCommunication),
            A("Microsoft.SkypeApp", "Skype", "Calls and chats with Skype contacts.", "still use Skype.", GroupCommunication),
            A("Microsoft.YourPhone", "Phone Link", "Shows your phone's texts, photos and calls on the PC.", "link your phone to this PC.", GroupCommunication),

            // ---- Xbox & games ----
            A("Microsoft.GamingApp", "Xbox", "The Xbox app: Game Pass, friends and PC game installs.", "play Game Pass or Xbox games on this PC.", GroupXbox),
            A("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "The overlay opened with Win+G for capture and performance.", "record clips or use Win+G while gaming.", GroupXbox),
            A("Microsoft.Xbox.TCUI", "Xbox sign-in screens", "Pop-ups some games show for Xbox sign-in and friends.", "play any game that signs in with Xbox.", GroupXbox),
            A("Microsoft.XboxIdentityProvider", "Xbox identity provider", "Signs Store and Xbox games in to your Xbox account.", "play Microsoft Store or Game Pass games.", GroupXbox),
            A("Microsoft.XboxSpeechToTextOverlay", "Xbox speech to text", "Turns game chat voices into text.", "use game chat transcription.", GroupXbox),
            A("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", "Card games with ads.", "play Solitaire.", GroupXbox),
            A("king.com.CandyCrushSaga", "Candy Crush Saga", "A free puzzle game with purchases.", "play it.", GroupXbox),
            A("king.com.CandyCrushSodaSaga", "Candy Crush Soda Saga", "A free puzzle game with purchases.", "play it.", GroupXbox),

            // ---- Promotional and manufacturer extras ----
            A("Disney.37853FC22B2CE", "Disney+", "Shortcut to the Disney+ streaming service.", "subscribe to Disney+.", GroupPromo),
            A("4DF9E0F8.Netflix", "Netflix", "Shortcut to the Netflix streaming service.", "watch Netflix in this app.", GroupPromo),
            A("AmazonVideo.PrimeVideo", "Prime Video", "Shortcut to Amazon Prime Video.", "watch Prime Video in this app.", GroupPromo),
            A("SpotifyAB.SpotifyMusic", "Spotify", "Music streaming (the Store version).", "listen with this version of Spotify.", GroupPromo),
            A("BytedancePte.Ltd.TikTok", "TikTok", "Short videos.", "use TikTok on this PC.", GroupPromo),
            A("Facebook.Facebook", "Facebook", "Shortcut to Facebook.", "use Facebook through this app.", GroupPromo),
            A("Facebook.InstagramBeta", "Instagram", "Shortcut to Instagram.", "use Instagram through this app.", GroupPromo),
            A("7EE7776C.LinkedInforWindows", "LinkedIn", "Shortcut to LinkedIn.", "use LinkedIn through this app.", GroupPromo),
        };

        public static BuiltInApp Find(string package)
        {
            return All.FirstOrDefault(a => string.Equals(a.Package, package, StringComparison.OrdinalIgnoreCase));
        }

        // ---- reading what is installed ----

        /// <summary>PowerShell that prints every package of the current user as one JSON array.</summary>
        public static string ListScript
        {
            get
            {
                return "ConvertTo-Json -Compress -Depth 2 -InputObject @(Get-AppxPackage | Select-Object Name,PackageFullName,PackageFamilyName,NonRemovable,IsFramework)";
            }
        }

        /// <summary>Parses the JSON from <see cref="ListScript"/>: a single object (one package), an array, or nothing.</summary>
        public static List<InstalledAppx> ParseInstalled(string json)
        {
            var list = new List<InstalledAppx>();
            foreach (var o in Cmd.JsonObjects(json))
            {
                string name = Cmd.Str(o, "Name");
                if (name.Length == 0) continue;
                list.Add(new InstalledAppx
                {
                    Name = name,
                    PackageFullName = Cmd.Str(o, "PackageFullName"),
                    PackageFamilyName = Cmd.Str(o, "PackageFamilyName"),
                    NonRemovable = Cmd.Bool(o, "NonRemovable"),
                    IsFramework = Cmd.Bool(o, "IsFramework"),
                });
            }
            return list;
        }

        /// <summary>The catalog packages that are installed and removable, by package name (case-insensitive).</summary>
        public static HashSet<string> RemovableInstalled(IEnumerable<InstalledAppx> installed)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in installed)
                if (!p.NonRemovable && !p.IsFramework && Find(p.Name) != null && !IsForbidden(p.Name)) set.Add(p.Name);
            return set;
        }

        // ---- removing ----

        static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,127}$", RegexOptions.Compiled);

        /// <summary>
        /// PowerShell that removes the given packages for the current user, optionally also from the Windows image so new accounts do
        /// not get them (administrator). Prints one "RESULT|OK|SKIP|FAIL|package|message" line per package. Refuses names that are not
        /// in the catalog or are forbidden.
        /// </summary>
        public static string RemoveScript(IEnumerable<string> packages, bool alsoProvisioned)
        {
            var names = packages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string n in names)
            {
                if (!SafeName.IsMatch(n)) throw new ArgumentException("Not a valid package name: " + n);
                if (IsForbidden(n) || Find(n) == null) throw new ArgumentException("Tweak Bench does not remove this package: " + n);
            }
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference='Continue'");
            sb.AppendLine("$names = @(" + string.Join(",", names.Select(Cmd.PsQuote)) + ")");
            if (alsoProvisioned) sb.AppendLine("$prov = @(Get-AppxProvisionedPackage -Online)");
            sb.AppendLine("foreach ($n in $names) {");
            sb.AppendLine("  try {");
            sb.AppendLine("    $found = @(Get-AppxPackage -Name $n)");
            sb.AppendLine("    if ($found.Count -eq 0) { Write-Output ('RESULT|SKIP|' + $n + '|Not installed for you.') } else {");
            sb.AppendLine("      foreach ($p in $found) { Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop }");
            sb.AppendLine("      Write-Output ('RESULT|OK|' + $n + '|Removed for your account.')");
            sb.AppendLine("    }");
            if (alsoProvisioned)
            {
                sb.AppendLine("    foreach ($q in @($prov | Where-Object { $_.DisplayName -eq $n })) {");
                sb.AppendLine("      Remove-AppxProvisionedPackage -Online -PackageName $q.PackageName -ErrorAction Stop | Out-Null");
                sb.AppendLine("      Write-Output ('RESULT|OK|' + $n + '|New accounts will not get it.')");
                sb.AppendLine("    }");
            }
            sb.AppendLine("  } catch { Write-Output ('RESULT|FAIL|' + $n + '|' + ($_.Exception.Message -replace '[\\r\\n]+',' ')) }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public sealed class RemoveResult
        {
            public string Kind = "";      // OK, SKIP or FAIL
            public string Package = "";
            public string Message = "";
        }

        /// <summary>Reads a "RESULT|KIND|package|message" line; null for any other output.</summary>
        public static RemoveResult ParseResultLine(string line)
        {
            if (line == null || !line.StartsWith("RESULT|", StringComparison.Ordinal)) return null;
            string[] parts = line.Split(new[] { '|' }, 4);
            if (parts.Length < 4) return null;
            return new RemoveResult { Kind = parts[1], Package = parts[2], Message = parts[3] };
        }
    }
}
