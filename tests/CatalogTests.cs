using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TweakBench.Core;

namespace TweakBench.Tests
{
    /// <summary>
    /// Tests written over whatever <see cref="Catalog.All"/> contains, so every catalog file is covered without edits here.
    /// Failures are reported by tweak id ("explorer.show-extensions: ...").
    /// </summary>
    public static class CatalogTests
    {
        public static void Run(T t)
        {
            LintSelfTest(t);

            IList<Tweak> all;
            try { all = Catalog.All; }
            catch (Exception ex)
            {
                t.Check(false, "Catalog.All could not be built: " + ex.Message.Replace("\n", " | "));
                return;
            }

            t.Check(all.Count > 0, "the catalog has tweaks");
            List<string> problems = Catalog.Validate(all);
            t.Check(problems.Count == 0, "Catalog.Validate passes" + (problems.Count == 0 ? "" : ": " + string.Join(" | ", problems)));

            Coverage(t, all);

            foreach (string p in Lint(all)) t.Check(false, p);

            ReadOnlyStatus(t, all);
            RoundTrips(t, all);
        }

        // ------------------------------------------------------------------------------------------------ coverage

        static void Coverage(T t, IList<Tweak> all)
        {
            foreach (Category c in Catalog.Categories)
                t.Check(all.Any(x => x.Category == c.Id), "category '" + c.Id + "' has at least one tweak");
            foreach (Preset p in Catalog.Presets)
                t.Check(all.Any(x => x.InPreset(p.Id)), "preset '" + p.Id + "' has at least one tweak");

            Tweak restart = Catalog.Find("update.no-auto-restart");
            t.Check(restart != null, "update.no-auto-restart exists (the Start page highlights it)");
            if (restart != null)
            {
                t.Check(restart.Risk != Risk.Advanced, "update.no-auto-restart is not hidden as Advanced");
                t.Check(restart.Recommended, "update.no-auto-restart is Recommended");
                t.Check(restart.NeedsAdmin, "update.no-auto-restart reports that it needs administrator rights");
            }
        }

        // ------------------------------------------------------------------------------------------------ lint

