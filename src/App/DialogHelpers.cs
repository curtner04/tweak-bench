using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TweakBench.App
{
    /// <summary>Small helpers shared by the dialogs: a themed window and a one-line text prompt.</summary>
    public static class DialogHelpers
    {
        /// <summary>A themed dialog window owned by the main window (the owner is skipped in screenshot mode).</summary>
        public static Window Create(UiContext ctx, string title, double width, double height, bool resizable = false)
        {
            // Never taller or wider than the screen's work area (small laptops, large text scaling).
            Rect work = SystemParameters.WorkArea;
            var w = new Window
            {
                Title = title, Width = Math.Min(width, Math.Max(320, work.Width - 40)), Height = Math.Min(height, Math.Max(240, work.Height - 40)), ShowInTaskbar = false,
                ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (ctx.Window != null && ctx.Window.IsLoaded) w.Owner = ctx.Window;
            Theme.Attach(w, ctx.Window);
            return w;
        }

        /// <summary>Asks for one line of text. Returns null when cancelled.</summary>
        public static string Prompt(UiContext ctx, string title, string message, string initial, string okText)
        {
            Window w = Create(ctx, title, 480, 230);
            var box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 12, 0, 0) };
            string result = null;
            Button ok = null;
            Action accept = () => { if (box.Text.Trim().Length > 0) { result = box.Text.Trim(); w.DialogResult = true; } };
            ok = Ui.Button(okText, accept, "Primary");
            ok.MinWidth = 90;
            Button cancel = Ui.Button("Cancel", () => w.DialogResult = false, "Ghost");
            cancel.MinWidth = 90;
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            box.TextChanged += (s, e) => ok.IsEnabled = box.Text.Trim().Length > 0;
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) accept(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
            panel.Children.Add(Ui.Heading(title));
            var text = Ui.Para(message);
            text.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(text);
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            w.Content = panel;
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            ok.IsEnabled = box.Text.Trim().Length > 0;
            return w.ShowDialog() == true ? result : null;
        }
    }
}
