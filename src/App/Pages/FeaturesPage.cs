using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TweakBench.Core;
using TweakBench.Tools;

namespace TweakBench.App
{
    /// <summary>Windows features: curated optional features with live state; switch, review, then DISM runs with live output.</summary>
    public sealed class FeaturesPage : PageBase
    {
        sealed class Row
        {
            public WindowsFeature Feature;
            public CheckBox Box;
            public Border Container;
            public ContentControl StatusHost;
            public TextBlock Staged;
            public FeatureState State = FeatureState.Unknown;
            public string Blocked;     // why it cannot be switched, or null

            public bool Wanted { get { return Box.IsChecked == true; } }
            public bool IsOn { get { return State == FeatureState.On; } }
            public bool HasChange { get { return Wanted != IsOn; } }
        }

        readonly List<Row> rows = new List<Row>();
        TextBlock countText, scanText;
        Button applyButton, discardButton, refreshButton;
        StackPanel resultHost;
        LiveRun run;
        bool scanning, scannedOnce, loading;

        public FeaturesPage(UiContext ctx) : base(ctx) { }
        public override string Id { get { return "features"; } }
        public override string Title { get { return "Windows features"; } }
        public override string Subtitle { get { return "Turn optional parts of Windows on or off, like Sandbox, Hyper-V and WSL."; } }
        public override string Glyph { get { return ""; } }

        protected override FrameworkElement Build()
        {
            var root = new StackPanel();
            root.Children.Add(ToolsUi.Intro("These are parts of Windows that are not always on. Each switch shows what is on right now. Flip the ones you want to change, "
                + "then press Review and apply. Tweak Bench never restarts your PC: when a feature needs a restart, it tells you."));
            if (!Ctx.IsElevated)
                root.Children.Add(Ui.Note("Run as administrator to turn features on or off. You can still see what is on right now. Use \"Run as administrator\" in the sidebar.", "Info"));

            countText = Ui.Text("", "Text", true);
            scanText = Ui.Text("", "Muted", false, 12);
            applyButton = Ui.Button("Review and apply", ReviewAndApply, "Primary");
            discardButton = Ui.Button("Discard changes", Discard, "Ghost");
            refreshButton = Ui.Button("Check again", () => Scan(true), "Ghost", "Reads the state of every feature again");
            root.Children.Add(ToolsUi.Toolbar(applyButton, countText, discardButton, refreshButton, scanText));

            resultHost = new StackPanel();
            root.Children.Add(resultHost);
            run = new LiveRun(Ctx, 200);
            run.RunningChanged += UpdateState;
            root.Children.Add(run.Panel);

            loading = true;
            foreach (string group in WindowsFeatures.Groups)
            {
                var groupRows = new List<UIElement>();
                foreach (var f in WindowsFeatures.All.Where(x => x.Group == group))
                {
                    var row = new Row { Feature = f, StatusHost = new ContentControl { Content = ToolsUi.StatusPill("Checking...", "muted") } };
                    string extra = f.Note;
                    if (f.Advice != FeatureAdvice.None && !string.IsNullOrEmpty(f.AdviceText)) extra = (string.IsNullOrEmpty(extra) ? "" : extra + " ") + f.AdviceText;
                    row.Container = ToolsUi.CheckRow(out row.Box, f.Title, f.Description, extra, row.StatusHost, true);
                    row.Box.IsEnabled = false;
                    row.Blocked = WindowsFeatures.EditionProblem(f, Ctx.System.Edition);
                    row.Box.Checked += (s, e) => Changed();
                    row.Box.Unchecked += (s, e) => Changed();
                    rows.Add(row);
                    groupRows.Add(row.Container);
                }
                root.Children.Add(ToolsUi.GroupCard(group, null, groupRows));
            }
            loading = false;

            if (Ctx.IsDemo) ApplyStates(DemoStates());
            UpdateState();
            return root;
        }

        static Dictionary<string, FeatureState> DemoStates()
        {
            var map = new Dictionary<string, FeatureState>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in WindowsFeatures.All) map[f.Name] = FeatureState.Off;
            foreach (string on in new[] { "VirtualMachinePlatform", "Microsoft-Hyper-V-All", "WindowsMediaPlayer", "Printing-PrintToPDFServices-Features", "WorkFolders-Client" }) map[on] = FeatureState.On;
            map.Remove("MicrosoftWindowsPowerShellV2Root");
            map["Microsoft-Windows-Subsystem-Linux"] = FeatureState.Off;
            return map;
        }

