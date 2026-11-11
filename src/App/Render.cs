using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>
    /// Screenshot mode (--render): shows the window with demo data (MemorySystem, a temporary data folder, never the real PC)
    /// and writes a PNG of every page, the review dialog before and after applying, and the settings dialog.
    /// </summary>
    public static class Renderer
    {
        const int Width = 1280, Height = 840;

        public static int Run(string folder, string theme, Action<string> log)
        {
            Directory.CreateDirectory(folder);
            // Demo data lives in a private temporary folder; the real settings and history are never read or written.
            string root = Path.Combine(Path.GetTempPath(), "TweakBench-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("TWEAKBENCH_DATA", Path.Combine(root, "data"));

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var system = MemorySystem.Demo();
            var settings = new AppSettings { Theme = theme, AskForAdmin = false, RestorePointFirst = true, LastPage = "home" };
            var window = new AppWindow(system, settings, true) { SkipAutoRefresh = true };
            Window win = window.Window;
            Prepare(win, Width, Height);
            win.Show();
            Pump();

            int failures = 0;
            Action<string, Action> step = (name, action) =>
            {
                try { action(); }
                catch (Exception ex) { failures++; log("FAILED " + name + ": " + ex); }
            };
            Action<string> shot = name =>
            {
                string file = Path.Combine(folder, name + ".png");
                Capture(win, file);
                log(file);
            };

            // History to look at: two batches and one undo, made through the real engine on the demo system.
            step("seed history", () => SeedHistory(window));
            window.Model.Refresh(true);
            Pump();

            step("home", () => { window.Navigate("home"); Settle(win); shot("01-home"); });

            // Staged choices make the pending bar, the highlighted cards and the badges visible on the pages.
            step("stage", () => StageSome(window));
            Pump();

            int n = 2;
            foreach (Category c in Catalog.Categories)
            {
                string id = c.Id;
                step("page " + id, () =>
                {
                    window.Navigate(id);
                    Settle(win);
                    shot((n++).ToString("00") + "-" + id);
                    double extra = ContentOverflow(window);
                    if (extra > 8)
                    {
                        win.Height = Math.Min(8000, Height + extra + 10);
                        Settle(win);
                        shot((n - 1).ToString("00") + "-" + id + "-full");
                        win.Height = Height;
                        Settle(win);
                    }
                });
            }

            // The smallest window size the app allows: nothing may be clipped or overlap.
            step("small", () =>
            {
                win.Width = 980; win.Height = 640;
                window.Navigate("privacy");
                Settle(win);
                shot("10-privacy-small");
                window.Navigate("home");
                Settle(win);
                shot("11-home-small");
                win.Width = Width; win.Height = Height;
                Settle(win);
            });

            step("search", () => { window.SearchFor("restart"); Settle(win); shot("20-search-restart"); window.SearchFor(""); });
            step("history", () => { window.Navigate("history"); Settle(win); shot("21-history"); });
            step("profiles", () => ShowProfiles(window, win, shot));
            foreach (string id in new[] { "apps", "install", "features", "repair" })
            {
                string pageId = id;
                step("tool " + pageId, () => { window.Navigate(pageId); Settle(win); shot("30-" + pageId); });
            }

            step("review", () => RenderReview(window, win, folder, log));
            step("settings", () => RenderDialog(new SettingsDialog(window.Ctx, () => { }).Window, 560, 640, Path.Combine(folder, "42-settings.png"), log));
            step("changelog", () => RenderDialog(new ChangelogWindow(window.Ctx).Window, 700, 560, Path.Combine(folder, "43-changelog.png"), log));

            // The home page once more, after everything was applied.
            step("home after", () => { window.Navigate("home"); Settle(win); shot("44-home-after"); });

            window.ForceClose();
            app.Shutdown();
            try { Directory.Delete(root, true); } catch (Exception) { }
            log(failures == 0 ? "Rendered all screenshots to " + folder : "Rendered with " + failures + " problem(s). See above.");
            return failures == 0 ? 0 : 2;
        }

        // ------------------------------------------------------------------ demo data

        static void SeedHistory(AppWindow window)
        {
            Engine engine = window.Ctx.Engine;
            var off = Catalog.All.Where(t => t.Recommended && t.ExclusiveGroup == null && engine.Status(t).State == TweakState.Off).ToList();
            if (off.Count < 6) off.AddRange(Catalog.All.Where(t => t.ExclusiveGroup == null && t.Risk != Risk.Advanced && !off.Contains(t) && engine.Status(t).State == TweakState.Off).Take(6 - off.Count));
            if (off.Count == 0) return;
            var first = off.Take(4).Select(t => new PlanItem(t, true)).ToList();
            BatchResult b1 = engine.Apply(first, "Applied " + first.Count + " changes");
            if (off.Count > 4)
            {
                var second = new List<PlanItem> { new PlanItem(off[4], true) };
                engine.Apply(second, off[4].Title);
            }
            // One undo, so the list shows the "Undone" state too.
            HistoryEntry entry = b1.Batch.Entries.FirstOrDefault(e => e.CanUndo);
            if (entry != null) engine.Undo(b1.Batch, entry);
            AutoProfile.Update(engine, window.Ctx.Profiles);
        }

        static void StageSome(AppWindow window)
        {
            TweakModel model = window.Model;
            var candidates = Catalog.All.Where(t => t.Risk != Risk.Advanced && model.CanStage(t) && model.StatusOf(t).State == TweakState.Off).ToList();
            // A spread over the categories, preferring recommended tweaks, plus one "turn off".
            var picked = new List<Tweak>();
            foreach (Category c in Catalog.Categories)
            {
                Tweak t = candidates.Where(x => x.Category == c.Id).OrderByDescending(x => x.Recommended).FirstOrDefault();
                if (t != null && picked.Count < 6) picked.Add(t);
            }
            foreach (Tweak t in picked) model.Stage(t, true);
            // Prefer one that Tweak Bench turned on itself (History holds its old values), so the review shows that wording.
            Func<Tweak, bool> canTurnOff = t => model.CanStage(t) && model.StatusOf(t).State == TweakState.On && !model.IsStaged(t);
            Tweak on = Catalog.All.FirstOrDefault(t => canTurnOff(t) && model.StatusOf(t).HasUndo) ?? Catalog.All.FirstOrDefault(canTurnOff);
            if (on != null) model.Stage(on, false);
        }

        static void ShowProfiles(AppWindow window, Window win, Action<string> shot)
        {
            // Save one profile so the list is not empty, then switch the History page to its Profiles tab.
            var profile = window.Ctx.Engine.CaptureProfile("Work PC", Catalog.All, false);
            window.Ctx.Profiles.Save(profile);
            window.Navigate("history");
            RadioButtonByContent(win, "Profiles");
            Settle(win);
            shot("22-profiles");
            RadioButtonByContent(win, "Changes");
        }

        static void RadioButtonByContent(DependencyObject root, string text)
        {
            var found = Find<System.Windows.Controls.RadioButton>(root).FirstOrDefault(r => r.Content as string == text);
            if (found != null) found.IsChecked = true;
        }

        static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T) yield return (T)child;
                foreach (T d in Find<T>(child)) yield return d;
            }
        }

        // ------------------------------------------------------------------ dialogs

        static void RenderReview(AppWindow window, Window main, string folder, Action<string> log)
        {
            List<PlanItem> plan = window.Model.Plan();
            var dialog = new ReviewDialog(window.Ctx, window.Model, plan);
            Prepare(dialog.Window, 780, 700);
            dialog.Window.Show();
            Settle(dialog.Window);
            string before = Path.Combine(folder, "40-review.png");
            Capture(dialog.Window, before);
            log(before);
            dialog.StartApply(true);
            Settle(dialog.Window);
            string after = Path.Combine(folder, "41-review-results.png");
            Capture(dialog.Window, after);
            log(after);
            dialog.Window.Close();
            Pump();
            window.Model.Refresh(true);
            Settle(main);
        }

        static void RenderDialog(Window dialog, int width, int height, string file, Action<string> log)
        {
            Prepare(dialog, width, height);
            dialog.Show();
            Settle(dialog);
            Capture(dialog, file);
            log(file);
            dialog.Close();
            Pump();
        }

        // ------------------------------------------------------------------ capturing

        static void Prepare(Window w, int width, int height)
        {
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = 0; w.Top = 0;
            w.MinWidth = 0; w.MinHeight = 0;
            w.Width = width; w.Height = height;
        }

        static void Pump()
        {
            Dispatcher d = Dispatcher.CurrentDispatcher;
            for (int i = 0; i < 3; i++)
            {
                d.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                d.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            }
        }

        static void Settle(Window w)
        {
            Pump();
            w.UpdateLayout();
            Pump();
            w.UpdateLayout();
        }

        /// <summary>How much taller the current page is than the visible scrolling area (so a "full page" shot can be taken).</summary>
        static double ContentOverflow(AppWindow window)
        {
            var scroll = Find<System.Windows.Controls.ScrollViewer>(window.Window).FirstOrDefault(s => s.Content is FrameworkElement && s.VerticalScrollBarVisibility == System.Windows.Controls.ScrollBarVisibility.Visible && s.ViewportHeight > 200);
            return scroll == null ? 0 : scroll.ExtentHeight - scroll.ViewportHeight;
        }

        static void Capture(Window w, string file)
        {
            // The window's own root visual covers the whole client area (including the margins of the content).
            var content = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
            if (width <= 0 || height <= 0) throw new InvalidOperationException("The window has no size yet.");
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, width, height);
                dc.DrawRectangle(w.Background, null, rect);
                dc.DrawRectangle(new VisualBrush(content)
                {
                    Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rect, ViewportUnits = BrushMappingMode.Absolute, Viewport = rect
                }, null, rect);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
        }
    }
}
