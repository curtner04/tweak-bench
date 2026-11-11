using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TweakBench.Core;

namespace TweakBench.Tools
{
    public sealed class DnsProvider
    {
        public string Id;
        public string Name;
        public string Blurb;
        /// <summary>Empty for "Automatic".</summary>
        public string[] V4 = new string[0];
        public string[] V6 = new string[0];
        public bool IsAutomatic { get { return V4.Length == 0 && V6.Length == 0; } }
        public string[] All { get { return V4.Concat(V6).ToArray(); } }
    }

    /// <summary>A network adapter that is connected, with its current DNS servers.</summary>
    public sealed class DnsAdapter
    {
        public string Name = "";
        public int Index;
        public string Description = "";
        public List<string> V4 = new List<string>();
        public List<string> V6 = new List<string>();
        /// <summary>True when the IPv4 servers were typed in (not handed out by the router).</summary>
        public bool StaticV4;
        public bool StaticV6;
    }

    /// <summary>What <c>dns-previous.json</c> holds so the last change can be undone.</summary>
    public sealed class DnsPrevious
    {
        public string Saved = "";
        public string Provider = "";
        public List<DnsPreviousAdapter> Adapters = new List<DnsPreviousAdapter>();
    }

    public sealed class DnsPreviousAdapter
    {
        public string Name = "";
        public int Index;
        public bool StaticV4;
        public bool StaticV6;
        public List<string> V4 = new List<string>();
        public List<string> V6 = new List<string>();
    }

    /// <summary>DNS providers, reading the active adapters, and changing their servers. No UI in here.</summary>
    public static class DnsTools
    {
        public const string AutomaticId = "auto";

        static DnsProvider P(string id, string name, string blurb, string[] v4, string[] v6)
        {
            return new DnsProvider { Id = id, Name = name, Blurb = blurb, V4 = v4, V6 = v6 };
        }

        // Addresses are the ones published by each provider:
        //   Cloudflare:  https://developers.cloudflare.com/1.1.1.1/ip-addresses/
        //   Google:      https://developers.google.com/speed/public-dns/docs/using
        //   Quad9:       https://docs.quad9.net/ (recommended, malware blocking)
        //   AdGuard:     https://adguard-dns.io/en/public-dns.html (default, ad blocking)
        //   OpenDNS:     https://support.opendns.com/ (OpenDNS Home / Sign In IPs)
        public static readonly List<DnsProvider> Providers = new List<DnsProvider>
        {
            P(AutomaticId, "Automatic (from your router)", "What Windows uses by default: your internet provider's servers, handed out by the router.", new string[0], new string[0]),
            P("cloudflare", "Cloudflare", "Fast and privacy-focused.",
                new[] { "1.1.1.1", "1.0.0.1" }, new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }),
            P("cloudflare-malware", "Cloudflare, malware blocking", "Cloudflare, plus it refuses addresses known to host malware.",
                new[] { "1.1.1.2", "1.0.0.2" }, new[] { "2606:4700:4700::1112", "2606:4700:4700::1002" }),
            P("google", "Google", "Fast and reliable. Google logs requests.",
                new[] { "8.8.8.8", "8.8.4.4" }, new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" }),
            P("quad9", "Quad9", "Blocks known malicious sites. Run by a non-profit.",
                new[] { "9.9.9.9", "149.112.112.112" }, new[] { "2620:fe::fe", "2620:fe::9" }),
            P("adguard", "AdGuard", "Blocks many ads and trackers for every app on the PC.",
                new[] { "94.140.14.14", "94.140.15.15" }, new[] { "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff" }),
            P("opendns", "OpenDNS", "Cisco's service, with optional phishing protection.",
                new[] { "208.67.222.222", "208.67.220.220" }, new[] { "2620:119:35::35", "2620:119:53::53" }),
        };

