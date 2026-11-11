using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TweakBench.Tools;

namespace TweakBench.Tests
{
    /// <summary>Tests for the tool pages' logic: catalogs, parsers and command builders. Nothing here touches the real PC.</summary>
    public static class ToolsTests
    {
        public static void Run(T t)
        {
            CommandLineQuoting(t);
            BuiltInAppCatalog(t);
            AppxParsing(t);
            AppxRemoveScript(t);
            WingetCatalogChecks(t);
            WingetExportParsing(t);
            WingetTableParsing(t);
            WingetCommands(t);
            FeatureCatalogChecks(t);
            FeatureParsing(t);
            FeatureCommands(t);
            DnsCatalogChecks(t);
            DnsParsing(t);
            DnsScripts(t);
            DnsPreviousFile(t);
            RepairCatalogChecks(t);
            RepairScripts(t);
            RepairVerdicts(t);
            TempCleaning(t);
            ReviewFixes(t);
        }

        // ---- quoting ----

        static void CommandLineQuoting(T t)
        {
            t.Equal("plain", Cmd.Arg("plain"), "plain argument stays as is");
            t.Equal("\"two words\"", Cmd.Arg("two words"), "spaces are quoted");
            t.Equal("\"say \\\"hi\\\"\"", Cmd.Arg("say \"hi\""), "inner quotes are escaped");
            t.Equal("\"C:\\a b\\\\\"", Cmd.Arg("C:\\a b\\"), "trailing backslash is doubled before the closing quote");
            t.Equal("\"\"", Cmd.Arg(""), "empty argument becomes an empty pair of quotes");
            t.Equal("'it''s'", Cmd.PsQuote("it's"), "PowerShell quotes are doubled");
            t.Equal("1.5 KB", Cmd.FormatSize(1536), "sizes are readable");
            t.Equal("3 files", Cmd.Plural(3, "file", "files"), "plural");
            t.Equal("1 file", Cmd.Plural(1, "file", "files"), "singular");
        }

        // ---- built-in apps ----

        static void BuiltInAppCatalog(T t)
        {
            var all = BuiltInApps.All;
            t.Check(all.Count >= 30 && all.Count <= 60, "the built-in app list has a sensible size (" + all.Count + ")");
            t.Equal(all.Count, all.Select(a => a.Package.ToLowerInvariant()).Distinct().Count(), "package names are unique");
            foreach (var a in all)
            {
                t.Check(!string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Description) && !string.IsNullOrWhiteSpace(a.KeepIf), a.Package + " has text");
                t.Check(BuiltInApps.Groups.Contains(a.Group), a.Package + " is in a known group");
                t.Check(!BuiltInApps.IsForbidden(a.Package), a.Package + " is not a forbidden package");
                t.Check(!a.Description.Contains("simply") && !a.Description.Contains(" just "), a.Package + " text avoids filler words");
            }
            foreach (string never in new[]
            {
                "Microsoft.WindowsStore", "Microsoft.DesktopAppInstaller", "Microsoft.SecHealthUI", "Microsoft.MicrosoftEdge.Stable", "windows.immersivecontrolpanel",
                "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.UI.Xaml.2.8", "Microsoft.VCLibs.140.00",
                "Microsoft.NET.Native.Framework.2.2", "Microsoft.WindowsAppRuntime.1.8", "MicrosoftWindows.Client.CBS", "Microsoft.Services.Store.Engagement",
                "Microsoft.StorePurchaseApp", "Microsoft.Winget.Source", "Microsoft.AAD.BrokerPlugin", "Microsoft.LockApp"
            })
            {
                t.Check(BuiltInApps.IsForbidden(never), never + " is recognised as forbidden");
                t.Check(BuiltInApps.Find(never) == null, never + " is not in the removal list");
            }
            t.Check(!BuiltInApps.IsForbidden("Clipchamp.Clipchamp"), "Clipchamp may be offered");
            t.Check(BuiltInApps.IsForbidden(""), "an empty name is forbidden");
        }

        static void AppxParsing(T t)
        {
            string array = "[{\"Name\":\"Clipchamp.Clipchamp\",\"PackageFullName\":\"Clipchamp.Clipchamp_4.0_x64__yxz\",\"PackageFamilyName\":\"Clipchamp.Clipchamp_yxz\",\"NonRemovable\":false,\"IsFramework\":false},"
                + "{\"Name\":\"Microsoft.WindowsStore\",\"PackageFullName\":\"x\",\"PackageFamilyName\":\"y\",\"NonRemovable\":false,\"IsFramework\":false},"
                + "{\"Name\":\"Microsoft.Xbox.TCUI\",\"PackageFullName\":\"x\",\"PackageFamilyName\":\"y\",\"NonRemovable\":true,\"IsFramework\":false},"
                + "{\"Name\":\"Microsoft.VCLibs.140.00\",\"PackageFullName\":\"x\",\"PackageFamilyName\":\"y\",\"NonRemovable\":false,\"IsFramework\":true}]";
            var list = BuiltInApps.ParseInstalled(array);
            t.Equal(4, list.Count, "array of packages is read");
            t.Equal("Clipchamp.Clipchamp", list[0].Name, "first package name");
            t.Check(list[2].NonRemovable && !list[2].IsFramework, "NonRemovable flag is read");
            t.Check(list[3].IsFramework, "IsFramework flag is read");

            var installed = BuiltInApps.RemovableInstalled(list);
            t.Check(installed.Contains("clipchamp.clipchamp"), "removable installed apps are matched case-insensitively");
            t.Check(!installed.Contains("Microsoft.WindowsStore"), "the Store is never offered even when installed");
            t.Check(!installed.Contains("Microsoft.Xbox.TCUI"), "a NonRemovable package is not offered");

            var single = BuiltInApps.ParseInstalled("{\"Name\":\"Microsoft.BingWeather\",\"PackageFullName\":\"a\",\"PackageFamilyName\":\"b\",\"NonRemovable\":false,\"IsFramework\":false}");
            t.Equal(1, single.Count, "a single object (PowerShell unrolls one result) is read");
            t.Equal("Microsoft.BingWeather", single[0].Name, "single object name");
            t.Equal(0, BuiltInApps.ParseInstalled("").Count, "empty output is an empty list");
            t.Equal(0, BuiltInApps.ParseInstalled("[]").Count, "an empty array is an empty list");
            t.Equal(0, BuiltInApps.ParseInstalled("not json at all").Count, "garbage is an empty list");
            t.Equal(1, BuiltInApps.ParseInstalled("\ufeff{\"Name\":\"A.B\"}").Count, "a byte order mark in front is tolerated");
        }

