using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>Glyph code points (Segoe Fluent Icons / Segoe MDL2 Assets) used by the tweak pages.</summary>
    public static class Icons
    {
        public const string Check = "", Cancel = "", Dash = "", Info = "", Warning = "", Shield = "";
        public const string Undo = "", Refresh = "", Home = "", History = "", Search = "", More = "";
        public const string Admin = "", Delete = "", Settings = "", Save = "", Folder = "", Restore = "";
    }

    /// <summary>
    /// One tweak as a card: plain-English title and summary, a "why" line, badges, a live status pill, a switch that stages the wanted
    /// state, and an expandable list of exactly what it changes. <see cref="Update"/> brings it in line with the model.
    /// </summary>
    public sealed class TweakCard
    {
        public readonly Tweak Tweak;
        public readonly Border Root;
        readonly UiContext ctx;
        readonly TweakModel model;

        readonly Border pill;
        readonly TextBlock pillText;
        readonly CheckBox sw;
        readonly TextBlock willText;
        readonly Button turnAllOff;
        readonly Button cancelStaged;
        readonly FrameworkElement undoNote;
        readonly FrameworkElement unavailableNote;
        readonly TextBlock unavailableText;
        readonly FrameworkElement adminNote;
        readonly FrameworkElement wrongUserNote;
        readonly Button detailsToggle;
        readonly StackPanel detailsPanel;
        bool detailsOpen, detailsDirty = true;

        public TweakCard(UiContext ctx, TweakModel model, Tweak tweak, bool showCategory = false)
        {
            this.ctx = ctx; this.model = model; Tweak = tweak;
            Root = Ui.Style(new Border(), "CardBorder");
            Root.Padding = new Thickness(18, 14, 18, 14);
            Root.Margin = new Thickness(0, 0, 0, 10);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Root.Child = grid;

            // ---- left: what it is
            var left = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            if (showCategory)
            {
                Category c = Catalog.FindCategory(tweak.Category);
                if (c != null) { var cap = Ui.Text(c.Title, "Muted", true, 11); cap.Margin = new Thickness(0, 0, 0, 3); left.Children.Add(cap); }
            }

            var titleRow = new WrapPanel { Orientation = Orientation.Horizontal };
            var title = new TextBlock { Text = tweak.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            titleRow.Children.Add(title);
            if (tweak.Recommended) titleRow.Children.Add(Badge(null, "Recommended", "Good", "GoodBg"));
            if (tweak.Risk == Risk.Moderate) titleRow.Children.Add(Badge(null, "Moderate", "Warn", "WarnBg"));
            if (tweak.Risk == Risk.Advanced) titleRow.Children.Add(Badge(null, "Advanced", "Bad", "BadBg"));
            if (tweak.NeedsAdmin && !ctx.IsElevated) titleRow.Children.Add(Badge(Icons.Shield, "Admin", "Info", "InfoBg"));
            left.Children.Add(titleRow);

            var summary = Ui.Para(tweak.Summary, "Text", 13.5);
            summary.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(summary);

            left.Children.Add(Labelled("Why: ", tweak.Why));
            if (!string.IsNullOrWhiteSpace(tweak.OffMeans)) left.Children.Add(Labelled("When off: ", tweak.OffMeans));

            if (!string.IsNullOrWhiteSpace(tweak.Caution))
            {
                Border note = Ui.Note(tweak.Caution, "Warn");
                note.Margin = new Thickness(0, 9, 0, 2);
                left.Children.Add(note);
            }

            string effect = EffectText(tweak.TakesEffect);
            if (effect != null) left.Children.Add(Line(Icons.Info, effect, "Muted"));
            undoNote = Line(Icons.Undo, "Turned on by Tweak Bench. Turning it off restores your previous values.", "Good");
            left.Children.Add(undoNote);
            adminNote = Line(Icons.Shield, "Needs administrator rights. Use \"Run as administrator\" to change it.", "Info");
            left.Children.Add(adminNote);
            wrongUserNote = Line(Icons.Warning, "Tweak Bench is running as a different Windows account than the one you are signed in with, so it cannot change this setting for you.", "Warn");
            left.Children.Add(wrongUserNote);
            unavailableNote = Line(Icons.Info, "", "Muted");
            unavailableText = (TextBlock)((Panel)unavailableNote).Children[1];
            left.Children.Add(unavailableNote);

            detailsToggle = Ui.Button("What it changes  ▸", ToggleDetails, "Link");
            detailsToggle.HorizontalAlignment = HorizontalAlignment.Left;
            detailsToggle.Margin = new Thickness(-2, 8, 0, 0);
            detailsToggle.FontSize = 12;
            left.Children.Add(detailsToggle);
            detailsPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
            left.Children.Add(detailsPanel);
            grid.Children.Add(left);

            // ---- right: status and switch
            var right = new StackPanel { MinWidth = 128, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(right, 1);
            pill = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(11, 2, 11, 3), HorizontalAlignment = HorizontalAlignment.Right };
            pillText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
            pill.Child = pillText;
            right.Children.Add(pill);

            sw = Ui.Style(new CheckBox { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }, "Switch");
            sw.Click += (s, e) => OnSwitchClicked();
            System.Windows.Controls.ToolTipService.SetShowOnDisabled(sw, true);
            System.Windows.Automation.AutomationProperties.SetName(sw, tweak.Title);
            right.Children.Add(sw);

            willText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            Ui.Ref(willText, TextBlock.ForegroundProperty, "AccentText");
            right.Children.Add(willText);

            cancelStaged = Ui.Button("Cancel", () => model.Unstage(Tweak), "Link", "Take back this choice. Nothing on your PC changes.");
            cancelStaged.HorizontalAlignment = HorizontalAlignment.Right;
            cancelStaged.FontSize = 12;
            cancelStaged.Margin = new Thickness(0, 2, -2, 0);
            cancelStaged.Visibility = Visibility.Collapsed;
            right.Children.Add(cancelStaged);

            turnAllOff = Ui.Button("Switch all off", () => model.Stage(Tweak, false), "Link", "Some of this tweak's settings are on. This turns all of them off.");
            turnAllOff.HorizontalAlignment = HorizontalAlignment.Right;
            turnAllOff.FontSize = 12;
            turnAllOff.Margin = new Thickness(0, 4, -2, 0);
            right.Children.Add(turnAllOff);
            grid.Children.Add(right);

            Update();
        }

        /// <summary>
        /// Flipping the switch stages the wanted state; flipping it back takes the choice back. A "Partly on" or unknown tweak shows its
        /// switch off, so the second click would otherwise stage a turn-off instead of taking the first choice back.
        /// </summary>
        void OnSwitchClicked()
        {
            bool want = sw.IsChecked == true;
            TweakStatus st = model.StatusOf(Tweak);
            bool neither = st != null && st.State != TweakState.On && st.State != TweakState.Off;
            if (neither && !want && model.StagedValue(Tweak) == true) model.Unstage(Tweak);
            else model.Stage(Tweak, want);
        }

        // ------------------------------------------------------------------ building blocks

        static Border Badge(string glyph, string text, string fgKey, string bgKey)
        {
            var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 8, 2), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            Ui.Ref(b, Border.BackgroundProperty, bgKey);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null) { var g = Ui.Glyph(glyph, 10, fgKey); g.Margin = new Thickness(0, 1, 4, 0); row.Children.Add(g); }
            var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
            Ui.Ref(t, TextBlock.ForegroundProperty, fgKey);
            row.Children.Add(t);
            b.Child = row;
            return b;
        }

        static TextBlock Labelled(string label, string text)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 5, 0, 0) };
            Ui.Ref(t, TextBlock.ForegroundProperty, "Muted");
            t.Inlines.Add(new Run(label) { FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new Run(text));
            return t;
        }

        static Grid Line(string glyph, string text, string fgKey)
        {
            var g = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Ui.Glyph(glyph, 12, fgKey);
            icon.Margin = new Thickness(0, 2, 7, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(icon);
            var t = Ui.Para(text, fgKey, 12);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        static string EffectText(Takes takes)
        {
            switch (takes)
            {
                case Takes.ExplorerRestart: return "Shows after File Explorer restarts.";
                case Takes.SignOut: return "Takes effect after you sign out and back in.";
                case Takes.PcRestart: return "Takes effect after you restart your PC. Tweak Bench never restarts it for you.";
                default: return null;
            }
        }

        // ------------------------------------------------------------------ state

        void ToggleDetails()
        {
            detailsOpen = !detailsOpen;
            detailsToggle.Content = detailsOpen ? "What it changes  ▾" : "What it changes  ▸";
            detailsPanel.Visibility = detailsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (detailsOpen && detailsDirty) BuildDetails();
        }

        void BuildDetails()
        {
            detailsDirty = false;
            detailsPanel.Children.Clear();
            TweakStatus st = model.StatusOf(Tweak);
            var box = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 10) };
            Ui.Ref(box, Border.BackgroundProperty, "Card2");
            var list = new StackPanel();
            for (int i = 0; i < Tweak.Changes.Count; i++)
            {
                Change c = Tweak.Changes[i];
                ChangeStatus cs = st == null ? null : st.Changes.FirstOrDefault(x => ReferenceEquals(x.Change, c));
                var row = new StackPanel { Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0) };
                var what = new TextBlock { Text = c.Describe(), FontFamily = Ui.MonoFont, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                Ui.Ref(what, TextBlock.ForegroundProperty, "Text");
                row.Children.Add(what);
                string now;
                if (cs == null) now = "not read yet";
                else if (cs.Unavailable != null) now = "not on this PC (" + cs.Unavailable + ")";
                else now = cs.Current ?? "unknown";
                var nowText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
                Ui.Ref(nowText, TextBlock.ForegroundProperty, "Muted");
                nowText.Inlines.Add(new Run("now: ") { FontWeight = FontWeights.SemiBold });
                nowText.Inlines.Add(new Run(now));
                row.Children.Add(nowText);
                list.Children.Add(row);
            }
            if (Tweak.NeedsAdmin)
            {
                var admin = Ui.Para("Needs administrator rights to change.", "Muted", 12);
                admin.Margin = new Thickness(0, 10, 0, 0);
                list.Children.Add(admin);
            }
            box.Child = list;
            detailsPanel.Children.Add(box);
        }

        void SetPill(string text, string fg, string bg, string tip)
        {
            pillText.Text = text;
            Ui.Ref(pillText, TextBlock.ForegroundProperty, fg);
            Ui.Ref(pill, Border.BackgroundProperty, bg);
            pill.ToolTip = tip;
        }

        /// <summary>Brings the card in line with the model: live status, staged choice, notes and the switch.</summary>
        public void Update()
        {
            TweakStatus st = model.StatusOf(Tweak);
            bool ready = model.Ready && st != null;
            bool? staged = model.StagedValue(Tweak);
            detailsDirty = true;
            if (detailsOpen) BuildDetails();

            bool updating = ready && model.IsUpdating(Tweak);
            if (!ready) SetPill("Checking…", "Muted", "BtnBg", "Reading the current setting from this PC.");
            else if (updating) SetPill("Updating…", "Muted", "BtnBg", "Tweak Bench is reading the new state from this PC.");
            else
            {
                switch (st.State)
                {
                    case TweakState.On: SetPill("On", "Good", "GoodBg", "Every setting this tweak controls is already set."); break;
                    case TweakState.Off: SetPill("Off", "Muted", "BtnBg", "None of the settings this tweak controls are set."); break;
                    case TweakState.Partial: SetPill("Partly on", "Warn", "WarnBg", "Some of the settings this tweak controls are set and some are not."); break;
                    case TweakState.Unavailable: SetPill("Not available here", "Muted", "BtnBg", st.Reason); break;
                    default: SetPill("Unknown", "Muted", "BtnBg", st.Reason ?? "The current state could not be read."); break;
                }
            }

            string wrongUser = model.WrongUserReason(Tweak);
            bool canStage = ready && !updating && model.CanStage(Tweak);
            sw.IsChecked = model.Desired(Tweak);
            sw.IsEnabled = canStage;
            if (!ready) sw.ToolTip = "Still reading the current setting.";
            else if (updating) sw.ToolTip = "Reading the new state from this PC. This takes a moment.";
            else if (st.State == TweakState.Unavailable) sw.ToolTip = st.Reason;
            else if (wrongUser != null) sw.ToolTip = wrongUser;
            else if (Tweak.NeedsAdmin && !ctx.IsElevated) sw.ToolTip = "Needs administrator rights. Use \"Run as administrator\" to change it.";
            else sw.ToolTip = sw.IsChecked == true ? "Switch off to stage turning this off." : "Switch on to stage turning this on. Nothing changes until you review and apply.";

            willText.Visibility = staged.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (staged.HasValue) willText.Text = staged.Value ? "Will turn on" : "Will turn off";
            string because = model.BecauseOf(Tweak);
            willText.ToolTip = because == null ? null : "Switched off automatically because you chose: " + because + ". Only one of these can be on.";
            if (because != null) willText.Text = "Will turn off (automatic)";
            cancelStaged.Visibility = staged.HasValue ? Visibility.Visible : Visibility.Collapsed;
            Ui.Ref(Root, Border.BorderBrushProperty, staged.HasValue ? "Accent" : "Border");
            Ui.Ref(Root, Border.BackgroundProperty, staged.HasValue ? "Selected" : "Card");

            turnAllOff.Visibility = ready && st.State == TweakState.Partial && canStage && staged != false ? Visibility.Visible : Visibility.Collapsed;
            undoNote.Visibility = ready && st.HasUndo && st.State != TweakState.Off ? Visibility.Visible : Visibility.Collapsed;
            adminNote.Visibility = ready && st.State != TweakState.Unavailable && Tweak.NeedsAdmin && !ctx.IsElevated ? Visibility.Visible : Visibility.Collapsed;
            wrongUserNote.Visibility = ready && st.State != TweakState.Unavailable && wrongUser != null ? Visibility.Visible : Visibility.Collapsed;
            bool unavailable = ready && (st.State == TweakState.Unavailable || st.State == TweakState.Unknown);
            unavailableNote.Visibility = unavailable ? Visibility.Visible : Visibility.Collapsed;
            if (unavailable) unavailableText.Text = (st.State == TweakState.Unavailable ? "Not available on this PC: " : "Could not read it: ") + (st.Reason ?? "reason unknown");
        }
    }
}
