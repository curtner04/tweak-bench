using System;

namespace TweakBench.Core
{
    /// <summary>Registry roots a tweak may touch. All access uses the 64-bit registry view.</summary>
    public enum Hive
    {
        /// <summary>HKEY_LOCAL_MACHINE (needs administrator rights to write).</summary>
        Machine,
        /// <summary>HKEY_CURRENT_USER of the signed-in user.</summary>
        User,
        /// <summary>HKEY_USERS\.DEFAULT (the sign-in screen; needs administrator rights to write).</summary>
        DefaultUser
    }

    public enum RegKind { Dword, Qword, String, ExpandString, MultiString, Binary }

    /// <summary>Service start types. Values match the registry "Start" value; AutomaticDelayed is Start=2 plus DelayedAutostart=1.</summary>
    public enum StartMode { Boot = 0, System = 1, Automatic = 2, Manual = 3, Disabled = 4, AutomaticDelayed = 5 }

    /// <summary>A registry value as read. Data is uint (Dword), ulong (Qword), string (String/ExpandString), string[] (MultiString) or byte[] (Binary).</summary>
    public sealed class RegData
    {
        public RegKind Kind;
        public object Data;
        public RegData() { }
        public RegData(RegKind kind, object data) { Kind = kind; Data = data; }
    }

    public sealed class ServiceInfo
    {
        public string Name;
        public string DisplayName;
        public StartMode Start;
        public bool Running;
    }

    public sealed class ProcessResult
    {
        public int ExitCode;
        public string Output = "";
        public string Error = "";
        public bool TimedOut;
        public bool Ok { get { return !TimedOut && ExitCode == 0; } }
        public string Combined { get { return (Output ?? "") + (string.IsNullOrEmpty(Error) ? "" : Environment.NewLine + Error); } }
    }

    /// <summary>
    /// Everything a tweak may read or change. <see cref="RealSystem"/> talks to Windows; <see cref="MemorySystem"/> is an in-memory
    /// stand-in used by tests and by the screenshot/demo mode so they never touch the real PC.
    /// Writes throw <see cref="UnauthorizedAccessException"/> (or another exception) with a plain message on failure.
    /// </summary>
    public interface ISystem
    {
        bool IsElevated { get; }
        /// <summary>Windows build number, for example 26100.</summary>
        int Build { get; }
        /// <summary>EditionID, for example "Professional", "Core" (Home), "Enterprise".</summary>
        string Edition { get; }

        /// <summary>Null when the key or the value does not exist. Name "" is the key's default value.</summary>
        RegData ReadValue(Hive hive, string path, string name);
        bool KeyExists(Hive hive, string path);
        /// <summary>True when the key exists and has no values and no subkeys.</summary>
        bool KeyIsEmpty(Hive hive, string path);
        /// <summary>Creates the key path as needed. Data types as in <see cref="RegData"/>.</summary>
        void WriteValue(Hive hive, string path, string name, RegKind kind, object data);
        /// <summary>No error when the value or key is already missing.</summary>
        void DeleteValue(Hive hive, string path, string name);
        /// <summary>Deletes the key and everything under it. No error when it is missing.</summary>
        void DeleteKey(Hive hive, string path);

        /// <summary>Null when the service is not installed.</summary>
        ServiceInfo GetService(string name);
        void SetServiceStart(string name, StartMode mode);
        void StartService(string name);
        void StopService(string name);

        /// <summary>Scheduled task enabled state by full path ("\Microsoft\Windows\...\Name"). Null when the task does not exist.</summary>
        bool? GetTaskEnabled(string path);
        void SetTaskEnabled(string path, bool enabled);

        /// <summary>Power setting index on the active power plan (powercfg subgroup/setting aliases or GUIDs). Null when unknown.</summary>
        uint? GetPowerValue(string subgroup, string setting, bool ac);
        void SetPowerValue(string subgroup, string setting, bool ac, uint value);

        /// <summary>Runs a program hidden and waits for it (no window, no shell). Output is captured.</summary>
        ProcessResult Run(string file, string arguments, int timeoutMs = 120000);
    }
}