        static readonly Regex BannedWords = new Regex(@"\b(simply|just)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex IdFormat = new Regex(@"^[a-z0-9]+\.[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        /// <summary>Catalog rules from DESIGN.md as a list of "id: problem" lines. Public so the self-test can aim it at bad tweaks.</summary>
        public static List<string> Lint(IList<Tweak> list)
        {
            var problems = new List<string>();
            foreach (Tweak tw in list)
            {
                string id = tw.Id ?? "(no id)";
                Action<string> add = text => problems.Add(id + ": " + text);

                if (tw.Id == null || !IdFormat.IsMatch(tw.Id)) add("id must be lower-case words joined by '-' after 'category.'.");

                // Text rules.
                if (tw.Title != null)
                {
                    if (tw.Title.Length > 70) add("title is longer than 70 characters (" + tw.Title.Length + ").");
                    if (tw.Title.TrimEnd().EndsWith(".", StringComparison.Ordinal)) add("title must not end with a period.");
                }
                if (tw.Summary != null && !tw.Summary.TrimEnd().EndsWith(".", StringComparison.Ordinal)) add("summary must end with a period.");
                foreach (var field in new[] { Tuple.Create("title", tw.Title), Tuple.Create("summary", tw.Summary), Tuple.Create("why", tw.Why), Tuple.Create("off-means", tw.OffMeans), Tuple.Create("caution", tw.Caution) })
                {
                    if (field.Item2 == null) continue;
                    Match m = BannedWords.Match(field.Item2);
                    if (m.Success) add(field.Item1 + " uses the word \"" + m.Value + "\".");
                    if (field.Item2.Contains("  ")) add(field.Item1 + " has a double space.");
                    if (field.Item2 != field.Item2.Trim()) add(field.Item1 + " has leading or trailing spaces.");
                }

                // Admin rules.
                foreach (Change c in tw.Changes)
                {
                    var reg = c as RegValue;
                    if (reg != null && reg.Hive != Hive.User && !reg.NeedsAdmin) add("a " + Reg.Root(reg.Hive) + " registry change must report NeedsAdmin: " + reg.Describe());
                    if (reg != null && reg.Hive == Hive.User && reg.NeedsAdmin) add("an HKCU registry change must not need administrator rights: " + reg.Describe());
                    if ((c is ServiceStart || c is TaskState || c is PowerValue) && !c.NeedsAdmin) add("service, task and power changes must report NeedsAdmin: " + c.Describe());
                }
                bool needs = tw.Changes.Any(c => c != null && c.NeedsAdmin);
                if (tw.NeedsAdmin != needs) add("NeedsAdmin does not match its changes.");

                // Value types, no-op changes and policy defaults.
                foreach (Change c in tw.Changes)
                {
                    var reg = c as RegValue;
                    if (reg != null)
                    {
                        if (!TypeOk(reg.ValueKind, reg.On)) add("'on' value has the wrong .NET type for " + reg.ValueKind + ": " + reg.Describe());
                        if (reg.Default != null && !TypeOk(reg.ValueKind, reg.Default)) add("Windows default has the wrong .NET type for " + reg.ValueKind + ": " + reg.Describe());
                        if (reg.Missing != null && !TypeOk(reg.ValueKind, reg.Missing)) add("WhenMissing value has the wrong .NET type for " + reg.ValueKind + ": " + reg.Describe());
                        if (reg.Default != null && TypeOk(reg.ValueKind, reg.On) && TypeOk(reg.ValueKind, reg.Default) && Reg.Same(reg.ValueKind, reg.On, reg.ValueKind, reg.Default))
                            add("'on' equals the Windows default, so the change does nothing: " + reg.Describe());
                        if (reg.Path != null && reg.Path.IndexOf(@"\Policies\", StringComparison.OrdinalIgnoreCase) >= 0 && reg.Default != null && string.IsNullOrEmpty(reg.DefaultDeletesKey))
                            add("policy values must restore by deletion (leave windowsDefault null): " + reg.Describe());
                        if (reg.Path != null && reg.Path.Contains("/")) add("registry path uses '/': " + reg.Path);
                    }
                    var svc = c as ServiceStart;
                    if (svc != null && svc.On == svc.Default) add("service 'on' start type equals the Windows default: " + svc.Describe());
                    if (svc != null && svc.Service != null && svc.Service.IndexOfAny(new[] { '\\', '/', ' ' }) >= 0) add("service name looks wrong: " + svc.Service);
                    var task = c as TaskState;
                    if (task != null && (task.TaskPath == null || !task.TaskPath.StartsWith("\\", StringComparison.Ordinal))) add("task path must start with a backslash: " + task.TaskPath);
                    var pow = c as PowerValue;
                    if (pow != null && pow.OnAc == pow.DefaultAc && pow.OnDc == pow.DefaultDc) add("power 'on' values equal the Windows defaults: " + pow.Describe());
                }

                // Things a tweak must never do.
                foreach (Change c in tw.Changes)
                {
                    string forbidden = Forbidden(c);
                    if (forbidden != null) add("not allowed (" + forbidden + "): " + c.Describe());
                }
            }

            // Two tweaks writing the same thing must be alternatives of one ExclusiveGroup.
            var writers = new Dictionary<string, List<Tweak>>(StringComparer.OrdinalIgnoreCase);
            foreach (Tweak tw in list)
                foreach (Change c in tw.Changes)
                {
                    string key = WriteKey(c);
                    if (key == null) continue;
                    List<Tweak> owners;
                    if (!writers.TryGetValue(key, out owners)) writers[key] = owners = new List<Tweak>();
                    if (!owners.Contains(tw)) owners.Add(tw);
                }
            foreach (var kv in writers)
            {
                if (kv.Value.Count < 2) continue;
                bool sameGroup = !string.IsNullOrEmpty(kv.Value[0].ExclusiveGroup) && kv.Value.All(x => x.ExclusiveGroup == kv.Value[0].ExclusiveGroup);
                if (sameGroup) continue;
                foreach (Tweak owner in kv.Value)
                    problems.Add(owner.Id + ": writes " + kv.Key + ", which is also written by " + string.Join(", ", kv.Value.Where(x => x != owner).Select(x => x.Id)) + " (give them one ExclusiveGroup or remove the overlap).");
            }
            return problems;
        }

        static bool TypeOk(RegKind kind, object value)
        {
            switch (kind)
            {
                case RegKind.Dword: return value is uint;
                case RegKind.Qword: return value is ulong;
                case RegKind.String:
                case RegKind.ExpandString: return value is string;
                case RegKind.MultiString: return value is string[];
                default: return value is byte[];
            }
        }

        static string WriteKey(Change c)
        {
            var reg = c as RegValue;
            if (reg != null) return "registry value " + Reg.Root(reg.Hive) + "\\" + (reg.Path ?? "").Trim('\\') + "  " + reg.NameText;
            var svc = c as ServiceStart;
            if (svc != null) return "service " + svc.Service;
            var task = c as TaskState;
            if (task != null) return "scheduled task " + task.TaskPath;
            var pow = c as PowerValue;
            if (pow != null) return "power setting " + pow.Subgroup + "/" + pow.Setting;
            return null;
        }

        static readonly string[] UacNames = { "enablelua", "consentpromptbehavioradmin", "consentpromptbehavioruser", "promptonsecuredesktop", "filteradministratortoken", "localaccounttokenfilterpolicy" };
        static readonly string[] SmartScreenNames = { "smartscreenenabled", "enablesmartscreen", "enablewebcontentevaluation", "preventsmartscreenpromptoverride", "preventsmartscreenpromptoverrideforfiles" };
        static readonly string[] VbsNames = { "enablevirtualizationbasedsecurity", "hypervisorenforcedcodeintegrity", "requireplatformsecurityfeatures" };
        static readonly string[] SpectreNames = { "featuresettingsoverride", "featuresettingsoverridemask" };
        static readonly string[] ProtectedServices = { "WinDefend", "mpssvc", "WdNisSvc", "BFE", "wscsvc", "SecurityHealthService", "EventLog" };

        /// <summary>A short reason when the change weakens security or switches off Windows Update; otherwise null.</summary>
        static string Forbidden(Change c)
        {
            var reg = c as RegValue;
            if (reg != null)
            {
                string p = (reg.Path ?? "").ToLowerInvariant(), n = (reg.Name ?? "").ToLowerInvariant();
                if (p.Contains("windows defender") || p.Contains("microsoft defender") || p.Contains("windows advanced threat protection")) return "Microsoft Defender";
                if (p.Contains("windowsfirewall") || p.Contains("firewallpolicy") || p.Contains(@"sharedaccess\parameters")) return "firewall";
                if (SmartScreenNames.Contains(n)) return "SmartScreen";
                if (UacNames.Contains(n)) return "UAC";
                if (p.Contains("deviceguard") || VbsNames.Contains(n)) return "virtualization-based security / Memory Integrity";
                if (SpectreNames.Contains(n)) return "CPU vulnerability mitigations";
                if (n == "disablewindowsupdateaccess") return "Windows Update";
                if (n == "noautoupdate" && p.EndsWith(@"windowsupdate\au", StringComparison.Ordinal) && Reg.Same(reg.ValueKind, reg.On, RegKind.Dword, 1u)) return "turns Automatic Updates off (NoAutoUpdate=1)";
            }
            var svc = c as ServiceStart;
            if (svc != null)
            {
                if (svc.Service == "wuauserv" && svc.On == StartMode.Disabled) return "disables Windows Update";
                if (ProtectedServices.Contains(svc.Service, StringComparer.OrdinalIgnoreCase) && svc.On != StartMode.Automatic && svc.On != StartMode.AutomaticDelayed && svc.On != svc.Default) return "security service " + svc.Service;
            }
            var task = c as TaskState;
            if (task != null && task.TaskPath != null && task.TaskPath.IndexOf(@"\Windows Defender\", StringComparison.OrdinalIgnoreCase) >= 0 && !task.EnabledWhenOn) return "Microsoft Defender task";
            var custom = c as CustomChange;
            if (custom != null)
            {
                string text = (custom.Text ?? "").ToLowerInvariant();
                if (text.Contains("defender") || text.Contains("firewall") || text.Contains("smartscreen") || text.Contains("uac")) return "security feature named in a command";
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------------ lint self-test

        static void LintSelfTest(T t)
        {
            Func<string, bool> bad = id => Lint(new List<Tweak> { SelfTestTweaks().First(x => x.Id == id) }).Any(p => p.StartsWith(id + ":", StringComparison.Ordinal));
            foreach (Tweak tw in SelfTestTweaks().Where(x => x.Id.StartsWith("bad.", StringComparison.Ordinal)))
                t.Check(bad(tw.Id), "lint catches " + tw.Id);
            t.Check(!bad("good.fine"), "lint accepts a correct tweak");

            Tweak a = Tw("dup.a", Reg.Dword(Hive.User, "Software\\Dup", "V", 1, 0));
            Tweak b = Tw("dup.b", Reg.Dword(Hive.User, "Software\\Dup", "V", 2, 0));
            List<string> overlap = Lint(new List<Tweak> { a, b });
            t.Check(overlap.Count(p => p.StartsWith("dup.a:", StringComparison.Ordinal)) == 1 && overlap.Count(p => p.StartsWith("dup.b:", StringComparison.Ordinal)) == 1, "lint reports both tweaks that write the same value");
            a.ExclusiveGroup = b.ExclusiveGroup = "dup";
            t.Check(Lint(new List<Tweak> { a, b }).Count == 0, "tweaks in one ExclusiveGroup may write the same value");
            b.ExclusiveGroup = "other";
            t.Check(Lint(new List<Tweak> { a, b }).Count > 0, "different ExclusiveGroups do not excuse a shared value");
        }

        static Tweak Tw(string id, params Change[] changes)
        {
            var t = new Tweak { Id = id, Category = id.Substring(0, id.IndexOf('.')), Title = "Show a thing", Summary = "A thing is shown.", Why = "It is useful." };
            t.Changes.AddRange(changes);
            return t;
        }

        static List<Tweak> SelfTestTweaks()
        {
            var list = new List<Tweak>();
            list.Add(Tw("good.fine", Reg.Dword(Hive.User, "Software\\Good", "V", 1, 0), Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "NoAutoRebootWithLoggedOnUsers", 1), Svc.Start("DiagTrack", StartMode.Disabled, StartMode.Automatic)));
            list.Add(Tw("bad.defender", Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", 1)));
            list.Add(Tw("bad.uac", Reg.Dword(Hive.Machine, Reg.MachinePolicySystem, "EnableLUA", 0, 1)));
            list.Add(Tw("bad.uac-prompt", Reg.Dword(Hive.Machine, Reg.MachinePolicySystem, "ConsentPromptBehaviorAdmin", 0, 5)));
            list.Add(Tw("bad.smartscreen", Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0)));
            list.Add(Tw("bad.firewall", Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\WindowsFirewall\StandardProfile", "EnableFirewall", 0)));
            list.Add(Tw("bad.noautoupdate", Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "NoAutoUpdate", 1)));
            list.Add(Tw("bad.wuauserv", Svc.Start("wuauserv", StartMode.Disabled, StartMode.Manual)));
            list.Add(Tw("bad.defender-service", Svc.Start("WinDefend", StartMode.Disabled, StartMode.Automatic)));
            list.Add(Tw("bad.firewall-service", Svc.Start("mpssvc", StartMode.Manual, StartMode.Automatic)));
            list.Add(Tw("bad.vbs", Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0)));
            list.Add(Tw("bad.spectre", Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride", 3)));
            list.Add(Tw("bad.defender-command", Custom.Make("Run Set-MpPreference to turn off Defender scanning", s => false, s => { }, s => { })));
            list.Add(Tw("bad.defender-task", Sched.Disable(@"\Microsoft\Windows\Windows Defender\Windows Defender Scheduled Scan")));
            var longTitle = Tw("bad.long-title", Reg.Dword(Hive.User, "Software\\X", "V", 1, 0)); longTitle.Title = new string('a', 71);
            var dotTitle = Tw("bad.dot-title", Reg.Dword(Hive.User, "Software\\X", "V2", 1, 0)); dotTitle.Title = "Show a thing.";
            var noDot = Tw("bad.no-dot", Reg.Dword(Hive.User, "Software\\X", "V3", 1, 0)); noDot.Summary = "A thing is shown";
            var simply = Tw("bad.simply", Reg.Dword(Hive.User, "Software\\X", "V4", 1, 0)); simply.Why = "This will simply work.";
            var just = Tw("bad.just", Reg.Dword(Hive.User, "Software\\X", "V5", 1, 0)); just.Summary = "It is just a setting.";
            var policyDefault = Tw("bad.policy-default", Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\Example", "P", 1, 0));
            var wrongType = Tw("bad.wrong-type", new RegValue { Hive = Hive.User, Path = "Software\\X", Name = "T", ValueKind = RegKind.Dword, On = 1 });
            var noOp = Tw("bad.noop", Reg.Dword(Hive.User, "Software\\X", "N", 1, 1));
            var badId = Tw("bad.Not_Kebab", Reg.Dword(Hive.User, "Software\\X", "I", 1, 0));
            list.AddRange(new[] { longTitle, dotTitle, noDot, simply, just, policyDefault, wrongType, noOp, badId });
            return list;
        }

        // ------------------------------------------------------------------------------------------------ read-only status

        /// <summary>Hands out the same answers as a MemorySystem but refuses every write, and records commands.</summary>
        sealed class ReadOnlySystem : ISystem
        {
            readonly MemorySystem inner;
            public readonly List<string> Commands = new List<string>();
            public ReadOnlySystem(MemorySystem inner) { this.inner = inner; }
            public bool IsElevated { get { return inner.IsElevated; } }
            public int Build { get { return inner.Build; } }
            public string Edition { get { return inner.Edition; } }
            public RegData ReadValue(Hive hive, string path, string name) { return inner.ReadValue(hive, path, name); }
            public bool KeyExists(Hive hive, string path) { return inner.KeyExists(hive, path); }
            public bool KeyIsEmpty(Hive hive, string path) { return inner.KeyIsEmpty(hive, path); }
            public void WriteValue(Hive hive, string path, string name, RegKind kind, object data) { throw new InvalidOperationException("write " + path + "\\" + name); }
            public void DeleteValue(Hive hive, string path, string name) { throw new InvalidOperationException("delete " + path + "\\" + name); }
            public void DeleteKey(Hive hive, string path) { throw new InvalidOperationException("delete key " + path); }
            public ServiceInfo GetService(string name) { return inner.GetService(name); }
            public void SetServiceStart(string name, StartMode mode) { throw new InvalidOperationException("service " + name); }
            public void StartService(string name) { throw new InvalidOperationException("start " + name); }
            public void StopService(string name) { throw new InvalidOperationException("stop " + name); }
            public bool? GetTaskEnabled(string path) { return inner.GetTaskEnabled(path); }
            public void SetTaskEnabled(string path, bool enabled) { throw new InvalidOperationException("task " + path); }
            public uint? GetPowerValue(string subgroup, string setting, bool ac) { return inner.GetPowerValue(subgroup, setting, ac); }
            public void SetPowerValue(string subgroup, string setting, bool ac, uint value) { throw new InvalidOperationException("power " + setting); }
            public ProcessResult Run(string file, string arguments, int timeoutMs = 120000) { lock (Commands) Commands.Add(file + " " + arguments); return inner.Run(file, arguments, timeoutMs); }
        }

        static readonly Regex MutatingCommand = new Regex(@"(\badd\b|\bdelete\b|\bconfig\b|/set|\bset\b|\bdisable|\benable|\bremove|\binstall|\bstop\b|\bstart\b|\bimport\b|-Set-|Set-[A-Z]|New-[A-Z]|Remove-[A-Z]|Enable-[A-Z]|Disable-[A-Z]|/Enable-|/Disable-)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static void ReadOnlyStatus(T t, IList<Tweak> all)
        {
            // Reading the state of every tweak (and running the audit) must never write or run a changing command.
            var inner = MemorySystem.Demo();
            var guarded = new ReadOnlySystem(inner);
            var engine = new Engine(guarded, null);
            int unreadable = 0;
            foreach (Tweak tw in all)
            {
                try
                {
                    TweakStatus st = engine.Status(tw);
                    foreach (ChangeStatus cs in st.Changes)
                    {
                        if (string.IsNullOrWhiteSpace(cs.Change.Describe())) t.Check(false, tw.Id + ": a change has an empty description.");
                        if (st.State != TweakState.Unavailable && string.IsNullOrWhiteSpace(cs.Current) && cs.Unavailable == null) t.Check(false, tw.Id + ": a change has an empty 'current' text.");
                    }
                }
                catch (Exception ex) { unreadable++; t.Check(false, tw.Id + ": reading the state threw " + ex.GetType().Name + ": " + ex.Message); }
            }
            t.Equal(0, unreadable, "no tweak throws while its state is read");
            foreach (string cmd in guarded.Commands)
                t.Check(!MutatingCommand.IsMatch(Visible(cmd)), "reading state ran a command that looks like it changes something: " + Shorten(cmd));

            var text = new StringWriter();
            var guarded2 = new ReadOnlySystem(MemorySystem.Demo());
            try { Audit.Write(guarded2, text); t.Check(true, "Audit.Write does not write"); }
            catch (Exception ex) { t.Check(false, "Audit.Write wrote or failed: " + ex.Message); }
            string report = text.ToString();
            t.Check(all.All(x => report.Contains(x.Id)), "the audit lists every tweak");
            t.Check(report.Contains("Summary:"), "the audit ends with a summary");
            foreach (string cmd in guarded2.Commands)
                t.Check(!MutatingCommand.IsMatch(Visible(cmd)), "the audit ran a command that looks like it changes something: " + Shorten(cmd));

            // The demo PC shows a mix of states.
            var demoEngine = new Engine(MemorySystem.Demo(), null);
            var states = all.Select(x => demoEngine.Status(x).State).ToList();
            t.Check(states.Contains(TweakState.On) && states.Contains(TweakState.Off), "the demo PC has tweaks that are on and tweaks that are off");
            Tweak restart = Catalog.Find("update.no-auto-restart");
            if (restart != null) t.Equal(TweakState.Off, demoEngine.Status(restart).State, "update.no-auto-restart is off on the demo PC");
        }

        /// <summary>The command as it would read in a console; PowerShell scripts passed as -EncodedCommand are decoded.</summary>
        static string Visible(string cmd)
        {
            cmd = cmd.Replace(ProcessRunner.PowerShell, "powershell.exe");
            int i = cmd.IndexOf("-EncodedCommand ", StringComparison.Ordinal);
            if (i >= 0)
            {
                try
                {
                    string script = Encoding.Unicode.GetString(Convert.FromBase64String(cmd.Substring(i + "-EncodedCommand ".Length).Trim()));
                    cmd = cmd.Substring(0, i) + script.Replace("$ErrorActionPreference='Stop';", "");
                }
                catch (Exception) { }
            }
            return cmd;
        }

        static string Shorten(string s) { return s.Length > 120 ? s.Substring(0, 120) + "..." : s; }

        // ------------------------------------------------------------------------------------------------ round trips

        static MemorySystem Fresh()
        {
            var sys = new MemorySystem(26300, "Professional", true);
            sys.SeedCatalogDefaults();
            return sys;
        }

        static string StateWithoutCustom(ISystem sys, Tweak tw)
        {
            var parts = new List<string>();
            foreach (Change c in tw.Changes)
            {
                if (c is CustomChange) continue;
                string unavailable = null;
                try { unavailable = c.UnavailableReason(sys); } catch (Exception) { }
                if (unavailable != null && c.Optional) { parts.Add("(skipped)"); continue; }
                Snapshot s = c.Capture(sys);
                parts.Add(c.Kind + ":" + string.Join(",", s.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)));
            }
            return string.Join(" | ", parts);
        }

        static void RoundTrips(T t, IList<Tweak> all)
        {
            string folder = t.TempFolder();
            int n = 0, checkedCount = 0, unavailable = 0;
            foreach (Tweak tw in all)
            {
                n++;
                bool simple = tw.Changes.All(c => !(c is CustomChange));
                try
                {
                    MemorySystem sys = Fresh();
                    var engine = new Engine(sys, new HistoryStore(Path.Combine(folder, n + ".json")), Catalog.Find) { Machine = "PC-A", User = "tester" };
                    TweakStatus start = engine.Status(tw);
                    if (start.State == TweakState.Unavailable)
                    {
                        unavailable++;
                        bool excusable = tw.NotApplicable != null || tw.MinBuild > sys.Build || (tw.MaxBuild > 0 && tw.MaxBuild < sys.Build);
                        t.Check(excusable, tw.Id + ": unavailable on a standard Windows 11 Pro build 26300 (" + start.Reason + ")");
                        continue;
                    }
                    checkedCount++;
                    if (simple && start.State != TweakState.Off)
                    {
                        t.Check(false, tw.Id + ": reads as " + start.State + " on a PC with Windows defaults (expected Off). Check windowsDefault / WhenMissing / the change list.");
                        continue;
                    }

                    string before = StateWithoutCustom(sys, tw);

                    BatchResult on = engine.Apply(new[] { new PlanItem(tw, true) }, "on");
                    ItemResult onResult = on.Items[0];
                    if (!onResult.Ok || onResult.Skipped) { t.Check(false, tw.Id + ": turning on failed: " + onResult.Message); continue; }
                    if (simple) t.Equal(TweakState.On, engine.Status(tw).State, tw.Id + ": reads as On after turning on");
                    else t.Check(StateWithoutCustom(sys, tw) != before || tw.Changes.All(c => c is CustomChange), tw.Id + ": turning on changed nothing it can read");

                    BatchResult off = engine.Apply(new[] { new PlanItem(tw, false) }, "off");
                    ItemResult offResult = off.Items[0];
                    if (!offResult.Ok || offResult.Skipped) { t.Check(false, tw.Id + ": turning off failed: " + offResult.Message); continue; }
                    if (simple) t.Equal(TweakState.Off, engine.Status(tw).State, tw.Id + ": reads as Off after turning off");
                    t.Equal(before, StateWithoutCustom(sys, tw), tw.Id + ": turning off restores every change exactly");

                    BatchResult again = engine.Apply(new[] { new PlanItem(tw, true) }, "on again");
                    if (!again.Items[0].Ok || again.Items[0].Skipped) { t.Check(false, tw.Id + ": turning on a second time failed: " + again.Items[0].Message); continue; }
                    ItemResult undo = engine.Undo(again.Batch, again.Batch.Entries[0]);
                    t.Check(undo.Ok, tw.Id + ": undo of the second turn-on failed: " + undo.Message);
                    t.Equal(before, StateWithoutCustom(sys, tw), tw.Id + ": undo restores every change exactly");

                    // Without history, "off" falls back to the Windows defaults.
                    MemorySystem sys2 = Fresh();
                    var noHistory = new Engine(sys2, new HistoryStore(Path.Combine(folder, n + "-b.json")), Catalog.Find) { Machine = "PC-A", User = "tester" };
                    ItemResult on2 = noHistory.Apply(new[] { new PlanItem(tw, true) }, "on").Items[0];
                    if (!on2.Ok || on2.Skipped) { t.Check(false, tw.Id + ": turning on (second system) failed: " + on2.Message); continue; }
                    // Forget the saved values so the Windows-default path is used.
                    var forgotten = new Engine(sys2, new HistoryStore(Path.Combine(folder, n + "-c.json")), Catalog.Find) { Machine = "PC-A", User = "tester" };
                    ItemResult off2 = forgotten.Apply(new[] { new PlanItem(tw, false) }, "off").Items[0];
                    t.Check(off2.Ok, tw.Id + ": turning off without history failed: " + off2.Message);
                    if (simple) t.Equal(TweakState.Off, forgotten.Status(tw).State, tw.Id + ": without history, off restores Windows defaults and reads as Off");
                }
                catch (Exception ex)
                {
                    t.Check(false, tw.Id + ": round trip threw " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            t.Check(checkedCount > 0 || all.Count == 0, "round trips ran for at least one tweak");
        }
    }
}