        public override void Shown()
        {
            if (!scannedOnce) Scan(false);
        }

        void Changed()
        {
            if (loading) return;
            foreach (var r in rows) UpdateRow(r);
            UpdateState();
        }

        void UpdateRow(Row r)
        {
            if (r.Staged == null) return;
            r.Staged.Text = r.HasChange ? (r.Wanted ? "Will turn on" : "Will turn off") : "";
            if (r.HasChange) Ui.Ref(r.Container, Border.BackgroundProperty, "Selected"); else r.Container.ClearValue(Border.BackgroundProperty);
        }

        IEnumerable<Row> Pending { get { return rows.Where(r => r.HasChange); } }

        void UpdateState()
        {
            if (applyButton == null) return;
            int n = Pending.Count();
            bool busy = run != null && run.Running;
            countText.Text = n == 0 ? "No changes yet" : n + (n == 1 ? " change ready" : " changes ready");
            applyButton.IsEnabled = n > 0 && !busy && Ctx.IsElevated;
            applyButton.ToolTip = Ctx.IsElevated ? null : "Needs administrator rights.";
            discardButton.IsEnabled = n > 0 && !busy;
            refreshButton.IsEnabled = !busy && !scanning;
            foreach (var r in rows) r.Box.IsEnabled = r.Blocked == null && Ctx.IsElevated && !busy && r.State != FeatureState.NotOnThisPc && r.State != FeatureState.Unknown;
        }

        void Discard()
        {
            loading = true;
            foreach (var r in rows) r.Box.IsChecked = r.IsOn;
            loading = false;
            Changed();
        }

        // ---- reading ----

        void Scan(bool announce)
        {
            if (scanning || (run != null && run.Running)) return;
            if (Ctx.IsDemo) { ApplyStates(DemoStates()); return; }
            scanning = true;
            scanText.Text = "Reading the state of each feature...";
            UpdateState();
            Dictionary<string, FeatureState> map = null;
            string problem = null;
            Ctx.RunInBackground("Reading Windows features", () =>
            {
                var r = Cmd.RunPowerShell(WindowsFeatures.ReadScript, 120000);
                map = WindowsFeatures.ParseCim(r.Output);
                if (map.Count == 0 && Ctx.IsElevated)
                {
                    var d = ProcessRunner.Run(RepairTools.DismPath, "/online /get-features /format:table", 180000, ProcessRunner.Oem);
                    map = WindowsFeatures.ParseDismTable(d.Output);
                }
                if (map.Count == 0) problem = Ctx.IsElevated ? "Windows did not list its features." : "Run as administrator to see feature status.";
            }, () =>
            {
                scanning = false;
                scannedOnce = true;
                if (problem != null)
                {
                    scanText.Text = problem;
                    foreach (var row in rows) row.StatusHost.Content = ToolsUi.StatusPill(Ctx.IsElevated ? "Unknown" : "Run as administrator to see status", "muted");
                    UpdateState();
                    return;
                }
                ApplyStates(map);
                if (announce) Ctx.SetStatus("Read the state of the Windows features.");
            }, ex =>
            {
                scanning = false;
                scanText.Text = "Could not read the features: " + ex.Message;
                UpdateState();
            });
        }

        void ApplyStates(Dictionary<string, FeatureState> map)
        {
            scannedOnce = true;
            loading = true;
            int on = 0;
            foreach (var row in rows)
            {
                row.State = WindowsFeatures.Lookup(map, row.Feature.Name);
                if (row.State == FeatureState.On) on++;
                string text, kind;
                switch (row.State)
                {
                    case FeatureState.On: text = "On"; kind = row.Feature.Advice == FeatureAdvice.KeepOff ? "warn" : "good"; break;
                    case FeatureState.Off: text = "Off"; kind = "muted"; break;
                    case FeatureState.Removed: text = "Off (files not on this PC)"; kind = "muted"; break;
                    case FeatureState.Pending: text = "Waiting for restart"; kind = "info"; break;
                    case FeatureState.NotOnThisPc: text = "Not on this PC"; kind = "muted"; break;
                    default: text = "Unknown"; kind = "muted"; break;
                }
                if (row.Blocked != null && row.State != FeatureState.On) { text = "Needs Windows Pro"; kind = "muted"; }
                var staged = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
                Ui.Ref(staged, TextBlock.ForegroundProperty, "AccentText");
                row.Staged = staged;
                var holder = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                holder.Children.Add(ToolsUi.StatusPill(text, kind));
                holder.Children.Add(staged);
                row.StatusHost.Content = holder;
                row.Box.IsChecked = row.State == FeatureState.On;
                string tip = row.Blocked ?? (row.State == FeatureState.NotOnThisPc ? "Windows does not list this feature on this PC." : (!Ctx.IsElevated ? "Needs administrator rights." : null));
                row.Box.ToolTip = tip;
                UpdateRow(row);
            }
            loading = false;
            scanText.Text = on + " of " + rows.Count + " features are on.";
            UpdateState();
        }

