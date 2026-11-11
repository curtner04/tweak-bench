using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>
    /// History of every change (newest first) with exact undo for one change or a whole batch, plus saved profiles
    /// (a remembered set of tweaks that can be reviewed and applied here or on another PC).
    /// </summary>
    public sealed class HistoryPage : PageBase
    {
        const int PageSize = 20;
        readonly TweakModel model;
        readonly Action openReview;
        readonly ContentControl host = new ContentControl();
        RadioButton tabChanges, tabProfiles;
        bool showProfiles;
        int shown = PageSize;

        public HistoryPage(UiContext ctx, TweakModel model, Action openReview) : base(ctx)
        {
            this.model = model;
            this.openReview = openReview;
        }

        public override string Id { get { return "history"; } }
        public override string Title { get { return "History"; } }
        public override string Subtitle { get { return "Everything Tweak Bench changed, with the exact previous values saved so each change can be undone."; } }
        public override string Glyph { get { return Icons.History; } }

        protected override FrameworkElement Build()
        {
            var root = new StackPanel();
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            tabChanges = new RadioButton { Content = "Changes", GroupName = "historyTabs", IsChecked = true };
            tabProfiles = new RadioButton { Content = "Profiles", GroupName = "historyTabs" };
            Ui.Style(tabChanges, "TabBtn"); Ui.Style(tabProfiles, "TabBtn");
            tabChanges.Checked += (s, e) => { showProfiles = false; Rebuild(); };
            tabProfiles.Checked += (s, e) => { showProfiles = true; Rebuild(); };
            tabs.Children.Add(tabChanges);
            tabs.Children.Add(tabProfiles);
            root.Children.Add(tabs);
            root.Children.Add(host);
            Rebuild();
            return root;
        }

        public override void Shown() { Rebuild(); }
        public override void TweaksChanged()
        {
            // Rebuilding would close the "Details" a reader opened, so only do it when the history itself changed.
            if (Ctx.History != null && host.Content != null && Signature() != signature) Rebuild();
        }

        string signature = "";

        string Signature()
        {
            int undone = 0, entries = 0;
            foreach (HistoryBatch b in Ctx.History.Batches) foreach (HistoryEntry e in b.Entries) { entries++; if (e.UndoneUtc != null) undone++; }
            return Ctx.History.Batches.Count + ":" + entries + ":" + undone + ":" + Ctx.IsElevated;
        }

        void Rebuild()
        {
            if (tabChanges == null) return;
            signature = Signature();
            host.Content = showProfiles ? BuildProfiles() : BuildChanges();
        }

        // ------------------------------------------------------------------ formatting

        static string Friendly(DateTime t)
        {
            DateTime today = DateTime.Today;
            string time = t.ToString("h:mm tt", CultureInfo.CurrentCulture);
            if (t.Date == today) return "Today, " + time;
            if (t.Date == today.AddDays(-1)) return "Yesterday, " + time;
            return t.ToString(t.Year == today.Year ? "ddd d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture) + ", " + time;
        }

        // ------------------------------------------------------------------ changes

        FrameworkElement BuildChanges()
        {
            var stack = new StackPanel();
            if (Ctx.History.LoadWarning != null) stack.Children.Add(Ui.Note(Ctx.History.LoadWarning, "Warn"));
            List<HistoryBatch> batches = Ctx.History.Batches.Where(b => b.Entries != null && b.Entries.Count > 0).Reverse().ToList();
            if (batches.Count == 0)
            {
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 24) };
                var g = Ui.Glyph(Icons.History, 30, "Muted");
                g.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(g);
                var t = Ui.Text("No changes yet", "Text", true, 15);
                t.Margin = new Thickness(0, 10, 0, 4);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(t);
                var p = Ui.Para("When you apply tweaks, each change is listed here with the values it replaced. You can undo any of them.", "Muted");
                p.TextAlignment = TextAlignment.Center;
                p.MaxWidth = 460;
                empty.Children.Add(p);
                stack.Children.Add(Ui.Card(empty));
                return stack;
            }
            foreach (HistoryBatch b in batches.Take(shown)) stack.Children.Add(BuildBatch(b));
            if (batches.Count > shown)
            {
                var more = Ui.Button("Show older changes (" + (batches.Count - shown) + " more)", () => { shown += PageSize; Rebuild(); }, "Ghost");
                more.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(more);
            }
            return stack;
        }

        FrameworkElement BuildBatch(HistoryBatch batch)
        {
            var stack = new StackPanel();
            bool anyUndo = batch.Entries.Any(e => e.CanUndo && Ctx.Engine.CannotUndo(batch, e) == null);

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = batch.Label ?? "Changes", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            int ok = batch.Entries.Count(e => e.Ok), bad = batch.Entries.Count - ok;
            string meta = Friendly(batch.Time) + "  ·  " + batch.Entries.Count + (batch.Entries.Count == 1 ? " change" : " changes") + (bad > 0 ? ", " + bad + " failed" : "");
            if (!Ctx.Engine.IsThisPc(batch)) meta += "  ·  made on " + batch.Machine;
            var m = Ui.Para(meta, "Muted", 12.5);
            m.Margin = new Thickness(0, 2, 0, 0);
            titles.Children.Add(m);
            head.Children.Add(titles);
            if (anyUndo && batch.Entries.Count > 1)
            {
                var undoAll = Ui.Button("Undo this batch", () => UndoBatch(batch), "Ghost", "Undo every change in this batch that can still be undone, newest first.");
                undoAll.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(undoAll, 1);
                head.Children.Add(undoAll);
            }
            stack.Children.Add(head);

            var rule = new System.Windows.Shapes.Rectangle { Height = 1, Margin = new Thickness(0, 12, 0, 4) };
            Ui.Ref(rule, System.Windows.Shapes.Shape.FillProperty, "Border");
            stack.Children.Add(rule);
            foreach (HistoryEntry e in batch.Entries) stack.Children.Add(BuildEntry(batch, e));
            return Ui.Card(stack, new Thickness(18, 15, 18, 10));
        }

        FrameworkElement BuildEntry(HistoryBatch batch, HistoryEntry entry)
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = Ui.Glyph(entry.Ok ? Icons.Check : Icons.Cancel, 14, entry.Ok ? "Good" : "Bad");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 3, 0, 0);
            grid.Children.Add(icon);

            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            // The action is a small label next to the title, so a title such as "Turn off the advertising ID" never reads "Turned on: Turn off...".
            string action = entry.Action == "on" ? (entry.Ok ? "Turned on" : "Turn on") : entry.Action == "off" ? (entry.Ok ? "Turned off" : "Turn off") : entry.Action == "undo" ? "Undone" : entry.Action;
            var titleRow = new WrapPanel { Orientation = Orientation.Horizontal };
            Border pill = ReviewDialog.ActionPill(action, entry.Ok && entry.Action == "on");
            pill.Margin = new Thickness(0, 0, 10, 0);
            titleRow.Children.Add(pill);
            titleRow.Children.Add(new TextBlock { Text = entry.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            text.Children.Add(titleRow);
            string line;
            if (!entry.Ok) line = "Failed: " + (entry.Error ?? "unknown error") + (entry.RolledBack ? " Everything was put back as it was." : "");
            else line = entry.UndoneUtc != null ? "Later undone" : "";
            var sub = Ui.Para(line, entry.Ok ? "Muted" : "Bad", 12.5);
            sub.VerticalAlignment = VerticalAlignment.Center;
            if (line.Length == 0) sub.Visibility = Visibility.Collapsed;
            var subRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
            subRow.Children.Add(sub);
            text.Children.Add(subRow);
            grid.Children.Add(text);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(right, 2);
            if (entry.CanUndo)
            {
                string why = Ctx.Engine.CannotUndo(batch, entry);
                if (why == null)
                {
                    right.Children.Add(Ui.Button("Undo", () => UndoEntry(batch, entry), "Small", "Put back the values that were there before this change."));
                }
                else
                {
                    var reason = Ui.Text("Cannot undo here", "Muted", false, 12);
                    reason.ToolTip = why;
                    right.Children.Add(reason);
                }
            }
            grid.Children.Add(right);
            wrap.Children.Add(grid);

            if (entry.Before != null && entry.Before.Count > 0)
            {
                var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(28, 6, 0, 2) };
                foreach (Snapshot snap in entry.Before)
                {
                    var box = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                    var what = new TextBlock { Text = snap.What ?? snap.Kind, FontFamily = Ui.MonoFont, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                    Ui.Ref(what, TextBlock.ForegroundProperty, "Text");
                    box.Children.Add(what);
                    var was = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
                    Ui.Ref(was, TextBlock.ForegroundProperty, "Muted");
                    was.Inlines.Add(new System.Windows.Documents.Run("Was: ") { FontWeight = FontWeights.SemiBold });
                    was.Inlines.Add(new System.Windows.Documents.Run(snap.Was ?? "unknown"));
                    box.Children.Add(was);
                    details.Children.Add(box);
                }
                Button toggle = null;
                toggle = Ui.Button("Details ▸", () =>
                {
                    bool open = details.Visibility != Visibility.Visible;
                    details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    toggle.Content = open ? "Details ▾" : "Details ▸";
                }, "Link");
                toggle.FontSize = 12;
                toggle.Margin = new Thickness(8, 0, 0, 0);
                subRow.Children.Add(toggle);
                wrap.Children.Add(details);
            }
            return wrap;
        }

        // ------------------------------------------------------------------ undo

        void UndoEntry(HistoryBatch batch, HistoryEntry entry)
        {
            Tweak t = Catalog.Find(entry.TweakId);
            ItemResult result = null;
            Ctx.RunInBackground("Undoing a change", () =>
            {
                result = Ctx.Engine.Undo(batch, entry);
                AutoProfile.Update(Ctx.Engine, Ctx.Profiles);
            }, () =>
            {
                string follow = result != null && result.Ok && t != null ? FollowUp(t.TakesEffect) : "";
                if (result != null && result.Ok && !result.Skipped) model.MarkUpdating(new[] { entry.TweakId });
                if (result != null && !string.IsNullOrEmpty(result.HistoryError))
                    Ctx.SetStatus((result.Ok ? "Undone, but " : "") + "the change history could not be saved: " + result.HistoryError, true);
                else
                    Ctx.SetStatus(result == null ? "Nothing was undone." : (result.Ok ? "Undone: " + entry.Title + "." + follow : "Could not undo " + entry.Title + ": " + result.Message), result != null && !result.Ok && !result.Skipped);
                Ctx.RefreshTweaks();
                Rebuild();
            });
        }

        void UndoBatch(HistoryBatch batch)
        {
            int n = batch.Entries.Count(e => e.CanUndo && Ctx.Engine.CannotUndo(batch, e) == null);
            if (!Ui.Confirm(Ctx.Window, "Undo " + n + (n == 1 ? " change" : " changes") + " from \"" + batch.Label + "\"?\n\nEach one goes back to the value it had before."))
                return;
            List<ItemResult> results = null;
            Ctx.RunInBackground("Undoing a batch", () =>
            {
                results = Ctx.Engine.UndoBatch(batch);
                AutoProfile.Update(Ctx.Engine, Ctx.Profiles);
            }, () =>
            {
                int done = results.Count(r => r.Ok && !r.Skipped), failed = results.Count(r => !r.Ok && !r.Skipped);
                bool explorer = results.Any(r => r.Ok && !r.Skipped && r.Item != null && r.Item.Tweak != null && r.Item.Tweak.TakesEffect == Takes.ExplorerRestart);
                model.MarkUpdating(results.Where(r => r.Ok && !r.Skipped && r.Item != null && r.Item.Tweak != null).Select(r => r.Item.Tweak.Id));
                string historyError = results.Select(r => r.HistoryError).FirstOrDefault(e => !string.IsNullOrEmpty(e));
                Ctx.SetStatus("Undone: " + done + (done == 1 ? " change" : " changes") + (failed > 0 ? ", " + failed + " failed (see the list)" : "") + "." + (explorer ? " Restart File Explorer to see the taskbar and Explorer changes." : "") + (historyError != null ? " The change history could not be saved: " + historyError : ""), failed > 0 || historyError != null);
                Ctx.RefreshTweaks();
                Rebuild();
            });
        }

        static string FollowUp(Takes takes)
        {
            switch (takes)
            {
                case Takes.ExplorerRestart: return " Restart File Explorer to see it.";
                case Takes.SignOut: return " Sign out and back in to see it.";
                case Takes.PcRestart: return " It takes effect after you restart your PC.";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ profiles

        FrameworkElement BuildProfiles()
        {
            var stack = new StackPanel();

            var intro = new Grid();
            intro.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            intro.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            text.Children.Add(Ui.Heading("Profiles"));
            var p = Ui.Para("A profile remembers which tweaks are on right now. Apply it later, or on another PC, to get the same setup. You always review the changes first.");
            p.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(p);
            intro.Children.Add(text);
            var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(buttons, 1);
            var save = Ui.IconButton(Icons.Save, "Save my current setup as a profile…", SaveCurrent, "Primary");
            var import = Ui.IconButton(Icons.Folder, "Import profile…", ImportProfile, "Ghost");
            import.Margin = new Thickness(0, 8, 0, 0);
            buttons.Children.Add(save);
            buttons.Children.Add(import);
            intro.Children.Add(buttons);
            stack.Children.Add(Ui.Card(intro, new Thickness(20, 16, 20, 18)));

            List<Profile> profiles = Ctx.Profiles.List();
            if (profiles.Count == 0)
            {
                stack.Children.Add(Ui.Para("No profiles yet. Save your current setup to create the first one.", "Muted"));
                return stack;
            }
            foreach (Profile profile in profiles) stack.Children.Add(BuildProfile(profile));
            return stack;
        }

        FrameworkElement BuildProfile(Profile profile)
        {
            bool automatic = profile.Name == AutoProfile.Name;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = profile.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            int on = profile.Tweaks.Count(t => t.On), off = profile.Tweaks.Count - on;
            DateTime created;
            string when = DateTime.TryParse(profile.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created) ? "  ·  saved " + Friendly(created.ToLocalTime()) : "";
            string meta = on + (on == 1 ? " tweak on" : " tweaks on") + (off > 0 ? ", " + off + " off" : "") + when + (string.IsNullOrEmpty(profile.Machine) ? "" : "  ·  from " + profile.Machine);
            var m = Ui.Para(meta, "Muted", 12.5);
            m.Margin = new Thickness(0, 2, 0, 0);
            info.Children.Add(m);
            if (automatic)
            {
                var a = Ui.Para("Kept up to date automatically from the tweaks you turn on. After moving to a new PC, review and apply it to get your setup back.", "Muted", 12.5);
                a.Margin = new Thickness(0, 3, 0, 0);
                info.Children.Add(a);
            }
            grid.Children.Add(info);

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(row, 1);
            Button apply = Ui.Button("Review & apply", () => ApplyProfile(profile), "Primary");
            Button export = Ui.Button("Export…", () => ExportProfile(profile), "Ghost");
            export.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(apply);
            row.Children.Add(export);
            if (!automatic)
            {
                Button delete = Ui.Button("Delete", () => DeleteProfile(profile), "Danger");
                delete.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(delete);
            }
            grid.Children.Add(row);
            return Ui.Card(grid, new Thickness(20, 14, 20, 15));
        }

        void SaveCurrent()
        {
            string name = DialogHelpers.Prompt(Ctx, "Save my setup", "Give this profile a name, for example \"Work PC\".", "My setup", "Save");
            if (name == null) return;
            string file = Ctx.Profiles.PathFor(name);
            if (System.IO.File.Exists(file) && !Ui.Confirm(Ctx.Window, "A profile named \"" + name + "\" already exists. Replace it?")) return;
            try
            {
                Profile p = Ctx.Engine.CaptureProfile(name, Catalog.All, false);
                Ctx.Profiles.Save(p);
                Ctx.SetStatus("Saved the profile \"" + name + "\" with " + p.Tweaks.Count + (p.Tweaks.Count == 1 ? " tweak." : " tweaks."));
            }
            catch (Exception ex) { Ctx.SetStatus("Could not save the profile: " + ex.Message, true); }
            Rebuild();
        }

        void ApplyProfile(Profile profile)
        {
            List<PlanItem> plan = Ctx.Engine.PlanFor(profile);
            if (plan.Count == 0)
            {
                Ui.Info(Ctx.Window, "This PC already matches \"" + profile.Name + "\". Nothing needs to change.");
                return;
            }
            model.StagePlan(plan);
            openReview();
        }

        void ExportProfile(Profile profile)
        {
            var dialog = new SaveFileDialog { Title = "Export profile", Filter = "Tweak Bench profile (*.json)|*.json", FileName = ProfileStore.SafeFileName(profile.Name) + ".json", OverwritePrompt = true };
            if (dialog.ShowDialog(Ctx.Window) != true) return;
            try { ProfileStore.SaveTo(profile, dialog.FileName); Ctx.SetStatus("Exported \"" + profile.Name + "\" to " + dialog.FileName + "."); }
            catch (Exception ex) { Ctx.SetStatus("Could not export the profile: " + ex.Message, true); }
        }

        void ImportProfile()
        {
            var dialog = new OpenFileDialog { Title = "Import profile", Filter = "Tweak Bench profile (*.json)|*.json|All files|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(Ctx.Window) != true) return;
            try
            {
                Profile p = ProfileStore.Read(dialog.FileName);
                if (System.IO.File.Exists(Ctx.Profiles.PathFor(p.Name)) && !Ui.Confirm(Ctx.Window, "A profile named \"" + p.Name + "\" already exists. Replace it?")) return;
                Ctx.Profiles.Save(p);
                int unknown = p.Tweaks.Count(t => Catalog.Find(t.Id) == null);
                Ctx.SetStatus("Imported \"" + p.Name + "\" (" + p.Tweaks.Count + " tweaks)." + (unknown > 0 ? " " + unknown + " are not in this version of Tweak Bench and will be ignored." : ""));
            }
            catch (Exception ex) { Ctx.SetStatus("That file is not a Tweak Bench profile: " + ex.Message, true); }
            Rebuild();
        }

        void DeleteProfile(Profile profile)
        {
            if (!Ui.Confirm(Ctx.Window, "Delete the profile \"" + profile.Name + "\"?\n\nThis only removes the saved list. Nothing on your PC changes.")) return;
            try { Ctx.Profiles.Delete(profile.Name); Ctx.SetStatus("Deleted the profile \"" + profile.Name + "\"."); }
            catch (Exception ex) { Ctx.SetStatus("Could not delete the profile: " + ex.Message, true); }
            Rebuild();
        }
    }
}