        static void AppxRemoveScript(T t)
        {
            string script = BuiltInApps.RemoveScript(new[] { "Clipchamp.Clipchamp", "Microsoft.BingWeather", "Clipchamp.Clipchamp" }, false);
            t.Check(script.Contains("@('Clipchamp.Clipchamp','Microsoft.BingWeather')"), "names are listed once, quoted");
            t.Check(script.Contains("Remove-AppxPackage"), "uses Remove-AppxPackage");
            t.Check(!script.Contains("-AllUsers"), "removal is for the current user only");
            t.Check(!script.Contains("Remove-AppxProvisionedPackage"), "provisioned removal is off by default");
            string withProv = BuiltInApps.RemoveScript(new[] { "Clipchamp.Clipchamp" }, true);
            t.Check(withProv.Contains("Remove-AppxProvisionedPackage -Online"), "provisioned removal when asked");
            t.Throws<ArgumentException>(() => BuiltInApps.RemoveScript(new[] { "Microsoft.WindowsStore" }, false), "the Store cannot be put in a removal script");
            t.Throws<ArgumentException>(() => BuiltInApps.RemoveScript(new[] { "Not.InTheCatalog" }, false), "unknown packages are refused");
            t.Throws<ArgumentException>(() => BuiltInApps.RemoveScript(new[] { "Clipchamp.Clipchamp'; calc; '" }, false), "injection attempts are refused");
            var r = BuiltInApps.ParseResultLine("RESULT|FAIL|Microsoft.BingWeather|Deployment failed | with a bar");
            t.Check(r != null && r.Kind == "FAIL" && r.Package == "Microsoft.BingWeather" && r.Message == "Deployment failed | with a bar", "result lines are read");
            t.Check(BuiltInApps.ParseResultLine("some other output") == null, "other lines are not results");
        }

        // ---- winget ----

