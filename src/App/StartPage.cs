using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>"Start here": how Tweak Bench works, the stop-automatic-restarts shortcut, the presets, a summary of this PC and quick actions.</summary>
    public sealed class StartPage : PageBase
    {
        public const string NoRestartId = "update.no-auto-restart";

        readonly TweakModel model;
        readonly Action openReview;

        // Highlighted card
        Tweak noRestart;
        Border highlightPill;
        TextBlock highlightPillText;
        TextBlock highlightNote;
        Button highlightButton;

        // Presets
        sealed class PresetView { public Preset Preset; public TextBlock Count; public Button Review; }
        readonly List<PresetView> presetViews = new List<PresetView>();

        // This PC
        StackPanel pcRows;
        Button restorePointButton;
        Border wrongUserNote;
        TextBlock wrongUserText;

        public StartPage(UiContext ctx, TweakModel model, Action openReview) : base(ctx)
        {
            this.model = model;
            this.openReview = openReview;
        }

        public override string Id { get { return "home"; } }
        public override string Title { get { return "Start here"; } }
        public override string Subtitle { get { return "Windows settings in plain English: see what is on, choose, review, apply, and undo any time."; } }
        public override string Glyph { get { return Icons.Home; } }

        protected override FrameworkElement Build()
        {
            var root = new StackPanel();
            wrongUserNote = Ui.Note("", "Warn", Icons.Warning);
            wrongUserNote.Margin = new Thickness(0, 0, 0, 12);
            wrongUserNote.Visibility = Visibility.Collapsed;
            wrongUserText = (TextBlock)((Grid)wrongUserNote.Child).Children[1];
            root.Children.Add(wrongUserNote);
            root.Children.Add(BuildSteps());
            root.Children.Add(BuildHighlight());
            root.Children.Add(BuildPresets());
            root.Children.Add(BuildThisPc());
            root.Children.Add(BuildQuickActions());
            model.Changed += UpdateDynamic;
            UpdateDynamic();
            return root;
        }

        // ------------------------------------------------------------------ how it works

        FrameworkElement BuildSteps()
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            wrap.Children.Add(SectionTitle("How it works", null));
            var grid = new Grid();
            string[][] steps =
            {
                new[] { "1", "See what is set", "Every tweak shows whether it is on or off right now. Tweak Bench reads it from this PC, so nothing is a guess." },
                new[] { "2", "Flip the switches", "Choose what you want. Nothing changes yet: your choices wait in a bar at the bottom of the window." },
                new[] { "3", "Review and apply", "Check the exact list, apply it, and undo any change later from History. Tweak Bench never restarts your PC." },
            };
            for (int i = 0; i < steps.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var circle = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Left };
                Ui.Ref(circle, Border.BackgroundProperty, "Accent");
                var n = new TextBlock { Text = steps[i][0], FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Ui.Ref(n, TextBlock.ForegroundProperty, "AccentFg");
                circle.Child = n;
                var stack = new StackPanel();
                stack.Children.Add(circle);
                var title = new TextBlock { Text = steps[i][1], FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 3) };
                stack.Children.Add(title);
                stack.Children.Add(Ui.Para(steps[i][2], "Muted", 13));
                Border card = Ui.Card(stack, new Thickness(18, 16, 18, 18));
                card.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == steps.Length - 1 ? 0 : 6, 12);
                Grid.SetColumn(card, i);
                grid.Children.Add(card);
            }
            wrap.Children.Add(grid);
            return wrap;
        }

        static FrameworkElement SectionTitle(string title, string subtitle)
        {
            var p = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
            p.Children.Add(Ui.Heading(title, "H2"));
            if (!string.IsNullOrEmpty(subtitle)) { var s = Ui.Para(subtitle, "Muted", 12.5); s.Margin = new Thickness(0, 3, 0, 0); p.Children.Add(s); }
            return p;
        }

        // ------------------------------------------------------------------ the restart shortcut

        FrameworkElement BuildHighlight()
        {
            noRestart = Catalog.Find(NoRestartId);
            var wrap = new StackPanel();
            wrap.Children.Add(SectionTitle("The one most people come for", null));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Ui.Glyph(Icons.Refresh, 20, "AccentText");
            glyph.Margin = new Thickness(0, 0, 10, 0);
            titleRow.Children.Add(glyph);
            titleRow.Children.Add(new TextBlock { Text = noRestart != null ? noRestart.Title : "Stop automatic restarts for updates", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            left.Children.Add(titleRow);
            string summary = noRestart != null ? noRestart.Summary : "Windows will not restart your PC on its own to finish installing updates.";
            string why = noRestart != null ? noRestart.Why : "Updates still download and install. Your PC just waits for you to restart it.";
            var s = Ui.Para(summary, "Text", 14);
            s.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(s);
            var w = Ui.Para(why, "Muted", 12.5);
            w.Margin = new Thickness(0, 5, 0, 0);
            left.Children.Add(w);
            highlightNote = Ui.Para("", "Muted", 12.5);
            highlightNote.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(highlightNote);
            grid.Children.Add(left);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
            Grid.SetColumn(right, 1);
            highlightPill = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(11, 2, 11, 3), HorizontalAlignment = HorizontalAlignment.Right };
            highlightPillText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
            highlightPill.Child = highlightPillText;
            right.Children.Add(highlightPill);
            highlightButton = Ui.Button("Review & apply", OnHighlightClick, "Primary");
            highlightButton.Padding = new Thickness(18, 9, 18, 9);
            highlightButton.Margin = new Thickness(0, 12, 0, 0);
            right.Children.Add(highlightButton);
            grid.Children.Add(right);

            Border card = Ui.Card(grid, new Thickness(22, 18, 22, 20));
            Ui.Ref(card, Border.BorderBrushProperty, "Accent");
            Ui.Ref(card, Border.BackgroundProperty, "Selected");
            card.Margin = new Thickness(0, 0, 0, 14);
            wrap.Children.Add(card);
            return wrap;
        }

        void SetHighlightPill(string text, string fg, string bg)
        {
            highlightPillText.Text = text;
            Ui.Ref(highlightPillText, TextBlock.ForegroundProperty, fg);
            Ui.Ref(highlightPill, Border.BackgroundProperty, bg);
        }

        void OnHighlightClick()
        {
            if (noRestart == null) return;
            if (noRestart.NeedsAdmin && !Ctx.IsElevated) { Ctx.RunAsAdmin(); return; }
            model.Stage(noRestart, true);
            openReview();
        }

        void UpdateHighlight()
        {
            if (noRestart == null)
            {
                SetHighlightPill("Coming soon", "Muted", "BtnBg");
                highlightNote.Text = "This tweak is not in this version of Tweak Bench's list yet.";
                highlightButton.Visibility = Visibility.Collapsed;
                return;
            }
            TweakStatus st = model.StatusOf(noRestart);
            highlightButton.Visibility = Visibility.Visible;
            highlightButton.IsEnabled = true;
            highlightButton.Content = "Review & apply";
            if (!model.Ready || st == null)
            {
                SetHighlightPill("Checking…", "Muted", "BtnBg");
                highlightNote.Text = "Reading the current setting from this PC.";
                highlightButton.IsEnabled = false;
                return;
            }
            switch (st.State)
            {
                case TweakState.On:
                    SetHighlightPill("On", "Good", "GoodBg");
                    highlightNote.Text = "Already on. Windows will wait for you to restart after installing updates.";
                    highlightButton.Visibility = Visibility.Collapsed;
                    break;
                case TweakState.Unavailable:
                    SetHighlightPill("Not available here", "Muted", "BtnBg");
                    highlightNote.Text = st.Reason ?? "This does not apply to this PC.";
                    highlightButton.Visibility = Visibility.Collapsed;
                    break;
                default:
                    SetHighlightPill(st.State == TweakState.Partial ? "Partly on" : st.State == TweakState.Off ? "Off" : "Unknown", st.State == TweakState.Partial ? "Warn" : "Muted", st.State == TweakState.Partial ? "WarnBg" : "BtnBg");
                    if (noRestart.NeedsAdmin && !Ctx.IsElevated)
                    {
                        highlightNote.Text = "This changes a system-wide setting, so it needs administrator rights.";
                        highlightButton.Content = "Run as administrator";
                    }
                    else highlightNote.Text = model.StagedValue(noRestart) == true ? "Ready to apply. You will see exactly what changes first." : "You will see exactly what changes before anything happens.";
                    break;
            }
        }

        // ------------------------------------------------------------------ presets

        FrameworkElement BuildPresets()
        {
            var wrap = new StackPanel();
            wrap.Children.Add(SectionTitle("Start from a preset", "Each preset picks a set of tweaks for you. You always get to review the list before anything changes."));
            var grid = new UniformGrid { Columns = 2 };
            foreach (Preset p in Catalog.Presets)
            {
                var view = new PresetView { Preset = p };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = p.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
                var blurb = Ui.Para(p.Blurb, "Muted", 13);
                blurb.Margin = new Thickness(0, 3, 0, 0);
                stack.Children.Add(blurb);
                var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                view.Count = Ui.Text("", "Muted", true, 12.5);
                row.Children.Add(view.Count);
                string presetId = p.Id;
                view.Review = Ui.Button("Review", () => ReviewPreset(presetId), "Ghost");
                view.Review.Padding = new Thickness(16, 5, 16, 5);
                Grid.SetColumn(view.Review, 1);
                row.Children.Add(view.Review);
                stack.Children.Add(row);
                Border card = Ui.Card(stack, new Thickness(18, 15, 18, 16));
                card.Margin = new Thickness(0, 0, presetViews.Count % 2 == 0 ? 12 : 0, 12);
                grid.Children.Add(card);
                presetViews.Add(view);
            }
            wrap.Children.Add(grid);
            return wrap;
        }

        void ReviewPreset(string presetId)
        {
            int needAdmin;
            List<Tweak> targets = model.PresetTargets(presetId, out needAdmin);
            model.StageAllOn(targets);
            if (model.Count == 0)
            {
                Ui.Info(Ctx.Window, needAdmin > 0 ? needAdmin + " of this preset's changes need administrator rights. Use Run as administrator, then try again." : "Everything in this preset is already set on this PC.");
                return;
            }
            openReview();
        }

        void UpdatePresets()
        {
            foreach (PresetView v in presetViews)
            {
                if (!model.Ready) { v.Count.Text = "Checking…"; v.Review.IsEnabled = false; continue; }
                int needAdmin;
                int n = model.PresetTargets(v.Preset.Id, out needAdmin).Count;
                string text = n == 0 ? "Already set" : n + (n == 1 ? " change to make" : " changes to make");
                if (needAdmin > 0) text += "  ·  +" + needAdmin + " need administrator";
                v.Count.Text = text;
                Ui.Ref(v.Count, TextBlock.ForegroundProperty, n == 0 ? "Good" : "Text");
                v.Review.IsEnabled = n > 0;
            }
        }

        // ------------------------------------------------------------------ this PC

        FrameworkElement BuildThisPc()
        {
            var wrap = new StackPanel();
            wrap.Children.Add(SectionTitle("This PC", null));
            pcRows = new StackPanel();
            Border card = Ui.Card(pcRows, new Thickness(20, 12, 20, 12));
            card.Margin = new Thickness(0, 0, 0, 14);
            wrap.Children.Add(card);
            FillPc(null);
            return wrap;
        }

        void FillPc(SystemInfo info)
        {
            pcRows.Children.Clear();
            if (info == null)
            {
                pcRows.Children.Add(Ui.Para("Reading this PC…", "Muted"));
                return;
            }
            string restart = info.RestartPending ? "Yes" + (info.RestartPendingReasons != null && info.RestartPendingReasons.Count > 0 ? ": " + string.Join(", ", info.RestartPendingReasons) : "") : "No";
            string uptime = info.Uptime.TotalDays >= 1 ? (int)info.Uptime.TotalDays + (((int)info.Uptime.TotalDays) == 1 ? " day " : " days ") + info.Uptime.Hours + " h" : info.Uptime.Hours + " h " + info.Uptime.Minutes + " min";
            AddPcRow("Windows", (info.ProductName ?? "Windows") + (string.IsNullOrEmpty(info.DisplayVersion) ? "" : " " + info.DisplayVersion) + ", build " + info.Build + "." + info.Ubr, null);
            AddPcRow("Edition", info.Edition ?? "", null);
            AddPcRow("Running for", uptime + (info.LastBoot > DateTime.MinValue ? "  (since " + info.LastBoot.ToString("ddd d MMM, h:mm tt") + ")" : ""), null);
            if (info.ElevatedAsOtherUser || !string.IsNullOrEmpty(Ctx.Engine.WrongUserReason))
                AddPcRow("Administrator", "Yes, but as a different Windows account than the one you are signed in with. Settings for your own account are switched off.", "Warn");
            else
                AddPcRow("Administrator", info.IsElevated ? "Yes. Tweak Bench can change system-wide settings." : "No. Only your own settings can be changed.", info.IsElevated ? "Good" : "Warn");
            AddPcRow("Restart pending", restart, info.RestartPending ? "Warn" : null);
        }

        void AddPcRow(string label, string value, string valueKey)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Ui.Text(label, "Muted"));
            var v = Ui.Text(value, valueKey ?? "Text", false, 0, true);
            v.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            pcRows.Children.Add(g);
        }

        void ReadPc()
        {
            if (Ctx.IsDemo)
            {
                try { FillPc(SystemInfo.Read(Ctx.System)); } catch (Exception ex) { pcRows.Children.Clear(); pcRows.Children.Add(Ui.Para("Could not read this PC: " + ex.Message, "Bad")); }
                return;
            }
            SystemInfo info = null;
            string error = null;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { info = SystemInfo.Read(Ctx.System); } catch (Exception ex) { error = ex.Message; }
                Ctx.OnUi(() =>
                {
                    if (info != null) FillPc(info);
                    else { pcRows.Children.Clear(); pcRows.Children.Add(Ui.Para("Could not read this PC: " + error, "Bad")); }
                });
            });
        }

        // ------------------------------------------------------------------ quick actions

        FrameworkElement BuildQuickActions()
        {
            var wrap = new StackPanel();
            wrap.Children.Add(SectionTitle("Quick actions", null));
            var row = new WrapPanel();
            restorePointButton = Ui.IconButton(Icons.Restore, "Create a restore point", CreateRestorePoint, "Ghost", "Saves a Windows restore point you can go back to. Needs administrator rights.");
            restorePointButton.Margin = new Thickness(0, 0, 10, 8);
            Button explorer = Ui.IconButton(Icons.Refresh, "Restart File Explorer", RestartExplorer, "Ghost", "Reloads the taskbar and folder windows. Your programs keep running.");
            explorer.Margin = new Thickness(0, 0, 10, 8);
            Button history = Ui.IconButton(Icons.History, "Open History", () => Ctx.Navigate("history"), "Ghost", "See every change Tweak Bench made, and undo any of them.");
            history.Margin = new Thickness(0, 0, 0, 8);
            row.Children.Add(restorePointButton);
            row.Children.Add(explorer);
            row.Children.Add(history);
            wrap.Children.Add(row);
            return wrap;
        }

        void CreateRestorePoint()
        {
            if (Ctx.IsDemo) { Ui.Info(Ctx.Window, "This is the demo, so no restore point is created."); return; }
            if (!Ctx.IsElevated) { Ui.Info(Ctx.Window, "Restore points need administrator rights. Use Run as administrator first."); return; }
            RestorePointResult result = null;
            restorePointButton.IsEnabled = false;
            Ctx.RunInBackground("Creating a restore point", () => { result = RestorePoint.Create(Ctx.System, "Tweak Bench: manual restore point"); }, () =>
            {
                restorePointButton.IsEnabled = true;
                bool ok = result != null && result.Ok;
                Ctx.SetStatus(ok ? "Restore point created." : "The restore point could not be created: " + (result == null ? "unknown reason" : result.Message), !ok);
                if (!ok) Ui.Info(Ctx.Window, "The restore point could not be created.\n\n" + (result == null ? "" : result.Message));
            }, ex => { restorePointButton.IsEnabled = true; Ctx.SetStatus("The restore point could not be created: " + ex.Message, true); });
        }

        void RestartExplorer()
        {
            if (Ctx.IsDemo) { Ui.Info(Ctx.Window, "This is the demo, so File Explorer is not restarted."); return; }
            if (!Ui.Confirm(Ctx.Window, "Restart File Explorer now?\n\nThe taskbar and open folder windows disappear for a moment and come back. Your programs keep running.")) return;
            string message = null;
            bool ok = false;
            Ctx.RunInBackground("Restarting File Explorer", () => { ok = ExplorerShell.Restart(out message); }, () => Ctx.SetStatus(ok ? "File Explorer restarted." : (message ?? "File Explorer could not be restarted."), !ok),
                ex => Ctx.SetStatus("File Explorer could not be restarted: " + ex.Message, true));
        }

        // ------------------------------------------------------------------ lifecycle

        void UpdateDynamic()
        {
            if (pcRows == null) return;
            string wrong = Ctx.Engine.WrongUserReason;
            wrongUserNote.Visibility = string.IsNullOrEmpty(wrong) ? Visibility.Collapsed : Visibility.Visible;
            wrongUserText.Text = wrong ?? "";
            UpdateHighlight();
            UpdatePresets();
            restorePointButton.IsEnabled = Ctx.IsElevated;
        }

        public override void Shown() { UpdateDynamic(); ReadPc(); }
        public override void TweaksChanged() { UpdateDynamic(); }
    }
}
