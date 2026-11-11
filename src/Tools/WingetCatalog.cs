using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TweakBench.Tools
{
    public sealed class WingetApp
    {
        /// <summary>winget package identifier (case-insensitive).</summary>
        public string Id;
        public string Name;
        public string Description;
        public string Category;
    }

    /// <summary>One row of a winget list / upgrade table.</summary>
    public sealed class WingetRow
    {
        public string Name = "";
        public string Id = "";
        public string Version = "";
        public string Available = "";
        public string Source = "";
    }

    /// <summary>The curated winget app list plus winget output parsers and command builders. No UI in here.</summary>
    public static class WingetCatalog
    {
        public const string Browsers = "Browsers";
        public const string Utilities = "Utilities";
        public const string Development = "Development";
        public const string Media = "Media & creative";
        public const string Gaming = "Gaming";
        public const string Communication = "Communication";
        public const string Productivity = "Productivity & office";
        public const string Security = "Security & privacy";

        public static readonly string[] Categories = { Browsers, Utilities, Development, Media, Gaming, Communication, Productivity, Security };

        static WingetApp A(string cat, string id, string name, string description)
        {
            return new WingetApp { Category = cat, Id = id, Name = name, Description = description };
        }

        // Every id below was checked with: winget show --id <id> -e --source winget --disable-interactivity
        public static readonly List<WingetApp> All = new List<WingetApp>
        {
            A(Browsers, "Google.Chrome", "Google Chrome", "The most used web browser."),
            A(Browsers, "Mozilla.Firefox", "Mozilla Firefox", "Open-source browser with strong privacy settings."),
            A(Browsers, "Brave.Brave", "Brave", "Chromium browser that blocks ads and trackers."),
            A(Browsers, "Vivaldi.Vivaldi", "Vivaldi", "Customisable browser with tab stacks and built-in tools."),
            A(Browsers, "Opera.Opera", "Opera", "Browser with a built-in sidebar, VPN and ad blocker."),
            A(Browsers, "Opera.OperaGX", "Opera GX", "Opera for gamers, with limits for memory and network use."),
            A(Browsers, "TorProject.TorBrowser", "Tor Browser", "Routes browsing through the Tor network for anonymity."),
            A(Browsers, "LibreWolf.LibreWolf", "LibreWolf", "A Firefox build hardened for privacy."),

            A(Utilities, "7zip.7zip", "7-Zip", "Opens and creates ZIP, 7z, RAR and many other archives."),
            A(Utilities, "Microsoft.PowerToys", "PowerToys", "Microsoft's power-user tools: FancyZones, quick launcher, text extractor."),
            A(Utilities, "voidtools.Everything", "Everything", "Finds any file on your PC instantly by name."),
            A(Utilities, "Notepad++.Notepad++", "Notepad++", "A fast text and code editor."),
            A(Utilities, "WinDirStat.WinDirStat", "WinDirStat", "Shows what is using your disk space as coloured blocks."),
            A(Utilities, "AntibodySoftware.WizTree", "WizTree", "A very fast disk space analyser."),
            A(Utilities, "ShareX.ShareX", "ShareX", "Screenshots, screen recording and quick sharing."),
            A(Utilities, "CPUID.CPU-Z", "CPU-Z", "Shows details about your processor, memory and motherboard."),
            A(Utilities, "REALiX.HWiNFO", "HWiNFO", "Detailed hardware information and live sensor readings."),
            A(Utilities, "TechPowerUp.GPU-Z", "GPU-Z", "Shows details and sensors of your graphics card."),
            A(Utilities, "Rufus.Rufus", "Rufus", "Makes bootable USB drives from ISO files."),
            A(Utilities, "Ventoy.Ventoy", "Ventoy", "One USB drive that boots many ISO files."),
            A(Utilities, "Balena.Etcher", "balenaEtcher", "Writes disk images to USB drives and SD cards."),
            A(Utilities, "AutoHotkey.AutoHotkey", "AutoHotkey", "Scripts for shortcuts, text expansion and automation."),
            A(Utilities, "Microsoft.Sysinternals.ProcessExplorer", "Process Explorer", "A far more detailed Task Manager."),
            A(Utilities, "BleachBit.BleachBit", "BleachBit", "Cleans cache, cookies and temporary files from many apps."),
            A(Utilities, "WinSCP.WinSCP", "WinSCP", "Transfers files over SFTP, SCP and FTP."),
            A(Utilities, "PuTTY.PuTTY", "PuTTY", "SSH and serial terminal."),
            A(Utilities, "Klocman.BulkCrapUninstaller", "Bulk Crap Uninstaller", "Removes many programs at once and cleans their leftovers."),
            A(Utilities, "RevoUninstaller.RevoUninstaller", "Revo Uninstaller", "Uninstalls programs and finds the files they leave behind."),

            A(Development, "Git.Git", "Git", "Version control, with Git Bash."),
            A(Development, "Microsoft.VisualStudioCode", "Visual Studio Code", "Popular free code editor with thousands of extensions."),
            A(Development, "GitHub.cli", "GitHub CLI", "Use GitHub from the command line."),
            A(Development, "GitHub.GitHubDesktop", "GitHub Desktop", "A friendly Git client for GitHub."),
            A(Development, "Python.Python.3.13", "Python 3.13", "The Python programming language."),
            A(Development, "OpenJS.NodeJS.LTS", "Node.js (LTS)", "JavaScript runtime, long-term support version."),
            A(Development, "Microsoft.WindowsTerminal", "Windows Terminal", "Tabbed terminal for PowerShell, Command Prompt and WSL."),
            A(Development, "Microsoft.PowerShell", "PowerShell 7", "The modern, cross-platform PowerShell."),
            A(Development, "Docker.DockerDesktop", "Docker Desktop", "Runs containers on your PC."),
            A(Development, "JetBrains.Toolbox", "JetBrains Toolbox", "Installs and updates JetBrains IDEs such as PyCharm."),
            A(Development, "Postman.Postman", "Postman", "Builds and tests web APIs."),
            A(Development, "Rustlang.Rustup", "Rust (rustup)", "The Rust toolchain installer."),
            A(Development, "GoLang.Go", "Go", "The Go programming language."),
            A(Development, "EclipseAdoptium.Temurin.21.JDK", "Java 21 (Temurin)", "A free Java development kit."),
            A(Development, "Microsoft.DotNet.SDK.10", ".NET SDK 10 (LTS)", "Build and run .NET apps."),
            A(Development, "WinMerge.WinMerge", "WinMerge", "Compares and merges files and folders."),

            A(Media, "VideoLAN.VLC", "VLC media player", "Plays almost any video or audio file."),
            A(Media, "clsid2.mpc-hc", "MPC-HC", "A lightweight classic video player."),
            A(Media, "OBSProject.OBSStudio", "OBS Studio", "Screen recording and live streaming."),
            A(Media, "Audacity.Audacity", "Audacity", "Records and edits audio."),
            A(Media, "HandBrake.HandBrake", "HandBrake", "Converts video from one format to another."),
            A(Media, "Gyan.FFmpeg", "FFmpeg", "Command-line tool for converting audio and video."),
            A(Media, "yt-dlp.yt-dlp", "yt-dlp", "Command-line video and audio downloader."),
            A(Media, "Spotify.Spotify", "Spotify", "Music streaming."),
            A(Media, "GIMP.GIMP.3", "GIMP", "Free photo editor."),
            A(Media, "Inkscape.Inkscape", "Inkscape", "Free vector graphics editor."),
            A(Media, "BlenderFoundation.Blender", "Blender", "3D modelling, animation and video editing."),
            A(Media, "KDE.Krita", "Krita", "Digital painting."),
            A(Media, "XnSoft.XnViewMP", "XnView MP", "Fast image viewer and converter."),

            A(Gaming, "Valve.Steam", "Steam", "The largest PC game store and launcher."),
            A(Gaming, "EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Epic's store and free weekly games."),
            A(Gaming, "GOG.Galaxy", "GOG Galaxy", "DRM-free games and a launcher that combines libraries."),
            A(Gaming, "Blizzard.BattleNet", "Battle.net", "Launcher for Blizzard games."),
            A(Gaming, "ElectronicArts.EADesktop", "EA app", "Launcher for Electronic Arts games."),
            A(Gaming, "Ubisoft.Connect", "Ubisoft Connect", "Launcher for Ubisoft games."),
            A(Gaming, "Playnite.Playnite", "Playnite", "Open-source launcher that puts every game library in one place."),
            A(Gaming, "Discord.Discord", "Discord", "Voice and text chat for gaming communities."),

            A(Communication, "Zoom.Zoom", "Zoom", "Video meetings."),
            A(Communication, "SlackTechnologies.Slack", "Slack", "Team chat."),
            A(Communication, "Microsoft.Teams", "Microsoft Teams", "Chat and meetings for work and school."),
            A(Communication, "Telegram.TelegramDesktop", "Telegram", "Fast messaging with large groups."),
            A(Communication, "OpenWhisperSystems.Signal", "Signal", "Private, end-to-end encrypted messaging."),
            A(Communication, "Element.Element", "Element", "Chat on the open Matrix network."),
            A(Communication, "Mozilla.Thunderbird", "Thunderbird", "Free email client."),

            A(Productivity, "TheDocumentFoundation.LibreOffice", "LibreOffice", "Free office suite: documents, spreadsheets and slides."),
            A(Productivity, "Obsidian.Obsidian", "Obsidian", "Notes stored as plain files, linked together."),
            A(Productivity, "Notion.Notion", "Notion", "Notes, wikis and tasks in one workspace."),
            A(Productivity, "Joplin.Joplin", "Joplin", "Open-source notes with sync."),
            A(Productivity, "Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", "Reads and fills in PDF files."),
            A(Productivity, "SumatraPDF.SumatraPDF", "SumatraPDF", "A very light PDF and e-book reader."),
            A(Productivity, "Dropbox.Dropbox", "Dropbox", "Cloud file sync."),
            A(Productivity, "Google.GoogleDrive", "Google Drive", "Cloud file sync from Google."),
            A(Productivity, "DigitalScholar.Zotero", "Zotero", "Collects and cites research sources."),
            A(Productivity, "calibre.calibre", "calibre", "Manages and converts e-books."),
            A(Productivity, "qBittorrent.qBittorrent", "qBittorrent", "Open-source torrent client without ads."),

            A(Security, "Bitwarden.Bitwarden", "Bitwarden", "Password manager."),
            A(Security, "KeePassXCTeam.KeePassXC", "KeePassXC", "Password manager that keeps your passwords in a local file."),
            A(Security, "Proton.ProtonPass", "Proton Pass", "Password manager with hide-my-email aliases."),
            A(Security, "Proton.ProtonVPN", "Proton VPN", "VPN with a free plan and no data limits."),
            A(Security, "MullvadVPN.MullvadVPN", "Mullvad VPN", "VPN that needs no account details."),
            A(Security, "WireGuard.WireGuard", "WireGuard", "Fast, simple VPN tunnels you configure yourself."),
            A(Security, "Tailscale.Tailscale", "Tailscale", "Private network between your own devices."),
            A(Security, "Cloudflare.Warp", "Cloudflare WARP", "Encrypts your traffic to Cloudflare's network."),
            A(Security, "Malwarebytes.Malwarebytes", "Malwarebytes", "On-demand scanner for malware and unwanted programs."),
            A(Security, "Cryptomator.Cryptomator", "Cryptomator", "Encrypts files before they reach cloud storage."),
            A(Security, "IDRIX.VeraCrypt", "VeraCrypt", "Encrypts whole drives and creates encrypted containers."),
            A(Security, "WiresharkFoundation.Wireshark", "Wireshark", "Looks inside network traffic."),
            A(Security, "Insecure.Nmap", "Nmap", "Scans networks for devices and open ports."),
            A(Security, "OO-Software.ShutUp10", "O&O ShutUp10++", "Switches off Windows telemetry and privacy settings."),
        };

        public static WingetApp Find(string id)
        {
            return All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        static readonly Regex IdPattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{1,127}$", RegexOptions.Compiled);

        /// <summary>True for text shaped like a winget package identifier ("Vendor.Package").</summary>
        public static bool LooksLikeId(string id) { return id != null && IdPattern.IsMatch(id) && id.Contains("."); }

        const string Agreements = " --accept-package-agreements --accept-source-agreements --disable-interactivity";

        static string CheckId(string id)
        {
            if (!IdPattern.IsMatch(id ?? "")) throw new ArgumentException("Not a valid winget id: " + id);
            return id;
        }

        public static string InstallArgs(string id) { return "install --id " + CheckId(id) + " -e --source winget" + Agreements; }
        public static string UpgradeOneArgs(string id) { return "upgrade --id " + CheckId(id) + " -e --source winget" + Agreements; }
        public static string UpgradeAllArgs() { return "upgrade --all --source winget" + Agreements; }
        /// <summary>Lists available updates. No agreement flags: Tweak Bench accepts terms only when the user chooses to install.</summary>
        public static string UpgradeListArgs() { return "upgrade --source winget --disable-interactivity"; }
        public static string ListArgs() { return "list --source winget --disable-interactivity"; }
        public static string ExportArgs(string file) { return "export -o " + Cmd.Arg(file) + " --source winget --disable-interactivity"; }

        // ---- parsers ----

        /// <summary>Package ids from the JSON written by "winget export" (case-insensitive set).</summary>
        public static HashSet<string> ParseExport(string json)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return ids;
            foreach (var root in Cmd.JsonObjects(json)) CollectIds(root, ids, 0);
            return ids;
        }

        // Looks at every object and array below the root so the exact nesting (Sources, Packages) does not matter.
        static void CollectIds(object node, HashSet<string> ids, int depth)
        {
            if (node == null || depth > 8) return;
            var dict = node as Dictionary<string, object>;
            if (dict != null)
            {
                string id = Cmd.Str(dict, "PackageIdentifier");
                if (id.Length > 0) ids.Add(id);
                foreach (var value in dict.Values) CollectIds(value, ids, depth + 1);
                return;
            }
            var items = node as System.Collections.IEnumerable;
            if (items != null && !(node is string)) foreach (object item in items) CollectIds(item, ids, depth + 1);
        }

        static readonly Regex IdToken = new Regex(@"(?<=^|\s)[A-Za-z0-9][A-Za-z0-9_+\-]*(?:\.[A-Za-z0-9_+\-]+)+(?=\s|$)", RegexOptions.Compiled);
        static readonly Regex HeaderId = new Regex(@"(?<=^|\s)Id(?=\s)", RegexOptions.Compiled);

        /// <summary>
        /// Parses the table printed by "winget list" or "winget upgrade": a header line (Name, Id, Version, optionally Available and
        /// Source), a dashed line, then one row per package aligned to the header columns. Footer text ("3 upgrades available.") and
        /// the second table winget prints for packages needing explicit targeting are handled.
        /// </summary>
        public static List<WingetRow> ParseTable(string output)
        {
            var rows = new List<WingetRow>();
            var lines = Cmd.Lines(output);
            int idCol = -1, verCol = -1, availCol = -1, srcCol = -1;
            bool inTable = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                // winget may start the first line with spinner characters; a header line has Name, Id and Version.
                if (!inTable || line.TrimStart().StartsWith("Name", StringComparison.Ordinal))
                {
                    var m = HeaderId.Match(line);
                    int name = line.IndexOf("Name", StringComparison.Ordinal);
                    int ver = line.IndexOf("Version", StringComparison.Ordinal);
                    if (m.Success && name >= 0 && ver > m.Index && i + 1 < lines.Count && lines[i + 1].TrimStart().StartsWith("--", StringComparison.Ordinal))
                    {
                        // Columns are relative to the start of "Name" so leading spinner debris cannot shift them.
                        idCol = m.Index - name; verCol = ver - name;
                        availCol = line.IndexOf("Available", StringComparison.Ordinal); if (availCol >= 0) availCol -= name;
                        srcCol = line.IndexOf("Source", StringComparison.Ordinal); if (srcCol >= 0) srcCol -= name;
                        inTable = true;
                        i++; // skip the dashed line
                        continue;
                    }
                }
                if (!inTable) continue;
                if (string.IsNullOrWhiteSpace(line)) { inTable = false; continue; }
                if (Regex.IsMatch(line, @"^\s*\d+\s+\S.*(available|installed)", RegexOptions.IgnoreCase) && line.TrimEnd().EndsWith(".", StringComparison.Ordinal)) { inTable = false; continue; }
                if (line.Length <= idCol) { inTable = false; continue; }

                var row = new WingetRow();
                row.Name = Slice(line, 0, idCol);
                row.Id = Slice(line, idCol, verCol);
                row.Version = Slice(line, verCol, availCol >= 0 ? availCol : (srcCol >= 0 ? srcCol : -1));
                if (availCol >= 0) row.Available = Slice(line, availCol, srcCol);
                if (srcCol >= 0) row.Source = Slice(line, srcCol, -1);
                if (!LooksLikeId(row.Id) || row.Id.Contains(" "))
                {
                    // Wide characters shift the columns: find the id token nearest the id column instead.
                    Match best = null; int bestDistance = int.MaxValue;
                    foreach (Match t in IdToken.Matches(line))
                    {
                        int distance = Math.Abs(t.Index - idCol);
                        if (distance < bestDistance && t.Index > 0) { best = t; bestDistance = distance; }
                    }
                    if (best == null) continue;
                    row.Id = best.Value;
                    string tail = line.Substring(best.Index + best.Length).Trim();
                    string[] parts = tail.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    row.Name = line.Substring(0, best.Index).Trim();
                    row.Version = parts.Length > 0 ? parts[0] : "";
                    row.Available = availCol >= 0 && parts.Length > 1 ? parts[1] : "";
                    row.Source = srcCol >= 0 && parts.Length > 2 ? parts[2] : "";
                }
                rows.Add(row);
            }
            return rows;
        }

        static string Slice(string line, int start, int end)
        {
            if (start >= line.Length) return "";
            if (end < 0 || end > line.Length) end = line.Length;
            if (end < start) return "";
            return line.Substring(start, end - start).Trim();
        }

        /// <summary>The ids in a "winget list" table.</summary>
        public static HashSet<string> ParseListIds(string output)
        {
            return new HashSet<string>(ParseTable(output).Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The rows of a "winget upgrade" table that really offer a newer version.</summary>
        public static List<WingetRow> ParseUpgrades(string output)
        {
            return ParseTable(output).Where(r => r.Available.Length > 0 && !string.Equals(r.Available, r.Version, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>True when winget says it cannot continue until the user accepts the source terms.</summary>
        public static bool NeedsSourceAgreement(string output)
        {
            return output != null && output.IndexOf("source", StringComparison.OrdinalIgnoreCase) >= 0 && output.IndexOf("agreement", StringComparison.OrdinalIgnoreCase) >= 0
                && (output.IndexOf("accept", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("agree", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>True when the exit code means the app is installed (including "restart to finish").</summary>
        public static bool IsInstallSuccess(int code)
        {
            uint u = (uint)code;
            return u == 0 || u == 0x8A150061u || u == 0x8A150109u;
        }

        /// <summary>True when an update run ended with nothing wrong ("no newer version" counts).</summary>
        public static bool IsUpdateSuccess(int code)
        {
            return IsInstallSuccess(code) || (uint)code == 0x8A15002Bu;
        }

        /// <summary>A plain sentence for a winget exit code.</summary>
        public static string DescribeExit(int code)
        {
            switch ((uint)code)
            {
                case 0: return "Installed.";
                case 0x8A150061u: return "Already installed.";
                case 0x8A15002Bu: return "No newer version is available.";
                case 0x8A150010u: return "There is no installer for this PC.";
                case 0x8A150109u: return "Installed. Restart your PC when convenient to finish; Tweak Bench will not restart it for you.";
                case 0x8A15010Au: return "Windows needs a restart before this can install. Restart your PC, then try again.";
                case 0x8A15010Bu: return "The installer asked Windows to restart.";
                case 0x8A15002Cu: return "Some apps could not be updated. See the lines above.";
                default: return "winget stopped with code 0x" + ((uint)code).ToString("X8") + ".";
            }
        }
    }
}
