using System;
using System.Collections.Generic;
using System.Globalization;

namespace TweakBench.Core
{
    /// <summary>
    /// Turns off the keyboard shortcut of Sticky Keys, Filter Keys and Toggle Keys by clearing only the "hotkey active" bit (0x4)
    /// in each Flags value. Every other accessibility option in those values is left exactly as it is. Undo puts that one bit
    /// back to what it was, so options changed in Settings in the meantime are kept.
    /// </summary>
    public sealed class AccessibilityHotkeyChange : Change
    {
        const uint HotkeyBit = 0x4;

        // Registry key, the Windows default Flags text, a plain name.
        static readonly string[][] Keys =
        {
            new[] { @"Control Panel\Accessibility\StickyKeys", "510", "Sticky Keys" },
            new[] { @"Control Panel\Accessibility\Keyboard Response", "126", "Filter Keys" },
            new[] { @"Control Panel\Accessibility\ToggleKeys", "62", "Toggle Keys" },
        };

        public override string Kind { get { return "accessibility-flags"; } }
        public override bool NeedsAdmin { get { return false; } }

        public override string Describe()
        {
            return @"HKCU\Control Panel\Accessibility\StickyKeys, Keyboard Response and ToggleKeys  Flags: only the shortcut bit (0x4) is cleared, every other bit stays as it is";
        }

        static uint? Number(object data)
        {
            if (data is uint) return (uint)data;
            if (data is ulong) return unchecked((uint)(ulong)data);
            string text = data as string;
            uint v;
            if (text != null && uint.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        static uint? Current(ISystem sys, string[] key)
        {
            RegData d = sys.ReadValue(Hive.User, key[0], "Flags");
            return d == null ? Number(key[1]) : Number(d.Data);   // an absent value behaves like the Windows default (shortcut on)
        }

        static void Write(ISystem sys, string path, RegKind kind, uint value)
        {
            if (kind == RegKind.Dword) sys.WriteValue(Hive.User, path, "Flags", RegKind.Dword, value);
            else sys.WriteValue(Hive.User, path, "Flags", RegKind.String, value.ToString(CultureInfo.InvariantCulture));
        }

        public override string DescribeCurrent(ISystem sys)
        {
            var parts = new List<string>();
            foreach (string[] key in Keys)
            {
                RegData d = sys.ReadValue(Hive.User, key[0], "Flags");
                uint? n = Current(sys, key);
                parts.Add(key[2] + (n == null ? " unknown" : (n.Value & HotkeyBit) == 0 ? " shortcut off" : " shortcut on") + (d == null ? " (not set)" : " (Flags " + Reg.Encode(d.Kind, d.Data) + ")"));
            }
            return string.Join(", ", parts);
        }

        public override bool? IsOn(ISystem sys)
        {
            bool all = true;
            foreach (string[] key in Keys)
            {
                uint? n = Current(sys, key);
                if (n == null) return null;
                if ((n.Value & HotkeyBit) != 0) all = false;
            }
            return all;
        }

        public override Snapshot Capture(ISystem sys)
        {
            var snap = new Snapshot { Kind = Kind, What = "Sticky Keys, Filter Keys and Toggle Keys shortcuts" };
            for (int i = 0; i < Keys.Length; i++)
            {
                RegData d = sys.ReadValue(Hive.User, Keys[i][0], "Flags");
                snap.Set("k" + i + ".exists", d == null ? "0" : "1");
                if (d != null) { snap.Set("k" + i + ".kind", d.Kind.ToString()); snap.Set("k" + i + ".data", Reg.Encode(d.Kind, d.Data)); }
            }
            snap.Was = DescribeCurrent(sys);
            return snap;
        }

        public override void Apply(ISystem sys)
        {
            foreach (string[] key in Keys)
            {
                RegData d = sys.ReadValue(Hive.User, key[0], "Flags");
                if (d == null) { Write(sys, key[0], RegKind.String, Number(key[1]).Value & ~HotkeyBit); continue; }
                uint? n = Number(d.Data);
                if (n == null) continue;   // not a number we understand: leave it alone
                if ((n.Value & HotkeyBit) != 0) Write(sys, key[0], d.Kind, n.Value & ~HotkeyBit);
            }
        }

        public override void Restore(ISystem sys, Snapshot before)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                string exists = before.Get("k" + i + ".exists");
                if (exists == null) continue;
                if (exists != "1") { sys.DeleteValue(Hive.User, Keys[i][0], "Flags"); continue; }
                RegKind kind = (RegKind)Enum.Parse(typeof(RegKind), before.Get("k" + i + ".kind"));
                object saved = Reg.Decode(kind, before.Get("k" + i + ".data"));
                RegData now = sys.ReadValue(Hive.User, Keys[i][0], "Flags");
                uint? was = Number(saved);
                uint? cur = now == null ? null : Number(now.Data);
                if (now == null || cur == null || was == null) { sys.WriteValue(Hive.User, Keys[i][0], "Flags", kind, saved); continue; }
                uint wanted = (was.Value & HotkeyBit) != 0 ? cur.Value | HotkeyBit : cur.Value & ~HotkeyBit;
                if (wanted != cur.Value) Write(sys, Keys[i][0], now.Kind, wanted);
            }
        }

