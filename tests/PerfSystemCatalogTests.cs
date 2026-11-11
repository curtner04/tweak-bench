using System;
using System.Linq;
using TweakBench.Core;

namespace TweakBench.Tests
{
    /// <summary>Behaviour of the performance, system, network and services catalog changes that go beyond a plain registry value.</summary>
    public static class PerfSystemCatalogTests
    {
        const string Sticky = @"Control Panel\Accessibility\StickyKeys";
        const string Filter = @"Control Panel\Accessibility\Keyboard Response";
        const string Toggle = @"Control Panel\Accessibility\ToggleKeys";

        public static void Run(T t)
        {
            GamingPreset(t);
            AccessibilityShortcuts(t);
            Teredo(t);
            PowerSettingUndo(t);
            Spotlight(t);
        }

        static void GamingPreset(T t)
        {
            var plans = Catalog.All.Where(x => x.InPreset("gaming") && x.ExclusiveGroup == "power-plan").ToList();
            t.Equal(1, plans.Count, "the gaming preset holds exactly one power plan");
            t.Check(!Catalog.All.Any(x => x.Id == "performance.background-apps-off" && x.InPreset("gaming")), "the gaming preset does not stop Store apps running in the background");
            t.Check(!Catalog.All.Any(x => x.Id == "performance.edge-background-off" && x.InPreset("gaming")), "the gaming preset does not apply the Edge policy");
        }

        static string Flags(MemorySystem sys, string key)
        {
            RegData d = sys.ReadValue(Hive.User, key, "Flags");
            return d == null ? "(none)" : d.Kind + ":" + Reg.Encode(d.Kind, d.Data);
        }

        static void AccessibilityShortcuts(T t)
        {
            Tweak tw = Catalog.Find("system.no-accessibility-shortcuts");
            t.Check(tw != null && tw.Changes.Count == 1 && tw.Changes[0] is AccessibilityHotkeyChange, "the accessibility tweak clears only the shortcut bit");
            if (tw == null) return;
            Change c = tw.Changes[0];

            // A PC where the shortcut bit is already clear in Sticky Keys (stored as a DWORD, as on some PCs) reads as On.
            var sys = new MemorySystem(26300, "Professional", true);
            sys.Seed(Hive.User, Sticky, "Flags", RegKind.Dword, 58u);
            sys.Seed(Hive.User, Filter, "Flags", RegKind.String, "127");
            sys.Seed(Hive.User, Toggle, "Flags", RegKind.String, "63");
            t.Equal<bool?>(false, c.IsOn(sys), "one key with the shortcut bit set reads as Off");

            Snapshot before = c.Capture(sys);
            c.Apply(sys);
            t.Equal<bool?>(true, c.IsOn(sys), "after turning on, every shortcut bit is clear");
            t.Equal("Dword:58", Flags(sys, Sticky), "a Flags value whose bit was already clear is not touched (and keeps its type)");
            t.Equal("String:123", Flags(sys, Filter), "only bit 0x4 is cleared (127 becomes 123)");
            t.Equal("String:59", Flags(sys, Toggle), "only bit 0x4 is cleared (63 becomes 59)");

            c.Restore(sys, before);
            t.Equal("Dword:58", Flags(sys, Sticky), "undo keeps Sticky Keys as it was");
            t.Equal("String:127", Flags(sys, Filter), "undo puts the Filter Keys value back");
            t.Equal("String:63", Flags(sys, Toggle), "undo puts the Toggle Keys value back");

            // Another option changed after the tweak was applied is kept by undo.
            c.Apply(sys);
            sys.Seed(Hive.User, Filter, "Flags", RegKind.String, "121");   // 123 -> 121: the user switched another option off
            c.Restore(sys, before);
            t.Equal("String:125", Flags(sys, Filter), "undo only brings back the shortcut bit and keeps later changes");

            // Values that are missing are created from the Windows defaults and removed again by undo.
            var empty = new MemorySystem(26300, "Professional", true);
            t.Equal<bool?>(false, c.IsOn(empty), "missing Flags values behave like the Windows defaults (shortcut on)");
            Snapshot none = c.Capture(empty);
            c.Apply(empty);
            t.Equal("String:506", Flags(empty, Sticky), "Sticky Keys: default 510 with the shortcut bit cleared");
            t.Equal("String:122", Flags(empty, Filter), "Filter Keys: default 126 with the shortcut bit cleared");
            t.Equal("String:58", Flags(empty, Toggle), "Toggle Keys: default 62 with the shortcut bit cleared");
            c.Restore(empty, none);
            t.Equal("(none)", Flags(empty, Sticky), "undo removes a value that did not exist");

            c.Apply(empty);
            c.RestoreDefault(empty);
            t.Equal("String:510", Flags(empty, Sticky), "without history, off sets the Windows default again");
        }

