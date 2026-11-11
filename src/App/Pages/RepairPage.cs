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
    /// <summary>Repair and tools: Windows repair commands with live output, quick fixes, and shortcuts to the classic Windows tools.</summary>
    public sealed class RepairPage : PageBase
    {
        readonly List<Button> taskButtons = new List<Button>();
        readonly Dictionary<RepairKind, Button> buttons = new Dictionary<RepairKind, Button>();
        StackPanel resultHost;
        LiveRun run;

        public RepairPage(UiContext ctx) : base(ctx) { }
        public override string Id { get { return "repair"; } }
        public override string Title { get { return "Repair & tools"; } }
        public override string Subtitle { get { return "Fix common Windows problems and open the classic admin tools."; } }
        public override string Glyph { get { return ""; } }

        protected override FrameworkElement Build()
        {
            var root = new StackPanel();
            root.Children.Add(ToolsUi.Intro("Each button runs one well-known Windows repair. Tweak Bench explains what it does first, shows the tool's own output while it works, "
                + "and tells you the result in plain words. Nothing here restarts your PC."));
            if (!Ctx.IsElevated)
                root.Children.Add(Ui.Note("Run as administrator to use the repairs marked with a shield. The quick actions and the tool shortcuts below work without it.", "Info"));

            resultHost = new StackPanel();
            root.Children.Add(resultHost);
            run = new LiveRun(Ctx, 280);
            run.RunningChanged += UpdateState;
            root.Children.Add(run.Panel);

            foreach (string group in RepairTools.Groups)
            {
                var rows = new List<UIElement>();
                foreach (var task in RepairTools.Tasks.Where(t => t.Group == group)) rows.Add(TaskRow(task));
                root.Children.Add(ToolsUi.GroupCard(group, null, rows));
            }
            root.Children.Add(ClassicToolsCard());

            if (Ctx.IsDemo)
            {
                run.ShowSample("Check system files", "Finished. No problems found.", new[]
                {
                    "Beginning system scan.  This process will take some time.",
                    "",
                    "Beginning verification phase of system scan.",
                    "Verification 100% complete.",
                    "",
                    "Windows Resource Protection did not find any integrity violations.",
                });
                ToolsUi.ShowNote(resultHost, "No problems found. Your Windows system files are intact.", "Good");
            }
            UpdateState();
            return root;
        }

        FrameworkElement TaskRow(RepairTask task)
        {
            var text = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Ui.Text(task.Title, "Text", true, 14));
            if (task.NeedsAdmin)
            {
                var shield = Ui.Glyph("", 13, "Muted");
                shield.Margin = new Thickness(8, 0, 0, 0);
                shield.ToolTip = "Needs administrator rights";
                title.Children.Add(shield);
            }
            text.Children.Add(title);
            var desc = Ui.Para(task.Description, "Text");
            desc.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(desc);

            var button = Ui.Button(task.Button, () => RunTask(task), task.Long ? "Primary" : "Ghost");
            button.MinWidth = 130;
            button.VerticalAlignment = VerticalAlignment.Center;
            taskButtons.Add(button);
            buttons[task.Kind] = button;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            text.Margin = new Thickness(0, 0, 14, 0);
            grid.Children.Add(text);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            var border = new Border { Child = grid, Padding = new Thickness(2, 10, 2, 10), BorderThickness = new Thickness(0, 1, 0, 0) };
            Ui.Ref(border, Border.BorderBrushProperty, "Border");
            return border;
        }

        FrameworkElement ClassicToolsCard()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var tool in RepairTools.Classic)
            {
                var t = tool;
                var glyph = Ui.Glyph(t.Glyph, 20, "AccentText");
                glyph.Margin = new Thickness(0, 0, 12, 0);
                glyph.VerticalAlignment = VerticalAlignment.Top;
                var text = new StackPanel { Width = 190 };
                text.Children.Add(Ui.Text(t.Name, "Text", true, 13));
                var purpose = Ui.Para(t.Purpose, "Muted", 11.5);
                purpose.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(purpose);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(glyph);
                row.Children.Add(text);
                var button = Ui.Button(row, () => OpenTool(t), "Ghost", t.Purpose);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.VerticalContentAlignment = VerticalAlignment.Top;
                button.Margin = new Thickness(0, 0, 10, 10);
                button.Padding = new Thickness(12, 10, 12, 10);
                button.Width = 262;
                button.MinHeight = 74;
                wrap.Children.Add(button);
            }
            return ToolsUi.GroupCard("Classic Windows tools", "Opens the original Windows tool in its own window. Some ask for administrator permission.", new UIElement[] { wrap });
        }

        void OpenTool(ClassicTool tool)
        {
            if (Ctx.IsDemo) { Ctx.SetStatus("Demo mode: " + tool.Name + " is not opened."); return; }
            string problem = RepairTools.Open(tool);
            if (problem != null) Ctx.SetStatus(problem, true); else Ctx.SetStatus("Opened " + tool.Name + ".");
        }

        void UpdateState()
        {
            bool busy = run != null && run.Running;
            foreach (var task in RepairTools.Tasks)
            {
                Button b;
                if (!buttons.TryGetValue(task.Kind, out b)) continue;
                bool needsAdminMissing = task.NeedsAdmin && !Ctx.IsElevated;
                b.IsEnabled = !busy && !needsAdminMissing;
                b.ToolTip = needsAdminMissing ? "Needs administrator rights. Use \"Run as administrator\" in the sidebar." : (busy ? "Another tool is running. Wait for it or press Stop." : null);
            }
        }

        // ---- running ----

        void RunTask(RepairTask task)
        {
            if (run.Running) return;
            if (Ctx.IsDemo) { DemoRun(task); return; }
            if (task.Long && LiveRun.Refuse(Ctx)) return;
            if (!string.IsNullOrEmpty(task.Confirm) && task.Kind != RepairKind.ClearTemp)
                if (!Ui.Confirm(Ctx.Window, task.Confirm, task.Title)) return;
            resultHost.Children.Clear();
            switch (task.Kind)
            {
                case RepairKind.Sfc: RunSfc(task); break;
                case RepairKind.DismRestoreHealth: RunDism(task, RepairTools.DismRestoreHealthArgs, true); break;
                case RepairKind.ComponentCleanup: RunDism(task, RepairTools.DismComponentCleanupArgs, false); break;
                case RepairKind.ResetWindowsUpdate: RunResetWindowsUpdate(task); break;
                case RepairKind.ResetNetwork: RunResetNetwork(task); break;
                case RepairKind.FlushDns: RunFlushDns(task); break;
                case RepairKind.ClearTemp: RunClearTemp(task); break;
                case RepairKind.RestorePoint: RunRestorePoint(task); break;
                case RepairKind.RestartExplorer: RunRestartExplorer(task); break;
                case RepairKind.OpenWindowsUpdate:
                    {
                        string problem = RepairTools.OpenWindowsUpdateSettings();
                        Ctx.SetStatus(problem ?? "Opened Windows Update settings.", problem != null);
                        break;
                    }
            }
        }

        void DemoRun(RepairTask task)
        {
            run.ShowSample(task.Title, "Demo mode: nothing was run.", new[] { "Demo mode: Tweak Bench shows sample data here and does not run " + task.Title.ToLowerInvariant() + "." });
            Ctx.SetStatus("Demo mode: nothing was run.");
        }

        void Show(RepairOutput.Verdict v)
        {
            ToolsUi.ShowNote(resultHost, v.Message, v.Ok ? (v.RestartNeeded ? "Info" : "Good") : "Warn");
            Ctx.SetStatus(v.Message, !v.Ok);
        }

        void RunSfc(RepairTask task)
        {
            var all = new StringBuilder();
            int exit = 0;
            run.Start(task.Title, (log, ct) =>
            {
                exit = ProcessRunner.Stream(RepairTools.SfcPath, RepairTools.SfcArgs, line => { lock (all) all.AppendLine(line); if (line.Trim().Length > 0) log(line); }, ct, RepairTools.SfcEncoding);
                if (exit == -1) return "Stopped.";
                string text; lock (all) text = all.ToString();
                return RepairOutput.SummarizeSfc(text, exit).Message;
            }, summary =>
            {
                if (exit == -1) { ToolsUi.ShowNote(resultHost, "Stopped. The check was not finished.", "Info"); return; }
                string text; lock (all) text = all.ToString();
                Show(RepairOutput.SummarizeSfc(text, exit));
            });
        }

        void RunDism(RepairTask task, string args, bool repair)
        {
            int exit = 0;
            run.Start(task.Title, (log, ct) =>
            {
                exit = ProcessRunner.Stream(RepairTools.DismPath, args, line => { if (line.Trim().Length > 0) log(line); }, ct, ProcessRunner.Oem);
                return RepairOutput.SummarizeDism(exit, repair).Message;
            }, summary => Show(RepairOutput.SummarizeDism(exit, repair)));
        }

        void RunResetWindowsUpdate(RepairTask task)
        {
            int exit = 0;
            string script = RepairTools.ResetWindowsUpdateScript(DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            run.Start(task.Title, (log, ct) =>
            {
                // Not cancellable: killing the script half-way would leave the update services stopped.
                log("This reset cannot be stopped half-way, so the update services always come back. It takes a few seconds.");
                exit = ProcessRunner.Stream(ProcessRunner.PowerShell, ProcessRunner.PowerShellArgs(script), line => { if (line.Trim().Length > 0) log(line); }, CancellationToken.None, Encoding.UTF8);
                if (exit == 0) return "Windows Update was reset. Check for updates again.";
                if (exit == 2) return "Some Windows services could not be started. See the lines above, then start them from Services.";
                return "Some parts could not be reset. See the lines above.";
            }, summary =>
            {
                ToolsUi.ShowNote(resultHost, summary + (exit == 0 ? " The old folders are kept next to the new ones with the name ending in .old-<date>; delete them later if you like." : ""), exit == 0 ? "Good" : "Warn");
                Ctx.SetStatus(summary, exit != 0);
            }, false);
        }

        void RunResetNetwork(RepairTask task)
        {
            bool failed = false; bool stopped = false;
            run.Start(task.Title, (log, ct) =>
            {
                foreach (var step in RepairTools.NetworkResetSteps())
                {
                    if (ct.IsCancellationRequested) { stopped = true; break; }
                    log("== " + step[2] + " ==");
                    int code = ProcessRunner.Stream(step[0], step[1], line => { if (line.Trim().Length > 0) log(line); }, ct, ProcessRunner.Oem);
                    if (code == -1) { stopped = true; break; }
                    if (code != 0) { failed = true; log("That step reported a problem (code " + code + ")."); }
                }
                if (stopped) return "Stopped before every step finished.";
                return failed ? "Finished, with problems. Restart the PC to finish whatever worked." : "Finished. Restart your PC to complete the reset.";
            }, summary =>
            {
                ToolsUi.ShowNote(resultHost, summary + " Tweak Bench does not restart it for you.", failed || stopped ? "Warn" : "Info");
                Ctx.SetStatus(summary, failed);
            });
        }

        void RunFlushDns(RepairTask task)
        {
            int exit = 0;
            run.Start(task.Title, (log, ct) =>
            {
                exit = ProcessRunner.Stream(RepairTools.FlushDnsPath, RepairTools.FlushDnsArgs, line => { if (line.Trim().Length > 0) log(line); }, ct, ProcessRunner.Oem);
                return exit == 0 ? "The DNS cache is empty." : "Windows did not flush the cache (code " + exit + "). Try again as administrator.";
            }, summary =>
            {
                ToolsUi.ShowNote(resultHost, summary, exit == 0 ? "Good" : "Warn");
                Ctx.SetStatus(summary, exit != 0);
            });
        }

        void RunClearTemp(RepairTask task)
        {
            var folders = RepairTools.TempFolders();
            DateTime now = DateTime.UtcNow;
            TempCleanResult found = null;
            bool stopped = false;
            if (folders.Count == 0) { ToolsUi.ShowNote(resultHost, "Tweak Bench found no standard Temp folder it can safely clear on this PC.", "Info"); return; }
            run.Start("Checking temporary files", (log, ct) =>
            {
                foreach (string f in folders) log("Looking in " + f + " (files older than " + (RepairTools.IsWindowsTemp(f) ? "7 days" : "24 hours") + ")");
                found = RepairTools.CleanTempFolders(folders, now, false, ct);
                stopped = ct.IsCancellationRequested;
                return stopped ? "Stopped. Nothing was deleted." : "Found " + RepairTools.DescribeTemp(found, false);
            }, summary =>
            {
                if (stopped) { ToolsUi.ShowNote(resultHost, "Stopped. Nothing was deleted.", "Info"); return; }
                if (found == null || found.Files == 0) { ToolsUi.ShowNote(resultHost, "Nothing to clear: there are no temporary files old enough (24 hours in your Temp folder, 7 days in C:\\Windows\\Temp).", "Info"); return; }
                string message = "Clear " + Cmd.Plural(found.Files, "temporary file", "temporary files") + " (" + Cmd.FormatSize(found.Bytes) + ") older than 24 hours (7 days in C:\\Windows\\Temp)?\n\n"
                    + ToolsUi.BulletList(folders) + "\nFiles in use are skipped. Newer files and everything outside these folders stay untouched.";
                if (!Ui.Confirm(Ctx.Window, message, task.Title)) { ToolsUi.ShowNote(resultHost, "Nothing was deleted.", "Info"); return; }
                TempCleanResult cleaned = null;
                run.Start(task.Title, (log, ct) =>
                {
                    cleaned = RepairTools.CleanTempFolders(folders, DateTime.UtcNow, true, ct);
                    return ct.IsCancellationRequested ? "Stopped. " + RepairTools.DescribeTemp(cleaned, true) : RepairTools.DescribeTemp(cleaned, true);
                }, result =>
                {
                    ToolsUi.ShowNote(resultHost, result, "Good");
                    Ctx.SetStatus(result);
                });
            });
        }

        void RunRestorePoint(RepairTask task)
        {
            bool ok = false;
            run.Start(task.Title, (log, ct) =>
            {
                log("Asking Windows to create a restore point. This can take a minute...");
                var r = RestorePoint.Create(Ctx.System, "Tweak Bench");
                ok = r.Ok;
                log(r.Message ?? "");
                return ok ? "Restore point created." : (string.IsNullOrWhiteSpace(r.Message) ? "Windows did not create the restore point." : r.Message);
            }, summary =>
            {
                ToolsUi.ShowNote(resultHost, summary, ok ? "Good" : "Warn");
                Ctx.SetStatus(summary, !ok);
            });
        }

        void RunRestartExplorer(RepairTask task)
        {
            bool ok = false;
            run.Start(task.Title, (log, ct) =>
            {
                string message;
                ok = ExplorerShell.Restart(out message);
                log(message ?? "");
                return ok ? "File Explorer restarted." : (string.IsNullOrWhiteSpace(message) ? "File Explorer could not be restarted." : message);
            }, summary =>
            {
                ToolsUi.ShowNote(resultHost, summary, ok ? "Good" : "Warn");
                Ctx.SetStatus(summary, !ok);
            });
        }
    }
}