        // ---- applying ----

        void ReviewAndApply()
        {
            var changes = Pending.ToList();
            if (changes.Count == 0) return;
            if (Ctx.IsDemo) { Ctx.SetStatus("Demo mode: nothing was changed."); return; }
            if (LiveRun.Refuse(Ctx)) return;
            var sb = new StringBuilder();
            sb.AppendLine("Make these changes with DISM?");
            sb.AppendLine();
            foreach (var r in changes)
                sb.AppendLine("   • " + (r.Wanted ? "Turn on: " : "Turn off: ") + r.Feature.Title + (r.Wanted && r.Feature.WithParents ? " (and the parts it depends on)" : ""));
            sb.AppendLine();
            sb.AppendLine("Tweak Bench will not restart your PC. If a feature needs a restart, you will be told.");
            if (changes.Any(r => r.Wanted && (r.State == FeatureState.Removed || r.Feature.Name == "NetFx3")))
                sb.AppendLine("Some features download their files from Windows Update, so the PC needs to be online.");
            foreach (var r in changes.Where(r => !r.Wanted && !string.IsNullOrEmpty(r.Feature.BreaksIfOff)))
                sb.AppendLine("Turning off " + r.Feature.Title + ": " + r.Feature.BreaksIfOff);
            if (!Ui.Confirm(Ctx.Window, sb.ToString().TrimEnd(), "Change Windows features")) return;

            var work = changes.Select(r => new { r.Feature, Enable = r.Wanted }).ToList();
            resultHost.Children.Clear();
            var done = new List<string>(); var failed = new List<string>();
            bool restart = false;
            run.Start("Changing " + Cmd.Plural(work.Count, "Windows feature", "Windows features"), (log, ct) =>
            {
                foreach (var w in work)
                {
                    if (ct.IsCancellationRequested) { log("Stopped before " + w.Feature.Title + "."); break; }
                    log("");
                    log("== " + (w.Enable ? "Turning on " : "Turning off ") + w.Feature.Title + " ==");
                    string args = w.Enable ? WindowsFeatures.EnableArgs(w.Feature) : WindowsFeatures.DisableArgs(w.Feature);
                    int code = ProcessRunner.Stream(RepairTools.DismPath, args, line => { if (line.Trim().Length > 0) log(line); }, ct, ProcessRunner.Oem);
                    if (code == -1) { log("Stopped."); failed.Add(w.Feature.Title + " (stopped)"); break; }
                    log((WindowsFeatures.IsSuccess(code) ? "Done: " : "Failed: ") + w.Feature.Title + " - " + WindowsFeatures.DescribeExit(code));
                    if (WindowsFeatures.IsSuccess(code)) { done.Add(w.Feature.Title); if (WindowsFeatures.NeedsRestart(code)) restart = true; }
                    else failed.Add(w.Feature.Title + " (" + WindowsFeatures.DescribeExit(code) + ")");
                }
                return failed.Count == 0 ? "Finished. " + Cmd.Plural(done.Count, "change", "changes") + " made." : "Finished. " + done.Count + " made, " + failed.Count + " failed.";
            }, summary =>
            {
                string text = summary;
                if (restart) text += " Restart your PC when convenient to finish. Tweak Bench never restarts it for you.";
                if (failed.Count > 0) text += " Not changed: " + string.Join("; ", failed) + ".";
                ToolsUi.ShowNote(resultHost, text, failed.Count == 0 ? (restart ? "Info" : "Good") : "Warn");
                Ctx.SetStatus(summary, failed.Count > 0);
                scannedOnce = false;
                Scan(false);
            });
        }
    }
}