        static string TeredoType(MemorySystem sys) { return TeredoStateChange.ReadType(sys); }

        static void Teredo(T t)
        {
            Tweak tw = Catalog.Find("network.teredo-off");
            t.Check(tw != null && !string.IsNullOrEmpty(tw.Caution) && tw.Caution.IndexOf("Xbox", StringComparison.Ordinal) >= 0, "the Teredo tweak warns about Xbox and Store multiplayer");
            if (tw == null) return;
            Change c = tw.Changes[0];

            var sys = new MemorySystem(26300, "Professional", true);
            sys.Run("netsh.exe", "interface teredo set state enterpriseclient");
            Snapshot before = c.Capture(sys);
            c.Apply(sys);
            t.Equal("disabled", TeredoType(sys), "Teredo is disabled after turning the tweak on");
            t.Equal<bool?>(true, c.IsOn(sys), "the tweak reads as On when Teredo is disabled");
            c.Restore(sys, before);
            t.Equal("enterpriseclient", TeredoType(sys), "undo puts back the exact Teredo type that was set before");

            c.Apply(sys);
            c.RestoreDefault(sys);
            t.Equal("default", TeredoType(sys), "without history, off returns Teredo to its default");
        }

        static void PowerSettingUndo(T t)
        {
            Tweak tw = Catalog.Find("performance.never-sleep-plugged-in");
            if (tw == null) { t.Check(false, "performance.never-sleep-plugged-in exists"); return; }
            var change = (AcPowerSetting)tw.Changes[0];

            var sys = new MemorySystem(26300, "Professional", true);
            sys.AddPower(change.Subgroup, change.Setting, 600u, 300u);
            Snapshot before = change.Capture(sys);
            t.Check(!string.IsNullOrEmpty(before.Get("plan")), "the snapshot records which power plan was changed");
            change.Apply(sys);
            t.Equal<uint?>(0u, sys.GetPowerValue(change.Subgroup, change.Setting, true), "turning on writes the plugged-in value");

            // Same plan still active: the value is written back the normal way.
            change.Restore(sys, before);
            t.Equal<uint?>(600u, sys.GetPowerValue(change.Subgroup, change.Setting, true), "undo restores the plugged-in value");
            t.Equal<uint?>(300u, sys.GetPowerValue(change.Subgroup, change.Setting, false), "the battery value is never touched");

            // A different plan is active at undo time: the saved plan is addressed by its GUID instead.
            change.Apply(sys);
            sys.Run(@"C:\Windows\System32\powercfg.exe", "/setactive " + PerfPowerPlans.High);
            sys.Commands.Clear();
            change.Restore(sys, before);
            t.Check(sys.Commands.Any(x => x.IndexOf("/setacvalueindex " + before.Get("plan"), StringComparison.OrdinalIgnoreCase) >= 0 && x.EndsWith(" 600", StringComparison.Ordinal)),
                "undo restores the plan that was changed, not the plan that is active now");
            t.Equal<uint?>(0u, sys.GetPowerValue(change.Subgroup, change.Setting, true), "the plan that is active now keeps its own value");
        }

        static void Spotlight(T t)
        {
            Tweak tw = Catalog.Find("system.no-spotlight-desktop");
            t.Check(tw != null, "system.no-spotlight-desktop still exists (ids are permanent)");
            if (tw == null) return;
            var reg = tw.Changes.Count == 1 ? tw.Changes[0] as RegValue : null;
            t.Check(reg != null && reg.Path.IndexOf("Policies", StringComparison.OrdinalIgnoreCase) < 0 && reg.Hive == Hive.User, "the Spotlight tweak uses a per-user setting that works on Windows 11 Pro");
            t.Check(!tw.InPreset("privacy"), "hiding the Spotlight icon is not part of the privacy preset");
        }
    }
}