        public override void RestoreDefault(ISystem sys)
        {
            foreach (string[] key in Keys)
            {
                RegData d = sys.ReadValue(Hive.User, key[0], "Flags");
                if (d == null) { sys.WriteValue(Hive.User, key[0], "Flags", RegKind.String, key[1]); continue; }
                uint? n = Number(d.Data);
                if (n != null && (n.Value & HotkeyBit) == 0) Write(sys, key[0], d.Kind, n.Value | HotkeyBit);
            }
        }
    }

    public static partial class Catalog
    {
        static partial void AddSystem(List<Tweak> list)
        {
            // Source: https://www.elevenforum.com/t/change-to-dark-or-light-color-mode-in-windows-11.555/
            list.Add(new Tweak
            {
                Id = "system.dark-mode", Category = "system",
                Title = "Use dark mode for Windows and apps",
                Summary = "Windows, the taskbar, Settings and apps that follow the system theme use dark colours.",
                Why = "Easier on the eyes in a dim room and uses less power on OLED screens. Some older programs ignore the setting and stay light.",
                OffMeans = "Windows and apps use the light colour mode.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "dark theme", "night mode", "colour mode", "color mode", "appearance", "AppsUseLightTheme" },
                Changes =
                {
                    Reg.Dword(Hive.User, Reg.Personalize, "AppsUseLightTheme", 0, windowsDefault: 1),
                    Reg.Dword(Hive.User, Reg.Personalize, "SystemUsesLightTheme", 0, windowsDefault: 1),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-stickykeys  (SKF_HOTKEYACTIVE = 0x4; likewise FKF_ and TKF_)
            list.Add(new Tweak
            {
                Id = "system.no-accessibility-shortcuts", Category = "system",
                Title = "Turn off the Sticky Keys, Filter Keys and Toggle Keys shortcuts",
                Summary = "Pressing Shift five times, holding Shift for eight seconds or holding Num Lock no longer opens an accessibility pop-up.",
                Why = "Stops the pop-ups that interrupt games and fast typing. People who rely on these features can still turn them on in Settings, only the keyboard shortcuts stop working.",
                OffMeans = "Pressing Shift five times offers to turn on Sticky Keys, and the other two shortcuts do the same for their features.",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut, Presets = new[] { "gaming", "clean" },
                Keywords = new[] { "sticky keys", "filter keys", "toggle keys", "shift five times", "accessibility", "popup", "pop-up", "stickykeys" },
                Changes = { new AccessibilityHotkeyChange() },
            });

            // Source: https://winaero.com/enable-numlock-logon-screen-windows-10
            list.Add(new Tweak
            {
                Id = "system.numlock-at-startup", Category = "system",
                Title = "Turn Num Lock on at sign-in and after startup",
                Summary = "The number pad types numbers as soon as you reach the sign-in screen.",
                Why = "Saves pressing Num Lock before typing a PIN. If your PC firmware or Fast Startup resets the key at boot, it can still start off.",
                OffMeans = "Num Lock starts off.",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut,
                Keywords = new[] { "numlock", "num lock", "number pad", "keypad", "keyboard", "InitialKeyboardIndicators", "pin" },
                Changes =
                {
                    Reg.String(Hive.User, @"Control Panel\Keyboard", "InitialKeyboardIndicators", "2", windowsDefault: "0"),
                    Reg.String(Hive.DefaultUser, @"Control Panel\Keyboard", "InitialKeyboardIndicators", "2147483650", windowsDefault: "2147483648"),
                },
            });

            // Source: https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/enable-verbose-startup-shutdown-logon-logoff-status-messages
            list.Add(new Tweak
            {
                Id = "system.detailed-startup-messages", Category = "system",
                Title = "Show detailed messages while starting, signing in and shutting down",
                Summary = "The screens that say \"Please wait\" tell you what Windows is doing at each step.",
                Why = "Helpful when startup or shutdown seems stuck: you can see which step takes the time. The extra text is harmless but busier.",
                OffMeans = "Windows shows short messages such as \"Welcome\" and \"Shutting down\".",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut,
                Keywords = new[] { "verbose", "verbosestatus", "boot", "slow shutdown", "status messages", "troubleshooting" },
                Changes = { Reg.Dword(Hive.Machine, Reg.MachinePolicySystem, "VerboseStatus", 1) },
            });

            // Source: https://winaero.com/show-bsod-details-instead-of-the-sad-smiley-in-windows-10
            list.Add(new Tweak
            {
                Id = "system.crash-screen-details", Category = "system",
                Title = "Show technical details on the crash screen",
                Summary = "If Windows crashes, the crash screen asks for the extra technical details, such as the error code and the driver involved.",
                Why = "Makes it easier to search for the cause of a crash. Windows 11 24H2 and later already show the stop code and the failing module, so on those versions this may change little.",
                OffMeans = "The crash screen shows what Windows shows by default.",
                Risk = Risk.Safe, TakesEffect = Takes.PcRestart,
                Keywords = new[] { "bsod", "blue screen", "stop code", "bug check", "crash", "displayparameters" },
                Changes = { Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", 1) },
            });

            // Source: https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/configure-system-failure-and-recovery-options  (Automatically restart = AutoReboot)
            list.Add(new Tweak
            {
                Id = "system.stay-on-crash-screen", Category = "system",
                Title = "Stay on the blue crash screen instead of restarting",
                Summary = "After a system crash, Windows waits on the blue screen until you restart the PC yourself.",
                Why = "Gives you time to read or photograph the error. The trade-off is that a PC that crashes while you are away stays stuck until someone restarts it.",
                OffMeans = "Windows restarts by itself after a crash.",
                Caution = "A PC that you use remotely or that runs unattended will not recover from a crash on its own.",
                Risk = Risk.Moderate, TakesEffect = Takes.PcRestart,
                Keywords = new[] { "bsod", "autoreboot", "automatically restart", "system failure", "crash", "reboot loop" },
                Changes = { Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", 0, windowsDefault: 1) },
            });

            // Source: https://www.howtogeek.com/323390/how-to-fix-windows-and-linux-showing-different-times-when-dual-booting
            list.Add(new Tweak
            {
                Id = "system.clock-in-utc", Category = "system",
                Title = "Keep the hardware clock in UTC (for dual boot with Linux)",
                Summary = "Windows stores the PC's clock in universal time, the way Linux and macOS do.",
                Why = "Fixes the clock being hours wrong after switching between Windows and Linux on the same PC. Leave it off if Windows is the only system.",
                OffMeans = "Windows stores local time in the hardware clock.",
                Caution = "Until the time is corrected once, the clock can be hours off. Windows is not officially documented to support this setting.",
                Risk = Risk.Advanced, TakesEffect = Takes.PcRestart,
                Keywords = new[] { "dual boot", "linux", "ubuntu", "time wrong", "rtc", "realtimeisuniversal", "timezone" },
                Changes = { Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\TimeZoneInformation", "RealTimeIsUniversal", 1) },
            });

            // Source: https://www.elevenforum.com/t/enable-or-disable-snap-layouts-for-maximize-button-in-windows-11.61/
            list.Add(new Tweak
            {
                Id = "system.no-snap-layouts-flyout", Category = "system",
                Title = "Turn off the snap layouts pop-up on the maximize button",
                Summary = "Pointing at the maximize button no longer opens the box of window layouts.",
                Why = "Removes a pop-up that appears when you only meant to maximise a window. Dragging a window to the screen edge and Win+arrow keys still snap windows.",
                OffMeans = "A layout picker appears when you hover over the maximize button.",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut, MinBuild = 22000,
                Keywords = new[] { "snap", "snap layouts", "maximize button", "window layout", "multitasking", "EnableSnapAssistFlyout" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "EnableSnapAssistFlyout", 0, windowsDefault: 1) },
            });

            // Source: https://www.elevenforum.com/t/enable-or-disable-snap-assist-in-windows-11.1308/
            list.Add(new Tweak
            {
                Id = "system.no-snap-suggestions", Category = "system",
                Title = "Turn off snap suggestions after snapping a window",
                Summary = "After you snap a window to one side, Windows no longer offers your other windows for the empty space.",
                Why = "Removes the overview that appears after every snap. You still choose the second window yourself.",
                OffMeans = "After snapping a window, Windows lists other open windows to fill the other half.",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut,
                Keywords = new[] { "snap assist", "snap", "multitasking", "window suggestions", "SnapAssist" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "SnapAssist", 0, windowsDefault: 1) },
            });

            // Source: https://www.elevenforum.com/t/turn-on-or-off-let-windows-manage-default-printer-in-windows-11.7759/
            list.Add(new Tweak
            {
                Id = "system.manual-default-printer", Category = "system",
                Title = "Stop Windows changing my default printer",
                Summary = "The default printer stays the one you chose instead of following the last printer you used.",
                Why = "Fixes documents printing on the wrong printer after you used another one once. You choose the default printer yourself.",
                OffMeans = "Windows makes the printer you used most recently, at your current location, the default.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately, Presets = new[] { "clean" },
                Keywords = new[] { "printer", "default printer", "printing", "LegacyDefaultPrinterMode" },
                Changes = { Reg.Dword(Hive.User, @"Software\Microsoft\Windows NT\CurrentVersion\Windows", "LegacyDefaultPrinterMode", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: https://www.elevenforum.com/t/add-or-remove-desktop-icons-in-windows-11.4827/
            list.Add(new Tweak
            {
                Id = "system.this-pc-desktop-icon", Category = "system",
                Title = "Show the This PC icon on the desktop",
                Summary = "A This PC shortcut appears on the desktop for quick access to your drives.",
                Why = "A familiar shortcut to your drives and devices. It adds one more icon to the desktop.",
                OffMeans = "The desktop does not show This PC (it is still in File Explorer).",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart,
                Keywords = new[] { "my computer", "computer", "desktop icons", "this pc", "shortcut" },
                Changes = { Reg.Dword(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0) },
            });

            // Source: https://www.elevenforum.com/t/change-what-alt-tab-shows-in-windows-11.3836/  (MultiTaskingAltTabFilter: 3 = open windows only)
            list.Add(new Tweak
            {
                Id = "system.alt-tab-windows-only", Category = "system",
                Title = "Show only open windows in Alt+Tab and snap suggestions",
                Summary = "Alt+Tab lists your open windows only, not every browser tab in Microsoft Edge.",
                Why = "Keeps Alt+Tab short and predictable. If you like jumping straight to an Edge tab with Alt+Tab, leave it off.",
                OffMeans = "Alt+Tab also lists recent Microsoft Edge tabs (the number is set in Settings).",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut, MinBuild = 22000, Presets = new[] { "clean" },
                Keywords = new[] { "alt tab", "alt+tab", "edge tabs", "task switcher", "multitasking", "MultiTaskingAltTabFilter" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "MultiTaskingAltTabFilter", 3) },
            });

            // Source: https://www.elevenforum.com/t/add-or-remove-learn-more-about-this-picture-desktop-icon-in-windows-11.18170/  (HideDesktopIcons\NewStartPanel, {2cc5ca98-6485-489a-920e-b3e88a6ccce3}: 1 = hidden)
            // This is a per-user setting, so it works on Pro. The Spotlight policies (DisableSpotlightCollectionOnDesktop) are documented as Enterprise/Education only.
            list.Add(new Tweak
            {
                Id = "system.no-spotlight-desktop", Category = "system",
                Title = "Hide the \"Learn more about this picture\" desktop icon",
                Summary = "The Windows Spotlight shortcut no longer sits on the desktop. Your wallpaper is not changed.",
                Why = "Keeps the desktop clear of the picture-of-the-day icon and its web link. It does not turn Spotlight off: to stop the daily pictures, choose your own wallpaper in Settings > Personalization > Background.",
                OffMeans = "Windows shows the icon while Spotlight is your desktop background.",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart, MinBuild = 22621, Presets = new[] { "clean" },
                Keywords = new[] { "spotlight", "wallpaper", "learn more about this picture", "background", "desktop icon", "2cc5ca98" },
                Changes = { Reg.Dword(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{2cc5ca98-6485-489a-920e-b3e88a6ccce3}", 1) },
            });
        }
    }
}
