using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TweakBench.Core;
using TweakBench.Notify;

namespace TweakBench.App
{
    /// <summary>
    /// The spot chooser: a borderless card about the size of a notification pop-up that stays on top of everything. Drag it to where pop-ups
    /// should appear (on any screen), nudge it with the arrow keys (1 px, Shift 10 px), then Save. Esc cancels.
    /// </summary>
    public sealed class NotificationPlacementWindow
    {
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

        /// <summary>The preview is never smaller than this, so its text and buttons fit (a real pop-up is about 362 x 140).</summary>
        const double MinWidthDip = 340, MinHeightDip = 176;

        public readonly Window Window;
        readonly IList<MonitorInfo> monitors;
        readonly TextBlock where;
        readonly Button save;
        readonly double widthDip, heightDip;
        NotificationSpot result;

        /// <summary>Opens the chooser and waits. Returns the chosen spot, or null when cancelled.</summary>
        /// <param name="current">Where the card starts (null: the usual bottom-right corner of the main screen).</param>
        /// <param name="cardWidthDip">Size of a real pop-up as last seen (0 = unknown).</param>
        public static NotificationSpot Pick(UiContext ctx, NotificationSpot current, IList<MonitorInfo> monitors, double cardWidthDip, double cardHeightDip)
        {
            var chooser = new NotificationPlacementWindow(ctx, monitors, cardWidthDip, cardHeightDip);
            chooser.Window.SourceInitialized += (s, e) => chooser.Place(current);
            chooser.Window.Loaded += (s, e) =>
            {
                chooser.Window.Dispatcher.BeginInvoke(new Action(() => { chooser.Place(current, true); chooser.UpdateWhere(); chooser.save.Focus(); }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
            chooser.Window.ShowDialog();
            return chooser.result;
        }

        /// <summary>Builds the window without positioning it (screenshot mode shows it as it is).</summary>
        public NotificationPlacementWindow(UiContext ctx, IList<MonitorInfo> monitors, double cardWidthDip, double cardHeightDip)
        {
            this.monitors = monitors ?? new List<MonitorInfo>();
            widthDip = Math.Max(MinWidthDip, Math.Min(600, cardWidthDip > 0 ? cardWidthDip : 0));
            heightDip = Math.Max(MinHeightDip, Math.Min(400, cardHeightDip > 0 ? cardHeightDip : 0));
            Window = new Window
            {
                Title = "Choose where notifications appear",
                Width = widthDip, Height = heightDip,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 100, Top = 100,
            };
            if (ctx.Window != null) Theme.Attach(Window, ctx.Window);
            else Window.Background = Brushes.Transparent;

            var grid = new Grid { Margin = new Thickness(16, 14, 16, 14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = Ui.Text("Drag me to where notifications should appear", "Text", true, 14, true);
            title.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(title);

            where = Ui.Para("", "Muted", 12);
            where.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(where, 1);
            grid.Children.Add(where);

            var hint = Ui.Para("Arrow keys nudge it by 1 px, Shift + arrow by 10 px. Esc cancels.", "Muted", 11.5);
            hint.VerticalAlignment = VerticalAlignment.Bottom;
            hint.Margin = new Thickness(0, 6, 0, 8);
            Grid.SetRow(hint, 2);
            grid.Children.Add(hint);

            save = Ui.Button("Save this spot", Save, "Primary");
            save.IsDefault = true;
            var cancel = Ui.Button("Cancel", () => Window.Close(), "Ghost");
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 3);
            grid.Children.Add(buttons);

            var frame = new Border { Child = grid, BorderThickness = new Thickness(2), Cursor = Cursors.SizeAll };
            Ui.Ref(frame, Border.BackgroundProperty, "Card");
            Ui.Ref(frame, Border.BorderBrushProperty, "Accent");
            Window.Content = frame;

            Window.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState != MouseButtonState.Pressed || FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
                try { Window.DragMove(); } catch (InvalidOperationException) { }
            };
            Window.LocationChanged += (s, e) => UpdateWhere();
            Window.PreviewKeyDown += OnKey;
            UpdateWhere(null);
        }

        static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T) return (T)d;
                d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        // ------------------------------------------------------------------ position

        MonitorInfo Main { get { return monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault(); } }

        /// <summary>Puts the card at <paramref name="current"/> (or the usual corner of the main screen) in physical pixels.</summary>
        void Place(NotificationSpot current, bool onlyIfMoved = false)
        {
            try
            {
                MonitorInfo monitor = current != null ? NotificationPlacement.Resolve(current, monitors) : Main;
                if (monitor == null) return;
                IntPtr hwnd = new WindowInteropHelper(Window).EnsureHandle();
                NotificationSpot spot = current ?? NotificationPlacement.Preset("bottom-right", monitor);
                int w = (int)Math.Round(widthDip * monitor.Dpi / 96.0), h = (int)Math.Round(heightDip * monitor.Dpi / 96.0);
                RectPx target = NotificationPlacement.CardRect(spot, monitor, w, h);
                // Moving between screens with different scaling makes WPF resize the window once; apply it again until it sticks.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    RectPx now = CurrentRect(hwnd);
                    if (now == target) break;
                    if (onlyIfMoved && attempt == 0 && now.Width == target.Width && now.Height == target.Height && Near(now, target)) break;
                    SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, SwpNoZOrder | SwpNoActivate);
                }
            }
            catch (Exception) { /* the card simply stays where Windows opened it */ }
        }

