using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TweakBench.App
{
    /// <summary>Shows the embedded CHANGELOG.md (dated version history).</summary>
    public sealed class ChangelogWindow
    {
        public readonly Window Window;

        public ChangelogWindow(UiContext ctx)
        {
            Window = DialogHelpers.Create(ctx, "Tweak Bench " + Release.Version + " — changelog", 700, 560, true);
            string text;
            try { text = AppShared.ReleaseNotes.Read(); }
            catch (Exception ex) { text = "The changelog could not be read: " + ex.Message; }
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = Ui.MonoFont, FontSize = 12.5,
                BorderThickness = new Thickness(0), Padding = new Thickness(22, 18, 22, 18), AcceptsReturn = true,
                VerticalContentAlignment = VerticalAlignment.Top, Background = Brushes.Transparent
            };
            box.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            Window.Content = box;
            Window.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Window.Close(); } };
        }

        public void ShowDialog() { Window.ShowDialog(); }
    }
}
