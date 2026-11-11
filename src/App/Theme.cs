using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TweakBench.App
{
    /// <summary>
    /// Light and dark colour sets. MainWindow.xaml defines every brush key once (dark defaults) and all styles use
    /// DynamicResource, so swapping the entries here restyles the whole window, including pages built in code.
    /// </summary>
    public static class Theme
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        static readonly Dictionary<string, string> Dark = new Dictionary<string, string>
        {
            { "Bg", "#111318" }, { "Card", "#1A1D24" }, { "Card2", "#22262F" }, { "Input", "#15181E" }, { "Border", "#2E333D" },
            { "Text", "#E8EAED" }, { "Muted", "#A0A7B4" }, { "Accent", "#4CA8FF" }, { "AccentFg", "#0B1220" }, { "AccentText", "#6CB8FF" },
            { "BtnBg", "#2A2F3A" }, { "Good", "#6CCB8A" }, { "GoodBg", "#1B3326" }, { "Warn", "#F0B44C" }, { "WarnBg", "#3A2E14" },
            { "Bad", "#FF8A80" }, { "BadBg", "#45201E" }, { "Info", "#7FB7FF" }, { "InfoBg", "#1B2A40" }, { "Selected", "#243246" }
        };

        static readonly Dictionary<string, string> Light = new Dictionary<string, string>
        {
            { "Bg", "#F3F5F8" }, { "Card", "#FFFFFF" }, { "Card2", "#F6F8FB" }, { "Input", "#FFFFFF" }, { "Border", "#D9DEE6" },
            { "Text", "#1B1F27" }, { "Muted", "#5B6575" }, { "Accent", "#0A64C8" }, { "AccentFg", "#FFFFFF" }, { "AccentText", "#0A5BB5" },
            { "BtnBg", "#E8ECF2" }, { "Good", "#1E7D3C" }, { "GoodBg", "#E2F3E7" }, { "Warn", "#8A5A00" }, { "WarnBg", "#FFF2D6" },
            { "Bad", "#B3261E" }, { "BadBg", "#FBE4E1" }, { "Info", "#0A5BB5" }, { "InfoBg", "#E4EEFB" }, { "Selected", "#E3ECF8" }
        };

        public static bool IsDark { get; private set; }

        /// <summary>True when Windows' "app mode" is dark (HKCU ...\Themes\Personalize\AppsUseLightTheme = 0). Light when unknown.</summary>
        public static bool SystemIsDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value == 0;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Applies "auto", "light" or "dark" to the resource dictionary.</summary>
        public static void Apply(ResourceDictionary resources, string mode)
        {
            bool dark = mode == "dark" || (mode != "light" && SystemIsDark());
            IsDark = dark;
            foreach (var kv in dark ? Dark : Light)
                resources[kv.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value));
        }

        /// <summary>Makes the title bar of a window follow the current theme (Windows 10 1809+ / Windows 11).</summary>
        public static void ApplyTitleBar(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                int dark = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            }
            catch (Exception) { /* older Windows: the title bar stays as it is */ }
        }

        /// <summary>Gives a dialog the main window's styles and colours (and keeps them in step when the theme changes).</summary>
        public static void Attach(Window dialog, Window main)
        {
            dialog.Resources.MergedDictionaries.Add(main.Resources);
            dialog.SetResourceReference(Window.BackgroundProperty, "Bg");
            dialog.SetResourceReference(Window.ForegroundProperty, "Text");
            dialog.FontFamily = main.FontFamily;
            dialog.FontSize = main.FontSize;
            dialog.UseLayoutRounding = true;
            dialog.SnapsToDevicePixels = true;
            dialog.SourceInitialized += (s, e) => ApplyTitleBar(dialog);
            if (main.Icon != null) dialog.Icon = main.Icon;
        }
    }
}
