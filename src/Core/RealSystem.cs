using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TweakBench.Core
{
    /// <summary>
    /// <see cref="ISystem"/> against the real Windows PC: the 64-bit registry, services, Task Scheduler, powercfg and processes.
    /// Reads never need administrator rights unless Windows protects the key; writes throw <see cref="UnauthorizedAccessException"/>
    /// with a plain message when Windows says no.
    /// </summary>
    public sealed class RealSystem : ISystem
    {
        const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";
        const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        readonly bool elevated;
        readonly int build;
        readonly string edition;
        readonly Dictionary<string, KeyValuePair<DateTime, uint?[]>> powerCache = new Dictionary<string, KeyValuePair<DateTime, uint?[]>>(StringComparer.OrdinalIgnoreCase);
        readonly object powerGate = new object();

        public RealSystem()
        {
            try { using (WindowsIdentity id = WindowsIdentity.GetCurrent()) elevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { elevated = false; }
            int b = 0;
            string ed = "";
            try
            {
                RegData number = ReadValue(Hive.Machine, CurrentVersionKey, "CurrentBuildNumber");
                if (number != null && number.Data != null) int.TryParse(Convert.ToString(number.Data, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
                RegData editionId = ReadValue(Hive.Machine, CurrentVersionKey, "EditionID");
                if (editionId != null && editionId.Data is string) ed = (string)editionId.Data;
            }
            catch (Exception) { }
            build = b;
            edition = ed;
        }

        public bool IsElevated { get { return elevated; } }
        public int Build { get { return build; } }
        public string Edition { get { return edition; } }

        // ------------------------------------------------------------------------------------------------ registry

        static RegistryKey Base(Hive hive)
        {
            RegistryHive h = hive == Hive.Machine ? RegistryHive.LocalMachine : hive == Hive.User ? RegistryHive.CurrentUser : RegistryHive.Users;
            return RegistryKey.OpenBaseKey(h, RegistryView.Registry64);
        }

        static string SubPath(Hive hive, string path)
        {
            path = (path ?? "").Trim('\\');
            if (hive != Hive.DefaultUser) return path;
            return path.Length == 0 ? ".DEFAULT" : ".DEFAULT\\" + path;
        }

        static UnauthorizedAccessException Denied(Hive hive, string path, bool writing, Exception inner)
        {
            string key = Reg.Root(hive) + "\\" + path;
            string text = hive == Hive.User
                ? "Windows did not allow " + (writing ? "changing " : "reading ") + key + ". A policy or a security program may be protecting it."
                : "Windows did not allow " + (writing ? "changing " : "reading ") + key + ". Run Tweak Bench as administrator and try again.";
            return new UnauthorizedAccessException(text, inner);
        }

        static RegData ToData(RegistryKey key, string name, object raw)
        {
            RegistryValueKind kind = key.GetValueKind(name);
            switch (kind)
            {
                case RegistryValueKind.DWord: return new RegData(RegKind.Dword, unchecked((uint)Convert.ToInt32(raw, CultureInfo.InvariantCulture)));
                case RegistryValueKind.QWord: return new RegData(RegKind.Qword, unchecked((ulong)Convert.ToInt64(raw, CultureInfo.InvariantCulture)));
                case RegistryValueKind.String: return new RegData(RegKind.String, (string)raw);
                case RegistryValueKind.ExpandString: return new RegData(RegKind.ExpandString, (string)raw);
                case RegistryValueKind.MultiString: return new RegData(RegKind.MultiString, (string[])raw);
                default: return new RegData(RegKind.Binary, raw as byte[] ?? new byte[0]);
            }
        }

        public RegData ReadValue(Hive hive, string path, string name)
        {
            try
            {
                using (RegistryKey root = Base(hive))
                using (RegistryKey key = root.OpenSubKey(SubPath(hive, path), false))
                {
                    if (key == null) return null;
                    object raw = key.GetValue(name ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (raw == null) return null;
                    return ToData(key, name ?? "", raw);
                }
            }
            catch (SecurityException ex) { throw Denied(hive, path, false, ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(hive, path, false, ex); }
        }

        public bool KeyExists(Hive hive, string path)
        {
            try
            {
                using (RegistryKey root = Base(hive))
                using (RegistryKey key = root.OpenSubKey(SubPath(hive, path), false))
                    return key != null;
            }
            catch (SecurityException ex) { throw Denied(hive, path, false, ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(hive, path, false, ex); }
        }

        public bool KeyIsEmpty(Hive hive, string path)
        {
            try
            {
                using (RegistryKey root = Base(hive))
                using (RegistryKey key = root.OpenSubKey(SubPath(hive, path), false))
                    return key != null && key.SubKeyCount == 0 && key.ValueCount == 0;
            }
            catch (SecurityException ex) { throw Denied(hive, path, false, ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(hive, path, false, ex); }
        }

        static object ToRaw(RegKind kind, object data, out RegistryValueKind valueKind)
        {
            switch (kind)
            {
                case RegKind.Dword: valueKind = RegistryValueKind.DWord; return unchecked((int)(uint)Reg.Normalize(kind, data));
                case RegKind.Qword: valueKind = RegistryValueKind.QWord; return unchecked((long)(ulong)Reg.Normalize(kind, data));
                case RegKind.String: valueKind = RegistryValueKind.String; return (string)data ?? "";
                case RegKind.ExpandString: valueKind = RegistryValueKind.ExpandString; return (string)data ?? "";
                case RegKind.MultiString: valueKind = RegistryValueKind.MultiString; return (string[])data ?? new string[0];
                default: valueKind = RegistryValueKind.Binary; return (byte[])data ?? new byte[0];
            }
        }

        public void WriteValue(Hive hive, string path, string name, RegKind kind, object data)
        {
            RegistryValueKind valueKind;
            object raw = ToRaw(kind, data, out valueKind);
            try
            {
                using (RegistryKey root = Base(hive))
                using (RegistryKey key = root.CreateSubKey(SubPath(hive, path)))
                {
                    if (key == null) throw new InvalidOperationException("Windows could not create " + Reg.Root(hive) + "\\" + path + ".");
                    key.SetValue(name ?? "", raw, valueKind);
                }
            }
            catch (SecurityException ex) { throw Denied(hive, path, true, ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(hive, path, true, ex); }
        }

        public void DeleteValue(Hive hive, string path, string name)
        {
            try
            {
                using (RegistryKey root = Base(hive))
                using (RegistryKey key = root.OpenSubKey(SubPath(hive, path), true))
                {
                    if (key == null) return;
                    key.DeleteValue(name ?? "", false);
                }
            }
            catch (SecurityException ex) { throw Denied(hive, path, true, ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(hive, path, true, ex); }
        }

        public void DeleteKey(Hive hive, string path)
        {
            string sub = SubPath(hive, path);
            if (sub.Length == 0 || sub == ".DEFAULT") throw new ArgumentException("Refusing to delete a registry root.");
            try
            {
                using (RegistryKey root = Base(hive))
                {
                    using (RegistryKey probe = root.OpenSubKey(sub, false)) { if (probe == null) return; }
                    root.DeleteSubKeyTree(sub, false);
                }
            }
            catch (SecurityException ex) { throw Denied(hive, path, true, ex); }
            catch (UnauthorizedAccessException ex) { throw Denied(hive, path, true, ex); }
        }

        // ------------------------------------------------------------------------------------------------ services

        static string ServiceKey(string name) { return ServicesKey + "\\" + name; }

        static bool ValidServiceName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(new[] { '\\', '/', '"', '\0' }) < 0;
        }

        public ServiceInfo GetService(string name)
        {
            if (!ValidServiceName(name)) return null;
            RegData start = ReadValue(Hive.Machine, ServiceKey(name), "Start");
            if (start == null || !(start.Data is uint)) return null;
            uint s = (uint)start.Data;
            if (s > 4) return null;
            // A key without a Type value is not a service (for example a leftover "Parameters" key).
            if (ReadValue(Hive.Machine, ServiceKey(name), "Type") == null) return null;
            StartMode mode = (StartMode)s;
            if (s == 2)
            {
                RegData delayed = ReadValue(Hive.Machine, ServiceKey(name), "DelayedAutostart");
                if (delayed != null && delayed.Data is uint && (uint)delayed.Data == 1) mode = StartMode.AutomaticDelayed;
            }
            var info = new ServiceInfo { Name = name, DisplayName = name, Start = mode };
            try
            {
                using (var sc = new ServiceController(name))
                {
                    info.DisplayName = sc.DisplayName;
                    info.Running = sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
                }
            }
            catch (Exception) { }
            return info;
        }

        public void SetServiceStart(string name, StartMode mode)
        {
            if (!ValidServiceName(name)) throw new ArgumentException("Not a valid service name: " + name);
            string word;
            switch (mode)
            {
                case StartMode.Boot: word = "boot"; break;
                case StartMode.System: word = "system"; break;
                case StartMode.Automatic: word = "auto"; break;
                case StartMode.AutomaticDelayed: word = "delayed-auto"; break;
                case StartMode.Manual: word = "demand"; break;
                default: word = "disabled"; break;
            }
            ProcessResult r = ProcessRunner.Run(ProcessRunner.System32("sc.exe"), "config \"" + name + "\" start= " + word, 30000);
            if (r.TimedOut) throw new System.TimeoutException("Changing the " + name + " service did not finish in time.");
            if (r.ExitCode == 5) throw new UnauthorizedAccessException("Windows did not allow changing the " + name + " service. Run Tweak Bench as administrator and try again.");
            if (r.ExitCode == 1060) throw new InvalidOperationException("The " + name + " service is not installed on this PC.");
            if (r.ExitCode != 0)
            {
                string text = (r.Combined ?? "").Trim();
                throw new InvalidOperationException("Windows could not change the " + name + " service (error " + r.ExitCode + ")" + (text.Length > 0 ? ": " + text : "."));
            }
            // sc.exe leaves an old "delayed start" flag in place for some changes: make sure it matches the requested mode.
            if (mode != StartMode.AutomaticDelayed)
            {
                RegData delayed = ReadValue(Hive.Machine, ServiceKey(name), "DelayedAutostart");
                if (delayed != null && delayed.Data is uint && (uint)delayed.Data == 1) WriteValue(Hive.Machine, ServiceKey(name), "DelayedAutostart", RegKind.Dword, 0u);
            }
        }

        static Exception ServiceError(string verb, string name, Exception ex)
        {
            var win = ex.InnerException as Win32Exception ?? ex as Win32Exception;
            if (win != null && win.NativeErrorCode == 5) return new UnauthorizedAccessException("Windows did not allow " + verb + " the " + name + " service. Run Tweak Bench as administrator and try again.", ex);
            return new InvalidOperationException("Could not " + verb.Replace("starting", "start").Replace("stopping", "stop") + " the " + name + " service: " + (win != null ? win.Message : ex.Message), ex);
        }

        public void StartService(string name)
        {
            if (!ValidServiceName(name)) throw new ArgumentException("Not a valid service name: " + name);
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Running) return;
                    if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            }
            catch (System.ServiceProcess.TimeoutException ex) { throw new System.TimeoutException("The " + name + " service did not start within 30 seconds.", ex); }
            catch (InvalidOperationException ex) { throw ServiceError("starting", name, ex); }
        }

        public void StopService(string name)
        {
            if (!ValidServiceName(name)) throw new ArgumentException("Not a valid service name: " + name);
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) return;
                    if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
            }
            catch (System.ServiceProcess.TimeoutException ex) { throw new System.TimeoutException("The " + name + " service did not stop within 30 seconds.", ex); }
            catch (InvalidOperationException ex) { throw ServiceError("stopping", name, ex); }
        }

        // ------------------------------------------------------------------------------------------------ scheduled tasks

        static void SplitTask(string path, out string folder, out string name)
        {
            string p = (path ?? "").Replace('/', '\\');
            if (!p.StartsWith("\\", StringComparison.Ordinal)) p = "\\" + p;
            int slash = p.LastIndexOf('\\');
            folder = slash <= 0 ? "\\" : p.Substring(0, slash);
            name = p.Substring(slash + 1);
        }

        static void ReleaseCom(object o) { try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch (Exception) { } }

        // The runtime turns some Task Scheduler error codes into .NET exceptions (file not found, access denied) before we see them.
        static bool IsNotFound(Exception ex)
        {
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException) return true;
            var com = ex as COMException;
            if (com == null) return false;
            uint hr = unchecked((uint)com.ErrorCode);
            return hr == 0x80070002 || hr == 0x80070003 || hr == 0x8007010B;
        }

        static Exception TaskError(string path, bool writing, Exception ex)
        {
            var com = ex as COMException;
            if (ex is UnauthorizedAccessException || (com != null && unchecked((uint)com.ErrorCode) == 0x80070005))
                return new UnauthorizedAccessException("Windows did not allow " + (writing ? "changing" : "reading") + " the scheduled task " + path + ". Run Tweak Bench as administrator and try again.", ex);
            if (ex is InvalidOperationException) return ex;
            return new InvalidOperationException("Task Scheduler could not " + (writing ? "change" : "read") + " " + path + ": " + ex.Message, ex);
        }

        // Task Scheduler is only reachable through COM; this is the one place the app uses "dynamic".
        public bool? GetTaskEnabled(string path)
        {
            string folderPath, taskName;
            SplitTask(path, out folderPath, out taskName);
            dynamic service = null, folder = null, task = null;
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) throw new InvalidOperationException("Task Scheduler is not available on this PC.");
                service = Activator.CreateInstance(type);
                service.Connect();
                folder = service.GetFolder(folderPath);
                task = folder.GetTask(taskName);
                return (bool)task.Enabled;
            }
            catch (Exception ex)
            {
                if (IsNotFound(ex)) return null;
                throw TaskError(path, false, ex);
            }
            finally { ReleaseCom(task); ReleaseCom(folder); ReleaseCom(service); }
        }

        public void SetTaskEnabled(string path, bool enabled)
        {
            string folderPath, taskName;
            SplitTask(path, out folderPath, out taskName);
            dynamic service = null, folder = null, task = null;
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) throw new InvalidOperationException("Task Scheduler is not available on this PC.");
                service = Activator.CreateInstance(type);
                service.Connect();
                folder = service.GetFolder(folderPath);
                task = folder.GetTask(taskName);
                task.Enabled = enabled;
            }
            catch (Exception ex)
            {
                if (IsNotFound(ex)) throw new InvalidOperationException("The scheduled task " + path + " does not exist on this PC.", ex);
                throw TaskError(path, true, ex);
            }
            finally { ReleaseCom(task); ReleaseCom(folder); ReleaseCom(service); }
        }

        // ------------------------------------------------------------------------------------------------ power

        static readonly Regex HexNumber = new Regex(@"0x[0-9a-fA-F]{1,8}", RegexOptions.Compiled);

        static string PowerKey(string subgroup, string setting) { return subgroup + "|" + setting; }

        /// <summary>Reads both values of a setting with one powercfg call (cached for a few seconds). [0] = plugged in, [1] = battery.</summary>
        uint?[] ReadPower(string subgroup, string setting)
        {
            string key = PowerKey(subgroup, setting);
            lock (powerGate)
            {
                KeyValuePair<DateTime, uint?[]> cached;
                if (powerCache.TryGetValue(key, out cached) && DateTime.UtcNow - cached.Key < TimeSpan.FromSeconds(3)) return cached.Value;
            }
            var values = new uint?[2];
            ProcessResult r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), "/q SCHEME_CURRENT " + subgroup + " " + setting, 15000);
            if (r.Ok) values = ParsePower(r.Output);
            lock (powerGate) powerCache[key] = new KeyValuePair<DateTime, uint?[]>(DateTime.UtcNow, values);
            return values;
        }

        /// <summary>
        /// Reads "powercfg /q" output. The last two hexadecimal numbers are "Current AC Power Setting Index" and
        /// "Current DC Power Setting Index" (the labels are translated on other Windows languages, the order is not).
        /// [0] = plugged in, [1] = battery; null when the output does not have them.
        /// </summary>
        internal static uint?[] ParsePower(string output)
        {
            var values = new uint?[2];
            MatchCollection matches = HexNumber.Matches(output ?? "");
            if (matches.Count >= 2)
            {
                values[0] = ParseHex(matches[matches.Count - 2].Value);
                values[1] = ParseHex(matches[matches.Count - 1].Value);
            }
            return values;
        }

        static uint? ParseHex(string text)
        {
            uint v;
            return uint.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out v) ? (uint?)v : null;
        }

        public uint? GetPowerValue(string subgroup, string setting, bool ac)
        {
            return ReadPower(subgroup, setting)[ac ? 0 : 1];
        }

        public void SetPowerValue(string subgroup, string setting, bool ac, uint value)
        {
            lock (powerGate) powerCache.Clear();
            string verb = ac ? "/setacvalueindex" : "/setdcvalueindex";
            ProcessResult r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), verb + " SCHEME_CURRENT " + subgroup + " " + setting + " " + value.ToString(CultureInfo.InvariantCulture), 30000);
            PowerResult(r, "change the " + setting + " power setting");
            r = ProcessRunner.Run(ProcessRunner.System32("powercfg.exe"), "/setactive SCHEME_CURRENT", 30000);
            PowerResult(r, "apply the power plan");
            lock (powerGate) powerCache.Clear();
        }

        static void PowerResult(ProcessResult r, string what)
        {
            if (r.TimedOut) throw new System.TimeoutException("Windows did not " + what + " in time.");
            if (r.ExitCode == 0) return;
            string text = (r.Combined ?? "").Trim();
            if (r.ExitCode == 5 || text.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new UnauthorizedAccessException("Windows did not allow Tweak Bench to " + what + ". Run Tweak Bench as administrator and try again.");
            throw new InvalidOperationException("Windows could not " + what + " (error " + r.ExitCode + ")" + (text.Length > 0 ? ": " + text : "."));
        }

        // ------------------------------------------------------------------------------------------------ programs

        public ProcessResult Run(string file, string arguments, int timeoutMs = 120000)
        {
            // PowerShellArgs makes PowerShell write UTF-8; everything else writes the console (OEM) code page.
            return ProcessRunner.Run(file, arguments, timeoutMs, ProcessRunner.IsPowerShell(file) ? System.Text.Encoding.UTF8 : null);
        }
    }
}