        static void WingetCatalogChecks(T t)
        {
            var all = WingetCatalog.All;
            t.Check(all.Count >= 60 && all.Count <= 120, "the winget list has a sensible size (" + all.Count + ")");
            t.Equal(all.Count, all.Select(a => a.Id.ToLowerInvariant()).Distinct().Count(), "winget ids are unique (case-insensitive)");
            foreach (var a in all)
            {
                t.Check(WingetCatalog.LooksLikeId(a.Id), a.Id + " looks like a winget id");
                t.Check(WingetCatalog.Categories.Contains(a.Category), a.Id + " is in a known category");
                t.Check(!string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Description), a.Id + " has text");
            }
            foreach (string c in WingetCatalog.Categories) t.Check(all.Any(a => a.Category == c), "category has apps: " + c);
            t.Check(WingetCatalog.Find("git.git") != null, "lookup ignores case");
            t.Check(!WingetCatalog.LooksLikeId("not an id"), "spaces are not an id");
            t.Check(!WingetCatalog.LooksLikeId("NoDot"), "an id has a dot");
            t.Check(!WingetCatalog.LooksLikeId("a.b; calc"), "shell characters are not an id");
        }

        static void WingetExportParsing(T t)
        {
            string json = "{\n\t\"$schema\" : \"https://aka.ms/winget-packages.schema.2.0.json\",\n\t\"Sources\" : \n\t[\n\t\t{\n\t\t\t\"Packages\" : \n\t\t\t[\n\t\t\t\t{ \"PackageIdentifier\" : \"Git.Git\" },\n\t\t\t\t{ \"PackageIdentifier\" : \"Notepad++.Notepad++\" }\n\t\t\t],\n\t\t\t\"SourceDetails\" : { \"Name\" : \"winget\" }\n\t\t}\n\t]\n}";
            var ids = WingetCatalog.ParseExport(json);
            t.Equal(2, ids.Count, "export json lists two packages");
            t.Check(ids.Contains("git.git") && ids.Contains("NOTEPAD++.NOTEPAD++"), "export ids are case-insensitive");
            t.Equal(0, WingetCatalog.ParseExport("").Count, "empty export");
            t.Equal(0, WingetCatalog.ParseExport("{\"Sources\":[]}").Count, "export without sources");
            t.Equal(0, WingetCatalog.ParseExport("garbage").Count, "export garbage");
        }

        static string Table(string[] header, string[][] rows, params int[] widths)
        {
            var sb = new StringBuilder();
            Func<string[], string> line = cols =>
            {
                var s = new StringBuilder();
                for (int i = 0; i < cols.Length; i++) s.Append(i < widths.Length ? cols[i].PadRight(widths[i]) : cols[i]);
                return s.ToString().TrimEnd();
            };
            sb.Append(line(header)).Append("\r\n");
            sb.Append(new string('-', widths.Sum() + 8)).Append("\r\n");
            foreach (var r in rows) sb.Append(line(r)).Append("\r\n");
            return sb.ToString();
        }

        static void WingetTableParsing(T t)
        {
            string list = "   - \r" + Table(new[] { "Name", "Id", "Version", "Available", "Source" }, new[]
            {
                new[] { "Git", "Git.Git", "2.52.0", "2.56.0.4", "winget" },
                new[] { "Notepad++ (64-bit x64)", "Notepad++.Notepad++", "8.9.6.4", "", "winget" },
                new[] { "Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247", "Microsoft.VCRedist.2015+.x64", "14.51.36247.0", "14.52.37029.0", "winget" },
            }, 62, 32, 16, 16, 8);
            var rows = WingetCatalog.ParseTable(list);
            t.Equal(3, rows.Count, "list table rows (spinner debris in front of the header is tolerated)");
            t.Equal("Git.Git", rows[0].Id, "first id");
            t.Equal("Git", rows[0].Name, "first name");
            t.Equal("2.52.0", rows[0].Version, "first version");
            t.Equal("2.56.0.4", rows[0].Available, "first available");
            t.Equal("winget", rows[0].Source, "first source");
            t.Equal("Notepad++.Notepad++", rows[1].Id, "plus signs in ids");
            t.Equal("", rows[1].Available, "empty available column");
            t.Equal("Microsoft.VCRedist.2015+.x64", rows[2].Id, "long names and ids");
            var ids = WingetCatalog.ParseListIds(list);
            t.Check(ids.Contains("git.git") && ids.Count == 3, "list ids as a set");

            string upgrade = Table(new[] { "Name", "Id", "Version", "Available", "Source" }, new[]
            {
                new[] { "Google Chrome", "Google.Chrome", "157.0.8103.39", "157.0.8103.51", "winget" },
                new[] { "OBS Studio", "OBSProject.OBSStudio", "32.1.2", "33.0.0", "winget" },
            }, 40, 32, 16, 16, 8)
                + "2 upgrades available.\r\n\r\nThe following packages have an upgrade available, but require explicit targeting for upgrade:\r\n"
                + Table(new[] { "Name", "Id", "Version", "Available" }, new[] { new[] { "Some Tool", "Vendor.SomeTool", "1.0", "2.0" } }, 40, 32, 16, 16)
                + "1 package(s) have pins that prevent upgrade.\r\n";
            var ups = WingetCatalog.ParseUpgrades(upgrade);
            t.Equal(3, ups.Count, "upgrade rows from both tables, footer ignored");
            t.Equal("Google.Chrome", ups[0].Id, "first upgrade id");
            t.Equal("157.0.8103.51", ups[0].Available, "first upgrade target");
            t.Equal("Vendor.SomeTool", ups[2].Id, "second table row");

            // Wide characters take two columns, so the id starts earlier in the string than its header.
            string header = "Name".PadRight(24) + "Id".PadRight(26) + "Version".PadRight(12) + "Available";
            string wideRow = "\u65E5\u672C\u8A9E".PadRight(3 + 18) + "Foo.Bar".PadRight(26) + "1.0".PadRight(12) + "2.0";
            string wide = header + "\r\n" + new string('-', 70) + "\r\n" + wideRow + "\r\n";
            var wideRows = WingetCatalog.ParseTable(wide);
            t.Equal(1, wideRows.Count, "wide-character row is still read");
            t.Equal("Foo.Bar", wideRows.Count > 0 ? wideRows[0].Id : "", "wide-character row id");

            t.Equal(0, WingetCatalog.ParseTable("").Count, "empty output");
            t.Equal(0, WingetCatalog.ParseTable("No installed package found matching input criteria.").Count, "no table at all");
            t.Equal(0, WingetCatalog.ParseUpgrades("No applicable upgrade found.").Count, "no upgrades");
            t.Check(WingetCatalog.NeedsSourceAgreement("The source requires the current agreements to be accepted before it can be used. --accept-source-agreements"), "source agreement message is recognised");
            t.Check(!WingetCatalog.NeedsSourceAgreement("Name Id"), "ordinary output is not an agreement request");
        }

        static void WingetCommands(T t)
        {
            string install = WingetCatalog.InstallArgs("VideoLAN.VLC");
            t.Equal("install --id VideoLAN.VLC -e --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity", install, "install arguments");
            t.Equal("upgrade --all --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity", WingetCatalog.UpgradeAllArgs(), "upgrade all arguments");
            t.Check(!WingetCatalog.UpgradeListArgs().Contains("accept"), "checking for updates does not accept any terms");
            t.Equal("export -o \"C:\\Temp Folder\\x.json\" --source winget --disable-interactivity", WingetCatalog.ExportArgs("C:\\Temp Folder\\x.json"), "export path with spaces is quoted");
            t.Throws<ArgumentException>(() => WingetCatalog.InstallArgs("Git.Git & calc"), "install refuses shell characters");
            t.Throws<ArgumentException>(() => WingetCatalog.UpgradeOneArgs(""), "upgrade refuses an empty id");
            t.Equal("Already installed.", WingetCatalog.DescribeExit(unchecked((int)0x8A150061u)), "already installed exit code");
            t.Equal("Installed.", WingetCatalog.DescribeExit(0), "success exit code");
        }

        // ---- features ----

        static void FeatureCatalogChecks(T t)
        {
            var all = WindowsFeatures.All;
            t.Equal(all.Count, all.Select(f => f.Name.ToLowerInvariant()).Distinct().Count(), "feature names are unique");
            foreach (string need in new[] { "Containers-DisposableClientVM", "Microsoft-Hyper-V-All", "VirtualMachinePlatform", "Microsoft-Windows-Subsystem-Linux", "NetFx3", "TelnetClient", "SMB1Protocol", "WindowsMediaPlayer", "ServicesForNFS-ClientOnly", "MicrosoftWindowsPowerShellV2Root" })
                t.Check(WindowsFeatures.Find(need) != null, "catalog has " + need);
            foreach (var f in all)
            {
                t.Check(WindowsFeatures.Groups.Contains(f.Group), f.Name + " is in a known group");
                t.Check(!string.IsNullOrWhiteSpace(f.Title) && !string.IsNullOrWhiteSpace(f.Description), f.Name + " has text");
                t.Check(WindowsFeatures.EnableArgs(f).StartsWith("/online /enable-feature /featurename:" + f.Name), f.Name + " builds enable arguments");
            }
            t.Equal(FeatureAdvice.KeepOff, WindowsFeatures.Find("SMB1Protocol").Advice, "SMB 1.0 is recommended off");
            t.Check(WindowsFeatures.IsHome("Core") && WindowsFeatures.IsHome("CoreSingleLanguage") && !WindowsFeatures.IsHome("Professional"), "Home editions are recognised");
            t.Check(WindowsFeatures.EditionProblem(WindowsFeatures.Find("Containers-DisposableClientVM"), "Core") != null, "Sandbox is blocked on Home");
            t.Check(WindowsFeatures.EditionProblem(WindowsFeatures.Find("Containers-DisposableClientVM"), "Professional") == null, "Sandbox is allowed on Pro");
            t.Check(WindowsFeatures.EditionProblem(WindowsFeatures.Find("VirtualMachinePlatform"), "Core") == null, "Virtual Machine Platform works on Home");
        }

        static void FeatureParsing(T t)
        {
            string cim = "[{\"Name\":\"Microsoft-Hyper-V-All\",\"InstallState\":1},{\"Name\":\"NetFx3\",\"InstallState\":2},{\"Name\":\"Foo\",\"InstallState\":3},{\"Name\":\"Bar\",\"InstallState\":4}]";
            var map = WindowsFeatures.ParseCim(cim);
            t.Equal(FeatureState.On, WindowsFeatures.Lookup(map, "microsoft-hyper-v-all"), "InstallState 1 is on (case-insensitive)");
            t.Equal(FeatureState.Off, WindowsFeatures.Lookup(map, "NetFx3"), "InstallState 2 is off");
            t.Equal(FeatureState.Removed, WindowsFeatures.Lookup(map, "Foo"), "InstallState 3 is removed");
            t.Equal(FeatureState.Unknown, WindowsFeatures.Lookup(map, "Bar"), "InstallState 4 is unknown");
            t.Equal(FeatureState.NotOnThisPc, WindowsFeatures.Lookup(map, "MicrosoftWindowsPowerShellV2Root"), "unlisted feature is not on this PC");
            var single = WindowsFeatures.ParseCim("{\"Name\":\"TelnetClient\",\"InstallState\":2}");
            t.Equal(FeatureState.Off, WindowsFeatures.Lookup(single, "TelnetClient"), "a single object is read");
            t.Equal(0, WindowsFeatures.ParseCim("").Count, "empty CIM output");

            string dism = "\r\nDeployment Image Servicing and Management tool\r\nVersion: 10.0.26100.1\r\n\r\nImage Version: 10.0.26300.1\r\n\r\nFeatures listing for package : Microsoft-Windows-Client-Desktop-Required-Package~31bf3856ad364e35~amd64~~10.0.26300.1\r\n\r\n"
                + "Feature Name                                    | State\r\n----------------------------------------------- | --------\r\n"
                + "Microsoft-Hyper-V-All                           | Enabled\r\nNetFx3                                          | Disabled\r\nTelnetClient                                    | Disable Pending\r\nSMB1Protocol                                    | Disabled with Payload Removed\r\n\r\nThe operation completed successfully.\r\n";
            var d = WindowsFeatures.ParseDismTable(dism);
            t.Equal(4, d.Count, "dism table has four features");
            t.Equal(FeatureState.On, d["Microsoft-Hyper-V-All"], "dism Enabled");
            t.Equal(FeatureState.Off, d["NetFx3"], "dism Disabled");
            t.Equal(FeatureState.Pending, d["TelnetClient"], "dism pending");
            t.Equal(FeatureState.Removed, d["SMB1Protocol"], "dism payload removed");
        }

        static void FeatureCommands(T t)
        {
            var sandbox = WindowsFeatures.Find("Containers-DisposableClientVM");
            t.Equal("/online /enable-feature /featurename:Containers-DisposableClientVM /all /norestart", WindowsFeatures.EnableArgs(sandbox), "enable with parents");
            t.Equal("/online /disable-feature /featurename:Containers-DisposableClientVM /norestart", WindowsFeatures.DisableArgs(sandbox), "disable never restarts");
            var telnet = WindowsFeatures.Find("TelnetClient");
            t.Equal("/online /enable-feature /featurename:TelnetClient /norestart", WindowsFeatures.EnableArgs(telnet), "enable without parents");
            t.Throws<ArgumentException>(() => WindowsFeatures.EnableArgs(new WindowsFeature { Name = "X /y" }), "feature names with spaces are refused");
            t.Check(WindowsFeatures.IsSuccess(0) && WindowsFeatures.IsSuccess(3010) && !WindowsFeatures.IsSuccess(1), "exit codes 0 and 3010 are success");
            t.Check(WindowsFeatures.NeedsRestart(3010) && !WindowsFeatures.NeedsRestart(0), "3010 needs a restart");
        }

        // ---- DNS ----

        static void DnsCatalogChecks(T t)
        {
            t.Equal(7, DnsTools.Providers.Count, "seven choices including automatic");
            t.Check(DnsTools.Providers[0].IsAutomatic, "the first choice is automatic");
            t.Equal(DnsTools.Providers.Count, DnsTools.Providers.Select(p => p.Id).Distinct().Count(), "provider ids are unique");
            foreach (var p in DnsTools.Providers.Skip(1))
            {
                t.Equal(2, p.V4.Length, p.Id + " has two IPv4 servers");
                t.Equal(2, p.V6.Length, p.Id + " has two IPv6 servers");
                foreach (string a in p.All) t.Check(DnsTools.IsAddress(a), p.Id + " address is valid: " + a);
                foreach (string a in p.V4) t.Check(a.IndexOf(':') < 0, p.Id + " IPv4 list has no IPv6 address");
                foreach (string a in p.V6) t.Check(a.IndexOf(':') >= 0, p.Id + " IPv6 list has only IPv6 addresses");
            }
            t.Equal("1.1.1.1", DnsTools.Find("cloudflare").V4[0], "Cloudflare primary");
            t.Equal("1.1.1.2", DnsTools.Find("cloudflare-malware").V4[0], "Cloudflare malware blocking primary");
            t.Equal("8.8.4.4", DnsTools.Find("google").V4[1], "Google secondary");
            t.Equal("9.9.9.9", DnsTools.Find("quad9").V4[0], "Quad9 primary");
            t.Equal("94.140.14.14", DnsTools.Find("adguard").V4[0], "AdGuard primary");
            t.Equal("208.67.222.222", DnsTools.Find("opendns").V4[0], "OpenDNS primary");
            t.Check(!DnsTools.IsAddress("8.8.8.8; calc") && !DnsTools.IsAddress("") && !DnsTools.IsAddress("dns.google"), "only IP addresses are accepted");
        }

        static void DnsParsing(T t)
        {
            string json = "[{\"Name\":\"Ethernet 2\",\"Index\":19,\"Description\":\"Intel(R) Ethernet\",\"V4\":[\"8.8.8.8\",\"1.1.1.1\"],\"V6\":[],\"StaticV4\":true,\"StaticV6\":false},"
                + "{\"Name\":\"Wi-Fi\",\"Index\":23,\"Description\":\"MediaTek\",\"V4\":\"192.168.1.1\",\"V6\":[\"fec0:0:0:ffff::1\",\"fec0:0:0:ffff::2\"],\"StaticV4\":false,\"StaticV6\":false}]";
            var list = DnsTools.ParseAdapters(json);
            t.Equal(2, list.Count, "two adapters");
            t.Equal(19, list[0].Index, "adapter index");
            t.Equal(2, list[0].V4.Count, "two IPv4 servers");
            t.Check(list[0].StaticV4 && !list[0].StaticV6, "static flags");
            t.Equal(1, list[1].V4.Count, "a single string becomes a one-item list");
            t.Equal(0, list[1].V6.Count, "the fec0:0:0:ffff placeholder servers are not real servers");
            t.Equal("Custom (8.8.8.8, 1.1.1.1)", DnsTools.Describe(list[0]), "mixed servers are custom");
            t.Equal("Automatic (192.168.1.1)", DnsTools.Describe(list[1]), "router servers are automatic");

            var google = new DnsAdapter { Name = "x", V4 = new List<string> { "8.8.4.4", "8.8.8.8" }, StaticV4 = true };
            t.Equal("google", DnsTools.Match(google).Id, "providers match in any order");
            var one = DnsTools.ParseAdapters("{\"Name\":\"Ethernet\",\"Index\":3,\"V4\":[],\"V6\":[],\"StaticV4\":false,\"StaticV6\":false}");
            t.Equal(1, one.Count, "a single adapter object is read");
            t.Equal(0, DnsTools.ParseAdapters("").Count, "no adapters");
        }

        static void DnsScripts(T t)
        {
            string apply = DnsTools.ApplyScript(DnsTools.Find("cloudflare"), new[] { 19, 23, 19 });
            t.Check(apply.Contains("Set-DnsClientServerAddress -InterfaceIndex 19 -ServerAddresses @('1.1.1.1','1.0.0.1','2606:4700:4700::1111','2606:4700:4700::1001')"), "apply sets IPv4 and IPv6 servers");
            t.Check(apply.Contains("-InterfaceIndex 23"), "every adapter is changed");
            t.Equal(2, CountOf(apply, "Set-DnsClientServerAddress"), "adapters are not repeated");
            string auto = DnsTools.ApplyScript(DnsTools.Providers[0], new[] { 5 });
            t.Check(auto.Contains("-InterfaceIndex 5 -ResetServerAddresses"), "automatic resets the servers");
            t.Throws<ArgumentException>(() => DnsTools.ApplyScript(DnsTools.Find("google"), new int[0]), "no adapters is refused");
            t.Throws<ArgumentException>(() => DnsTools.ApplyScript(new DnsProvider { Id = "x", V4 = new[] { "1.1.1.1; calc" } }, new[] { 1 }), "bad addresses are refused");

            var prev = new DnsPrevious();
            prev.Adapters.Add(new DnsPreviousAdapter { Name = "Ethernet 2", Index = 19, StaticV4 = true, V4 = new List<string> { "8.8.8.8", "1.1.1.1" } });
            prev.Adapters.Add(new DnsPreviousAdapter { Name = "Wi-Fi", Index = 23, V4 = new List<string> { "192.168.1.1" } });
            string restore = DnsTools.RestoreScript(prev);
            t.Check(restore.Contains("Where-Object Name -eq 'Ethernet 2'"), "restore finds adapters by name");
            t.Check(restore.Contains("-ServerAddresses @('8.8.8.8','1.1.1.1')"), "restore re-enters typed servers");
            t.Equal(1, CountOf(restore, "-ServerAddresses"), "automatic adapters only reset");
            t.Equal(2, CountOf(restore, "-ResetServerAddresses"), "every adapter is reset first");
        }

        static int CountOf(string text, string part)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(part, i, StringComparison.Ordinal)) >= 0) { n++; i += part.Length; }
            return n;
        }

        static void DnsPreviousFile(T t)
        {
            string file = Path.Combine(t.TempFolder(), "dns-previous.json");
            t.Check(DnsTools.LoadPrevious(file) == null, "no file means nothing to undo");
            var adapters = new List<DnsAdapter>
            {
                new DnsAdapter { Name = "Ethernet 2", Index = 19, V4 = new List<string> { "8.8.8.8" }, V6 = new List<string>(), StaticV4 = true },
                new DnsAdapter { Name = "Wi-Fi", Index = 23, V4 = new List<string> { "192.168.1.1" } },
            };
            DnsTools.SavePrevious(DnsTools.Snapshot(adapters, "cloudflare"), file);
            var loaded = DnsTools.LoadPrevious(file);
            t.Check(loaded != null, "saved file loads");
            if (loaded != null)
            {
                t.Equal(2, loaded.Adapters.Count, "both adapters saved");
                t.Equal("Ethernet 2", loaded.Adapters[0].Name, "adapter name saved");
                t.Check(loaded.Adapters[0].StaticV4 && loaded.Adapters[0].V4.SequenceEqual(new[] { "8.8.8.8" }), "static servers saved");
                t.Check(!loaded.Adapters[1].StaticV4, "automatic adapter saved as automatic");
                t.Equal("cloudflare", loaded.Provider, "new provider saved");
            }
            File.WriteAllText(file, "{ broken");
            t.Check(DnsTools.LoadPrevious(file) == null, "a broken file is treated as nothing to undo");
            DnsTools.ClearPrevious(file);
            t.Check(!File.Exists(file), "clearing removes the file");
        }

        // ---- review fixes ----

        // Ids last checked with: winget show --id <id> -e --source winget --disable-interactivity (2026-11-11).
        static void ReviewFixes(T t)
        {
            t.Check(WingetCatalog.Find("MullvadVPN.MullvadVPN") != null, "Mullvad uses its real winget id");
            t.Check(WingetCatalog.Find("Mullvad.MullvadVPN") == null, "the old Mullvad id is gone");
            t.Check(WingetCatalog.Find("Microsoft.DotNet.SDK.10") != null, ".NET SDK 10 is offered");
            t.Check(WingetCatalog.Find("Microsoft.DotNet.SDK.9") == null, ".NET SDK 9 (past support) is not offered");

            t.Check(WingetCatalog.IsInstallSuccess(unchecked((int)0x8A150109u)), "installed, restart to finish counts as installed");
            t.Check(!WingetCatalog.IsInstallSuccess(unchecked((int)0x8A15010Au)), "restart needed first is not installed");
            t.Check(!WingetCatalog.IsInstallSuccess(unchecked((int)0x8A15002Bu)), "no newer version is not an install");
            t.Check(WingetCatalog.IsUpdateSuccess(unchecked((int)0x8A15002Bu)), "no newer version is fine for an update");
            t.Check(!WingetCatalog.IsUpdateSuccess(unchecked((int)0x8A15002Cu)), "update all with failures is not success");
            t.Check(WingetCatalog.DescribeExit(unchecked((int)0x8A150109u)).Contains("Restart"), "restart-to-finish is explained");
            t.Check(WingetCatalog.DescribeExit(unchecked((int)0x8A15010Au)).Contains("Restart"), "restart-first is explained");
            t.Check(WingetCatalog.DescribeExit(unchecked((int)0x8A15010Bu)).Contains("restart"), "installer-restart is explained");
            t.Check(WingetCatalog.DescribeExit(unchecked((int)0x8A15002Cu)).Contains("could not be updated"), "update-all failures are explained");

            // DNS: the earliest saved servers survive a second change.
            var original = DnsTools.Snapshot(new[] { new DnsAdapter { Name = "Ethernet 2", Index = 19, V4 = new List<string> { "8.8.8.8", "1.1.1.1" }, StaticV4 = true } }, "cloudflare");
            var second = DnsTools.Snapshot(new[]
            {
                new DnsAdapter { Name = "Ethernet 2", Index = 19, V4 = new List<string> { "1.1.1.1", "1.0.0.1" }, StaticV4 = true },
                new DnsAdapter { Name = "Wi-Fi", Index = 23, V4 = new List<string> { "192.168.1.1" } },
            }, "google");
            var merged = DnsTools.MergeSnapshots(original, second);
            t.Equal(2, merged.Adapters.Count, "the merged snapshot has both adapters");
            t.Check(merged.Adapters[0].V4.SequenceEqual(new[] { "8.8.8.8", "1.1.1.1" }), "the first-saved servers are kept");
            t.Check(merged.Adapters.Any(a => a.Name == "Wi-Fi"), "a new adapter is added");
            t.Equal("google", merged.Provider, "the latest provider is recorded");
            t.Check(object.ReferenceEquals(second, DnsTools.MergeSnapshots(null, second)), "with nothing saved the new snapshot is used");

            // Windows Update reset: remembers what ran and reports start failures.
            string script = RepairTools.ResetWindowsUpdateScript("20261111-120000");
            t.Check(script.Contains("$wasRunning") && script.Contains("finally"), "reset remembers running services and always restarts them");
            t.Check(script.Contains("$startFailed") && script.Contains("exit 2"), "reset reports services that did not start");

            // Windows features: warnings for what stops working.
            foreach (var name in new[] { "VirtualMachinePlatform", "Microsoft-Windows-Subsystem-Linux", "Containers", "HypervisorPlatform", "Microsoft-Hyper-V-All" })
                t.Check(!string.IsNullOrWhiteSpace(WindowsFeatures.Find(name).BreaksIfOff), name + " has a turn-off warning");

            // Temp cleaning: ages and unsafe roots.
            t.Equal(TimeSpan.FromDays(7), RepairTools.AgeFor(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")), "Windows Temp keeps files for 7 days");
            t.Equal(TimeSpan.FromHours(24), RepairTools.AgeFor(t.TempFolder()), "other Temp folders use 24 hours");
            string real = Path.Combine(t.TempFolder(), "realroot");
            Directory.CreateDirectory(real);
            string victim = Path.Combine(real, "old.tmp");
            File.WriteAllBytes(victim, new byte[10]);
            SetOld(victim, DateTime.UtcNow.AddDays(-30));
            string link = Path.Combine(t.TempFolder(), "linkroot");
            var mk = Cmd.RunPowerShell("cmd /c mklink /J " + Cmd.PsQuote(link) + " " + Cmd.PsQuote(real), 20000);
            if (Directory.Exists(link))
            {
                var r = RepairTools.CleanTemp(new[] { link }, DateTime.UtcNow.AddHours(-24), true, CancellationToken.None);
                t.Equal(0, r.Files, "a Temp root that is a junction is not cleaned");
                t.Check(File.Exists(victim), "the junction target is untouched");
                t.Check(!RepairTools.IsSafeTempRoot(link), "a junction is not a safe Temp root");
                try { Directory.Delete(link, false); } catch (Exception) { }
            }
            t.Check(!RepairTools.IsSafeTempRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "the profile folder is not a Temp root");
        }

        // ---- repair ----

        static void RepairCatalogChecks(T t)
        {
            t.Equal(RepairTools.Tasks.Count, RepairTools.Tasks.Select(x => x.Kind).Distinct().Count(), "each repair kind appears once");
            foreach (var task in RepairTools.Tasks)
            {
                t.Check(RepairTools.Groups.Contains(task.Group), task.Kind + " is in a known group");
                t.Check(!string.IsNullOrWhiteSpace(task.Description), task.Kind + " has a description");
            }
            foreach (var kind in new[] { RepairKind.ResetWindowsUpdate, RepairKind.ResetNetwork, RepairKind.ComponentCleanup, RepairKind.Sfc, RepairKind.DismRestoreHealth, RepairKind.ClearTemp, RepairKind.RestartExplorer })
                t.Check(!string.IsNullOrWhiteSpace(RepairTools.Task(kind).Confirm), kind + " asks for confirmation");
            t.Check(RepairTools.Tasks.Count(x => x.Kind == RepairKind.Sfc && x.Long) == 1, "SFC is a long task");

            t.Equal(RepairTools.Classic.Count, RepairTools.Classic.Select(c => c.Id).Distinct().Count(), "classic tool ids are unique");
            t.Check(RepairTools.Classic.Count >= 24, "at least 24 classic tools (" + RepairTools.Classic.Count + ")");
            foreach (var c in RepairTools.Classic)
                t.Check(!string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Purpose) && !string.IsNullOrWhiteSpace(c.File) && !string.IsNullOrWhiteSpace(c.Glyph), c.Id + " has a name, purpose, file and icon");
            foreach (string id in new[] { "controlpanel", "network", "power", "sound", "sysprops", "envvars", "programs", "devmgmt", "diskmgmt", "services", "eventvwr", "taskschd", "regedit", "netplwiz", "firewall", "msconfig", "resmon", "reliability", "winupdate" })
                t.Check(RepairTools.Classic.Any(c => c.Id == id), "classic tool present: " + id);
            t.Equal("sysdm.cpl,EditEnvironmentVariables", RepairTools.Classic.First(c => c.Id == "envvars").Arguments, "environment variables dialog arguments");
            t.Equal(Encoding.Unicode, RepairTools.SfcEncoding, "SFC output is read as UTF-16");
        }

        static void RepairScripts(T t)
        {
            string script = RepairTools.ResetWindowsUpdateScript("20261111-120000");
            t.Check(script.Contains("'wuauserv','bits','cryptsvc'") && script.Contains("'cryptsvc','bits','wuauserv'"), "services stop and start");
            t.Check(script.Contains("SoftwareDistribution") && script.Contains("catroot2"), "both folders are handled");
            t.Check(script.Contains("Rename-Item") && !script.Contains("Remove-Item"), "folders are renamed, never deleted");
            t.Check(script.Contains("'20261111-120000'"), "time stamp is used");
            t.Throws<ArgumentException>(() => RepairTools.ResetWindowsUpdateScript("1'; calc; '"), "bad time stamps are refused");
            var steps = RepairTools.NetworkResetSteps();
            t.Equal(3, steps.Count, "three network reset steps");
            t.Equal("winsock reset", steps[0][1], "winsock reset first");
            t.Equal("int ip reset", steps[1][1], "ip reset second");
            t.Equal("/flushdns", steps[2][1], "flush last");
            t.Equal("/scannow", RepairTools.SfcArgs, "sfc arguments");
            t.Equal("/Online /Cleanup-Image /RestoreHealth", RepairTools.DismRestoreHealthArgs, "dism restore health arguments");
            t.Equal("/Online /Cleanup-Image /StartComponentCleanup", RepairTools.DismComponentCleanupArgs, "dism cleanup arguments");
        }

        static void RepairVerdicts(T t)
        {
            var clean = RepairOutput.SummarizeSfc("Beginning system scan.\r\nWindows Resource Protection did not find any integrity violations.\r\n", 0);
            t.Check(clean.Ok && !clean.RestartNeeded, "sfc: no violations");
            var fixedIt = RepairOutput.SummarizeSfc("Windows Resource Protection found corrupt files and successfully repaired them.", 0);
            t.Check(fixedIt.Ok && fixedIt.RestartNeeded, "sfc: repaired");
            var stuck = RepairOutput.SummarizeSfc("Windows Resource Protection found corrupt files but was unable to fix some of them.", 0);
            t.Check(!stuck.Ok && stuck.Message.Contains("image"), "sfc: unable to fix points at the image repair");
            var failed = RepairOutput.SummarizeSfc("", 1);
            t.Check(!failed.Ok, "sfc: failure by exit code");
            t.Check(RepairOutput.SummarizeDism(0, true).Ok, "dism 0 is ok");
            var restart = RepairOutput.SummarizeDism(3010, true);
            t.Check(restart.Ok && restart.RestartNeeded, "dism 3010 is ok and needs a restart");
            t.Check(!RepairOutput.SummarizeDism(740, false).Ok, "dism 740 is a failure");
        }

        static void TempCleaning(T t)
        {
            string root = t.TempFolder();
            DateTime now = DateTime.UtcNow;
            DateTime old = now.AddDays(-3);
            string oldFile = Path.Combine(root, "old.tmp");
            string newFile = Path.Combine(root, "new.tmp");
            File.WriteAllBytes(oldFile, new byte[1000]);
            File.WriteAllBytes(newFile, new byte[500]);
            SetOld(oldFile, old);
            string oldDir = Path.Combine(root, "olddir");
            Directory.CreateDirectory(oldDir);
            string nested = Path.Combine(oldDir, "nested.tmp");
            File.WriteAllBytes(nested, new byte[2000]);
            SetOld(nested, old);
            Directory.SetLastWriteTimeUtc(oldDir, old);
            string mixedDir = Path.Combine(root, "mixed");
            Directory.CreateDirectory(mixedDir);
            string keep = Path.Combine(mixedDir, "keep.tmp");
            File.WriteAllBytes(keep, new byte[10]);
            string lockedFile = Path.Combine(mixedDir, "locked.tmp");
            File.WriteAllBytes(lockedFile, new byte[300]);
            SetOld(lockedFile, old);
            Directory.SetLastWriteTimeUtc(mixedDir, old);

            DateTime cutoff = now.AddHours(-24);
            var scan = RepairTools.CleanTemp(new[] { root }, cutoff, false, CancellationToken.None);
            t.Equal(3, scan.Files, "scan finds the three old files");
            t.Equal(3300L, scan.Bytes, "scan adds up their sizes");
            t.Check(File.Exists(oldFile) && File.Exists(nested), "scanning deletes nothing");

            using (var held = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var done = RepairTools.CleanTemp(new[] { root }, cutoff, true, CancellationToken.None);
                t.Equal(2, done.Files, "locked files are skipped");
                t.Equal(3000L, done.Bytes, "bytes freed exclude the locked file");
                t.Check(done.Skipped >= 1, "the skipped file is counted");
            }
            t.Check(!File.Exists(oldFile), "old file is gone");
            t.Check(File.Exists(newFile), "new file stays");
            t.Check(!Directory.Exists(oldDir), "an old folder that became empty is removed");
            t.Check(Directory.Exists(mixedDir) && File.Exists(keep) && File.Exists(lockedFile), "a folder with newer or locked files stays");
            t.Check(Directory.Exists(root), "the root folder itself stays");

            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var none = RepairTools.CleanTemp(new[] { root }, cutoff, true, cancelled.Token);
            t.Equal(0, none.Files, "a cancelled run deletes nothing");
            var driveRoot = RepairTools.CleanTemp(new[] { Path.GetPathRoot(root) }, DateTime.UtcNow.AddYears(1), false, CancellationToken.None);
            t.Equal(0, driveRoot.Files, "a drive root is never cleaned");
            t.Check(RepairTools.DescribeTemp(new TempCleanResult { Bytes = 2048, Files = 2 }, true).StartsWith("Freed 2 KB"), "freed message");
        }

        static void SetOld(string file, DateTime old)
        {
            File.SetCreationTimeUtc(file, old);
            File.SetLastWriteTimeUtc(file, old);
            File.SetLastAccessTimeUtc(file, old);
        }
    }
}
