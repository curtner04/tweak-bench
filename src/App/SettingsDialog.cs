using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>Settings: theme, restore point before applying, ask for administrator rights, show advanced tweaks. Changes apply and save at once.</summary>
    public sealed class SettingsDialog
    {
        public readonly Window Window;

        /// <param name="optionsChanged">Called after any option changed (the window re-applies the theme and refreshes the pages).</param>
        public SettingsDialog(UiContext ctx, Action optionsChanged)
        {
            Window = DialogHelpers.Create(ctx, "Settings", 560, 640);
            AppSettings s = ctx.Settings;
            Action changed = () => { ctx.SaveSettings(); optionsChanged(); };

            var stack = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
            stack.Children.Add(Ui.Heading("Settings", "H1"));
            var intro = Ui.Para("Changes apply and save as soon as you make them.");
            intro.Margin = new Thickness(0, 4, 0, 18);
            stack.Children.Add(intro);

            // Appearance
            stack.Children.Add(Caption("Appearance"));
            var themes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            foreach (var option in new[] { new[] { "auto", "Follow Windows" }, new[] { "light", "Light" }, new[] { "dark", "Dark" } })
            {
                string value = option[0];
                var radio = new RadioButton { Content = option[1], GroupName = "theme", IsChecked = s.Theme == value, Margin = new Thickness(0, 0, 26, 0) };
                radio.Checked += (a, b) => { if (s.Theme != value) { s.Theme = value; changed(); } };
                themes.Children.Add(radio);
            }
            stack.Children.Add(themes);

            // Safety
            stack.Children.Add(Caption("Safety"));
            stack.Children.Add(Option("Create a restore point before applying changes", "Needs administrator rights. You can still untick it in the review window.", s.RestorePointFirst, v => { s.RestorePointFirst = v; changed(); }));
            stack.Children.Add(Option("Ask for administrator rights when Tweak Bench opens", "Most tweaks change the whole PC, so they need it. Takes effect the next time Tweak Bench starts. If you say no, your own settings still work.", s.AskForAdmin, v => { s.AskForAdmin = v; changed(); }));

            // Tweaks
            var tweaksCaption = Caption("Tweaks");
            tweaksCaption.Margin = new Thickness(0, 6, 0, 8);
            stack.Children.Add(tweaksCaption);
            stack.Children.Add(Option("Show advanced tweaks", "Advanced tweaks can break a feature or an app. They are hidden unless you turn this on.", s.ShowAdvanced, v => { s.ShowAdvanced = v; changed(); }));

            // Data
            var dataCaption = Caption("Your data");
            dataCaption.Margin = new Thickness(0, 6, 0, 8);
            stack.Children.Add(dataCaption);
            var path = Ui.Para("History, profiles and settings are kept in:\n" + Paths.Data);
            stack.Children.Add(path);
            var open = Ui.Button("Open this folder", () =>
            {
                if (ctx.IsDemo) { Ui.Info(Window, "This is the demo, so there is no real data folder."); return; }
                try { System.IO.Directory.CreateDirectory(Paths.Data); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Paths.Data + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { Ui.Info(Window, ex.Message); }
            }, "Link");
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(-2, 4, 0, 0);
            stack.Children.Add(open);

            var close = Ui.Button("Close", () => Window.Close(), "Primary");
            close.MinWidth = 96;
            close.IsCancel = true;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 20, 0, 0);
            stack.Children.Add(close);

            Window.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        static TextBlock Caption(string text)
        {
            var t = Ui.Text(text.ToUpperInvariant(), "Muted", true, 11);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        static FrameworkElement Option(string label, string help, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = label, IsChecked = value };
            box.Click += (s, e) => set(box.IsChecked == true);
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            p.Children.Add(box);
            var h = Ui.Para(help, "Muted", 12);
            h.Margin = new Thickness(27, 3, 0, 0);
            p.Children.Add(h);
            return p;
        }

        public void ShowDialog() { Window.ShowDialog(); }
    }
}
