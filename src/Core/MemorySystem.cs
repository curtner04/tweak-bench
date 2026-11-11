using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TweakBench.Core
{
    /// <summary>
    /// A complete in-memory stand-in for Windows: registry tree, services, scheduled tasks, power values and a command log.
    /// Tests and the screenshot/demo mode use it so they never touch the real PC. It behaves like the real thing where it
    /// matters: keys are case-insensitive, writes create keys, empty keys can be removed, and writes need administrator
    /// rights for HKLM, services, tasks and power settings.
    /// </summary>
    public sealed class MemorySystem : ISystem
    {
        sealed class Node
        {
            public readonly Dictionary<string, Node> Keys = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, RegData> Values = new Dictionary<string, RegData>(StringComparer.OrdinalIgnoreCase);
        }

        readonly Dictionary<Hive, Node> roots = new Dictionary<Hive, Node>
        {
            { Hive.Machine, new Node() }, { Hive.User, new Node() }, { Hive.DefaultUser, new Node() }
        };
        readonly Dictionary<string, ServiceInfo> services = new Dictionary<string, ServiceInfo>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, bool> tasks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, uint> power = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        readonly object gate = new object();
        readonly Dictionary<string, string> powerPlans = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { BalancedPlan, "Balanced" }, { "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "High performance" },
            { "a1841308-3541-4fab-bc81-f71556f20b4a", "Power saver" }, { "e9a42b02-d5df-448d-aa00-03f14749eb61", "Ultimate Performance" },
        };
        // Plans Windows has built in but does not list until they are created with /duplicatescheme.
        readonly HashSet<string> hiddenPlans = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "e9a42b02-d5df-448d-aa00-03f14749eb61" };
        string teredoType = "default";
        bool seeding;
        const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
        const string PowerSchemesKey = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
        const string PowerKeyPath = @"SYSTEM\CurrentControlSet\Control\Power";

        public MemorySystem(int build = 26300, string edition = "Professional", bool elevated = true)
        {
            Build = build;
            Edition = edition;
            IsElevated = elevated;
            SeedWindowsVersion();
            SeedPower();
        }

        public bool IsElevated { get; set; }
        public int Build { get; set; }
        public string Edition { get; set; }

        /// <summary>Every <see cref="Run"/> call as "file args", in order.</summary>
        public List<string> Commands { get; private set; } = new List<string>();
        /// <summary>Optional fake for <see cref="Run"/>. Return null (or leave unset) for "exit code 0, no output".</summary>
        public Func<string, string, ProcessResult> RunHandler;
        /// <summary>Tests: every write (registry, service, task, power) throws <see cref="UnauthorizedAccessException"/>.</summary>
        public bool FailWrites;
        /// <summary>
        /// Tests: a write throws when this returns true for its description. Descriptions look like
        /// "reg HKCU\Software\X\Name", "service Name", "task \Path\Name" or "power SUB/SETTING/ac".
        /// </summary>
        public Func<string, bool> FailWhen;

        // ------------------------------------------------------------------------------------------------ setup helpers

        void SeedWindowsVersion()
        {
            const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            // Like the real registry: ProductName still says "Windows 10" on Windows 11.
            string product = string.Equals(Edition, "Core", StringComparison.OrdinalIgnoreCase) ? "Windows 10 Home" :
                string.Equals(Edition, "Professional", StringComparison.OrdinalIgnoreCase) ? "Windows 10 Pro" : "Windows 10 " + Edition;
            RawWrite(Hive.Machine, key, "ProductName", RegKind.String, product);
            RawWrite(Hive.Machine, key, "EditionID", RegKind.String, Edition ?? "");
            RawWrite(Hive.Machine, key, "CurrentBuildNumber", RegKind.String, Build.ToString(CultureInfo.InvariantCulture));
            RawWrite(Hive.Machine, key, "DisplayVersion", RegKind.String, Build >= 26300 ? "26H2" : Build >= 26200 ? "25H2" : Build >= 26100 ? "24H2" : Build >= 22631 ? "23H2" : "22H2");
            RawWrite(Hive.Machine, key, "UBR", RegKind.Dword, 1000u);
        }

        void SeedPower()
        {
            RawWrite(Hive.Machine, PowerSchemesKey, "ActivePowerScheme", RegKind.String, BalancedPlan);
            RawWrite(Hive.Machine, PowerKeyPath, "HibernateEnabled", RegKind.Dword, 1u);
        }

        public void AddService(string name, StartMode start, bool running, string displayName = null)
        {
            lock (gate) services[name] = new ServiceInfo { Name = name, DisplayName = displayName ?? name, Start = start, Running = running };
        }

        public void AddTask(string path, bool enabled)
        {
            lock (gate) tasks[NormalizeTask(path)] = enabled;
        }

        /// <summary>Adds a power setting of the active plan (plugged in and battery values).</summary>
        public void AddPower(string subgroup, string setting, uint ac, uint dc)
        {
            lock (gate) { power[PowerKey(subgroup, setting, true)] = ac; power[PowerKey(subgroup, setting, false)] = dc; }
        }

        /// <summary>Sets a registry value without any permission or failure checks (test and demo set-up).</summary>
        public void Seed(Hive hive, string path, string name, RegKind kind, object data) { RawWrite(hive, path, name, kind, data); }

        /// <summary>
        /// Adds everything the catalog refers to in its Windows-default state: services and tasks that exist, power values, and the
        /// default value of every registry setting that has one. Settings with no default stay absent. Anything already present is kept.
        /// </summary>
        public void SeedCatalogDefaults()
        {
            try { SeedCatalogDefaultsCore(); } finally { SeedDone(); }
        }

        void SeedCatalogDefaultsCore()
        {
            foreach (Tweak t in Catalog.All)
            {
                foreach (Change c in t.Changes)
                {
                    var reg = c as RegValue;
                    var svc = c as ServiceStart;
                    var task = c as TaskState;
                    var pow = c as PowerValue;
                    lock (gate)
                    {
                        if (reg != null)
                        {
                            if (reg.Default != null && string.IsNullOrEmpty(reg.DefaultDeletesKey) && !HasValue(reg.Hive, reg.Path, reg.Name))
                                RawWrite(reg.Hive, reg.Path, reg.Name, reg.ValueKind, reg.Default);
                        }
                        else if (svc != null)
                        {
                            if (!services.ContainsKey(svc.Service))
                            {
                                bool auto = svc.Default == StartMode.Automatic || svc.Default == StartMode.AutomaticDelayed || svc.Default == StartMode.Boot || svc.Default == StartMode.System;
                                services[svc.Service] = new ServiceInfo { Name = svc.Service, DisplayName = svc.Service, Start = svc.Default, Running = auto };
                            }
                        }
                        else if (task != null)
                        {
                            string key = NormalizeTask(task.TaskPath);
                            if (!tasks.ContainsKey(key)) tasks[key] = !task.EnabledWhenOn;
                        }
                        else if (pow != null)
                        {
                            if (!power.ContainsKey(PowerKey(pow.Subgroup, pow.Setting, true))) power[PowerKey(pow.Subgroup, pow.Setting, true)] = pow.DefaultAc;
                            if (!power.ContainsKey(PowerKey(pow.Subgroup, pow.Setting, false))) power[PowerKey(pow.Subgroup, pow.Setting, false)] = pow.DefaultDc;
                        }
                        else if (!(c is CustomChange))
                        {
                            // A change type the catalog defines itself: put it into its Windows-default state the way the app would.
                            seeding = true;
                            try { c.RestoreDefault(this); } catch (Exception) { } finally { seeding = false; }
                        }
                    }
                }
            }
        }

        void SeedDone() { lock (gate) Commands.Clear(); }

        bool HasValue(Hive hive, string path, string name)
        {
            Node n = FindNode(hive, path, false);
            return n != null && n.Values.ContainsKey(name ?? "");
        }

        /// <summary>
        /// A PC for screenshots: a realistic mix of settings already on (file extensions shown, dark mode, a handful of privacy and
        /// clean-up tweaks, one half-applied) with the rest at the Windows defaults. "Stop automatic restarts" stays off so the
        /// start page shows the invitation. Never touches the real PC.
        /// </summary>
        public static MemorySystem Demo()
        {
            var sys = new MemorySystem(26300, "Professional", true);
            IList<Tweak> all;
            try { all = Catalog.All; }
            catch (Exception) { return sys; }
            sys.SeedCatalogDefaults();

            var explicitOn = new[] { "explorer.show-extensions", "explorer.show-file-extensions", "system.dark-mode", "system.dark-theme", "system.app-dark-mode" };
            var neverOn = new[] { "update.no-auto-restart" };
            int partial = 0;
            foreach (Tweak t in all)
            {
                if (neverOn.Contains(t.Id)) continue;
                if (t.Risk == Risk.Advanced) continue;
                if (t.MinBuild > sys.Build || (t.MaxBuild > 0 && t.MaxBuild < sys.Build)) continue;
                if (t.Changes.Any(c => c is CustomChange)) continue;
                int hash = 17;
                foreach (char ch in t.Id) hash = unchecked(hash * 31 + ch);
                hash &= 0x7fffffff;
                bool on = explicitOn.Contains(t.Id) || (t.Risk == Risk.Safe && hash % 5 < 2);
                try
                {
                    if (on) { foreach (Change c in t.Changes) c.Apply(sys); }
                    else if (partial < 2 && t.Changes.Count > 1 && t.Risk == Risk.Safe && hash % 7 == 3)
                    {
                        // One of several changes applied: the card shows "Partly on".
                        t.Changes[0].Apply(sys);
                        partial++;
                    }
                }
                catch (Exception) { }
            }
            sys.Commands.Clear();
            return sys;
        }

        // ------------------------------------------------------------------------------------------------ registry tree

        static string[] Parts(string path)
        {
            return (path ?? "").Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        }

        Node FindNode(Hive hive, string path, bool create)
        {
            Node n = roots[hive];
            foreach (string part in Parts(path))
            {
                Node child;
                if (!n.Keys.TryGetValue(part, out child))
                {
                    if (!create) return null;
                    child = new Node();
                    n.Keys[part] = child;
                }
                n = child;
            }
            return n;
        }

        static object CloneData(object data)
        {
            var s = data as string[]; if (s != null) return (string[])s.Clone();
            var b = data as byte[]; if (b != null) return (byte[])b.Clone();
            return data;
        }

        void RawWrite(Hive hive, string path, string name, RegKind kind, object data)
        {
            lock (gate)
            {
                Node n = FindNode(hive, path, true);
                object stored = kind == RegKind.Dword || kind == RegKind.Qword ? Reg.Normalize(kind, data) : CloneData(data);
                if (kind == RegKind.String || kind == RegKind.ExpandString) stored = (string)data ?? "";
                n.Values[name ?? ""] = new RegData(kind, stored);
            }
        }

        void CheckWrite(Hive hive, string what, bool needsAdmin)
        {
            if (seeding) return;
            if (FailWrites) throw new UnauthorizedAccessException("Windows did not allow this change (simulated failure).");
            if (needsAdmin && !IsElevated) throw new UnauthorizedAccessException("Windows did not allow this change: administrator rights are needed (" + what + ").");
            if (FailWhen != null && FailWhen(what)) throw new UnauthorizedAccessException("Windows did not allow this change (simulated failure for " + what + ").");
        }

        public RegData ReadValue(Hive hive, string path, string name)
        {
            lock (gate)
            {
                Node n = FindNode(hive, path, false);
                RegData d;
                if (n == null || !n.Values.TryGetValue(name ?? "", out d)) return null;
                return new RegData(d.Kind, CloneData(d.Data));
            }
        }

        public bool KeyExists(Hive hive, string path) { lock (gate) return FindNode(hive, path, false) != null; }

        public bool KeyIsEmpty(Hive hive, string path)
        {
            lock (gate)
            {
                Node n = FindNode(hive, path, false);
                return n != null && n.Keys.Count == 0 && n.Values.Count == 0;
            }
        }

        public void WriteValue(Hive hive, string path, string name, RegKind kind, object data)
        {
            CheckWrite(hive, "write reg " + Reg.Root(hive) + "\\" + path + "\\" + name, hive != Hive.User);
            RawWrite(hive, path, name, kind, data);
        }

        public void DeleteValue(Hive hive, string path, string name)
        {
            CheckWrite(hive, "delete reg " + Reg.Root(hive) + "\\" + path + "\\" + name, hive != Hive.User);
            lock (gate)
            {
                Node n = FindNode(hive, path, false);
                if (n != null) n.Values.Remove(name ?? "");
            }
        }

        public void DeleteKey(Hive hive, string path)
        {
            CheckWrite(hive, "delete key " + Reg.Root(hive) + "\\" + path, hive != Hive.User);
            lock (gate)
            {
                string[] parts = Parts(path);
                if (parts.Length == 0) throw new ArgumentException("Refusing to delete a registry root.");
                Node parent = roots[hive];
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    Node child;
                    if (!parent.Keys.TryGetValue(parts[i], out child)) return;
                    parent = child;
                }
                parent.Keys.Remove(parts[parts.Length - 1]);
            }
        }

        // ------------------------------------------------------------------------------------------------ services

        public ServiceInfo GetService(string name)
        {
            lock (gate)
            {
                ServiceInfo s;
                if (name == null || !services.TryGetValue(name, out s)) return null;
                return new ServiceInfo { Name = s.Name, DisplayName = s.DisplayName, Start = s.Start, Running = s.Running };
            }
        }

        ServiceInfo Existing(string name)
        {
            ServiceInfo s;
            if (name == null || !services.TryGetValue(name, out s)) throw new InvalidOperationException("The " + name + " service is not installed on this PC.");
            return s;
        }

        public void SetServiceStart(string name, StartMode mode)
        {
            CheckWrite(Hive.Machine, "service " + name, true);
            lock (gate) Existing(name).Start = mode;
        }

        public void StartService(string name)
        {
            CheckWrite(Hive.Machine, "service " + name, true);
            lock (gate)
            {
                ServiceInfo s = Existing(name);
                if (s.Start == StartMode.Disabled) throw new InvalidOperationException("The " + name + " service is disabled and cannot be started.");
                s.Running = true;
            }
        }

        public void StopService(string name)
        {
            CheckWrite(Hive.Machine, "service " + name, true);
            lock (gate) Existing(name).Running = false;
        }

        // ------------------------------------------------------------------------------------------------ tasks

        static string NormalizeTask(string path)
        {
            string p = (path ?? "").Replace('/', '\\');
            return p.StartsWith("\\", StringComparison.Ordinal) ? p : "\\" + p;
        }

        public bool? GetTaskEnabled(string path)
        {
            lock (gate)
            {
                bool e;
                return tasks.TryGetValue(NormalizeTask(path), out e) ? (bool?)e : null;
            }
        }

        public void SetTaskEnabled(string path, bool enabled)
        {
            CheckWrite(Hive.Machine, "task " + NormalizeTask(path), true);
            lock (gate)
            {
                string key = NormalizeTask(path);
                if (!tasks.ContainsKey(key)) throw new InvalidOperationException("The scheduled task " + path + " does not exist on this PC.");
                tasks[key] = enabled;
            }
        }

        // ------------------------------------------------------------------------------------------------ power

        static string PowerKey(string subgroup, string setting, bool ac) { return (subgroup ?? "") + "/" + (setting ?? "") + "/" + (ac ? "ac" : "dc"); }

        public uint? GetPowerValue(string subgroup, string setting, bool ac)
        {
            lock (gate)
            {
                uint v;
                return power.TryGetValue(PowerKey(subgroup, setting, ac), out v) ? (uint?)v : null;
            }
        }

        public void SetPowerValue(string subgroup, string setting, bool ac, uint value)
        {
            CheckWrite(Hive.Machine, "power " + PowerKey(subgroup, setting, ac), true);
            lock (gate) power[PowerKey(subgroup, setting, ac)] = value;
        }

        // ------------------------------------------------------------------------------------------------ programs

        public ProcessResult Run(string file, string arguments, int timeoutMs = 120000)
        {
            lock (gate) Commands.Add(file + " " + arguments);
            ProcessResult r = RunHandler == null ? null : RunHandler(file, arguments);
            if (r == null) r = Emulate(file, arguments);
            return r ?? new ProcessResult { ExitCode = 0 };
        }

        // ------------------------------------------------------------------------------------------------ tiny command emulation

        static ProcessResult Text(string output) { return new ProcessResult { ExitCode = 0, Output = output + Environment.NewLine }; }
        static ProcessResult Fail(string message) { return new ProcessResult { ExitCode = 1, Error = message + Environment.NewLine }; }

        /// <summary>
        /// When no <see cref="RunHandler"/> answers, a few commands the catalog uses behave like Windows: powercfg power plans and
        /// hibernation, and netsh Teredo. Anything else succeeds with no output.
        /// </summary>
        ProcessResult Emulate(string file, string arguments)
        {
            string exe = (System.IO.Path.GetFileName(file ?? "") ?? "").ToLowerInvariant();
            string a = (arguments ?? "").Trim();
            string lower = a.ToLowerInvariant();
            lock (gate)
            {
                if (exe == "powercfg.exe")
                {
                    RegData activeData = ReadValue(Hive.Machine, PowerSchemesKey, "ActivePowerScheme");
                    string active = (activeData == null ? BalancedPlan : (string)activeData.Data).Trim('{', '}').ToLowerInvariant();
                    string name;
                    if (lower == "/getactivescheme")
                        return Text("Power Scheme GUID: " + active + "  (" + (powerPlans.TryGetValue(active, out name) ? name : "Custom") + ")");
                    if (lower == "/list" || lower == "/l")
                    {
                        var lines = new List<string> { "Existing Power Schemes (* Active)", "-----------------------------------" };
                        foreach (var kv in powerPlans)
                            if (!hiddenPlans.Contains(kv.Key)) lines.Add("Power Scheme GUID: " + kv.Key + "  (" + kv.Value + ")" + (string.Equals(kv.Key, active, StringComparison.OrdinalIgnoreCase) ? " *" : ""));
                        return Text(string.Join(Environment.NewLine, lines));
                    }
                    if (lower.StartsWith("/setactive ", StringComparison.Ordinal))
                    {
                        string guid = lower.Substring("/setactive ".Length).Trim().Trim('{', '}');
                        if (guid == "scheme_current") return Text("");
                        if (!powerPlans.ContainsKey(guid) || hiddenPlans.Contains(guid)) return Fail("The power scheme, subgroup or setting specified does not exist.");
                        if (!IsElevated) return new ProcessResult { ExitCode = 5, Error = "Access is denied." };
                        RawWrite(Hive.Machine, PowerSchemesKey, "ActivePowerScheme", RegKind.String, guid);
                        return Text("");
                    }
                    if (lower.StartsWith("/duplicatescheme ", StringComparison.Ordinal))
                    {
                        string template = lower.Substring("/duplicatescheme ".Length).Trim().Split(' ')[0].Trim('{', '}');
                        string templateName;
                        if (!powerPlans.TryGetValue(template, out templateName)) return Fail("The power scheme, subgroup or setting specified does not exist.");
                        string created = Guid.NewGuid().ToString();
                        powerPlans[created] = templateName;
                        return Text("Power Scheme GUID: " + created + "  (" + templateName + ")");
                    }
                    if (lower.StartsWith("/hibernate ", StringComparison.Ordinal) || lower.StartsWith("-hibernate ", StringComparison.Ordinal))
                    {
                        string mode = lower.Substring("/hibernate ".Length).Trim();
                        if (mode == "off" || mode == "on") RawWrite(Hive.Machine, PowerKeyPath, "HibernateEnabled", RegKind.Dword, mode == "on" ? 1u : 0u);
                        return Text("");
                    }
                }
                else if (exe == "netsh.exe")
                {
                    if (lower == "interface teredo show state")
                        return Text("Teredo Parameters" + Environment.NewLine + "---------------------------------------------" + Environment.NewLine + "Type                    : " + teredoType + Environment.NewLine + "Server Name             : win10.ipv6.microsoft.com." + Environment.NewLine + "State                   : offline");
                    if (lower.StartsWith("interface teredo set state ", StringComparison.Ordinal))
                    {
                        teredoType = lower.Substring("interface teredo set state ".Length).Trim().Split(' ')[0];
                        return Text("Ok.");
                    }
                }
            }
            return null;
        }
    }
}