        static bool Near(RectPx a, RectPx b) { return Math.Abs(a.Left - b.Left) <= 1 && Math.Abs(a.Top - b.Top) <= 1; }

        RectPx CurrentRect(IntPtr hwnd)
        {
            NativeRect r;
            return GetWindowRect(hwnd, out r) ? new RectPx(r.L, r.T, r.R, r.B) : default(RectPx);
        }

        void Nudge(int dx, int dy)
        {
            IntPtr hwnd = new WindowInteropHelper(Window).Handle;
            if (hwnd == IntPtr.Zero) return;
            RectPx r = CurrentRect(hwnd);
            SetWindowPos(hwnd, IntPtr.Zero, r.Left + dx, r.Top + dy, r.Width, r.Height, SwpNoZOrder | SwpNoActivate);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            switch (e.Key)
            {
                case Key.Left: Nudge(-step, 0); break;
                case Key.Right: Nudge(step, 0); break;
                case Key.Up: Nudge(0, -step); break;
                case Key.Down: Nudge(0, step); break;
                default: return;
            }
            e.Handled = true;
        }

        // ------------------------------------------------------------------ result

        /// <summary>The screen the card is mostly on (nearest screen when it is on none).</summary>
        public static MonitorInfo MonitorFor(RectPx card, IList<MonitorInfo> monitors)
        {
            MonitorInfo best = null;
            long bestArea = -1;
            foreach (MonitorInfo m in monitors)
            {
                long w = Math.Min(card.Right, m.Bounds.Right) - Math.Max(card.Left, m.Bounds.Left);
                long h = Math.Min(card.Bottom, m.Bounds.Bottom) - Math.Max(card.Top, m.Bounds.Top);
                long area = w > 0 && h > 0 ? w * h : 0;
                if (area > bestArea) { best = m; bestArea = area; }
            }
            if (best != null && bestArea > 0) return best;
            double cx = (card.Left + card.Right) / 2.0, cy = (card.Top + card.Bottom) / 2.0, nearest = double.MaxValue;
            foreach (MonitorInfo m in monitors)
            {
                double dx = Math.Max(Math.Max(m.Bounds.Left - cx, 0), cx - m.Bounds.Right), dy = Math.Max(Math.Max(m.Bounds.Top - cy, 0), cy - m.Bounds.Bottom);
                double d = dx * dx + dy * dy;
                if (d < nearest) { nearest = d; best = m; }
            }
            return best;
        }

        NotificationSpot SpotNow()
        {
            IntPtr hwnd = new WindowInteropHelper(Window).Handle;
            if (hwnd == IntPtr.Zero) return null;
            RectPx card = CurrentRect(hwnd);
            if (card.IsEmpty) return null;
            MonitorInfo monitor = MonitorFor(card, monitors);
            if (monitor == null) return null;
            // Pop-ups always stay inside the screen's work area (not over the taskbar, not off the edge): describe and save the spot
            // where the card will really land, not where a card dragged past the edge happens to be.
            RectPx inside = NotificationPlacement.CardRect(NotificationPlacement.SpotFromCard(card, monitor), monitor, card.Width, card.Height);
            return NotificationPlacement.SpotFromCard(inside, monitor);
        }

        void UpdateWhere() { UpdateWhere(SpotNow()); }

        void UpdateWhere(NotificationSpot spot)
        {
            where.Text = spot == null ? "Pop-ups will appear where this card is." : "Now: " + NotificationPlacement.Describe(spot, monitors);
        }

        /// <summary>Screenshot mode: shows the description for a made-up spot.</summary>
        public void ShowSpot(NotificationSpot spot) { UpdateWhere(spot); }

        void Save()
        {
            result = SpotNow();
            Window.Close();
        }
    }
}
