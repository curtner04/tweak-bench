using System;
using System.Collections.Generic;

namespace TweakBench.Core
{
    /// <summary>
    /// The Teredo type ("netsh interface teredo set state ..."). On = disabled. The type that was set before is saved, so undo
    /// puts back client, enterpriseclient, server and so on exactly, not just "default".
    /// </summary>
    public sealed class TeredoStateChange : Change
    {
        static readonly string[] KnownTypes = { "default", "disabled", "client", "enterpriseclient", "natawareclient", "server" };

        public override string Kind { get { return "teredo"; } }
        public override bool NeedsAdmin { get { return true; } }
        public override string Describe() { return "Teredo state: disabled (netsh interface teredo set state disabled)"; }

        /// <summary>The Type line of "netsh interface teredo show state", lower case, or null when it cannot be read. The keywords are not translated by netsh.</summary>
        public static string ReadType(ISystem sys)
        {
            ProcessResult r = sys.Run(ProcessRunner.System32("netsh.exe"), "interface teredo show state", 20000);
            if (r == null || r.ExitCode != 0) return null;
            foreach (string raw in (r.Output ?? "").Split('\n'))
            {
                string line = raw.Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string value = line.Substring(colon + 1).Trim().ToLowerInvariant();
                if (Array.IndexOf(KnownTypes, value) >= 0) return value;
            }
            return null;
        }

        static void SetType(ISystem sys, string type) { Custom.Must(sys, ProcessRunner.System32("netsh.exe"), "interface teredo set state " + type); }

        public override string DescribeCurrent(ISystem sys)
        {
            string type = ReadType(sys);
            return type == null ? "unknown" : type == "disabled" ? "disabled" : type == "default" ? "default" : "enabled (" + type + ")";
        }

        public override bool? IsOn(ISystem sys)
        {
            string type = ReadType(sys);
            return type == null ? (bool?)null : type == "disabled";
        }

        public override Snapshot Capture(ISystem sys)
        {
            var snap = new Snapshot { Kind = Kind, What = Describe() };
            string type = ReadType(sys);
            if (type != null) snap.Set("type", type);
            snap.Was = DescribeCurrent(sys);
            return snap;
        }

        public override void Apply(ISystem sys) { SetType(sys, "disabled"); }

        public override void Restore(ISystem sys, Snapshot before)
        {
            string type = before.Get("type");
            if (type == null) return;   // it could not be read before the change
            SetType(sys, Array.IndexOf(KnownTypes, type) >= 0 ? type : "default");
        }

        public override void RestoreDefault(ISystem sys) { SetType(sys, "default"); }
    }

    public static partial class Catalog
    {

        static partial void AddNetwork(List<Tweak> list)
        {
            // Source: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-r2-and-2008/ee126159(v=ws.10)  (netsh interface teredo set state)
            list.Add(new Tweak
            {
                Id = "network.teredo-off", Category = "network",
                Title = "Turn off Teredo tunnelling",
                Summary = "Windows stops wrapping IPv6 traffic inside IPv4 packets to reach IPv6 sites through your router.",
                Why = "Teredo is a leftover transition feature that few networks need today. Turning it off removes one more way for outside traffic to reach your PC, and avoids an odd network adapter in Device Manager. A few online games and peer-to-peer apps used it to find other players.",
                OffMeans = "Windows uses the Teredo default and may create a tunnel when it has no native IPv6. Undo puts back the Teredo type that was set before (default if there is no history).",
                Caution = "Xbox app and Microsoft Store game multiplayer or party chat can stop working (they may report Teredo problems). Turn this card off if that happens.",
                Risk = Risk.Advanced, TakesEffect = Takes.Immediately,
                Keywords = new[] { "ipv6", "tunnel", "tunneling", "xbox live", "teredo adapter", "netsh", "nat type" },
                Changes = { new TeredoStateChange() },
            });

            // Source: https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows  (DisabledComponents = 0x20)
            list.Add(new Tweak
            {
                Id = "network.prefer-ipv4", Category = "network",
                Title = "Prefer IPv4 over IPv6",
                Summary = "When a site is reachable both ways, Windows tries the older IPv4 route first.",
                Why = "Works around sites, games or VPNs that are slow or fail because IPv6 is misconfigured on your network. IPv6 stays installed and still works when IPv4 is not available.",
                OffMeans = "Windows prefers IPv6 when a site offers it.",
                Risk = Risk.Advanced, TakesEffect = Takes.PcRestart,
                Keywords = new[] { "ipv6", "ipv4", "slow websites", "connection problems", "vpn", "disabledcomponents", "dns" },
                Changes = { Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", 0x20) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-dnsclient  (DNS Client: Turn off multicast name resolution)
            list.Add(new Tweak
            {
                Id = "network.llmnr-off", Category = "network",
                Title = "Turn off LLMNR name lookups",
                Summary = "Windows stops asking every PC on the local network to answer when it cannot find a name through DNS.",
                Why = "LLMNR can be abused by another device on a shared or public network to trick your PC into sending sign-in details. Turning it off is a common hardening step. Looking up other PCs by name still works through DNS.",
                OffMeans = "Windows may ask the local network for a name when DNS does not know it.",
                Caution = "A home device that has no DNS name, such as a few printers or NAS boxes, may no longer be found by name. Use its IP address instead.",
                Risk = Risk.Moderate, TakesEffect = Takes.PcRestart,
                Keywords = new[] { "multicast", "name resolution", "security", "hardening", "responder", "enablemulticast", "network discovery" },
                Changes = { Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-remoteassistance  (Solicited Remote Assistance; fAllowToGetHelp is the System Properties > Remote checkbox)
            list.Add(new Tweak
            {
                Id = "network.remote-assistance-off", Category = "network",
                Title = "Do not allow Remote Assistance connections",
                Summary = "Nobody can be invited to see or control this PC through Windows Remote Assistance.",
                Why = "Closes a remote-access feature most people never use. You can still get help through other tools such as Quick Assist, which asks for your permission each time.",
                OffMeans = "Windows allows Remote Assistance invitations (the box in System Properties, Remote tab, is ticked).",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "remote", "msra", "invitation", "support", "security", "hardening", "fallowtogethelp" },
                Changes = { Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, windowsDefault: 1) },
            });
        }
    }
}
