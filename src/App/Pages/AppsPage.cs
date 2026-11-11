using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using TweakBench.Core;
using TweakBench.Tools;

namespace TweakBench.App
{
    /// <summary>Small building blocks shared by the four tool pages and the DNS panel.</summary>
    internal static class ToolsUi
    {
        /// <summary>The short explanation at the top of a page.</summary>
        public static FrameworkElement Intro(string text)
        {
            var p = Ui.Para(text, "Text", 14);
            p.Margin = new Thickness(0, 0, 0, 12);
            return p;
        }

        /// <summary>A card with a title, an optional blurb and the given rows.</summary>
        public static Border GroupCard(string title, string blurb, IEnumerable<UIElement> rows)
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Heading(title, "H2"));
            if (!string.IsNullOrEmpty(blurb)) { var b = Ui.Para(blurb); b.Margin = new Thickness(0, 3, 0, 4); panel.Children.Add(b); }
            var gap = new Border { Height = 6 };
            panel.Children.Add(gap);
            foreach (var row in rows) panel.Children.Add(row);
            var card = Ui.Card(panel);
            card.Margin = new Thickness(0, 0, 0, 14);
            return card;
        }

        /// <summary>One selectable row: a check box with a title, a description and extra lines, and a status element on the right.</summary>
        public static Border CheckRow(out CheckBox box, string title, string description, string extra, FrameworkElement status, bool switchStyle = false)
        {
            var text = new StackPanel();
            text.Children.Add(Ui.Text(title, "Text", true, 14));
            if (!string.IsNullOrEmpty(description)) { var d = Ui.Para(description, "Text"); d.Margin = new Thickness(0, 2, 0, 0); text.Children.Add(d); }
            if (!string.IsNullOrEmpty(extra)) { var e = Ui.Para(extra, "Muted", 12); e.Margin = new Thickness(0, 2, 0, 0); text.Children.Add(e); }
            box = new CheckBox { Content = text, VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
            if (switchStyle) Ui.Style(box, "Switch");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(box);
            if (status != null)
            {
                status.VerticalAlignment = VerticalAlignment.Top;
                status.Margin = new Thickness(0, 2, 0, 0);
                Grid.SetColumn(status, 1);
                grid.Children.Add(status);
            }
            var border = new Border { Child = grid, Padding = new Thickness(2, 8, 2, 8), BorderThickness = new Thickness(0, 1, 0, 0) };
            Ui.Ref(border, Border.BorderBrushProperty, "Border");
            return border;
        }

        public static FrameworkElement StatusPill(string text, string kind)
        {
            switch (kind)
            {
                case "good": return Ui.Pill(text, "Good", "GoodBg");
                case "warn": return Ui.Pill(text, "Warn", "WarnBg");
                case "bad": return Ui.Pill(text, "Bad", "BadBg");
                case "info": return Ui.Pill(text, "Info", "InfoBg");
                default: return Ui.Pill(text, "Muted", "BtnBg");
            }
        }

        /// <summary>A line of buttons and text for a page's toolbar, inside a card.</summary>
        public static Border Toolbar(params UIElement[] items)
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var item in items)
            {
                var fe = item as FrameworkElement;
                if (fe != null) { fe.Margin = new Thickness(0, 4, 12, 4); fe.VerticalAlignment = VerticalAlignment.Center; }
                wrap.Children.Add(item);
            }
            var card = Ui.Card(wrap);
            card.Margin = new Thickness(0, 0, 0, 14);
            return card;
        }

        /// <summary>Lists names in a confirmation message, at most <paramref name="max"/> lines.</summary>
        public static string BulletList(IEnumerable<string> names, int max = 25)
        {
            var list = names.ToList();
            var sb = new StringBuilder();
            foreach (string n in list.Take(max)) sb.AppendLine("   • " + n);
            if (list.Count > max) sb.AppendLine("   • and " + (list.Count - max) + " more");
            return sb.ToString();
        }

        /// <summary>Shows a result note in a host panel, replacing what was there.</summary>
        public static void ShowNote(Panel host, string text, string kind)
        {
            host.Children.Clear();
            if (!string.IsNullOrEmpty(text)) host.Children.Add(Ui.Note(text, kind));
        }
    }

    /// <summary>
    /// A live output area with a Stop button for long tools. Hidden until the first run. The work runs on a background thread
    /// through <see cref="UiContext.RunInBackground"/>, so Restart waits for it.
    /// </summary>
    internal sealed class LiveRun
    {
        readonly UiContext ctx;
        public readonly OutputConsole Console;
        public readonly Border Panel;
        readonly TextBlock heading;
        readonly TextBlock state;
        readonly Button stop;
        CancellationTokenSource cts;
        string what;

        // Only one long tool runs at a time across every tool page: DISM, SFC, winget and package removal collide when they overlap.
        static LiveRun active;

        /// <summary>
        /// True (and says why in the status line) when a long tool is already running on any tool page. Call it before asking for confirmation.
        /// </summary>
        public static bool Refuse(UiContext ctx)
        {
            var a = active;
            if (a == null) return false;
            ctx.SetStatus("Wait for \"" + a.what + "\" to finish, then try again.", true);
            return true;
        }

        public bool Running { get; private set; }
        /// <summary>Raised on the UI thread when a run starts or ends.</summary>
        public event Action RunningChanged = () => { };

        public LiveRun(UiContext context, double height = 240)
        {
            ctx = context;
            Console = new OutputConsole(height);
            heading = Ui.Text("", "Text", true, 14);
            state = Ui.Text("", "Muted", false, 12);
            stop = Ui.Button("Stop", () => Stop(), "Small", "Stops the tool that is running now");
            stop.IsEnabled = false;
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(heading);
            state.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(state);
            top.Children.Add(left);
            Grid.SetColumn(stop, 1);
            top.Children.Add(stop);
            var stack = new StackPanel();
            stack.Children.Add(top);
            Console.Root.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(Console.Root);
            Panel = Ui.Card(stack);
            Panel.Margin = new Thickness(0, 0, 0, 14);
            Panel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Shows sample text without running anything (demo mode and screenshots).</summary>
        public void ShowSample(string title, string status, IEnumerable<string> lines)
        {
            Panel.Visibility = Visibility.Visible;
            heading.Text = title;
            state.Text = status;
            Console.Clear();
            foreach (string l in lines) Console.AppendLine(l);
        }

        public void Stop()
        {
            var c = cts;
            if (c == null || c.IsCancellationRequested) return;
            stop.IsEnabled = false;
            state.Text = "Stopping...";
            c.Cancel();
        }

        /// <summary>
        /// Runs <paramref name="work"/> in the background. It receives a function that writes a line to the console and a token that is
        /// cancelled by Stop, and returns a one-line result. <paramref name="done"/> runs on the UI thread with that result.
        /// </summary>
        public void Start(string what, Func<Action<string>, CancellationToken, string> work, Action<string> done, bool canStop = true)
        {
            if (Running) return;
            if (Refuse(ctx)) return;
            active = this;
            this.what = what;
            Running = true;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            Panel.Visibility = Visibility.Visible;
            heading.Text = what;
            state.Text = "Running. You can keep using Tweak Bench while this finishes.";
            stop.IsEnabled = canStop;
            Console.Clear();
            RunningChanged();
            string result = null;
            Action<string> log = line => Console.AppendLine(line);
            ctx.RunInBackground(what, () =>
            {
                try { result = work(log, token); }
                catch (Exception ex) { result = "Stopped by an error: " + ex.Message; log(result); }
            }, () => Finish(result, done), ex => Finish("Stopped by an error: " + ex.Message, done));
        }

        void Finish(string result, Action<string> done)
        {
            Running = false;
            if (active == this) active = null;
            stop.IsEnabled = false;
            state.Text = result ?? "";
            if (cts != null) { cts.Dispose(); cts = null; }
            RunningChanged();
            if (done != null) done(result ?? "");
        }
    }

    /// <summary>Remove built-in apps: a curated list with live status; removal always shows a confirmation first.</summary>
    public sealed class AppsPage : PageBase
    {
        sealed class Row
        {
            public BuiltInApp App;
            public CheckBox Box;
            public Border Container;
            public ContentControl StatusHost;
            public bool? Installed; // null = not checked yet
        }

        readonly List<Row> rows = new List<Row>();
        TextBlock countText;
        TextBlock scanText;
        Button removeButton, refreshButton;
        CheckBox provisionedBox;
        StackPanel resultHost;
        LiveRun run;
        bool scanning;
        bool scannedOnce;
        bool loading;

        public AppsPage(UiContext ctx) : base(ctx) { }
        public override string Id { get { return "apps"; } }
        public override string Title { get { return "Remove built-in apps"; } }
        public override string Subtitle { get { return "Get rid of the extra apps that come with Windows and with new PCs."; } }
        public override string Glyph { get { return ""; } }

        protected override FrameworkElement Build()
        {
            var root = new StackPanel();
            root.Children.Add(ToolsUi.Intro("Tick the apps you do not want, then press Remove. Tweak Bench shows exactly what will go before anything happens. "
                + "Removing an app only affects your account unless you choose otherwise, and you can get any of them back from the Microsoft Store."));

            countText = Ui.Text("", "Text", true);
            scanText = Ui.Text("", "Muted", false, 12);
            removeButton = Ui.Button("Remove selected apps", ReviewAndRemove, "Danger");
            refreshButton = Ui.Button("Check again", () => Scan(true), "Ghost", "Looks at which of these apps are installed right now");
            provisionedBox = new CheckBox
            {
                Content = "Also stop them coming back for new user accounts on this PC",
                ToolTip = Ctx.IsElevated ? "Also removes them from the Windows image, so new accounts do not get them." : "Needs administrator rights. Use \"Run as administrator\" in the sidebar.",
                IsEnabled = Ctx.IsElevated
            };
            provisionedBox.Checked += (s, e) => UpdateState();
            provisionedBox.Unchecked += (s, e) => UpdateState();
            root.Children.Add(ToolsUi.Toolbar(removeButton, countText, refreshButton, provisionedBox, scanText));

            resultHost = new StackPanel();
            root.Children.Add(resultHost);
            run = new LiveRun(Ctx, 200);
            run.RunningChanged += UpdateState;
            root.Children.Add(run.Panel);

            var pending = new HashSet<string>(Ctx.Settings.PendingRemove ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            loading = true;
            foreach (string group in BuiltInApps.Groups)
            {
                var groupRows = new List<UIElement>();
                foreach (var app in BuiltInApps.All.Where(a => a.Group == group))
                {
                    var row = new Row { App = app, StatusHost = new ContentControl { Content = ToolsUi.StatusPill("Checking...", "muted") } };
                    row.Container = ToolsUi.CheckRow(out row.Box, app.Name, app.Description, "Keep it if you " + app.KeepIf, row.StatusHost);
                    row.Box.IsChecked = pending.Contains(app.Package);
                    row.Box.Checked += (s, e) => { SavePending(); UpdateState(); };
                    row.Box.Unchecked += (s, e) => { SavePending(); UpdateState(); };
                    rows.Add(row);
                    groupRows.Add(row.Container);
                }
                root.Children.Add(ToolsUi.GroupCard(group, BuiltInApps.GroupBlurb(group), groupRows));
            }
            loading = false;
            UpdateState();
            if (Ctx.IsDemo) Scan(false);
            return root;
        }

        public override void Shown()
        {
            if (!scannedOnce) Scan(false);
        }

        public override void SavePending()
        {
            if (loading || rows.Count == 0) return;
            Ctx.Settings.PendingRemove = rows.Where(r => r.Box.IsChecked == true).Select(r => r.App.Package).ToList();
        }

        IEnumerable<Row> Selected { get { return rows.Where(r => r.Box.IsChecked == true && r.Installed != false); } }

        void UpdateState()
        {
            if (removeButton == null) return;
            int n = Selected.Count();
            bool busy = run != null && run.Running;
            countText.Text = n == 0 ? "Nothing selected" : n + (n == 1 ? " app selected" : " apps selected");
            removeButton.IsEnabled = n > 0 && !busy && !scanning;
            refreshButton.IsEnabled = !busy && !scanning;
            provisionedBox.IsEnabled = Ctx.IsElevated && !busy;
        }

        static HashSet<string> DemoInstalled()
        {
            return new HashSet<string>(new[]
            {
                "Clipchamp.Clipchamp", "Microsoft.BingWeather", "Microsoft.BingNews", "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.MicrosoftOfficeHub",
                "Microsoft.MicrosoftSolitaireCollection", "Microsoft.MicrosoftStickyNotes", "Microsoft.Todos", "Microsoft.PowerAutomateDesktop",
                "Microsoft.WindowsFeedbackHub", "Microsoft.WindowsMaps", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo", "Microsoft.OutlookForWindows",
                "Microsoft.YourPhone", "MSTeams", "Microsoft.GamingApp", "Microsoft.XboxGamingOverlay", "Microsoft.Xbox.TCUI", "Microsoft.XboxIdentityProvider",
                "Microsoft.Copilot", "MicrosoftCorporationII.QuickAssist", "Disney.37853FC22B2CE", "SpotifyAB.SpotifyMusic", "king.com.CandyCrushSaga"
            }, StringComparer.OrdinalIgnoreCase);
        }

        void Scan(bool announce)
        {
            if (scanning) return;
            if (Ctx.IsDemo) { ApplyInstalled(DemoInstalled()); return; }
            scanning = true;
            scanText.Text = "Checking which apps are installed...";
            UpdateState();
            HashSet<string> found = null;
            Ctx.RunInBackground("Checking built-in apps", () =>
            {
                var r = Cmd.RunPowerShell(BuiltInApps.ListScript, 120000);
                if (string.IsNullOrWhiteSpace(r.Output))
                    throw new InvalidOperationException(r.TimedOut ? "Windows took too long to list the apps." : "Windows did not list the apps. " + r.Error.Trim());
                found = BuiltInApps.RemovableInstalled(BuiltInApps.ParseInstalled(r.Output));
            }, () =>
            {
                scanning = false;
                ApplyInstalled(found);
                if (announce) Ctx.SetStatus("Checked the installed apps.");
            }, ex =>
            {
                scanning = false;
                scanText.Text = "Could not check: " + ex.Message;
                UpdateState();
            });
        }

        void ApplyInstalled(HashSet<string> installed)
        {
            scannedOnce = true;
            loading = true;
            int count = 0;
            foreach (var row in rows)
            {
                bool here = installed.Contains(row.App.Package);
                row.Installed = here;
                if (here) count++;
                row.StatusHost.Content = here ? ToolsUi.StatusPill("Installed", "info") : ToolsUi.StatusPill("Not installed", "muted");
                row.Box.IsEnabled = here;
                if (!here) row.Box.IsChecked = false;
                row.Container.Opacity = here ? 1.0 : 0.6;
            }
            loading = false;
            SavePending();
            scanText.Text = count + " of these " + rows.Count + " apps are installed on this PC.";
            UpdateState();
        }

        void ReviewAndRemove()
        {
            var chosen = Selected.ToList();
            if (chosen.Count == 0) return;
            if (Ctx.IsDemo) { Ctx.SetStatus("Demo mode: nothing was changed."); return; }
            if (LiveRun.Refuse(Ctx)) return;
            bool provisioned = provisionedBox.IsChecked == true && Ctx.IsElevated;
            var sb = new StringBuilder();
            sb.AppendLine("Remove " + (chosen.Count == 1 ? "this app" : "these " + chosen.Count + " apps") + " from your account?");
            sb.AppendLine();
            sb.Append(ToolsUi.BulletList(chosen.Select(r => r.App.Name)));
            sb.AppendLine();
            if (provisioned) sb.AppendLine("They will also be taken out of the Windows image, so new user accounts on this PC will not get them.");
            else sb.AppendLine("Other user accounts and new accounts are not changed.");
            sb.AppendLine("You can reinstall any of them from the Microsoft Store. Nothing else is touched.");
            if (chosen.Any(r => r.App.Group == BuiltInApps.GroupXbox && r.App.Package.StartsWith("Microsoft.Xbox", StringComparison.OrdinalIgnoreCase) || r.App.Package == "Microsoft.GamingApp"))
                sb.AppendLine().AppendLine("Note: Xbox pieces are used by Game Pass and some Store games to sign in. Remove them only if you do not play those.");
            if (!Ui.Confirm(Ctx.Window, sb.ToString().TrimEnd(), "Remove built-in apps")) return;

            var packages = chosen.Select(r => r.App.Package).ToList();
            string script = BuiltInApps.RemoveScript(packages, provisioned);
            resultHost.Children.Clear();
            var results = new List<BuiltInApps.RemoveResult>();
            run.Start("Removing " + Cmd.Plural(packages.Count, "app", "apps"), (log, ct) =>
            {
                int code = ProcessRunner.Stream(ProcessRunner.PowerShell, ProcessRunner.PowerShellArgs(script), line =>
                {
                    var r = BuiltInApps.ParseResultLine(line);
                    if (r != null)
                    {
                        lock (results) results.Add(r);
                        var app = BuiltInApps.Find(r.Package);
                        string name = app == null ? r.Package : app.Name;
                        log((r.Kind == "OK" ? "Done: " : r.Kind == "SKIP" ? "Skipped: " : "Failed: ") + name + " - " + r.Message);
                    }
                    else if (line.Trim().Length > 0) log(line);
                }, ct, Encoding.UTF8);
                if (code == -1) return "Stopped. Apps already handled stay removed.";
                lock (results)
                {
                    int ok = results.Where(r => r.Kind == "OK").Select(r => r.Package).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    int bad = results.Count(r => r.Kind == "FAIL");
                    return bad == 0 ? "Finished. " + Cmd.Plural(ok, "app", "apps") + " removed." : "Finished. " + ok + " removed, " + bad + " could not be removed.";
                }
            }, summary =>
            {
                List<BuiltInApps.RemoveResult> copy;
                lock (results) copy = results.ToList();
                var failed = copy.Where(r => r.Kind == "FAIL").ToList();
                var okNames = copy.Where(r => r.Kind == "OK").Select(r => r.Package).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(p => { var a = BuiltInApps.Find(p); return a == null ? p : a.Name; }).ToList();
                string text = summary;
                if (okNames.Count > 0) text += " Removed: " + string.Join(", ", okNames) + ". Reinstall any of them from the Microsoft Store.";
                if (failed.Count > 0)
                    text += " Not removed: " + string.Join("; ", failed.Select(f => { var a = BuiltInApps.Find(f.Package); return (a == null ? f.Package : a.Name) + " (" + f.Message + ")"; })) + ".";
                ToolsUi.ShowNote(resultHost, text, failed.Count == 0 && okNames.Count > 0 ? "Good" : failed.Count > 0 ? "Warn" : "Info");
                Ctx.SetStatus(summary, failed.Count > 0);
                Scan(false);
            });
        }
    }
}
