using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TweakBench.Core
{
    /// <summary>A registry value. "On" writes <see cref="On"/>; the Windows default writes <see cref="Default"/> or deletes the value.</summary>
    public sealed class RegValue : Change
    {
        public Hive Hive;
        public string Path;
        /// <summary>Value name; "" is the key's (Default) value.</summary>
        public string Name;
        public RegKind ValueKind;
        /// <summary>uint, ulong, string, string[] or byte[] matching <see cref="ValueKind"/>.</summary>
        public object On;
        /// <summary>Written by "restore Windows default". Null deletes the value instead.</summary>
        public object Default;
        /// <summary>How Windows behaves when the value is absent. Null: an absent value counts as off.</summary>
        public object Missing;
        /// <summary>When set, "restore Windows default" deletes this whole key (and everything under it) instead of the value.</summary>
        public string DefaultDeletesKey;

        public override string Kind { get { return "registry"; } }
        public override bool NeedsAdmin { get { return Hive != Hive.User; } }

        public string KeyText { get { return Reg.Root(Hive) + "\\" + Path; } }
        public string NameText { get { return Name == "" ? "(Default)" : Name; } }

        public override string Describe() { return KeyText + "  " + NameText + " = " + Reg.Format(ValueKind, On); }

        public override string DescribeCurrent(ISystem sys)
        {
            RegData d = sys.ReadValue(Hive, Path, Name);
            return d == null ? "not set" : Reg.Format(d.Kind, d.Data);
        }

        public override bool? IsOn(ISystem sys)
        {
            RegData d = sys.ReadValue(Hive, Path, Name);
            if (d == null) return Missing != null && Reg.Same(ValueKind, Missing, ValueKind, On);
            return Reg.Same(d.Kind, d.Data, ValueKind, On, StrictType);
        }

        /// <summary>
        /// True when a value of the wrong type (text where a number belongs) must not count as matching. Windows ignores
        /// wrongly typed Group Policy and system-wide values, so for those a "1" stored as text is not "on" and Apply rewrites it.
        /// Per-user Explorer-style settings tolerate it.
        /// </summary>
        public bool StrictType
        {
            get { string p = Path ?? ""; return Hive != Hive.User || p.IndexOf(@"\Policies\", StringComparison.OrdinalIgnoreCase) >= 0 || p.StartsWith(@"Policies\", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Fluent: how Windows behaves when the value is absent.</summary>
        public RegValue WhenMissing(object behavesAs) { Missing = Reg.Normalize(ValueKind, behavesAs); return this; }

        public override Snapshot Capture(ISystem sys)
        {
            var s = new Snapshot { Kind = Kind, What = KeyText + "  " + NameText };
            RegData d = sys.ReadValue(Hive, Path, Name);
            s.Set("exists", d == null ? "0" : "1");
            if (d != null) { s.Set("kind", d.Kind.ToString()); s.Set("data", Reg.Encode(d.Kind, d.Data)); }
            s.Set("missingFrom", Reg.FirstMissingKey(sys, Hive, Path) ?? "");
            s.Was = d == null ? "not set" : Reg.Format(d.Kind, d.Data);
            return s;
        }

        public override void Apply(ISystem sys) { sys.WriteValue(Hive, Path, Name, ValueKind, On); }

        public override void Restore(ISystem sys, Snapshot before)
        {
            if (before.Get("exists") == "1")
            {
                RegKind kind = (RegKind)Enum.Parse(typeof(RegKind), before.Get("kind"));
                sys.WriteValue(Hive, Path, Name, kind, Reg.Decode(kind, before.Get("data")));
                return;
            }
            sys.DeleteValue(Hive, Path, Name);
            // Remove keys that Tweak Bench itself created, as long as nothing else was put in them since.
            string missingFrom = before.Get("missingFrom");
            if (!string.IsNullOrEmpty(missingFrom)) Reg.RemoveEmptyKeys(sys, Hive, Path, missingFrom);
        }

        public override void RestoreDefault(ISystem sys)
        {
            if (!string.IsNullOrEmpty(DefaultDeletesKey)) { sys.DeleteKey(Hive, DefaultDeletesKey); return; }
            if (Default == null) sys.DeleteValue(Hive, Path, Name);
            else sys.WriteValue(Hive, Path, Name, ValueKind, Default);
        }
    }

    /// <summary>A Windows service's start type.</summary>
    public sealed class ServiceStart : Change
    {
        public string Service;
        public StartMode On;
        public StartMode Default;
        /// <summary>Stop the service right away when "on" sets it to Manual or Disabled (errors stopping it are ignored; the start type still applies after a restart).</summary>
        public bool StopNow = true;

        public override string Kind { get { return "service"; } }
        public override bool NeedsAdmin { get { return true; } }
        public override string Describe() { return "Service " + Service + ": start type " + Svc.Text(On) + (StopNow && (On == StartMode.Disabled || On == StartMode.Manual) ? " (stopped now)" : ""); }

        public override string DescribeCurrent(ISystem sys)
        {
            ServiceInfo s = sys.GetService(Service);
            return s == null ? "not installed" : Svc.Text(s.Start) + (s.Running ? ", running" : ", stopped");
        }

        public override string UnavailableReason(ISystem sys) { return sys.GetService(Service) == null ? "The " + Service + " service is not installed on this PC." : null; }

        public override bool? IsOn(ISystem sys)
        {
            ServiceInfo s = sys.GetService(Service);
            if (s == null) return null;
            return s.Start == On;
        }

        public override Snapshot Capture(ISystem sys)
        {
            var snap = new Snapshot { Kind = Kind, What = "Service " + Service };
            ServiceInfo s = sys.GetService(Service);
            snap.Set("exists", s == null ? "0" : "1");
            if (s != null) { snap.Set("start", ((int)s.Start).ToString(CultureInfo.InvariantCulture)); snap.Set("running", s.Running ? "1" : "0"); }
            snap.Was = s == null ? "not installed" : Svc.Text(s.Start) + (s.Running ? ", running" : ", stopped");
            return snap;
        }

        public override void Apply(ISystem sys)
        {
            sys.SetServiceStart(Service, On);
            if (StopNow && (On == StartMode.Disabled || On == StartMode.Manual))
            {
                ServiceInfo s = sys.GetService(Service);
                if (s != null && s.Running) { try { sys.StopService(Service); } catch (Exception) { } }
            }
        }

        public override void Restore(ISystem sys, Snapshot before)
        {
            if (before.Get("exists") != "1") return;
            sys.SetServiceStart(Service, (StartMode)int.Parse(before.Get("start"), CultureInfo.InvariantCulture));
            if (before.Get("running") == "1")
            {
                ServiceInfo s = sys.GetService(Service);
                if (s != null && !s.Running) { try { sys.StartService(Service); } catch (Exception) { } }
            }
        }

        public override void RestoreDefault(ISystem sys)
        {
            sys.SetServiceStart(Service, Default);
            if (Default == StartMode.Automatic || Default == StartMode.AutomaticDelayed)
            {
                ServiceInfo s = sys.GetService(Service);
                if (s != null && !s.Running) { try { sys.StartService(Service); } catch (Exception) { } }
            }
        }
    }

    /// <summary>A scheduled task's enabled flag.</summary>
    public sealed class TaskState : Change
    {
        /// <summary>Full path, for example \Microsoft\Windows\Application Experience\ProgramDataUpdater.</summary>
        public string TaskPath;
        /// <summary>The task's enabled flag when the tweak is on (usually false: the tweak disables it).</summary>
        public bool EnabledWhenOn;

        public override string Kind { get { return "task"; } }
        public override bool NeedsAdmin { get { return true; } }
        public override string Describe() { return "Scheduled task " + TaskPath + ": " + (EnabledWhenOn ? "enabled" : "disabled"); }

        public override string DescribeCurrent(ISystem sys)
        {
            bool? e;
            try { e = sys.GetTaskEnabled(TaskPath); }
            catch (UnauthorizedAccessException) { return "could not be read (Windows did not allow it)"; }
            return e == null ? "not found" : e.Value ? "enabled" : "disabled";
        }

        // A task that exists but cannot be read (access denied) is not "missing": keep it in the tweak so the tweak shows
        // an unknown state instead of quietly ignoring it. Capture then fails, so nothing is changed without a way back.
        public override string UnavailableReason(ISystem sys)
        {
            try { return sys.GetTaskEnabled(TaskPath) == null ? "The scheduled task " + TaskPath + " does not exist on this PC." : null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public override bool? IsOn(ISystem sys)
        {
            bool? e;
            try { e = sys.GetTaskEnabled(TaskPath); }
            catch (UnauthorizedAccessException) { return null; }
            if (e == null) return null;
            return e.Value == EnabledWhenOn;
        }

        public override Snapshot Capture(ISystem sys)
        {
            var snap = new Snapshot { Kind = Kind, What = "Scheduled task " + TaskPath };
            bool? e = sys.GetTaskEnabled(TaskPath);
            snap.Set("exists", e == null ? "0" : "1");
            if (e != null) snap.Set("enabled", e.Value ? "1" : "0");
            snap.Was = e == null ? "not found" : e.Value ? "enabled" : "disabled";
            return snap;
        }

        public override void Apply(ISystem sys) { sys.SetTaskEnabled(TaskPath, EnabledWhenOn); }

        public override void Restore(ISystem sys, Snapshot before)
        {
            if (before.Get("exists") != "1") return;
            sys.SetTaskEnabled(TaskPath, before.Get("enabled") == "1");
        }

        public override void RestoreDefault(ISystem sys) { sys.SetTaskEnabled(TaskPath, !EnabledWhenOn); }
    }

    /// <summary>A power-plan setting on the active plan, for plugged in (AC) and battery (DC).</summary>
    public sealed class PowerValue : Change
    {
        /// <summary>Plain name, for example "Allow wake timers".</summary>
        public string Label;
        public string Subgroup;
        public string Setting;
        public uint OnAc, OnDc, DefaultAc, DefaultDc;

        public override string Kind { get { return "power"; } }
        public override bool NeedsAdmin { get { return true; } }
        public override string Describe() { return "Active power plan: " + Label + " (" + Subgroup + "/" + Setting + ") = " + OnAc + " plugged in, " + OnDc + " on battery"; }

        public override string DescribeCurrent(ISystem sys)
        {
            uint? ac = sys.GetPowerValue(Subgroup, Setting, true), dc = sys.GetPowerValue(Subgroup, Setting, false);
            return (ac == null ? "?" : ac.Value.ToString(CultureInfo.InvariantCulture)) + " plugged in, " + (dc == null ? "?" : dc.Value.ToString(CultureInfo.InvariantCulture)) + " on battery";
        }

        public override bool? IsOn(ISystem sys)
        {
            uint? ac = sys.GetPowerValue(Subgroup, Setting, true), dc = sys.GetPowerValue(Subgroup, Setting, false);
            if (ac == null) return null;
            return ac.Value == OnAc && (dc == null || dc.Value == OnDc);
        }

        public override Snapshot Capture(ISystem sys)
        {
            var snap = new Snapshot { Kind = Kind, What = "Power plan: " + Label };
            uint? ac = sys.GetPowerValue(Subgroup, Setting, true), dc = sys.GetPowerValue(Subgroup, Setting, false);
            if (ac != null) snap.Set("ac", ac.Value.ToString(CultureInfo.InvariantCulture));
            if (dc != null) snap.Set("dc", dc.Value.ToString(CultureInfo.InvariantCulture));
            string scheme = ActiveScheme(sys);
            if (scheme != null) snap.Set("scheme", scheme);
            snap.Was = DescribeCurrent(sys);
            return snap;
        }

        static readonly System.Text.RegularExpressions.Regex Guid = new System.Text.RegularExpressions.Regex("[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>GUID of the active power plan (lower case), or null when it cannot be read.</summary>
        static string ActiveScheme(ISystem sys)
        {
            try
            {
                ProcessResult r = sys.Run(ProcessRunner.System32("powercfg.exe"), "/getactivescheme", 15000);
                if (r != null && r.Ok)
                {
                    var m = Guid.Match(r.Output ?? "");
                    if (m.Success) return m.Value.ToLowerInvariant();
                }
            }
            catch (Exception) { }
            return null;
        }

        public override void Apply(ISystem sys) { sys.SetPowerValue(Subgroup, Setting, true, OnAc); sys.SetPowerValue(Subgroup, Setting, false, OnDc); }

        public override void Restore(ISystem sys, Snapshot before)
        {
            string ac = before.Get("ac"), dc = before.Get("dc");
            if (ac == null && dc == null) throw new InvalidOperationException("The power setting could not be read before the change, so it cannot be put back automatically.");
            // The values are written to the active plan, so make sure that is still the plan they came from.
            string scheme = before.Get("scheme");
            if (scheme != null)
            {
                string now = ActiveScheme(sys);
                if (now != null && !string.Equals(now, scheme, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The active power plan is not the one that was changed. Switch back to that plan in Power Options and try again, so the old values go to the right plan.");
            }
            if (ac != null) sys.SetPowerValue(Subgroup, Setting, true, uint.Parse(ac, CultureInfo.InvariantCulture));
            if (dc != null) sys.SetPowerValue(Subgroup, Setting, false, uint.Parse(dc, CultureInfo.InvariantCulture));
        }

        public override void RestoreDefault(ISystem sys) { sys.SetPowerValue(Subgroup, Setting, true, DefaultAc); sys.SetPowerValue(Subgroup, Setting, false, DefaultDc); }
    }

    /// <summary>
    /// Anything else (a powercfg or netsh command, a feature toggle). <see cref="Check"/> reads the state; undo replays
    /// <see cref="TurnOn"/> or <see cref="TurnOff"/> according to the state saved before the change.
    /// Use the <see cref="ISystem"/> passed in for every read and command so tests can run it against <see cref="MemorySystem"/>.
    /// </summary>
    public sealed class CustomChange : Change
    {
        public string Text;
        public Func<ISystem, bool?> Check;
        public Action<ISystem> TurnOn;
        public Action<ISystem> TurnOff;
        public Func<ISystem, string> Current;
        public Func<ISystem, string> Unavailable;
        public bool Admin = true;

        public override string Kind { get { return "custom"; } }
        public override bool NeedsAdmin { get { return Admin; } }
        public override string Describe() { return Text; }

        public override string DescribeCurrent(ISystem sys)
        {
            if (Current != null) { try { return Current(sys); } catch (Exception ex) { return "unknown (" + ex.Message + ")"; } }
            bool? on = IsOn(sys);
            return on == null ? "unknown" : on.Value ? "on" : "off";
        }

        public override string UnavailableReason(ISystem sys) { return Unavailable == null ? null : Unavailable(sys); }

        public override bool? IsOn(ISystem sys)
        {
            try { return Check(sys); } catch (Exception) { return null; }
        }

        public override Snapshot Capture(ISystem sys)
        {
            bool? on = IsOn(sys);
            var snap = new Snapshot { Kind = Kind, What = Text };
            snap.Set("state", on == null ? "unknown" : on.Value ? "on" : "off");
            snap.Was = on == null ? "unknown" : on.Value ? "on" : "off";
            return snap;
        }

        public override void Apply(ISystem sys) { TurnOn(sys); }

        public override void Restore(ISystem sys, Snapshot before)
        {
            string was = before.Get("state");
            if (was == "on") TurnOn(sys);
            else if (was == "off") TurnOff(sys);
            else throw new InvalidOperationException("Its state could not be read before the change, so it cannot be put back automatically.");
        }

        public override void RestoreDefault(ISystem sys) { TurnOff(sys); }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Factories used by the catalog files (src\Catalog\*.cs).
    // ------------------------------------------------------------------------------------------------------------

    public static class Reg
    {
        public const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        public const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
        public const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        public const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        public const string WindowsUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        public const string AutoUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        public const string WindowsUpdateUx = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        public const string DataCollectionPolicy = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
        public const string SystemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
        public const string MachinePolicySystem = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

        /// <summary>A DWORD value. <paramref name="windowsDefault"/> null deletes the value when restoring the default.</summary>
        public static RegValue Dword(Hive hive, string path, string name, uint on, uint? windowsDefault = null)
        {
            return new RegValue { Hive = hive, Path = path, Name = name, ValueKind = RegKind.Dword, On = on, Default = windowsDefault.HasValue ? (object)windowsDefault.Value : null };
        }

        public static RegValue Qword(Hive hive, string path, string name, ulong on, ulong? windowsDefault = null)
        {
            return new RegValue { Hive = hive, Path = path, Name = name, ValueKind = RegKind.Qword, On = on, Default = windowsDefault.HasValue ? (object)windowsDefault.Value : null };
        }

        /// <summary>A REG_SZ value. <paramref name="windowsDefault"/> null deletes the value when restoring the default.</summary>
        public static RegValue String(Hive hive, string path, string name, string on, string windowsDefault = null)
        {
            return new RegValue { Hive = hive, Path = path, Name = name, ValueKind = RegKind.String, On = on, Default = windowsDefault };
        }

        public static RegValue Expand(Hive hive, string path, string name, string on, string windowsDefault = null)
        {
            return new RegValue { Hive = hive, Path = path, Name = name, ValueKind = RegKind.ExpandString, On = on, Default = windowsDefault };
        }

        public static RegValue Multi(Hive hive, string path, string name, string[] on, string[] windowsDefault = null)
        {
            return new RegValue { Hive = hive, Path = path, Name = name, ValueKind = RegKind.MultiString, On = on, Default = windowsDefault };
        }

        public static RegValue Binary(Hive hive, string path, string name, byte[] on, byte[] windowsDefault = null)
        {
            return new RegValue { Hive = hive, Path = path, Name = name, ValueKind = RegKind.Binary, On = on, Default = windowsDefault };
        }

        /// <summary>
        /// A key whose existence is the setting: "on" sets <paramref name="path"/>'s (Default) value to <paramref name="defaultValue"/>;
        /// the Windows default deletes <paramref name="deleteOnDefault"/> (the key itself or a parent) entirely.
        /// Example: the classic context menu (HKCU\Software\Classes\CLSID\{86ca1aa0-...}\InprocServer32 with an empty default).
        /// </summary>
        public static RegValue KeyDefault(Hive hive, string path, string defaultValue, string deleteOnDefault)
        {
            return new RegValue { Hive = hive, Path = path, Name = "", ValueKind = RegKind.String, On = defaultValue ?? "", DefaultDeletesKey = deleteOnDefault ?? path };
        }

        public static string Root(Hive hive)
        {
            switch (hive)
            {
                case Hive.Machine: return "HKLM";
                case Hive.User: return "HKCU";
                default: return @"HKU\.DEFAULT";
            }
        }

        public static object Normalize(RegKind kind, object value)
        {
            if (value == null) return null;
            switch (kind)
            {
                case RegKind.Dword: return value is uint ? value : (object)unchecked((uint)Convert.ToInt64(value, CultureInfo.InvariantCulture));
                case RegKind.Qword: return value is ulong ? value : (object)Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                default: return value;
            }
        }

        static bool IsNumeric(RegKind k) { return k == RegKind.Dword || k == RegKind.Qword; }
        static bool IsText(RegKind k) { return k == RegKind.String || k == RegKind.ExpandString; }

        static ulong ToUlong(object v) { return v is uint ? (uint)v : v is ulong ? (ulong)v : v is int ? unchecked((uint)(int)v) : Convert.ToUInt64(v, CultureInfo.InvariantCulture); }

        /// <summary>Compares two registry values, tolerating numbers stored as text and vice versa.</summary>
        public static bool Same(RegKind ak, object a, RegKind bk, object b) { return Same(ak, a, bk, b, false); }

        /// <summary>As above; with <paramref name="strictType"/> a number never equals text (and the reverse).</summary>
        public static bool Same(RegKind ak, object a, RegKind bk, object b, bool strictType)
        {
            if (a == null || b == null) return a == null && b == null;
            if (strictType && IsNumeric(ak) != IsNumeric(bk) && (IsNumeric(ak) || IsNumeric(bk))) return false;
            if (IsNumeric(ak) && IsNumeric(bk)) return ToUlong(a) == ToUlong(b);
            if (IsText(ak) && IsText(bk)) return string.Equals((string)a, (string)b, StringComparison.OrdinalIgnoreCase);
            if (IsNumeric(ak) && IsText(bk)) return string.Equals(ToUlong(a).ToString(CultureInfo.InvariantCulture), ((string)b).Trim(), StringComparison.Ordinal);
            if (IsText(ak) && IsNumeric(bk)) return string.Equals(((string)a).Trim(), ToUlong(b).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            if (ak == RegKind.MultiString && bk == RegKind.MultiString) return ((string[])a).SequenceEqual((string[])b, StringComparer.OrdinalIgnoreCase);
            if (ak == RegKind.Binary && bk == RegKind.Binary) return ((byte[])a).SequenceEqual((byte[])b);
            return false;
        }

        public static string Format(RegKind kind, object data)
        {
            if (data == null) return "not set";
            switch (kind)
            {
                case RegKind.Dword:
                case RegKind.Qword:
                    ulong n = ToUlong(data);
                    return (n >= 0x10000 ? "0x" + n.ToString("X", CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture)) + (kind == RegKind.Dword ? " (DWORD)" : " (QWORD)");
                case RegKind.String:
                case RegKind.ExpandString:
                    return "\"" + (string)data + "\"";
                case RegKind.MultiString:
                    return string.Join("; ", (string[])data);
                default:
                    var bytes = (byte[])data;
                    return bytes.Length == 0 ? "(empty binary)" : BitConverter.ToString(bytes.Take(32).ToArray()).Replace("-", " ") + (bytes.Length > 32 ? " ..." : "");
            }
        }

        public static string Encode(RegKind kind, object data)
        {
            switch (kind)
            {
                case RegKind.Dword:
                case RegKind.Qword: return ToUlong(data).ToString(CultureInfo.InvariantCulture);
                case RegKind.String:
                case RegKind.ExpandString: return (string)data ?? "";
                case RegKind.MultiString: return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\0", (string[])data)));
                default: return Convert.ToBase64String((byte[])data ?? new byte[0]);
            }
        }

        public static object Decode(RegKind kind, string text)
        {
            switch (kind)
            {
                case RegKind.Dword: return unchecked((uint)ulong.Parse(text, CultureInfo.InvariantCulture));
                case RegKind.Qword: return ulong.Parse(text, CultureInfo.InvariantCulture);
                case RegKind.String:
                case RegKind.ExpandString: return text ?? "";
                case RegKind.MultiString:
                    string joined = Encoding.UTF8.GetString(Convert.FromBase64String(text ?? ""));
                    return joined.Length == 0 ? new string[0] : joined.Split('\0');
                default: return Convert.FromBase64String(text ?? "");
            }
        }

        /// <summary>The shortest prefix of <paramref name="path"/> that does not exist yet, or null when the whole path exists.</summary>
        public static string FirstMissingKey(ISystem sys, Hive hive, string path)
        {
            string[] parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            string current = "";
            foreach (string part in parts)
            {
                current = current.Length == 0 ? part : current + "\\" + part;
                if (!sys.KeyExists(hive, current)) return current;
            }
            return null;
        }

        /// <summary>Deletes <paramref name="path"/> and its parents up to and including <paramref name="stopAt"/> while they are empty.</summary>
        public static void RemoveEmptyKeys(ISystem sys, Hive hive, string path, string stopAt)
        {
            string current = path.TrimEnd('\\');
            while (current.Length >= stopAt.Length && current.StartsWith(stopAt, StringComparison.OrdinalIgnoreCase))
            {
                if (!sys.KeyExists(hive, current)) { }
                else if (sys.KeyIsEmpty(hive, current)) sys.DeleteKey(hive, current);
                else return;
                if (current.Length == stopAt.Length) return;
                int slash = current.LastIndexOf('\\');
                if (slash <= 0) return;
                current = current.Substring(0, slash);
            }
        }
    }

    public static class Svc
    {
        /// <summary>A service start type. Services are optional: when one is not installed it is skipped.</summary>
        public static ServiceStart Start(string service, StartMode on, StartMode windowsDefault, bool stopNow = true)
        {
            return new ServiceStart { Service = service, On = on, Default = windowsDefault, StopNow = stopNow, Optional = true };
        }

        public static string Text(StartMode m)
        {
            switch (m)
            {
                case StartMode.AutomaticDelayed: return "Automatic (delayed start)";
                case StartMode.Automatic: return "Automatic";
                case StartMode.Manual: return "Manual";
                case StartMode.Disabled: return "Disabled";
                default: return m.ToString();
            }
        }
    }

    public static class Sched
    {
        /// <summary>Disables a scheduled task when the tweak is on (re-enables it for the Windows default). Missing tasks are skipped.</summary>
        public static TaskState Disable(string taskPath)
        {
            return new TaskState { TaskPath = taskPath, EnabledWhenOn = false, Optional = true };
        }
    }

    public static class Power
    {
        /// <summary>A setting of the active power plan. Use powercfg aliases (SUB_SLEEP, RTCWAKE) or GUIDs.</summary>
        public static PowerValue Value(string label, string subgroup, string setting, uint onAc, uint onDc, uint defaultAc, uint defaultDc)
        {
            return new PowerValue { Label = label, Subgroup = subgroup, Setting = setting, OnAc = onAc, OnDc = onDc, DefaultAc = defaultAc, DefaultDc = defaultDc };
        }
    }

    public static class Custom
    {
        public static CustomChange Make(string text, Func<ISystem, bool?> check, Action<ISystem> turnOn, Action<ISystem> turnOff, bool admin = true, Func<ISystem, string> current = null, Func<ISystem, string> unavailable = null)
        {
            return new CustomChange { Text = text, Check = check, TurnOn = turnOn, TurnOff = turnOff, Admin = admin, Current = current, Unavailable = unavailable };
        }

        /// <summary>Runs a command through <see cref="ISystem.Run"/> and throws with its output when it fails (for TurnOn/TurnOff bodies).</summary>
        public static ProcessResult Must(ISystem sys, string file, string arguments, int timeoutMs = 120000)
        {
            ProcessResult r = sys.Run(file, arguments, timeoutMs);
            if (r.TimedOut) throw new TimeoutException(file + " did not finish in time.");
            if (r.ExitCode != 0)
            {
                string text = (r.Combined ?? "").Trim();
                throw new InvalidOperationException(System.IO.Path.GetFileName(file) + " " + arguments + " failed (exit code " + r.ExitCode + ")" + (text.Length > 0 ? ": " + text : "."));
            }
            return r;
        }
    }
}
