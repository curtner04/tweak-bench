using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using TweakBench.Core;
using TweakBench.Tools;

namespace TweakBench.App
{
    /// <summary>Install apps: a curated winget list with live installed status, updates, and live output.</summary>
    public sealed class InstallPage : PageBase
    {
        sealed class Row
        {
            public WingetApp App;
            public CheckBox Box;
            public Border Container;
            public ContentControl StatusHost;
            public bool? Installed;
        }

        readonly List<Row> rows = new List<Row>();
        readonly Dictionary<string, Border> groupCards = new Dictionary<string, Border>();
        TextBlock countText, statusText;
        Button installButton, checkButton, updateAllButton, refreshButton;
        TextBox search;
        StackPanel resultHost, updatesHost;
        LiveRun run;
        string wingetPath;
        bool wingetKnown, scanning, scannedOnce, loading;
        List<WingetRow> upgrades;

        public InstallPage(UiContext ctx) : base(ctx) { }
        public override string Id { get { return "install"; } }
        public override string Title { get { return "Install apps"; } }
        public override string Subtitle { get { return "Install popular free apps in one go, and keep them up to date."; } }
        public override string Glyph { get { return ""; } }

        protected override FrameworkElement Build()
        {
            var root = new StackPanel();
            root.Children.Add(ToolsUi.Intro("Tick the apps you want and press Install. Tweak Bench uses winget, the package manager built into Windows, to download "
                + "each app from its publisher. You see exactly what will be installed before anything starts."));

            statusText = Ui.Text("", "Muted", false, 12);
            countText = Ui.Text("", "Text", true);
            installButton = Ui.Button("Install selected apps", ReviewAndInstall, "Primary");
            checkButton = Ui.Button("Check for updates", CheckUpdates, "Ghost", "Lists installed apps that have a newer version");
            updateAllButton = Ui.Button("Update everything", UpdateAll, "Ghost", "Updates every app winget knows about");
            refreshButton = Ui.Button("Check again", () => Scan(true), "Ghost", "Looks at which of these apps are installed right now");
            root.Children.Add(ToolsUi.Toolbar(installButton, countText, checkButton, updateAllButton, refreshButton, statusText));

            resultHost = new StackPanel();
            root.Children.Add(resultHost);
            run = new LiveRun(Ctx, 240);
            run.RunningChanged += UpdateState;
            root.Children.Add(run.Panel);
            updatesHost = new StackPanel();
            root.Children.Add(updatesHost);

            search = Ui.Style(new TextBox { ToolTip = "Type to filter the list by name or description" }, "Search");
            search.Margin = new Thickness(0, 0, 0, 12);
            search.TextChanged += (s, e) => ApplyFilter();
            var searchLabel = Ui.Text("Filter the list", "Muted", false, 12);
            searchLabel.Margin = new Thickness(2, 0, 0, 4);
            root.Children.Add(searchLabel);
            root.Children.Add(search);

            var pending = new HashSet<string>(Ctx.Settings.PendingInstall ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            loading = true;
            foreach (string category in WingetCatalog.Categories)
            {
                var groupRows = new List<UIElement>();
                foreach (var app in WingetCatalog.All.Where(a => a.Category == category))
                {
                    var row = new Row { App = app, StatusHost = new ContentControl { Content = ToolsUi.StatusPill("Checking...", "muted") } };
                    row.Container = ToolsUi.CheckRow(out row.Box, app.Name, app.Description, app.Id, row.StatusHost);
                    row.Box.IsChecked = pending.Contains(app.Id);
                    row.Box.Checked += (s, e) => { SavePending(); UpdateState(); };
                    row.Box.Unchecked += (s, e) => { SavePending(); UpdateState(); };
                    rows.Add(row);
                    groupRows.Add(row.Container);
                }
                var card = ToolsUi.GroupCard(category, null, groupRows);
                groupCards[category] = card;
                root.Children.Add(card);
            }
            loading = false;

            if (Ctx.IsDemo)
            {
                wingetKnown = true; wingetPath = "winget";
                statusText.Text = "winget is ready.";
                ApplyInstalled(DemoInstalled());
                ShowUpdates(new List<WingetRow>
                {
                    new WingetRow { Name = "Google Chrome", Id = "Google.Chrome", Version = "157.0.8103.39", Available = "157.0.8103.51" },
                    new WingetRow { Name = "OBS Studio", Id = "OBSProject.OBSStudio", Version = "32.1.2", Available = "33.0.0" },
                    new WingetRow { Name = "Notepad++", Id = "Notepad++.Notepad++", Version = "8.9.6.4", Available = "8.9.9" },
                });
                run.ShowSample("Installing 2 apps", "Finished. 2 of 2 installed.", new[]
                {
                    "== VLC media player (VideoLAN.VLC) ==",
                    "Found VLC media player [VideoLAN.VLC] Version 3.0.21",
                    "Downloading https://get.videolan.org/vlc/3.0.21/win64/vlc-3.0.21-win64.exe",
                    "Successfully verified installer hash",
                    "Starting package install...",
                    "Successfully installed",
                    "Done: VLC media player.",
                });
            }
            UpdateState();
            return root;
        }

        static HashSet<string> DemoInstalled()
        {
            return new HashSet<string>(new[] { "Google.Chrome", "Git.Git", "Notepad++.Notepad++", "OBSProject.OBSStudio", "Microsoft.PowerToys", "Valve.Steam", "Discord.Discord", "Microsoft.VisualStudioCode", "7zip.7zip" },
                StringComparer.OrdinalIgnoreCase);
        }

        public override void Shown()
        {
            if (!scannedOnce) Scan(false);
        }

        public override void SavePending()
        {
            if (loading || rows.Count == 0) return;
            Ctx.Settings.PendingInstall = rows.Where(r => r.Box.IsChecked == true).Select(r => r.App.Id).ToList();
        }

        IEnumerable<Row> Selected { get { return rows.Where(r => r.Box.IsChecked == true && r.Installed != true); } }

        void UpdateState()
        {
            if (installButton == null) return;
            int n = Selected.Count();
            bool busy = run != null && run.Running;
            bool ready = wingetKnown && wingetPath != null;
            countText.Text = n == 0 ? "Nothing selected" : n + (n == 1 ? " app selected" : " apps selected");
            installButton.IsEnabled = n > 0 && !busy && ready;
            checkButton.IsEnabled = !busy && ready;
            updateAllButton.IsEnabled = !busy && ready;
            refreshButton.IsEnabled = !busy && !scanning;
        }

        void ApplyFilter()
        {
            string q = (search.Text ?? "").Trim();
            foreach (var group in WingetCatalog.Categories)
            {
                bool any = false;
                foreach (var row in rows.Where(r => r.App.Category == group))
                {
                    bool show = q.Length == 0 || row.App.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || row.App.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || row.App.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    row.Container.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    any |= show;
                }
                groupCards[group].Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ---- installed status ----

        void ShowMissingWinget()
        {
            resultHost.Children.Clear();
            resultHost.Children.Add(Ui.Note("winget was not found on this PC. It comes with \"App Installer\" from the Microsoft Store. Open the Microsoft Store, search for "
                + "App Installer, install or update it, then press Check again. Until then, installing and updating are switched off.", "Warn"));
            statusText.Text = "winget is missing.";
            foreach (var row in rows) { row.StatusHost.Content = ToolsUi.StatusPill("Unknown", "muted"); }
        }

        void Scan(bool announce)
        {
            if (scanning || (run != null && run.Running)) return;
            if (Ctx.IsDemo) { ApplyInstalled(DemoInstalled()); return; }
            scanning = true;
            statusText.Text = "Checking which apps are installed...";
            UpdateState();
            HashSet<string> found = null;
            string problem = null;
            Ctx.RunInBackground("Checking installed apps", () =>
            {
                wingetPath = Cmd.FindWinget();
                wingetKnown = true;
                if (wingetPath == null) return;
                string file = Path.Combine(Path.GetTempPath(), "TweakBench-winget-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var r = ProcessRunner.Run(wingetPath, WingetCatalog.ExportArgs(file), 180000, Encoding.UTF8);
                    if (File.Exists(file)) found = WingetCatalog.ParseExport(File.ReadAllText(file, Encoding.UTF8));
                    else
                    {
                        // winget export did not write a file: fall back to reading the list table.
                        var l = ProcessRunner.Run(wingetPath, WingetCatalog.ListArgs(), 180000, Encoding.UTF8);
                        found = WingetCatalog.ParseListIds(l.Output);
                        if (found.Count == 0)
                            problem = WingetCatalog.NeedsSourceAgreement(r.Combined + l.Combined)
                                ? "winget needs you to accept Microsoft's source terms once. Installing an app from this page asks you first."
                                : "winget did not list your installed apps.";
                    }
                }
                finally { try { File.Delete(file); } catch (Exception) { } }
            }, () =>
            {
                scanning = false;
                scannedOnce = true;
                if (wingetPath == null) { ShowMissingWinget(); UpdateState(); return; }
                resultHost.Children.Clear();
                if (found == null || problem != null && found.Count == 0)
                {
                    statusText.Text = problem ?? "Could not read the installed apps.";
                    foreach (var row in rows) row.StatusHost.Content = ToolsUi.StatusPill("Unknown", "muted");
                    UpdateState();
                    return;
                }
                ApplyInstalled(found);
                if (announce) Ctx.SetStatus("Checked the installed apps.");
            }, ex =>
            {
                scanning = false;
                statusText.Text = "Could not check: " + ex.Message;
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
                bool here = installed.Contains(row.App.Id);
                row.Installed = here;
                if (here) count++;
                row.StatusHost.Content = here ? ToolsUi.StatusPill("Installed", "good") : ToolsUi.StatusPill("Not installed", "muted");
                row.Box.IsEnabled = !here;
                if (here) row.Box.IsChecked = false;
            }
            loading = false;
            SavePending();
            if (wingetKnown && wingetPath != null || Ctx.IsDemo) statusText.Text = count + " of " + rows.Count + " listed apps are installed.";
            UpdateState();
        }

        // ---- install ----

        void ReviewAndInstall()
        {
            var chosen = Selected.ToList();
            if (chosen.Count == 0 || wingetPath == null) return;
            if (Ctx.IsDemo) { Ctx.SetStatus("Demo mode: nothing was changed."); return; }
            if (LiveRun.Refuse(Ctx)) return;
            var sb = new StringBuilder();
            sb.AppendLine("Install " + (chosen.Count == 1 ? "this app" : "these " + chosen.Count + " apps") + " with winget?");
            sb.AppendLine();
            sb.Append(ToolsUi.BulletList(chosen.Select(r => r.App.Name + "  (" + r.App.Id + ")")));
            sb.AppendLine();
            sb.AppendLine("They install one after another, and you can stop at any point.");
            sb.AppendLine("Installing through winget accepts each app's license terms and Microsoft's winget source terms on your behalf. Some installers show their own windows or ask for administrator permission.");
            if (!Ui.Confirm(Ctx.Window, sb.ToString().TrimEnd(), "Install apps")) return;

            var list = chosen.Select(r => r.App).ToList();
            resultHost.Children.Clear();
            string exe = wingetPath;
            var good = new List<string>(); var bad = new List<string>(); bool restartNote = false;
            run.Start("Installing " + Cmd.Plural(list.Count, "app", "apps"), (log, ct) =>
            {
                int index = 0;
                foreach (var app in list)
                {
                    if (ct.IsCancellationRequested) { log("Stopped before " + app.Name + "."); break; }
                    index++;
                    log("");
                    log("== " + index + " of " + list.Count + ": " + app.Name + " (" + app.Id + ") ==");
                    int code = ProcessRunner.Stream(exe, WingetCatalog.InstallArgs(app.Id), line => { if (line.Trim().Length > 0 && !IsSpinner(line)) log(line); }, ct, Encoding.UTF8);
                    if (code == -1) { log("Stopped."); bad.Add(app.Name + " (stopped)"); break; }
                    bool ok = WingetCatalog.IsInstallSuccess(code);
                    log((ok ? "Done: " : "Failed: ") + app.Name + " - " + WingetCatalog.DescribeExit(code));
                    if (ok && (uint)code == 0x8A150109u) restartNote = true;
                    if (ok) good.Add(app.Name); else bad.Add(app.Name + " (" + WingetCatalog.DescribeExit(code) + ")");
                }
                return bad.Count == 0 && good.Count == list.Count ? "Finished. " + Cmd.Plural(good.Count, "app", "apps") + " installed."
                    : "Finished. " + good.Count + " of " + list.Count + " installed.";
            }, summary =>
            {
                string text = summary;
                if (restartNote) text += " Restart your PC when convenient to finish an install. Tweak Bench will not restart it for you.";
                if (bad.Count > 0) text += " Not installed: " + string.Join("; ", bad) + ".";
                ToolsUi.ShowNote(resultHost, text, bad.Count == 0 ? "Good" : "Warn");
                Ctx.SetStatus(summary, bad.Count > 0);
                Scan(false);
            });
        }

        static bool IsSpinner(string line)
        {
            string t = line.Trim();
            return t.Length <= 1 && (t == "-" || t == "\\" || t == "|" || t == "/");
        }

        // ---- updates ----

        void CheckUpdates()
        {
            if (wingetPath == null) return;
            if (Ctx.IsDemo) { Ctx.SetStatus("Demo mode: sample updates are shown."); return; }
            resultHost.Children.Clear();
            string exe = wingetPath;
            string output = ""; string failure = null;
            Ctx.RunInBackground("Checking for app updates", () =>
            {
                var r = ProcessRunner.Run(exe, WingetCatalog.UpgradeListArgs(), 240000, Encoding.UTF8);
                output = r.Output;
                if (r.TimedOut) failure = "winget took too long to answer.";
                else if (WingetCatalog.NeedsSourceAgreement(r.Combined) && WingetCatalog.ParseTable(output).Count == 0)
                    failure = "winget needs you to accept Microsoft's source terms once. Install an app from this page and Tweak Bench asks you first.";
            }, () =>
            {
                if (failure != null) { ToolsUi.ShowNote(resultHost, failure, "Warn"); return; }
                var found = WingetCatalog.ParseUpgrades(output);
                ShowUpdates(found);
                Ctx.SetStatus(found.Count == 0 ? "All apps are up to date." : Cmd.Plural(found.Count, "update is", "updates are") + " available.");
            });
        }

        void ShowUpdates(List<WingetRow> found)
        {
            upgrades = found;
            updatesHost.Children.Clear();
            if (found.Count == 0)
            {
                updatesHost.Children.Add(Ui.Note("Everything winget knows about is up to date.", "Good"));
                return;
            }
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int r = 0;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(grid, Ui.Text("App", "Muted", true, 12), 0, 0);
            AddCell(grid, Ui.Text("Installed", "Muted", true, 12), 0, 1);
            AddCell(grid, Ui.Text("Available", "Muted", true, 12), 0, 2);
            foreach (var u in found)
            {
                r++;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var name = new StackPanel { Margin = new Thickness(0, 6, 8, 6) };
                name.Children.Add(Ui.Text(u.Name, "Text", true));
                name.Children.Add(Ui.Text(u.Id, "Muted", false, 11));
                AddCell(grid, name, r, 0);
                var v1 = Ui.Text(u.Version, "Muted"); v1.Margin = new Thickness(0, 6, 8, 6);
                AddCell(grid, v1, r, 1);
                var v2 = Ui.Text(u.Available, "Good", true); v2.Margin = new Thickness(0, 6, 8, 6);
                AddCell(grid, v2, r, 2);
                string id = u.Id; string title = u.Name;
                var b = Ui.Button("Update", () => UpdateOne(id, title), "Small", "Updates " + title);
                b.Margin = new Thickness(0, 4, 0, 4);
                b.IsEnabled = wingetPath != null && !run.Running;
                AddCell(grid, b, r, 3);
            }
            var stack = new StackPanel();
            stack.Children.Add(Ui.Heading(found.Count == 1 ? "1 update available" : found.Count + " updates available", "H2"));
            stack.Children.Add(Ui.Spacer(6));
            stack.Children.Add(grid);
            var card = Ui.Card(stack);
            card.Margin = new Thickness(0, 0, 0, 14);
            updatesHost.Children.Add(card);
        }

        static void AddCell(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row); Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        void UpdateOne(string id, string title)
        {
            if (wingetPath == null || run.Running) return;
            if (Ctx.IsDemo) { Ctx.SetStatus("Demo mode: nothing was changed."); return; }
            if (LiveRun.Refuse(Ctx)) return;
            if (!Ui.Confirm(Ctx.Window, "Update " + title + " (" + id + ") with winget?\n\nThis accepts the app's license terms and Microsoft's winget source terms on your behalf. The app may close while it updates.", "Update app")) return;
            string exe = wingetPath;
            run.Start("Updating " + title, (log, ct) =>
            {
                int code = ProcessRunner.Stream(exe, WingetCatalog.UpgradeOneArgs(id), line => { if (line.Trim().Length > 0 && !IsSpinner(line)) log(line); }, ct, Encoding.UTF8);
                if (code == -1) return "Stopped.";
                if ((uint)code == 0x8A15002Bu) return "Already up to date. " + title + " has no newer version.";
                if (WingetCatalog.IsInstallSuccess(code)) return "Finished. " + title + " is up to date." + ((uint)code == 0x8A150109u ? " Restart your PC when convenient to finish; Tweak Bench will not restart it for you." : "");
                return "Finished with a problem: " + WingetCatalog.DescribeExit(code);
            }, summary => { Ctx.SetStatus(summary); CheckUpdates(); });
        }

        void UpdateAll()
        {
            if (wingetPath == null || run.Running) return;
            if (Ctx.IsDemo) { Ctx.SetStatus("Demo mode: nothing was changed."); return; }
            if (LiveRun.Refuse(Ctx)) return;
            string message = "Update every app that winget knows about?\n\nApps that are running may close while they update. This also updates runtimes and developer tools (for example .NET, Visual C++ and Visual Studio Build Tools). This accepts each app's license terms and Microsoft's winget source terms on your behalf, and you can stop at any point. To update only one app, cancel and use the Update button on its row.";
            if (upgrades != null && upgrades.Count > 0)
                message += "\n\nUpdates found earlier:\n" + ToolsUi.BulletList(upgrades.Select(u => u.Name + "  " + u.Version + " to " + u.Available), 25);
            else
                message += "\n\nNo update check has been run yet, so the list is not known. Choose Check for updates first to see it.";
            if (!Ui.Confirm(Ctx.Window, message, "Update everything")) return;
            string exe = wingetPath;
            resultHost.Children.Clear();
            run.Start("Updating all apps", (log, ct) =>
            {
                int code = ProcessRunner.Stream(exe, WingetCatalog.UpgradeAllArgs(), line => { if (line.Trim().Length > 0 && !IsSpinner(line)) log(line); }, ct, Encoding.UTF8);
                if (code == -1) return "Stopped. Updates already finished stay installed.";
                if (WingetCatalog.IsUpdateSuccess(code)) return "Finished. Everything winget knows about is up to date." + ((uint)code == 0x8A150109u ? " Restart your PC when convenient to finish; Tweak Bench will not restart it for you." : "");
                return "Finished. Some apps could not be updated: " + WingetCatalog.DescribeExit(code);
            }, summary => { Ctx.SetStatus(summary); Scan(false); });
        }
    }
}
