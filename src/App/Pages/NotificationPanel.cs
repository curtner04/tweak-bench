using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using TweakBench.Core;
using TweakBench.Notify;

namespace TweakBench.App
{
    /// <summary>
    /// "Notification pop-ups": moves the Windows notification pop-up to a spot the user chooses, on any screen. The moving is done by the small
    /// helper "Tweak Bench Notifications.exe" (see src\Notify); this panel switches it on and off, picks the spot, and sends a test pop-up.
    /// Shown at the top of the System &amp; desktop page. Nothing here blocks the window: registry, process and screen work runs in the background.
    /// </summary>
    public static class NotificationPanel
    {
        /// <summary>The words people can type in the search box to find this panel.</summary>
        public static readonly string[] SearchWords = { "notification", "notifications", "pop-up", "popup", "pop-ups", "popups", "toast", "toasts", "banner", "banners", "alert", "alerts", "corner", "position", "move" };

        public static FrameworkElement Build(UiContext ctx) { return new View(ctx, false).Root; }

        /// <summary>Screenshot mode only: the panel in its "on" state with made-up values (no registry, process or screen is touched).</summary>
        public static FrameworkElement BuildDemo(UiContext ctx, bool on) { return new View(ctx, on).Root; }

        /// <summary>Full path of the running Tweak Bench exe.</summary>
        public static string AppExePath
        {
            get
            {
                try { return Assembly.GetExecutingAssembly().Location; }
                catch (Exception) { return ""; }
            }
        }

        /// <summary>The helper next to the running exe.</summary>
        public static string HelperPath { get { return NotificationHelper.ExePathNextTo(AppExePath); } }

        // ------------------------------------------------------------------ the helper (background thread only)

