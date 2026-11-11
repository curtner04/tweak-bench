using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TweakBench.Core
{
    /// <summary>Power plan helpers (read through the registry where possible so a status refresh stays fast and language-independent).</summary>
    static class PerfPowerPlans
    {
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string High = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string Saver = "a1841308-3541-4fab-bc81-f71556f20b4a";
        public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        static readonly Regex Line = new Regex(@"([0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})\s*(?:\((.*)\))?", RegexOptions.Compiled);

        static string Exe { get { return ProcessRunner.System32("powercfg.exe"); } }

        public static bool IsBuiltIn(string guid)
        {
            return Same(guid, Balanced) || Same(guid, High) || Same(guid, Saver) || Same(guid, Ultimate);
        }

        public static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        /// <summary>The GUID of the active plan, or null when it cannot be read.</summary>
        public static string ActiveGuid(ISystem sys)
        {
            RegData d = sys.ReadValue(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme");
            string s = d == null ? null : d.Data as string;
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim().Trim('{', '}').ToLowerInvariant();
            string name;
            return ParseActive(sys, out name);
        }

        /// <summary>The name of the active plan as powercfg prints it (null when unknown).</summary>
        public static string ActiveName(ISystem sys)
        {
            string name;
            ParseActive(sys, out name);
            return name;
        }

        static string ParseActive(ISystem sys, out string name)
        {
            name = null;
            ProcessResult r = sys.Run(Exe, "/getactivescheme", 15000);
            if (r == null || r.ExitCode != 0) return null;
            foreach (string raw in (r.Output ?? "").Split('\n'))
            {
                Match m = Line.Match(raw.Trim());
                if (!m.Success) continue;
                name = m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
                return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        /// <summary>The GUID of an existing plan whose name contains <paramref name="namePart"/>, from "powercfg /list".</summary>
        public static string FindByName(ISystem sys, string namePart)
        {
            ProcessResult r = sys.Run(Exe, "/list", 15000);
            if (r == null || r.ExitCode != 0) return null;
            foreach (string raw in (r.Output ?? "").Split('\n'))
            {
                Match m = Line.Match(raw.Trim());
                if (!m.Success || !m.Groups[2].Success) continue;
                if (m.Groups[2].Value.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0) return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        /// <summary>The GUID when a plan with that GUID is listed by "powercfg /list", otherwise null.</summary>
        public static string FindByGuid(ISystem sys, string guid)
        {
            ProcessResult r = sys.Run(Exe, "/list", 15000);
            if (r == null || r.ExitCode != 0) return guid;   // cannot tell: let the restore try
            foreach (string raw in (r.Output ?? "").Split('\n'))
            {
                Match m = Line.Match(raw.Trim());
                if (m.Success && Same(m.Groups[1].Value, guid)) return guid;
            }
            return null;
        }

        public static void Activate(ISystem sys, string guid) { Custom.Must(sys, Exe, "/setactive " + guid, 30000); }

        /// <summary>Copies a built-in plan template into a new plan and returns the new GUID.</summary>
        public static string Duplicate(ISystem sys, string templateGuid)
        {
            ProcessResult r = Custom.Must(sys, Exe, "/duplicatescheme " + templateGuid, 30000);
            Match m = Line.Match(r.Output ?? "");
            if (!m.Success) throw new InvalidOperationException("powercfg did not report the new power plan.");
            return m.Groups[1].Value.ToLowerInvariant();
        }
    }

    /// <summary>Makes the Ultimate Performance or the High performance power plan the active plan. Undo reactivates the plan that was active before.</summary>
    public sealed class PowerPlanChange : Change
    {
        public enum Plan { Ultimate, High }

        readonly Plan plan;
        public PowerPlanChange(Plan which) { plan = which; }

        public override string Kind { get { return "power-plan"; } }
        public override bool NeedsAdmin { get { return true; } }

        string PlanName { get { return plan == Plan.Ultimate ? "Ultimate Performance" : "High performance"; } }

        public override string Describe()
        {
            return "Active power plan: " + PlanName + (plan == Plan.Ultimate ? " (created from Windows' built-in template when it does not exist yet)" : "");
        }

        public override string DescribeCurrent(ISystem sys)
        {
            string name = PerfPowerPlans.ActiveName(sys);
            string guid = PerfPowerPlans.ActiveGuid(sys);
            if (name == null && guid == null) return "unknown";
            return "active plan: " + (name ?? guid);
        }

        public override bool? IsOn(ISystem sys)
        {
            string guid = PerfPowerPlans.ActiveGuid(sys);
            if (guid == null) return null;
            if (plan == Plan.Ultimate)
            {
                if (PerfPowerPlans.Same(guid, PerfPowerPlans.Ultimate)) return true;
                if (PerfPowerPlans.IsBuiltIn(guid)) return false;
                string name = PerfPowerPlans.ActiveName(sys);
                return name != null && name.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (PerfPowerPlans.Same(guid, PerfPowerPlans.High)) return true;
            if (PerfPowerPlans.IsBuiltIn(guid)) return false;
            string n = PerfPowerPlans.ActiveName(sys);
            return n != null && string.Equals(n, "High performance", StringComparison.OrdinalIgnoreCase);
        }

        public override Snapshot Capture(ISystem sys)
        {
            var snap = new Snapshot { Kind = Kind, What = "Active power plan" };
            string guid = PerfPowerPlans.ActiveGuid(sys);
            string name = PerfPowerPlans.ActiveName(sys);
            if (guid != null) snap.Set("guid", guid);
            snap.Was = name ?? guid ?? "unknown";
            return snap;
        }

        public override void Apply(ISystem sys)
        {
            if (plan == Plan.Ultimate)
            {
                string existing = PerfPowerPlans.FindByName(sys, "Ultimate");
                if (existing != null) { PerfPowerPlans.Activate(sys, existing); return; }
                // The built-in plan can exist without being listed.
                ProcessResult direct = sys.Run(ProcessRunner.System32("powercfg.exe"), "/setactive " + PerfPowerPlans.Ultimate, 30000);
                if (direct != null && direct.ExitCode == 0) return;
                PerfPowerPlans.Activate(sys, PerfPowerPlans.Duplicate(sys, PerfPowerPlans.Ultimate));
                return;
            }
            ProcessResult r = sys.Run(ProcessRunner.System32("powercfg.exe"), "/setactive " + PerfPowerPlans.High, 30000);
            if (r != null && r.ExitCode == 0) return;
            PerfPowerPlans.Activate(sys, PerfPowerPlans.Duplicate(sys, PerfPowerPlans.High));
        }

        public override void Restore(ISystem sys, Snapshot before)
        {
            string guid = before.Get("guid");
            if (string.IsNullOrEmpty(guid)) { RestoreDefault(sys); return; }
            try { PerfPowerPlans.Activate(sys, guid); }
            catch (Exception) { RestoreDefault(sys); }   // the old plan was deleted since
        }

        public override void RestoreDefault(ISystem sys) { PerfPowerPlans.Activate(sys, PerfPowerPlans.Balanced); }
    }

    /// <summary>One setting of the active power plan, changed for "plugged in" only (the battery value is never touched).</summary>
    public sealed class AcPowerSetting : Change
    {
        public string Label, Subgroup, Setting;
        public uint OnValue, DefaultValue;

        public override string Kind { get { return "power-ac"; } }
        public override bool NeedsAdmin { get { return true; } }

        public override string Describe()
        {
            return "Active power plan, plugged in only: " + Label + " (" + Subgroup + "/" + Setting + ") = " + OnValue.ToString(CultureInfo.InvariantCulture)
                + ". Undo puts back the value saved for that plan; without history the usual default (" + DefaultValue.ToString(CultureInfo.InvariantCulture) + ") is written to the active plan";
        }

        public override string DescribeCurrent(ISystem sys)
        {
            uint? v = sys.GetPowerValue(Subgroup, Setting, true);
            return v == null ? "unknown" : v.Value.ToString(CultureInfo.InvariantCulture) + " plugged in";
        }

        public override bool? IsOn(ISystem sys)
        {
            uint? v = sys.GetPowerValue(Subgroup, Setting, true);
            if (v == null) return null;
            return v.Value == OnValue;
        }

        public override Snapshot Capture(ISystem sys)
        {
            var snap = new Snapshot { Kind = Kind, What = "Power plan (plugged in): " + Label };
            uint? v = sys.GetPowerValue(Subgroup, Setting, true);
            if (v != null) snap.Set("ac", v.Value.ToString(CultureInfo.InvariantCulture));
            string plan = PerfPowerPlans.ActiveGuid(sys);
            if (plan != null) snap.Set("plan", plan);
            snap.Was = DescribeCurrent(sys);
            return snap;
        }

        public override void Apply(ISystem sys) { sys.SetPowerValue(Subgroup, Setting, true, OnValue); }

        public override void Restore(ISystem sys, Snapshot before)
        {
            string ac = before.Get("ac");
            if (ac == null) return;
            uint value = uint.Parse(ac, CultureInfo.InvariantCulture);
            string plan = before.Get("plan");
            string active = PerfPowerPlans.ActiveGuid(sys);
            if (string.IsNullOrEmpty(plan) || active == null || PerfPowerPlans.Same(plan, active))
            {
                sys.SetPowerValue(Subgroup, Setting, true, value);
                return;
            }
            // Another plan is active now: put the value back on the plan that was changed, not on the one in use.
            if (PerfPowerPlans.FindByGuid(sys, plan) == null) return;   // that plan has been deleted since: nothing left to put back
            Custom.Must(sys, ProcessRunner.System32("powercfg.exe"), "/setacvalueindex " + plan + " " + Subgroup + " " + Setting + " " + value.ToString(CultureInfo.InvariantCulture), 30000);
        }

        public override void RestoreDefault(ISystem sys) { sys.SetPowerValue(Subgroup, Setting, true, DefaultValue); }
    }

    public static partial class Catalog
    {
        const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";
        const string SessionPowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

        static uint? PerfNumber(RegData d)
        {
            if (d == null) return null;
            if (d.Data is uint) return (uint)d.Data;
            if (d.Data is ulong) return unchecked((uint)(ulong)d.Data);
            if (d.Data is string) { uint v; if (uint.TryParse(((string)d.Data).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v; }
            return null;
        }

        static bool? HibernationIsOff(ISystem sys)
        {
            uint? v = PerfNumber(sys.ReadValue(Hive.Machine, PowerKey, "HibernateEnabled"));
            if (v == null) v = PerfNumber(sys.ReadValue(Hive.Machine, PowerKey, "HibernateEnabledDefault"));
            if (v == null) return null;
            return v.Value == 0;
        }

        static partial void AddPerformance(List<Tweak> list)
        {
            // Source: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options
            list.Add(new Tweak
            {
                Id = "performance.ultimate-power-plan", Category = "performance",
                Title = "Use the Ultimate Performance power plan",
                Summary = "Windows keeps the processor ready at full speed instead of slowing down to save power.",
                Why = "Useful on a desktop used for gaming, video editing or other heavy work, where short delays when the PC wakes up from idle are annoying. The trade-off is higher power use and more heat, even when the PC is idle.",
                OffMeans = "Turning it off switches back to the Balanced plan (or the plan you used before, from History).",
                Caution = "Not for laptops on battery: it drains the battery much faster.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately, ExclusiveGroup = "power-plan",
                Keywords = new[] { "power plan", "power options", "high performance", "ultimate", "cpu", "speed" },
                Changes = { new PowerPlanChange(PowerPlanChange.Plan.Ultimate) },
            });

            // Source: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options
            list.Add(new Tweak
            {
                Id = "performance.high-power-plan", Category = "performance",
                Title = "Use the High performance power plan",
                Summary = "Windows favours speed over saving power, without going as far as Ultimate Performance.",
                Why = "A middle step for gaming or heavy work on a desktop. It uses more power than Balanced but is the plan most programs and drivers expect to find.",
                OffMeans = "Turning it off switches back to the Balanced plan (or the plan you used before, from History).",
                Caution = "On a laptop this shortens battery life.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately, ExclusiveGroup = "power-plan", Presets = new[] { "gaming" },
                Keywords = new[] { "power plan", "power options", "balanced", "performance" },
                Changes = { new PowerPlanChange(PowerPlanChange.Plan.High) },
            });

            // Source: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options  (powercfg /hibernate)
            list.Add(new Tweak
            {
                Id = "performance.turn-off-hibernation", Category = "performance",
                Title = "Turn off hibernation",
                Summary = "Deletes the hibernation file (hiberfil.sys) and removes Hibernate and Fast Startup.",
                Why = "Frees several gigabytes of disk space (the file is sized as a share of your memory). The trade-off is that you can no longer hibernate, and Fast Startup stops working too.",
                OffMeans = "Hibernation is available and Windows keeps a hibernation file on the system drive.",
                Caution = "Laptops lose Hibernate, which saves your session when the battery runs out.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately, Keywords = new[] { "hiberfil", "hiberfil.sys", "disk space", "hibernate" },
                Changes =
                {
                    Custom.Make("Hibernation: off (powercfg /hibernate off; HKLM\\" + PowerKey + "  HibernateEnabled = 0)",
                        sys => HibernationIsOff(sys),
                        sys => Custom.Must(sys, ProcessRunner.System32("powercfg.exe"), "/hibernate off"),
                        sys => Custom.Must(sys, ProcessRunner.System32("powercfg.exe"), "/hibernate on"),
                        true,
                        sys => { bool? off = HibernationIsOff(sys); return off == null ? "unknown" : off.Value ? "hibernation is off" : "hibernation is on"; }),
                },
            });

            // Source: https://www.elevenforum.com/t/turn-on-or-off-fast-startup-in-windows-11.1212/
            list.Add(new Tweak
            {
                Id = "performance.turn-off-fast-startup", Category = "performance",
                Title = "Turn off Fast Startup",
                Summary = "Shut down fully switches the PC off instead of saving part of Windows to disk for a faster start.",
                Why = "With Fast Startup, Shut down is really a deep sleep, so drivers, updates and glitches can survive it. Turning it off makes Shut down a clean reset, at the cost of a slightly slower start.",
                OffMeans = "Fast Startup is on, and only Restart gives you a completely fresh Windows.",
                Risk = Risk.Safe, TakesEffect = Takes.PcRestart, Presets = new[] { "gaming" },
                Keywords = new[] { "hiberboot", "shutdown", "fast boot", "quick boot" },
                Changes = { Reg.Dword(Hive.Machine, SessionPowerKey, "HiberbootEnabled", 0, windowsDefault: 1) },
            });

            // Source: https://learn.microsoft.com/en-us/answers/questions/3738984/how-to-disable-xbox-game-bar  and https://winutil.christitus.com/dev/tweaks/essential-tweaks/gamedvr
            list.Add(new Tweak
            {
                Id = "performance.game-bar-recording-off", Category = "performance",
                Title = "Turn off Game Bar clip capture (Win+Alt+R)",
                Summary = "The Xbox Game Bar stops capturing gameplay clips and screenshots.",
                Why = "Stops Game Bar capturing, which helps if you use another recorder (OBS, your graphics card's own tool) or none at all. Game Bar's background recording is already off unless you turned it on, so do not expect a speed-up.",
                OffMeans = "Game Bar can take captures with Win+Alt+R.",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut, Presets = new[] { "gaming" },
                Keywords = new[] { "game dvr", "xbox", "capture", "record", "gamedvr" },
                Changes =
                {
                    Reg.Dword(Hive.User, @"System\GameConfigStore", "GameDVR_Enabled", 0, windowsDefault: 1),
                    Reg.Dword(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                },
            });

            // Source: https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/  (HwSchMode 2 = on, 1 = off)
            list.Add(new Tweak
            {
                Id = "performance.gpu-scheduling", Category = "performance",
                Title = "Turn on hardware-accelerated GPU scheduling",
                Summary = "Your graphics card manages its own work queue instead of Windows doing it on the processor.",
                Why = "Can lower latency and free some processor time in games. It only works with a recent graphics card and driver, and a few games or capture tools behave worse with it. If you notice stutter, turn this card off or use Settings > System > Display > Graphics > Default graphics settings.",
                OffMeans = "Turning this off removes the setting and lets the graphics driver choose, which on many PCs means it stays on. This card cannot force it off: use Settings > System > Display > Graphics > Default graphics settings for that. It only shows \"On\" when the setting was made here or in Settings.",
                Risk = Risk.Moderate, TakesEffect = Takes.PcRestart, MinBuild = 19041, Presets = new[] { "gaming" },
                Keywords = new[] { "hags", "gpu", "graphics", "nvidia", "amd", "latency", "hwschmode" },
                Changes = { Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa  (SPI_SETMOUSE: acceleration thresholds and speed)
            list.Add(new Tweak
            {
                Id = "performance.mouse-acceleration-off", Category = "performance",
                Title = "Turn off mouse acceleration",
                Summary = "The pointer moves the same distance for the same mouse movement, however fast you move it.",
                Why = "Gamers and designers prefer it because the mouse becomes predictable. The trade-off is that you may need a higher mouse speed to cross a large screen quickly. This is the \"Enhance pointer precision\" switch.",
                OffMeans = "Windows speeds the pointer up when you move the mouse quickly (Enhance pointer precision).",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut, Presets = new[] { "gaming" },
                Keywords = new[] { "enhance pointer precision", "pointer", "mouse speed", "aim", "mousespeed" },
                Changes =
                {
                    Reg.String(Hive.User, @"Control Panel\Mouse", "MouseSpeed", "0", windowsDefault: "1"),
                    Reg.String(Hive.User, @"Control Panel\Mouse", "MouseThreshold1", "0", windowsDefault: "6"),
                    Reg.String(Hive.User, @"Control Panel\Mouse", "MouseThreshold2", "0", windowsDefault: "10"),
                },
            });

            // Source: https://winaero.com/disable-animate-windows-when-minimizing-and-maximizing-in-windows-10
            list.Add(new Tweak
            {
                Id = "performance.window-animations-off", Category = "performance",
                Title = "Turn off window minimize and maximize animations",
                Summary = "Windows and taskbar buttons change instantly instead of sliding or fading.",
                Why = "Makes the desktop feel snappier, especially on older PCs or remote desktop sessions. It looks a little less polished.",
                OffMeans = "Windows animate when you minimise, maximise or restore them.",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut,
                Keywords = new[] { "animation", "minimize", "maximize", "effects", "visual effects", "minanimate" },
                Changes =
                {
                    Reg.String(Hive.User, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", windowsDefault: "1"),
                    Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "TaskbarAnimations", 0, windowsDefault: 1),
                },
            });

            // Source: https://winaero.com/how-to-enable-or-disable-transparency-in-windows-11
            list.Add(new Tweak
            {
                Id = "performance.transparency-off", Category = "performance",
                Title = "Turn off transparency effects",
                Summary = "The taskbar, Start menu and app windows use solid colours instead of frosted glass.",
                Why = "Saves a little graphics work, which helps on weak graphics hardware, and makes text a bit easier to read. The desktop looks plainer.",
                OffMeans = "Windows shows see-through acrylic and Mica effects.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "acrylic", "mica", "glass", "blur", "translucent", "enabletransparency" },
                Changes = { Reg.Dword(Hive.User, Reg.Personalize, "EnableTransparency", 0, windowsDefault: 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/application-management/enterprise-background-activity-controls  (Privacy/LetAppsRunInBackground, value 2 = force deny)
            list.Add(new Tweak
            {
                Id = "performance.background-apps-off", Category = "performance",
                Title = "Stop Microsoft Store apps running in the background",
                Summary = "Store apps only work while you are using them.",
                Why = "Saves battery, memory and a little network traffic if you have many Store apps (Mail, Weather, News, Xbox and similar). The trade-off is that those apps will not update live tiles or fetch data in the background.",
                OffMeans = "Each Store app decides for itself in its Advanced options.",
                Caution = "Apps such as Mail and Calendar will not show new-item notifications until you open them.",
                Risk = Risk.Moderate, TakesEffect = Takes.SignOut,
                Keywords = new[] { "uwp", "store apps", "background activity", "battery", "letappsruninbackground" },
                Changes = { Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2) },
            });

            // Source: https://winaero.com/disable-power-throttling
            list.Add(new Tweak
            {
                Id = "performance.power-throttling-off", Category = "performance",
                Title = "Turn off power throttling",
                Summary = "Windows no longer slows down background programs to save battery.",
                Why = "Helps if a background program, such as a render, a game launcher or a build tool, runs slower than it should while you use another window. On a laptop it uses more battery.",
                OffMeans = "Windows may limit the processor speed of programs it considers to be working in the background.",
                Risk = Risk.Advanced, TakesEffect = Takes.PcRestart,
                Keywords = new[] { "efficiency mode", "throttling", "cpu", "background", "powerthrottlingoff" },
                Changes = { Reg.Dword(Hive.Machine, PowerKey + @"\PowerThrottling", "PowerThrottlingOff", 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options  (SUB_SLEEP / STANDBYIDLE, in seconds)
            list.Add(new Tweak
            {
                Id = "performance.never-sleep-plugged-in", Category = "performance",
                Title = "Never put the PC to sleep while plugged in",
                Summary = "On the current power plan, the PC stays awake when it is on mains power. Battery behaviour is not changed.",
                Why = "Stops downloads, backups, remote access and long renders from being cut off when you step away. The trade-off is that the PC keeps using power until you put it to sleep yourself.",
                OffMeans = "The PC goes to sleep after the idle time set in Power & battery settings. If Tweak Bench has no saved value to put back, it sets 30 minutes on the active plan.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "sleep", "standby", "idle", "always on", "power", "standbyidle" },
                Changes = { new AcPowerSetting { Label = "Sleep after (seconds)", Subgroup = "SUB_SLEEP", Setting = "STANDBYIDLE", OnValue = 0, DefaultValue = 1800 } },
            });

            // Source: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/usb-settings-usb-selective-suspend-setting
            list.Add(new Tweak
            {
                Id = "performance.usb-suspend-off", Category = "performance",
                Title = "Turn off USB selective suspend while plugged in",
                Summary = "Windows stops powering down idle USB devices when the PC is on mains power.",
                Why = "Fixes USB devices that disconnect, stutter or wake up slowly, such as headsets, audio interfaces and external drives. The trade-off is a tiny increase in power use.",
                OffMeans = "Windows may switch idle USB devices into a low-power state. If Tweak Bench has no saved value to put back, it turns the setting on for the active plan.",
                Risk = Risk.Advanced, TakesEffect = Takes.Immediately,
                Keywords = new[] { "usb", "disconnect", "power saving", "selective suspend", "device keeps disconnecting" },
                Changes = { new AcPowerSetting { Label = "USB selective suspend (0 = disabled)", Subgroup = "2a737441-1930-4402-8d77-b2bebba308a3", Setting = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", OnValue = 0, DefaultValue = 1 } },
            });

            // Source: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/backgroundmodeenabled and .../startupboostenabled
            list.Add(new Tweak
            {
                Id = "performance.edge-background-off", Category = "performance",
                Title = "Stop Microsoft Edge running in the background",
                Summary = "Edge no longer starts with Windows and does not keep running after you close its last window.",
                Why = "Saves memory and startup time if you rarely use Edge, or use another browser. Edge opens a little slower the first time you start it.",
                OffMeans = "Edge may start with Windows and keep running in the background.",
                Caution = "Edge will show \"managed by your organization\" because this uses Edge's policy settings.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "edge", "startup boost", "background mode", "browser", "memory" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0),
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0),
                },
            });
        }
    }
}
