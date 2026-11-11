using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>
    /// "Review &amp; apply": lists exactly what will change (each item can be dropped), optionally creates a restore point first, applies
    /// on a background thread with progress, then shows what was done and what still needs a restart. Nothing here restarts the PC.
    /// </summary>
    public sealed class ReviewDialog
    {
        sealed class Row
        {
            public PlanItem Item;
            public CheckBox Check;
            public string Blocked;     // why the item cannot be applied here, or null
        }

        public readonly Window Window;
        readonly UiContext ctx;
        readonly TweakModel model;
        readonly List<Row> rows = new List<Row>();
        readonly ContentControl body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        readonly TextBlock heading = Ui.Heading("Review changes", "H1");
        readonly TextBlock sub = Ui.Para("");
        readonly StackPanel footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        FrameworkElement reviewView;
        CheckBox restoreBox;
        Button applyButton;
        bool applying;

        /// <summary>The result of the batch once it ran, otherwise null.</summary>
        public BatchResult Result { get; private set; }

        public ReviewDialog(UiContext ctx, TweakModel model, IList<PlanItem> plan)
        {
            this.ctx = ctx; this.model = model;
            Window = DialogHelpers.Create(ctx, "Review changes", 780, 700, true);
            Window.MinWidth = Math.Min(640, Window.Width); Window.MinHeight = Math.Min(480, Window.Height);
            foreach (PlanItem p in plan)
            {
                var row = new Row { Item = p };
                TweakStatus st = model.StatusOf(p.Tweak);
                if (st != null && st.State == TweakState.Unavailable) row.Blocked = "Not available on this PC: " + st.Reason;
                else if (p.Tweak.NeedsAdmin && !ctx.IsElevated) row.Blocked = "Needs administrator rights, so it will be skipped.";
                else if (model.WrongUserReason(p.Tweak) != null) row.Blocked = "Skipped: Tweak Bench is running as a different Windows account than the one you are signed in with.";
                else if (st != null && ((p.TurnOn && st.State == TweakState.On) || (!p.TurnOn && st.State == TweakState.Off))) row.Blocked = "Already " + (p.TurnOn ? "on" : "off") + ".";
                rows.Add(row);
            }

            var root = new Grid { Margin = new Thickness(26, 22, 26, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            head.Children.Add(heading);
            sub.Margin = new Thickness(0, 4, 0, 0);
            head.Children.Add(sub);
            root.Children.Add(head);
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            footer.Margin = new Thickness(0, 16, 0, 0);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Window.Content = root;

            Window.Closing += (s, e) =>
            {
                if (applying) { e.Cancel = true; Ui.Info(Window, "Tweak Bench is still applying changes. Closing now could leave one half-finished. Wait until it is done, then close this window."); }
            };
            ShowReview();
        }

        /// <summary>Shows the dialog and returns when it is closed.</summary>
        public void ShowDialog()
        {
            Window.ShowDialog();
        }

        // ------------------------------------------------------------------ step 1: review

        static string EffectWords(Takes t)
        {
            switch (t)
            {
                case Takes.ExplorerRestart: return "after restarting File Explorer";
                case Takes.SignOut: return "after you sign out and back in";
                case Takes.PcRestart: return "after you restart your PC";
                default: return "immediately";
            }
        }

        void ShowReview()
        {
            heading.Text = "Review changes";
            sub.Text = "Nothing has changed yet. Untick anything you do not want, then choose Apply.";
            if (reviewView == null) reviewView = BuildReview();
            body.Content = reviewView;
            footer.Children.Clear();
            Button cancel = Ui.Button("Cancel", () => Window.Close(), "Ghost");
            cancel.MinWidth = 96;
            cancel.IsCancel = true;
            applyButton = Ui.Button("Apply", () => StartApply(false), "Primary");
            applyButton.MinWidth = 150;
            applyButton.Margin = new Thickness(10, 0, 0, 0);
            footer.Children.Add(cancel);
            footer.Children.Add(applyButton);
            UpdateApplyButton();
        }

        FrameworkElement BuildReview()
        {
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            scroll.Content = stack;
            layout.Children.Add(scroll);

            int blocked = rows.Count(r => r.Blocked != null && r.Blocked.StartsWith("Needs administrator", StringComparison.Ordinal));
            if (blocked > 0)
            {
                Border n = Ui.Note(blocked + (blocked == 1 ? " change needs" : " changes need") + " administrator rights, so Tweak Bench left " + (blocked == 1 ? "it" : "them") + " unticked. Close this window and choose Run as administrator to include " + (blocked == 1 ? "it." : "them."), "Warn");
                n.Margin = new Thickness(0, 0, 0, 10);
                stack.Children.Add(n);
            }

            foreach (Row r in rows)
            {
                Row row = r;
                Tweak t = row.Item.Tweak;
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                row.Check = new CheckBox { IsChecked = row.Blocked == null, IsEnabled = row.Blocked == null, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
                row.Check.Click += (s, e) => UpdateApplyButton();
                System.Windows.Automation.AutomationProperties.SetName(row.Check, t.Title + (row.Item.TurnOn ? ", turn on" : ", turn off"));
                grid.Children.Add(row.Check);

                var text = new StackPanel();
                Grid.SetColumn(text, 1);
                var titleRow = new WrapPanel { Orientation = Orientation.Horizontal };
                Border action = ActionPill(row.Item.TurnOn ? "Turn on" : "Turn off", row.Item.TurnOn && row.Blocked == null);
                action.Margin = new Thickness(0, 0, 10, 0);
                titleRow.Children.Add(action);
                var title = new TextBlock { Text = t.Title, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                Ui.Ref(title, TextBlock.ForegroundProperty, row.Blocked == null ? "Text" : "Muted");
                titleRow.Children.Add(title);
                text.Children.Add(titleRow);
                var summary = Ui.Para(t.Summary, "Muted", 12.5);
                summary.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(summary);

                // Only say something extra when there is something to say.
                var meta = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                if (t.Risk == Risk.Moderate) meta.Children.Add(Chip("Moderate", "Warn", "WarnBg"));
                if (t.Risk == Risk.Advanced) meta.Children.Add(Chip("Advanced", "Bad", "BadBg"));
                if (!row.Item.TurnOn)
                {
                    // Only promise exact values when History holds them; otherwise turning off sets the Windows default.
                    TweakStatus st = model.StatusOf(t);
                    if (st != null && st.HasUndo) meta.Children.Add(Chip("Restores the values from before Tweak Bench turned it on", "Good", "GoodBg"));
                    else meta.Children.Add(Chip("Sets the Windows default", "Warn", "WarnBg"));
                }
                if (t.TakesEffect != Takes.Immediately)
                {
                    var effect = Ui.Text("Takes effect " + EffectWords(t.TakesEffect), "Muted", false, 12);
                    effect.VerticalAlignment = VerticalAlignment.Center;
                    meta.Children.Add(effect);
                }
                if (meta.Children.Count > 0) text.Children.Add(meta);
                string because = model.BecauseOf(t);
                if (because != null && row.Blocked == null)
                {
                    var auto = Ui.Para("Switched off automatically because you chose: " + because + ". Only one of these can be on.", "Muted", 12);
                    auto.Margin = new Thickness(0, 4, 0, 0);
                    text.Children.Add(auto);
                }
                if (row.Blocked != null)
                {
                    var why = Ui.Para(row.Blocked, "Warn", 12);
                    why.Margin = new Thickness(0, 4, 0, 0);
                    text.Children.Add(why);
                }
                grid.Children.Add(text);

                var card = Ui.Card(grid, new Thickness(14, 10, 14, 11));
                card.Margin = new Thickness(0, 0, 0, 8);
                stack.Children.Add(card);
            }

            // Pinned below the list: what happens afterwards, and the restore point choice.
            var bottom = new StackPanel { Margin = new Thickness(0, 6, 8, 0) };
            Grid.SetRow(bottom, 1);
            layout.Children.Add(bottom);
            int explorer = rows.Count(r => r.Blocked == null && r.Item.Tweak.TakesEffect == Takes.ExplorerRestart);
            int signOut = rows.Count(r => r.Blocked == null && r.Item.Tweak.TakesEffect == Takes.SignOut);
            int pc = rows.Count(r => r.Blocked == null && r.Item.Tweak.TakesEffect == Takes.PcRestart);
            var after = new List<string>();
            if (explorer > 0) after.Add(explorer + " need File Explorer to restart (a button appears when the changes are done)");
            if (signOut > 0) after.Add(signOut + " need you to sign out and back in");
            if (pc > 0) after.Add(pc + " need a PC restart (Tweak Bench never restarts it for you)");
            if (after.Count > 0)
            {
                Border n = Ui.Note("After applying: " + string.Join("; ", after) + ".", "Info");
                n.Margin = new Thickness(0, 2, 0, 4);
                bottom.Children.Add(n);
            }

            restoreBox = new CheckBox { Content = "Create a restore point first (recommended)", IsChecked = ctx.Settings.RestorePointFirst && ctx.IsElevated, IsEnabled = ctx.IsElevated, Margin = new Thickness(2, 10, 0, 0) };
            bottom.Children.Add(restoreBox);
            string restoreHelp = ctx.IsElevated
                ? "If something goes wrong you can go back to the restore point from Windows. Every change is also saved in History, so you can undo it one by one."
                : "Restore points need administrator rights. Every change is still saved in History, so you can undo it one by one.";
            var help = Ui.Para(restoreHelp, "Muted", 12);
            help.Margin = new Thickness(29, 3, 0, 0);
            bottom.Children.Add(help);
            return layout;
        }

        /// <summary>A small coloured label that says what a row does ("Turn on", "Turned off"...), shown next to the tweak title.</summary>
        internal static Border ActionPill(string text, bool strong)
        {
            Border b = strong ? Ui.Pill(text, "AccentFg", "Accent", 11) : Ui.Pill(text, "Text", "BtnBg", 11);
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        static Border Chip(string text, string fg, string bg)
        {
            Border b = Ui.Pill(text, fg, bg, 11);
            b.Margin = new Thickness(0, 0, 8, 0);
            return b;
        }

        void UpdateApplyButton()
        {
            int n = rows.Count(r => r.Check != null && r.Check.IsChecked == true);
            applyButton.Content = n == 0 ? "Nothing to apply" : n == 1 ? "Apply 1 change" : "Apply " + n + " changes";
            applyButton.IsEnabled = n > 0;
        }

        // ------------------------------------------------------------------ step 2: apply

        /// <summary>Applies the ticked items. <paramref name="synchronous"/> is for screenshot mode (no background thread).</summary>
        public void StartApply(bool synchronous)
        {
            if (applying) return;
            List<PlanItem> plan = rows.Where(r => r.Check != null && r.Check.IsChecked == true).Select(r => r.Item).ToList();
            if (plan.Count == 0) return;
            bool restore = restoreBox != null && restoreBox.IsChecked == true;
            string label = plan.Count == 1 ? plan[0].Tweak.Title : "Applied " + plan.Count + " changes";
            string restoreName = "Tweak Bench: " + (plan.Count == 1 ? plan[0].Tweak.Title : plan.Count + " changes");

            applying = true;
            heading.Text = "Applying changes…";
            sub.Text = "Please keep this window open until it finishes.";
            footer.Children.Clear();
            var status = Ui.Text("Starting…", "Text", false, 14);
            status.Margin = new Thickness(0, 24, 0, 10);
            var bar = new ProgressBar { Minimum = 0, Maximum = plan.Count, Value = 0, Height = 8 };
            body.Content = Ui.Card(Ui.Stack(0, status, bar), new Thickness(20));

            IDisposable busy = ctx.BeginBusy("Applying changes");
            BatchResult result = null;
            bool proceed = true;
            string restoreNote = null;

            Action<int, int, string> progress = (done, total, current) => ctx.OnUi(() =>
            {
                bar.Value = done;
                status.Text = current == null ? "Finishing…" : "Applying " + (done + 1) + " of " + total + ": " + current;
            });

            Action work = () =>
            {
                if (restore)
                {
                    ctx.OnUi(() => status.Text = "Creating a restore point… (this can take a minute)");
                    RestorePointResult rp = RestorePoint.Create(ctx.System, restoreName);
                    if (rp != null && rp.Ok) restoreNote = rp.Message;
                    else
                    {
                        string msg = rp == null ? "No reason was given." : rp.Message;
                        bool go = false;
                        ctx.Dispatcher.Invoke(new Action(() => go = Ui.Confirm(Window, "Tweak Bench could not create a restore point.\n\n" + msg + "\n\nApply the changes anyway? Each one is still saved in History, so you can undo it there.", "Restore point")));
                        if (!go) { proceed = false; return; }
                        restoreNote = "No restore point was created";
                    }
                }
                result = ctx.Engine.Apply(plan, label, progress);
                AutoProfile.Update(ctx.Engine, ctx.Profiles);
            };

            Action finish = () =>
            {
                busy.Dispose();
                applying = false;
                if (!proceed || result == null) { ShowReview(); return; }
                Result = result;
                model.Completed(result.Items);
                ctx.RefreshTweaks();
                ShowResults(result, restoreNote);
            };

            if (synchronous)
            {
                try { work(); } catch (Exception ex) { FailApply(busy, ex); return; }
                finish();
                return;
            }
            Task.Run(() =>
            {
                Exception error = null;
                try { work(); } catch (Exception ex) { error = ex; }
                ctx.OnUi(() => { if (error != null) FailApply(busy, error); else finish(); });
            });
        }

        void FailApply(IDisposable busy, Exception ex)
        {
            busy.Dispose();
            applying = false;
            ctx.RefreshTweaks();
            heading.Text = "Something went wrong";
            sub.Text = "Tweak Bench stopped before finishing. Check History to see what was changed; every change there can be undone.";
            body.Content = Ui.Note(ex.Message, "Bad");
            footer.Children.Clear();
            Button close = Ui.Button("Close", () => Window.Close(), "Primary");
            close.MinWidth = 96;
            close.IsCancel = true;
            footer.Children.Add(close);
        }

        // ------------------------------------------------------------------ step 3: results

        void ShowResults(BatchResult result, string restoreNote)
        {
            int ok = result.Succeeded, skipped = result.SkippedCount, failed = result.Failed;
            heading.Text = failed > 0 ? "Finished, with problems" : ok > 0 ? "Done" : "Nothing was changed";
            var parts = new List<string>();
            if (ok > 0) parts.Add(ok + (ok == 1 ? " change made" : " changes made"));
            if (skipped > 0) parts.Add(skipped + " skipped");
            if (failed > 0) parts.Add(failed + " failed");
            if (restoreNote != null) parts.Add(restoreNote.TrimEnd('.'));
            sub.Text = string.Join("  ·  ", parts) + ".";

            // What still needs doing comes first, pinned above the list of results.
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var followUps = new StackPanel { Margin = new Thickness(0, 0, 8, 6) };
            layout.Children.Add(followUps);
            if (!string.IsNullOrEmpty(result.HistoryError))
            {
                Border warn = Ui.Note("The changes were made, but Tweak Bench could not save them to its history file (" + result.HistoryError + "). Undo works until you close Tweak Bench; after that, turning a tweak off restores the Windows default.", "Warn");
                warn.Margin = new Thickness(0, 0, 0, 6);
                followUps.Children.Add(warn);
            }
            if (result.NeedsExplorerRestart)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Border note = Ui.Note("Some changes show up after File Explorer restarts. The taskbar and open folders flicker for a moment; your programs keep running.", "Info");
                note.Margin = new Thickness(0);
                row.Children.Add(note);
                var restart = Ui.Button("Restart File Explorer now", null, "Primary");
                restart.VerticalAlignment = VerticalAlignment.Center;
                restart.Margin = new Thickness(12, 0, 0, 0);
                restart.Click += (s, e) => RestartExplorer(restart);
                Grid.SetColumn(restart, 1);
                row.Children.Add(restart);
                followUps.Children.Add(row);
            }
            if (result.NeedsSignOut)
                followUps.Children.Add(Ui.Note("Some changes need you to sign out and back in before they show up.", "Info"));
            if (result.NeedsPcRestart)
                followUps.Children.Add(Ui.Note("Restart your PC when convenient — Tweak Bench never restarts it for you.", "Info"));
            if (failed > 0)
            {
                bool notPutBack = result.Batch != null && result.Batch.Entries.Any(e => !e.Ok && !e.RolledBack);
                if (notPutBack) followUps.Children.Add(Ui.Note("A failed change could not be fully put back. Open History to see what was changed and undo it.", "Bad"));
                else followUps.Children.Add(Ui.Note("A failed change was put back as it was. You can see and undo everything in History.", "Warn"));
            }

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            scroll.Content = stack;
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);

            foreach (ItemResult r in result.Items)
            {
                string glyph, key;
                if (r.Skipped && r.Ok) { glyph = Icons.Check; key = "Muted"; }
                else if (r.Skipped) { glyph = Icons.Dash; key = "Warn"; }
                else if (r.Ok) { glyph = Icons.Check; key = "Good"; }
                else { glyph = Icons.Cancel; key = "Bad"; }
                var grid = new Grid { Margin = new Thickness(2, 0, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = Ui.Glyph(glyph, 14, key);
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 3, 0, 0);
                grid.Children.Add(icon);
                var col = new StackPanel();
                Grid.SetColumn(col, 1);
                var headLine = new WrapPanel { Orientation = Orientation.Horizontal };
                bool done = r.Ok && !r.Skipped;
                string pillText = done ? (r.Item.TurnOn ? "Turned on" : "Turned off") : (r.Item.TurnOn ? "Turn on" : "Turn off");
                Border pill = ActionPill(pillText, done && r.Item.TurnOn);
                pill.Margin = new Thickness(0, 0, 10, 0);
                headLine.Children.Add(pill);
                headLine.Children.Add(new TextBlock { Text = r.Item.Tweak.Title, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                col.Children.Add(headLine);
                var msg = Ui.Para(r.Message ?? "", r.Ok || r.Skipped ? "Muted" : "Bad", 12.5);
                msg.Margin = new Thickness(0, 1, 0, 0);
                col.Children.Add(msg);
                grid.Children.Add(col);
                var card = Ui.Card(grid, new Thickness(14, 9, 14, 10));
                card.Margin = new Thickness(0, 0, 0, 8);
                stack.Children.Add(card);
            }

            body.Content = layout;
            footer.Children.Clear();
            Button history = Ui.Button("Open History", () => { Window.Close(); ctx.Navigate("history"); }, "Ghost");
            history.MinWidth = 120;
            Button close = Ui.Button("Close", () => Window.Close(), "Primary");
            close.MinWidth = 96;
            close.IsCancel = true;
            close.Margin = new Thickness(10, 0, 0, 0);
            footer.Children.Add(history);
            footer.Children.Add(close);
        }

        void RestartExplorer(Button button)
        {
            if (ctx.IsDemo) { Ui.Info(Window, "This is the demo, so File Explorer is not restarted."); return; }
            button.IsEnabled = false;
            button.Content = "Restarting…";
            string message = null;
            bool ok = false;
            ctx.RunInBackground("Restarting File Explorer", () => { ok = ExplorerShell.Restart(out message); }, () =>
            {
                button.IsEnabled = !ok;
                button.Content = ok ? "File Explorer restarted" : "Restart File Explorer now";
                ctx.SetStatus(ok ? "File Explorer restarted." : (message ?? "File Explorer could not be restarted."), !ok);
                if (!ok && message != null) Ui.Info(Window, message);
            }, ex => { button.IsEnabled = true; button.Content = "Restart File Explorer now"; ctx.SetStatus("File Explorer could not be restarted: " + ex.Message, true); });
        }
    }
}