        public static DnsProvider Find(string id)
        {
            return Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Names the provider whose IPv4 servers match (any order, primary alone also matches); "Automatic" when static servers are not set; otherwise null.</summary>
        public static DnsProvider Match(DnsAdapter a)
        {
            if (!a.StaticV4 && !a.StaticV6) return Providers[0];
            var have = new HashSet<string>(a.V4, StringComparer.OrdinalIgnoreCase);
            if (have.Count == 0) return null;
            foreach (var p in Providers.Skip(1))
                if (p.V4.Length == have.Count && p.V4.All(have.Contains)) return p;
            return null;
        }

        /// <summary>A short line such as "Google (8.8.8.8, 8.8.4.4)" or "Automatic (192.168.1.1)".</summary>
        public static string Describe(DnsAdapter a)
        {
            var p = Match(a);
            string servers = a.V4.Count > 0 ? string.Join(", ", a.V4) : (a.V6.Count > 0 ? string.Join(", ", a.V6) : "no servers");
            if (p != null) return (p.IsAutomatic ? "Automatic" : p.Name) + " (" + servers + ")";
            return "Custom (" + servers + ")";
        }

        // ---- reading ----

        /// <summary>PowerShell that prints the connected physical adapters and their DNS servers as a JSON array. Read-only.</summary>
        public static string ReadScript
        {
            get
            {
                return
                    "$out = @()\r\n" +
                    "foreach ($a in @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up')) {\r\n" +
                    "  $v4 = @((Get-DnsClientServerAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv4).ServerAddresses)\r\n" +
                    "  $v6 = @((Get-DnsClientServerAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv6).ServerAddresses)\r\n" +
                    "  $s4 = $false; $s6 = $false\r\n" +
                    "  try { $s4 = -not [string]::IsNullOrEmpty((Get-ItemProperty -Path ('HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\' + $a.InterfaceGuid) -Name NameServer -ErrorAction Stop).NameServer) } catch {}\r\n" +
                    "  try { $s6 = -not [string]::IsNullOrEmpty((Get-ItemProperty -Path ('HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters\\Interfaces\\' + $a.InterfaceGuid) -Name NameServer -ErrorAction Stop).NameServer) } catch {}\r\n" +
                    "  $out += [pscustomobject]@{ Name = [string]$a.Name; Index = [int]$a.InterfaceIndex; Description = [string]$a.InterfaceDescription; V4 = $v4; V6 = $v6; StaticV4 = $s4; StaticV6 = $s6 }\r\n" +
                    "}\r\n" +
                    "ConvertTo-Json -Compress -Depth 4 -InputObject $out";
            }
        }

        /// <summary>Windows fills IPv6 with the placeholder servers fec0:0:0:ffff::1-3 when none are set; those are not real servers.</summary>
        public static bool IsPlaceholder(string address)
        {
            return address != null && address.StartsWith("fec0:0:0:ffff::", StringComparison.OrdinalIgnoreCase);
        }

        static readonly Regex Address = new Regex(@"^[0-9A-Fa-f:.%]+$", RegexOptions.Compiled);

        public static bool IsAddress(string text)
        {
            System.Net.IPAddress ip;
            return !string.IsNullOrWhiteSpace(text) && Address.IsMatch(text) && System.Net.IPAddress.TryParse(text, out ip);
        }

        public static List<DnsAdapter> ParseAdapters(string json)
        {
            var list = new List<DnsAdapter>();
            foreach (var o in Cmd.JsonObjects(json))
            {
                string name = Cmd.Str(o, "Name");
                if (name.Length == 0) continue;
                list.Add(new DnsAdapter
                {
                    Name = name,
                    Index = Cmd.Int(o, "Index"),
                    Description = Cmd.Str(o, "Description"),
                    V4 = Cmd.StrList(o, "V4").Where(x => !IsPlaceholder(x)).ToList(),
                    V6 = Cmd.StrList(o, "V6").Where(x => !IsPlaceholder(x)).ToList(),
                    StaticV4 = Cmd.Bool(o, "StaticV4"),
                    StaticV6 = Cmd.Bool(o, "StaticV6"),
                });
            }
            return list;
        }

        public static List<DnsAdapter> ReadAdapters()
        {
            var r = Cmd.RunPowerShell(ReadScript, 60000);
            if (!r.Ok && string.IsNullOrWhiteSpace(r.Output)) throw new InvalidOperationException("Windows would not list the network adapters. " + FirstLine(r.Error));
            return ParseAdapters(r.Output);
        }

        static string FirstLine(string text)
        {
            return (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        }

        // ---- changing ----

        /// <summary>The PowerShell that sets (or resets) the DNS servers of the given adapter indexes. Needs administrator rights.</summary>
        public static string ApplyScript(DnsProvider provider, IEnumerable<int> adapterIndexes)
        {
            var indexes = adapterIndexes.Distinct().ToList();
            if (indexes.Count == 0) throw new ArgumentException("No adapters.");
            foreach (string a in provider.All) if (!IsAddress(a)) throw new ArgumentException("Bad DNS address: " + a);
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference='Stop'");
            foreach (int i in indexes)
            {
                if (provider.IsAutomatic)
                    sb.AppendLine("Set-DnsClientServerAddress -InterfaceIndex " + i + " -ResetServerAddresses");
                else
                    sb.AppendLine("Set-DnsClientServerAddress -InterfaceIndex " + i + " -ServerAddresses @(" + string.Join(",", provider.All.Select(Cmd.PsQuote)) + ")");
                sb.AppendLine("Write-Output ('OK|' + " + i + ")");
            }
            sb.AppendLine("try { Clear-DnsClientCache } catch {}");
            return sb.ToString();
        }

        /// <summary>The PowerShell that puts back what <see cref="DnsPrevious"/> saved: reset to automatic first, then re-enter any servers that were typed in.</summary>
        public static string RestoreScript(DnsPrevious previous)
        {
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference='Stop'");
            foreach (var a in previous.Adapters)
            {
                var servers = new List<string>();
                if (a.StaticV4) servers.AddRange(a.V4);
                if (a.StaticV6) servers.AddRange(a.V6);
                foreach (string s in servers) if (!IsAddress(s)) throw new ArgumentException("Bad DNS address in the saved file: " + s);
                // Match by name first: adapter numbers can change after a driver update.
                sb.AppendLine("$idx = (Get-NetAdapter -Physical | Where-Object Name -eq " + Cmd.PsQuote(a.Name) + " | Select-Object -First 1).InterfaceIndex");
                sb.AppendLine("if (-not $idx) { $idx = " + a.Index + " }");
                sb.AppendLine("Set-DnsClientServerAddress -InterfaceIndex $idx -ResetServerAddresses");
                if (servers.Count > 0)
                    sb.AppendLine("Set-DnsClientServerAddress -InterfaceIndex $idx -ServerAddresses @(" + string.Join(",", servers.Select(Cmd.PsQuote)) + ")");
                sb.AppendLine("Write-Output ('OK|' + $idx)");
            }
            sb.AppendLine("try { Clear-DnsClientCache } catch {}");
            return sb.ToString();
        }

        // ---- remembering the previous servers ----

        public static string PreviousFile { get { return Path.Combine(Paths.Data, "dns-previous.json"); } }

        public static DnsPrevious Snapshot(IEnumerable<DnsAdapter> adapters, string newProviderId)
        {
            var p = new DnsPrevious { Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Provider = newProviderId };
            foreach (var a in adapters)
                p.Adapters.Add(new DnsPreviousAdapter { Name = a.Name, Index = a.Index, StaticV4 = a.StaticV4, StaticV6 = a.StaticV6, V4 = a.V4.ToList(), V6 = a.V6.ToList() });
            return p;
        }

        public static void SavePrevious(DnsPrevious previous, string file = null)
        {
            AtomicFile.WriteAllText(file ?? PreviousFile, Json.SerializeIndented(previous));
        }

        /// <summary>Null when nothing was saved or the file is unreadable.</summary>
        public static DnsPrevious LoadPrevious(string file = null)
        {
            try
            {
                string text = AtomicFile.ReadAllTextOrNull(file ?? PreviousFile);
                if (string.IsNullOrWhiteSpace(text)) return null;
                var p = Json.Deserialize<DnsPrevious>(text);
                return p != null && p.Adapters != null && p.Adapters.Count > 0 ? p : null;
            }
            catch (Exception) { return null; }
        }

        public static void ClearPrevious(string file = null)
        {
            try { File.Delete(file ?? PreviousFile); } catch (Exception) { }
        }

        /// <summary>
        /// Keeps the earliest saved servers: adapters already in <paramref name="existing"/> keep their original entry, and adapters seen for the first time are added.
        /// Without this, a second change would make Undo go back to the first change instead of to what the user started with.
        /// </summary>
        public static DnsPrevious MergeSnapshots(DnsPrevious existing, DnsPrevious fresh)
        {
            if (existing == null || existing.Adapters.Count == 0) return fresh;
            var merged = new DnsPrevious { Saved = existing.Saved, Provider = fresh.Provider };
            merged.Adapters.AddRange(existing.Adapters);
            foreach (var a in fresh.Adapters)
                if (!merged.Adapters.Any(x => string.Equals(x.Name, a.Name, StringComparison.OrdinalIgnoreCase))) merged.Adapters.Add(a);
            return merged;
        }

        /// <summary>
        /// Changes the servers of the adapters that are connected right now. The adapter list is read again here, so a Wi-Fi to Ethernet switch since the panel loaded is noticed:
        /// when the connected set differs from <paramref name="adapters"/> (what the person confirmed) this throws and nothing changes.
        /// The first saved servers are kept until Undo; a change that is not needed, or that fails before touching anything, leaves the saved file as it was.
        /// </summary>
        public static void Apply(DnsProvider provider, IList<DnsAdapter> adapters)
        {
            if (adapters.Count == 0) throw new InvalidOperationException("There is no connected network adapter to change.");
            var fresh = ReadAdapters();
            if (fresh.Count == 0) throw new InvalidOperationException("There is no connected network adapter to change.");
            var shown = new HashSet<int>(adapters.Select(a => a.Index));
            var now = new HashSet<int>(fresh.Select(a => a.Index));
            if (!shown.SetEquals(now))
                throw new InvalidOperationException("Your network connections changed while this page was open. Nothing was changed. Choose Check again, then try again.");
            if (fresh.All(a => { var m = Match(a); return m != null && m.Id == provider.Id; }))
                return;   // already set: leave the saved servers alone

            string oldText = null;
            try { oldText = AtomicFile.ReadAllTextOrNull(PreviousFile); } catch (Exception) { }
            bool hadOld = LoadPrevious() != null;
            SavePrevious(MergeSnapshots(LoadPrevious(), Snapshot(fresh, provider.Id)));
            var r = Cmd.RunPowerShell(ApplyScript(provider, fresh.Select(a => a.Index)), 60000);
            if (!r.Ok)
            {
                bool changedSome = (r.Output ?? "").Contains("OK|");
                if (!changedSome)
                {
                    // Nothing was touched: put the file back as it was so Undo is not offered for a change that never happened.
                    if (hadOld && oldText != null) { try { AtomicFile.WriteAllText(PreviousFile, oldText); } catch (Exception) { } }
                    else ClearPrevious();
                }
                throw new InvalidOperationException(Explain(r));
            }
        }

        public static void Undo()
        {
            var previous = LoadPrevious();
            if (previous == null) throw new InvalidOperationException("There is nothing to undo.");
            var r = Cmd.RunPowerShell(RestoreScript(previous), 60000);
            if (!r.Ok) throw new InvalidOperationException(Explain(r));
            ClearPrevious();
        }

        static string Explain(ProcessResult r)
        {
            string text = FirstLine(r.Error);
            if (text.Length == 0) text = FirstLine(r.Output);
            if (r.TimedOut) text = "Windows took too long to answer.";
            if (text.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("administrator", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Windows refused the change. Run Tweak Bench as administrator and try again.";
            return text.Length == 0 ? "Windows did not accept the change." : text;
        }
    }
}
