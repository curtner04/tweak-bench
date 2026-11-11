using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TweakBench.App
{
    /// <summary>
    /// Small factory helpers for code-built pages. Colours are theme resource keys (see Theme.cs / MainWindow.xaml):
    /// Bg, Card, Card2, Input, Border, Text, Muted, Accent, AccentFg, AccentText, BtnBg, Good, GoodBg, Warn, WarnBg, Bad, BadBg,
    /// Info, InfoBg, Selected. Style keys: Primary, Danger, Ghost, Small, Link (buttons), Switch (check box), H1, H2, Cap,
    /// MutedText (text), CardBorder, NoteBorder (borders), Search (text box).
    /// </summary>
    public static class Ui
    {
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Courier New");

        public static void Ref(FrameworkElement element, DependencyProperty property, string key) { element.SetResourceReference(property, key); }

        public static T Style<T>(T element, string styleKey) where T : FrameworkElement
        {
            if (styleKey != null) element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
            return element;
        }

        public static TextBlock Text(string text, string foregroundKey = null, bool bold = false, double size = 0, bool wrap = false)
        {
            var t = new TextBlock { Text = text ?? "", TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            if (!wrap) t.TextTrimming = TextTrimming.CharacterEllipsis;
            if (foregroundKey != null) Ref(t, TextBlock.ForegroundProperty, foregroundKey);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            if (size > 0) t.FontSize = size;
            return t;
        }

        /// <summary>Wrapped paragraph text (muted by default).</summary>
        public static TextBlock Para(string text, string foregroundKey = "Muted", double size = 0)
        {
            var t = Text(text, foregroundKey, false, size, true);
            t.VerticalAlignment = VerticalAlignment.Top;
            return t;
        }

        public static TextBlock Heading(string text, string styleKey = "H2") { return Style(new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap }, styleKey); }

        public static TextBlock Glyph(string code, double size = 16, string foregroundKey = null)
        {
            var t = new TextBlock { Text = code ?? "", FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            if (foregroundKey != null) Ref(t, TextBlock.ForegroundProperty, foregroundKey);
            return t;
        }

        public static Border Pill(string text, string foregroundKey, string backgroundKey, double size = 11)
        {
            var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            Ref(b, Border.BackgroundProperty, backgroundKey);
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold };
            Ref(t, TextBlock.ForegroundProperty, foregroundKey);
            b.Child = t;
            return b;
        }

        public static Button Button(object content, Action click, string styleKey = null, string tooltip = null)
        {
            var b = new Button { Content = content };
            if (styleKey != null) b.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
            if (tooltip != null) b.ToolTip = tooltip;
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        /// <summary>A button with an icon and a label.</summary>
        public static Button IconButton(string glyph, string label, Action click, string styleKey = null, string tooltip = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Glyph(glyph, 14));
            var t = new TextBlock { Text = label, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(t);
            return Button(row, click, styleKey, tooltip);
        }

        public static Border Card(UIElement child, Thickness? padding = null)
        {
            var b = Style(new Border { Child = child }, "CardBorder");
            if (padding.HasValue) b.Padding = padding.Value;
            return b;
        }

        /// <summary>A tinted note: kind is "Warn", "Info", "Bad" or "Good".</summary>
        public static Border Note(string text, string kind = "Info", string glyph = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Glyph(glyph ?? (kind == "Warn" ? "" : kind == "Bad" ? "" : kind == "Good" ? "" : ""), 14, kind);
            icon.Margin = new Thickness(0, 1, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(icon);
            var t = Para(text, "Text");
            Grid.SetColumn(t, 1);
            grid.Children.Add(t);
            var b = new Border { Child = grid, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 6, 0, 6) };
            Ref(b, Border.BackgroundProperty, kind + "Bg");
            return b;
        }

        public static StackPanel Row(double gap, params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < children.Length; i++)
            {
                var fe = children[i] as FrameworkElement;
                if (fe != null && i > 0) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                p.Children.Add(children[i]);
            }
            return p;
        }

        public static StackPanel Stack(double gap, params UIElement[] children)
        {
            var p = new StackPanel();
            for (int i = 0; i < children.Length; i++)
            {
                var fe = children[i] as FrameworkElement;
                if (fe != null && i > 0) fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
                p.Children.Add(children[i]);
            }
            return p;
        }

        public static FrameworkElement Spacer(double height) { return new Border { Height = height }; }

        /// <summary>A section title with an optional muted line under it.</summary>
        public static StackPanel Section(string title, string subtitle = null)
        {
            var p = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
            p.Children.Add(Heading(title, "H2"));
            if (!string.IsNullOrEmpty(subtitle)) { var s = Para(subtitle); s.Margin = new Thickness(0, 3, 0, 0); p.Children.Add(s); }
            return p;
        }

        public static bool Confirm(Window owner, string message, string title = "Tweak Bench")
        {
            return MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public static void Info(Window owner, string message, string title = "Tweak Bench")
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void CopyText(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { Clipboard.SetText(text ?? ""); return; }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(40); }
            }
        }
    }

    /// <summary>
    /// A read-only, monospace, auto-scrolling output log for long tools (SFC, DISM, winget). <see cref="AppendLine"/> is safe
    /// from any thread. Repeated progress lines ("Verification 12% complete.") replace the previous line instead of piling up.
    /// </summary>
    public sealed class OutputConsole
    {
        public readonly Border Root;
        readonly TextBox box;
        readonly Dispatcher dispatcher;
        readonly List<string> lines = new List<string>();
        const int MaxLines = 4000;

        public OutputConsole(double height = 260)
        {
            box = new TextBox
            {
                IsReadOnly = true, FontFamily = Ui.MonoFont, FontSize = 12, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(10, 8, 10, 8),
                // The window's TextBox style centres content vertically; a log must start at the top.
                VerticalContentAlignment = VerticalAlignment.Top, MinHeight = 0
            };
            box.SetResourceReference(Control.ForegroundProperty, "Text");
            Root = new Border { Child = box, Height = height, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            Ui.Ref(Root, Border.BackgroundProperty, "Input");
            Ui.Ref(Root, Border.BorderBrushProperty, "Border");
            dispatcher = Dispatcher.CurrentDispatcher;
        }

        static bool IsProgress(string line)
        {
            string t = line.Trim();
            return t.EndsWith("%", StringComparison.Ordinal) || t.EndsWith("% complete.", StringComparison.OrdinalIgnoreCase) || (t.StartsWith("[", StringComparison.Ordinal) && t.Contains("%"));
        }

        public void AppendLine(string line)
        {
            if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(() => AppendLine(line))); return; }
            line = line ?? "";
            if (line.Trim().Length == 0 && lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) return;
            if (lines.Count > 0 && IsProgress(line) && IsProgress(lines[lines.Count - 1])) lines[lines.Count - 1] = line;
            else lines.Add(line);
            if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
            box.Text = string.Join(Environment.NewLine, lines);
            box.ScrollToEnd();
        }

        public void Clear()
        {
            if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(Clear)); return; }
            lines.Clear();
            box.Text = "";
        }

        public string Text { get { return string.Join(Environment.NewLine, lines); } }
    }
}