        /// <summary>
        /// Brings the helper in line with the wish: on = start-up entry present and the helper running this version from this folder
        /// (started, reloaded or replaced as needed); off = start-up entry removed and the helper stopped. Blocks for a few seconds at most.
        /// </summary>
        public static bool Sync(bool want, out string error)
        {
            error = "";
            try
            {
                string helper = HelperPath;
                if (!want)
                {
                    NotificationHelper.UnregisterStartup();
                    if (!NotificationHelper.Quit(4000)) { error = "The helper is still closing. It will be gone in a moment."; return false; }
                    return true;
                }
                if (!File.Exists(helper))
                {
                    error = "The helper \"" + NotificationHelper.ExeName + "\" was not found next to Tweak Bench, so pop-ups cannot be moved. Reinstall Tweak Bench to get it back.";
                    return false;
                }
                string wanted = "\"" + helper + "\"";
                string have = NotificationHelper.StartupCommand();
                if (!string.Equals(have, wanted, StringComparison.OrdinalIgnoreCase)) NotificationHelper.RegisterStartup(helper);

                if (NotificationHelper.IsRunning())
                {
                    NotificationStatus st = NotificationStatusFile.Read();
                    bool old = st != null && (!string.Equals(st.Version ?? "", Release.Version, StringComparison.Ordinal)
                        || (!string.IsNullOrEmpty(st.ExePath) && !SamePath(st.ExePath, helper)));
                    if (!old) { NotificationHelper.Reload(); return true; }
                    if (!NotificationHelper.Quit(5000)) { error = "The older notification helper did not close, so the new one could not start. Sign out and in again, or end \"" + NotificationHelper.ExeName + "\" in Task Manager."; return false; }
                }
                return NotificationHelper.Start(helper, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Called once when Tweak Bench opens: when the option is on, makes sure the start-up entry and the running helper belong to THIS
        /// version and folder. This also brings the feature back after Export / import on a new PC. Quiet unless something fails.
        /// </summary>
        public static void EnsureOnStart(UiContext ctx)
        {
            if (ctx.IsDemo || !ctx.Settings.MoveNotifications || ctx.Settings.NotificationSpot == null) return;
            Task.Run(() =>
            {
                string error;
                if (!Sync(true, out error) && error.Length > 0) ctx.SetStatus("Notification pop-ups: " + error, true);
            });
        }

        // ------------------------------------------------------------------ the panel

        sealed class Snapshot
        {
            public bool Running;
            public NotificationStatus Status;
            public string SpotText = "";
            public bool HelperPresent = true;
        }

        sealed class View
        {
            readonly UiContext ctx;
            readonly bool demoOn;
            public readonly FrameworkElement Root;
            readonly Border pillHost = new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
            readonly TextBlock statusText;
            readonly TextBlock spotText;
            readonly CheckBox toggle;
            readonly Button startButton, chooseButton, testButton;
            readonly List<Button> presetButtons = new List<Button>();
            readonly StackPanel resultHost = new StackPanel();
            readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            bool busy, refreshing;

            static readonly string[][] Presets =
            {
                new[] { "top-left", "Top left" }, new[] { "top-center", "Top centre" }, new[] { "top-right", "Top right" },
                new[] { "bottom-left", "Bottom left" }, new[] { "bottom-center", "Bottom centre" }, new[] { "bottom-right", "Bottom right" },
            };

            public View(UiContext context, bool demoOn)
            {
                ctx = context;
                this.demoOn = demoOn;
                var stack = new StackPanel();
                stack.Children.Add(Ui.Heading("Notification pop-ups", "H2"));
                var intro = Ui.Para("Windows puts notification pop-ups in the bottom-right corner of your main screen. Tweak Bench can move them to any spot on any screen, "
                    + "so they stay out of your way. A small helper, Tweak Bench Notifications, does the moving. It runs in the background while this is on and starts when you sign in. "
                    + "Turning it off puts everything back the way Windows does it.", "Text");
                intro.Margin = new Thickness(0, 4, 0, 12);
                stack.Children.Add(intro);

                // Status line
                statusText = Ui.Para("", "Text");
                startButton = Ui.Button("Start it", () => ApplyHelper("Starting the notification helper", true), "Small", "Starts the helper again");
                startButton.Margin = new Thickness(12, 0, 0, 0);
                startButton.VerticalAlignment = VerticalAlignment.Top;
                startButton.Visibility = Visibility.Collapsed;
                var statusRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                statusRow.Children.Add(pillHost);
                Grid.SetColumn(statusText, 1);
                statusRow.Children.Add(statusText);
                Grid.SetColumn(startButton, 2);
                statusRow.Children.Add(startButton);
                stack.Children.Add(statusRow);

                toggle = Ui.Style(new CheckBox { Content = "Move notification pop-ups", HorizontalAlignment = HorizontalAlignment.Left }, "Switch");
                toggle.ToolTip = "On: pop-ups appear at the spot you choose below. Off: Windows places them as usual.";
                toggle.Click += (s, e) => OnToggle();
                stack.Children.Add(toggle);

                // Where
                var where = Ui.Text("Where should they appear?", "Muted", true, 12);
                where.Margin = new Thickness(0, 16, 0, 0);
                stack.Children.Add(where);
                spotText = Ui.Para("", "Text", 13);
                spotText.Margin = new Thickness(0, 4, 0, 8);
                stack.Children.Add(spotText);

                var presets = new WrapPanel();
                foreach (var preset in Presets)
                {
                    string name = preset[0];
                    var b = Ui.Button(preset[1], () => ChoosePreset(name), "Small", "Puts pop-ups at the " + preset[1].ToLowerInvariant() + " of your main screen, 12 px from the edges");
                    b.Margin = new Thickness(0, 0, 8, 6);
                    presetButtons.Add(b);
                    presets.Children.Add(b);
                }
                stack.Children.Add(presets);

                chooseButton = Ui.Button("Choose a spot on any screen…", ChooseSpot, "Ghost", "Opens a card the size of a pop-up. Drag it where you want pop-ups to appear.");
                testButton = Ui.Button("Send a test notification", SendTest, "Ghost", "Shows a real Windows notification so you can see where it lands");
                var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var b in new[] { chooseButton, testButton }) { b.Margin = new Thickness(0, 0, 10, 6); actions.Children.Add(b); }
                stack.Children.Add(actions);
                stack.Children.Add(resultHost);

                var good = Ui.Text("Good to know", "Muted", true, 12);
                good.Margin = new Thickness(0, 10, 0, 4);
                stack.Children.Add(good);
                foreach (string line in new[]
                {
                    "A pop-up may flash for a moment in the old corner before it moves.",
                    "Pop-ups that an app draws itself (some games and chat apps) are not moved.",
                    "The pop-up has a see-through edge that still catches clicks while it shows. On a screen with other scaling than your main screen, that edge above it is taller.",
                    "When Do Not Disturb is on, Windows shows no pop-ups at all.",
                })
                {
                    var p = Ui.Para("•  " + line, "Muted", 12);
                    p.Margin = new Thickness(0, 0, 0, 2);
                    stack.Children.Add(p);
                }

                var card = Ui.Card(stack);
                card.Margin = new Thickness(0, 0, 0, 16);
                Root = card;

                if (ctx.IsDemo) ShowDemo();
                else
                {
                    ShowSnapshot(new Snapshot { SpotText = "…" });
                    timer.Tick += (s, e) => { if (Root.IsVisible && !busy) Refresh(); };
                    card.Loaded += (s, e) => { Refresh(); timer.Start(); };
                    card.Unloaded += (s, e) => timer.Stop();
                }
            }

            // ---- showing the state

            void ShowDemo()
            {
                toggle.IsChecked = demoOn;
                if (demoOn)
                {
                    SetPill("On", "Good", "GoodBg");
                    statusText.Text = "Working. Pop-ups go to your chosen spot. Moved 7 pop-ups since the helper started.";
                    spotText.Text = "Now: Top centre of screen 3 (main screen, DELL U2720Q)";
                }
                else
                {
                    SetPill("Off", "Muted", "Card2");
                    statusText.Text = "Off. Windows places pop-ups in its usual spot: the bottom-right corner of your main screen.";
                    spotText.Text = "No spot has been chosen yet. Pick one below and Tweak Bench turns this on.";
                }
                startButton.Visibility = Visibility.Collapsed;
                UpdateEnabled();
            }

            void SetPill(string text, string fgKey, string bgKey) { pillHost.Child = Ui.Pill(text, fgKey, bgKey, 11.5); }

            void UpdateEnabled()
            {
                bool free = !busy;
                toggle.IsEnabled = free;
                startButton.IsEnabled = free;
                chooseButton.IsEnabled = free;
                testButton.IsEnabled = free;
                foreach (var b in presetButtons) b.IsEnabled = free;
            }

            void ShowSnapshot(Snapshot s)
            {
                AppSettings settings = ctx.Settings;
                bool want = settings.MoveNotifications;
                toggle.IsChecked = want;
                bool haveSpot = settings.NotificationSpot != null;
                spotText.Text = haveSpot ? "Now: " + s.SpotText : "No spot has been chosen yet. Pick one below and Tweak Bench turns this on.";
                startButton.Visibility = Visibility.Collapsed;

                if (!want)
                {
                    SetPill("Off", "Muted", "Card2");
                    statusText.Text = "Off. Windows places pop-ups in its usual spot: the bottom-right corner of your main screen.";
                }
                else if (!s.Running)
                {
                    SetPill("Not running", "Warn", "WarnBg");
                    statusText.Text = s.HelperPresent
                        ? "This is on, but the helper is not running right now, so pop-ups appear where Windows puts them."
                        : "This is on, but the helper \"" + NotificationHelper.ExeName + "\" was not found next to Tweak Bench.";
                    startButton.Visibility = s.HelperPresent ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (s.Status != null && s.Status.State == NotificationStatus.StateError)
                {
                    SetPill("Problem", "Bad", "BadBg");
                    string why = string.IsNullOrWhiteSpace(s.Status.LastError) ? "The helper reported a problem." : s.Status.LastError.Trim();
                    statusText.Text = "The helper is running but reported a problem: " + why;
                }
                else if (s.Status == null || s.Status.State == NotificationStatus.StateStopped || s.Status.State == NotificationStatus.StateOff)
                {
                    SetPill("Starting", "Info", "InfoBg");
                    statusText.Text = "The helper is starting…";
                }
                else
                {
                    SetPill("On", "Good", "GoodBg");
                    int moved = s.Status.Moved;
                    statusText.Text = "Working. Pop-ups go to your chosen spot. "
                        + (moved == 0 ? "No pop-up has needed moving yet since the helper started." : "Moved " + moved + (moved == 1 ? " pop-up" : " pop-ups") + " since the helper started.");
                }
                UpdateEnabled();
            }

            /// <summary>Reads helper, status file and screens off the UI thread, then shows them.</summary>
            void Refresh()
            {
                if (ctx.IsDemo || refreshing) return;
                refreshing = true;
                NotificationSpot spot = ctx.Settings.NotificationSpot == null ? null : ctx.Settings.NotificationSpot.Clone();
                string helper = HelperPath;
                Task.Run(() =>
                {
                    var snap = new Snapshot();
                    try
                    {
                        snap.Running = NotificationHelper.IsRunning();
                        snap.Status = NotificationStatusFile.Read();
                        snap.HelperPresent = File.Exists(helper);
                        if (spot != null) snap.SpotText = NotificationPlacement.Describe(spot, Monitors.All());
                    }
                    catch (Exception ex) { snap.SpotText = "(could not read the screens: " + ex.Message + ")"; }
                    ctx.OnUi(() => { refreshing = false; if (!busy) ShowSnapshot(snap); });
                });
            }

            // ---- doing things

            void OnToggle()
            {
                bool want = toggle.IsChecked == true;
                if (want && ctx.Settings.NotificationSpot == null)
                {
                    toggle.IsChecked = false;
                    ChooseSpot();
                    return;
                }
                ctx.Settings.MoveNotifications = want;
                ctx.SaveSettings();
                ApplyHelper(want ? "Turning on notification pop-up moving" : "Turning off notification pop-up moving", want);
            }

            /// <summary>Saves nothing itself: the caller has already stored the settings. Starts, reloads or stops the helper in the background.</summary>
            void ApplyHelper(string what, bool want)
            {
                if (busy) return;
                if (ctx.IsDemo) { ToolsUi.ShowNote(resultHost, "Demo mode: nothing was changed.", "Info"); return; }
                ClearResult();
                busy = true;
                UpdateEnabled();
                statusText.Text = want ? "Setting this up…" : "Turning this off…";
                string error = "";
                bool ok = false;
                ctx.RunInBackground(what, () => { ok = Sync(want, out error); }, () =>
                {
                    busy = false;
                    if (ok)
                    {
                        ToolsUi.ShowNote(resultHost, want
                            ? "On. The next pop-up will appear at your spot. Use \"Send a test notification\" to see it."
                            : "Off. Windows places pop-ups as usual again, and the helper no longer starts when you sign in.", "Good");
                        ctx.SetStatus(want ? "Notification pop-ups will move to your spot." : "Notification pop-ups are back in Windows' usual spot.");
                    }
                    else
                    {
                        // A missing helper means this cannot work at all: switch the option back off so the page does not promise it.
                        if (want && !File.Exists(HelperPath)) { ctx.Settings.MoveNotifications = false; ctx.SaveSettings(); }
                        ToolsUi.ShowNote(resultHost, error.Length > 0 ? error : "Something went wrong.", "Bad");
                        ctx.SetStatus("Notification pop-ups: " + error, true);
                    }
                    Refresh();
                }, ex =>
                {
                    busy = false;
                    ToolsUi.ShowNote(resultHost, ex.Message, "Bad");
                    Refresh();
                });
            }

            void ClearResult() { resultHost.Children.Clear(); }

            /// <summary>Stores the spot, turns the option on and tells the helper.</summary>
            void UseSpot(NotificationSpot spot)
            {
                ctx.Settings.NotificationSpot = spot;
                ctx.Settings.MoveNotifications = true;
                ctx.SaveSettings();
                ApplyHelper("Moving notification pop-ups", true);
            }

            void ChoosePreset(string name)
            {
                if (busy) return;
                if (ctx.IsDemo) { ToolsUi.ShowNote(resultHost, "Demo mode: nothing was changed.", "Info"); return; }
                busy = true;
                UpdateEnabled();
                NotificationSpot spot = null;
                ctx.RunInBackground("Reading the screens", () =>
                {
                    List<MonitorInfo> monitors = Monitors.All();
                    MonitorInfo main = monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
                    if (main == null) throw new InvalidOperationException("No screen was found.");
                    spot = NotificationPlacement.Preset(name, main);
                }, () => { busy = false; if (spot != null) UseSpot(spot); else UpdateEnabled(); },
                ex => { busy = false; UpdateEnabled(); ToolsUi.ShowNote(resultHost, ex.Message, "Bad"); });
            }

            void ChooseSpot()
            {
                if (busy) return;
                if (ctx.IsDemo) { ToolsUi.ShowNote(resultHost, "Demo mode: the spot chooser is not opened.", "Info"); return; }
                busy = true;
                UpdateEnabled();
                List<MonitorInfo> monitors = null;
                double cardW = 0, cardH = 0;
                NotificationSpot current = ctx.Settings.NotificationSpot == null ? null : ctx.Settings.NotificationSpot.Clone();
                ctx.RunInBackground("Reading the screens", () =>
                {
                    monitors = Monitors.All();
                    NotificationStatus st = NotificationStatusFile.Read();
                    if (st != null) { cardW = st.LastCardWidthDip; cardH = st.LastCardHeightDip; }
                }, () =>
                {
                    busy = false;
                    UpdateEnabled();
                    if (monitors == null || monitors.Count == 0) { ToolsUi.ShowNote(resultHost, "No screen was found.", "Bad"); return; }
                    NotificationSpot picked = NotificationPlacementWindow.Pick(ctx, current, monitors, cardW, cardH);
                    if (picked != null) UseSpot(picked);
                    else Refresh();
                }, ex => { busy = false; UpdateEnabled(); ToolsUi.ShowNote(resultHost, ex.Message, "Bad"); });
            }

            // ---- test notification

            const string Aumid = "TweakBench.App";

            void SendTest()
            {
                if (busy) return;
                if (ctx.IsDemo) { ToolsUi.ShowNote(resultHost, "Demo mode: no notification is sent.", "Info"); return; }
                ClearResult();
                busy = true;
                UpdateEnabled();
                statusText.Text = "Sending a test notification…";
                var known = new List<string> { ToastWindow.DefaultTitle };
                known.AddRange(ctx.Settings.NotificationTitles ?? new List<string>());
                string exeFolder = Path.GetDirectoryName(AppExePath) ?? "";
                string result = "", kind = "Info", learned = null;
                ctx.RunInBackground("Sending a test notification", () => { TestWork(known, exeFolder, out result, out kind, out learned); }, () =>
                {
                    busy = false;
                    if (learned != null)
                    {
                        if (ctx.Settings.NotificationTitles == null) ctx.Settings.NotificationTitles = new List<string>();
                        if (!ctx.Settings.NotificationTitles.Any(t => string.Equals(t, learned, StringComparison.OrdinalIgnoreCase))) ctx.Settings.NotificationTitles.Add(learned);
                        ctx.SaveSettings();
                        Task.Run(() => { try { NotificationHelper.Reload(); } catch (Exception) { } });
                    }
                    ToolsUi.ShowNote(resultHost, result, kind);
                    Refresh();
                }, ex => { busy = false; ToolsUi.ShowNote(resultHost, "The test notification could not be sent: " + ex.Message, "Bad"); Refresh(); });
            }

            static void TestWork(List<string> known, string exeFolder, out string message, out string kind, out string learnedTitle)
            {
                learnedTitle = null;
                RegisterSender(exeFolder);
                string script =
                    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null;"
                    + "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null;"
                    + "$x = New-Object Windows.Data.Xml.Dom.XmlDocument;"
                    + "$x.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>Tweak Bench test</text><text>If you can read this, notification pop-ups work. You can ignore it.</text></binding></visual></toast>');"
                    + "$n = [Windows.UI.Notifications.ToastNotification]::new($x);"
                    + "$n.ExpirationTime = [DateTimeOffset]::Now.AddMinutes(2);"
                    + "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('" + Aumid + "').Show($n);";
                ProcessResult run = ProcessRunner.Run(ProcessRunner.PowerShell, ProcessRunner.PowerShellArgs(script), 30000);
                if (!run.Ok)
                {
                    string why = (run.Error ?? "").Trim();
                    if (why.Length == 0) why = (run.Output ?? "").Trim();
                    if (run.TimedOut) why = "Windows took too long to answer.";
                    message = "Windows would not show the test notification" + (why.Length > 0 ? ": " + FirstLine(why) : ".");
                    kind = "Bad";
                    return;
                }

                // Watch up to 6 seconds for the pop-up. A pop-up with a title we do not know is how other display languages are learned.
                MonitorInfo main = Monitors.All().FirstOrDefault(m => m.Primary);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 6000)
                {
                    IntPtr hwnd = ToastWindow.Find(known);
                    if (hwnd != IntPtr.Zero && !ToastWindow.IsCloaked(hwnd) && ToastWindow.GetRect(hwnd).Height > 40)
                    {
                        message = "The test pop-up appeared. If it is not where you expect, check that the switch above is on and the helper is running.";
                        kind = "Good";
                        return;
                    }
                    if (hwnd == IntPtr.Zero && main != null)
                    {
                        IntPtr popup = ToastWindow.FindVisibleShellPopup(main);
                        if (popup != IntPtr.Zero)
                        {
                            string title = (ToastWindow.TitleOf(popup) ?? "").Trim();
                            if (title.Length > 0 && !known.Any(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase)))
                            {
                                learnedTitle = title;
                                message = "The test pop-up appeared in the usual corner. Windows calls its window \"" + title + "\" on this PC, so Tweak Bench remembered that name. Send another test to see it move.";
                                kind = "Good";
                                return;
                            }
                        }
                    }
                    System.Threading.Thread.Sleep(200);
                }
                message = "No pop-up appeared. Do Not Disturb hides pop-ups, so check that it is off (Settings, System, Notifications). "
                    + "Pop-ups may also be hidden if Tweak Bench notifications are blocked there, or this version of Windows draws them differently.";
                kind = "Warn";
            }

            static string FirstLine(string text)
            {
                int i = text.IndexOfAny(new[] { '\r', '\n' });
                string line = i < 0 ? text : text.Substring(0, i);
                return line.Length > 200 ? line.Substring(0, 200) + "…" : line;
            }

            /// <summary>Registers the sender name "Tweak Bench" (with the app icon when present) so Windows shows the test under it.</summary>
            static void RegisterSender(string exeFolder)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Aumid))
                    {
                        if (key == null) return;
                        key.SetValue("DisplayName", "Tweak Bench", RegistryValueKind.String);
                        string icon = Path.Combine(exeFolder ?? "", "assets", "TB.png");
                        if (File.Exists(icon)) key.SetValue("IconUri", icon, RegistryValueKind.String);
                    }
                }
                catch (Exception) { /* the test still works under a generic name */ }
            }
        }
    }
}
