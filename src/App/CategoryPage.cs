using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>One page of tweaks (a <see cref="Category"/> of the catalog). The Network page also shows the DNS chooser at the top, and System &amp; desktop the notification pop-up mover.</summary>
    public sealed class CategoryPage : PageBase, IHeaderActions
    {
        readonly Category category;
        readonly TweakModel model;
        readonly List<TweakCard> cards = new List<TweakCard>();
        StackPanel list;
        TextBlock summary;
        Button selectRecommended;
        CheckBox advancedSwitch;
        FrameworkElement actions;
        bool builtWithAdvanced;

        public CategoryPage(UiContext ctx, TweakModel model, Category category) : base(ctx)
        {
            this.model = model;
            this.category = category;
        }

        public override string Id { get { return category.Id; } }
        public override string Title { get { return category.Title; } }
        public override string Subtitle { get { return category.Blurb; } }
        public override string Glyph { get { return category.Glyph; } }

        public FrameworkElement HeaderActions
        {
            get
            {
                if (actions == null) BuildActions();
                return actions;
            }
        }

        void BuildActions()
        {
            selectRecommended = Ui.Button("Select recommended", SelectRecommended, "Ghost", "Stage every recommended tweak on this page that is not on yet. Nothing changes until you review and apply.");
            advancedSwitch = Ui.Style(new CheckBox { Content = "Show advanced tweaks", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) }, "Switch");
            advancedSwitch.ToolTip = "Advanced tweaks can break a feature or an app. They stay hidden until you ask for them.";
            advancedSwitch.Click += (s, e) =>
            {
                Ctx.Settings.ShowAdvanced = advancedSwitch.IsChecked == true;
                Ctx.SaveSettings();
                model.Notify();
            };
            var row = Ui.Row(0, selectRecommended, advancedSwitch);
            row.VerticalAlignment = VerticalAlignment.Center;
            actions = row;
            SyncActions();
        }

        void SyncActions()
        {
            if (actions == null) return;
            advancedSwitch.IsChecked = Ctx.Settings.ShowAdvanced;
            bool anyRecommended = Catalog.InCategory(category.Id).Any(t => t.Recommended);
            selectRecommended.Visibility = anyRecommended ? Visibility.Visible : Visibility.Collapsed;
            advancedSwitch.Visibility = Catalog.InCategory(category.Id).Any(t => t.Risk == Risk.Advanced) ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override FrameworkElement Build()
        {
            var root = new StackPanel();
            if (category.Id == "network")
            {
                FrameworkElement dns = DnsPanel.Build(Ctx);
                if (dns != null) { root.Children.Add(dns); dns.Margin = new Thickness(dns.Margin.Left, dns.Margin.Top, dns.Margin.Right, Math.Max(dns.Margin.Bottom, 14)); }
            }
            if (category.Id == "system")
            {
                FrameworkElement notify = NotificationPanel.Build(Ctx);
                if (notify != null) root.Children.Add(notify);
            }
            summary = Ui.Para("", "Muted", 12.5);
            summary.Margin = new Thickness(2, 0, 0, 12);
            root.Children.Add(summary);
            list = new StackPanel();
            root.Children.Add(list);
            model.Changed += Update;
            Rebuild();
            return root;
        }

        void Rebuild()
        {
            list.Children.Clear();
            cards.Clear();
            builtWithAdvanced = Ctx.Settings.ShowAdvanced;
            var tweaks = Catalog.InCategory(category.Id).ToList();
            var shown = tweaks.Where(t => builtWithAdvanced || t.Risk != Risk.Advanced).ToList();
            foreach (Tweak t in shown)
            {
                var card = new TweakCard(Ctx, model, t);
                cards.Add(card);
                list.Children.Add(card.Root);
            }
            if (tweaks.Count == 0)
                list.Children.Add(Ui.Note("There are no tweaks on this page yet.", "Info"));
            int hidden = tweaks.Count - shown.Count;
            if (hidden > 0)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 0, 0) };
                row.Children.Add(Ui.Text(hidden + (hidden == 1 ? " advanced tweak is hidden." : " advanced tweaks are hidden."), "Muted"));
                var show = Ui.Button("Show them", () => { Ctx.Settings.ShowAdvanced = true; Ctx.SaveSettings(); model.Notify(); }, "Link");
                show.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(show);
                list.Children.Add(row);
            }
        }

        void Update()
        {
            if (list == null) return;
            if (builtWithAdvanced != Ctx.Settings.ShowAdvanced) Rebuild();
            foreach (TweakCard c in cards) c.Update();
            SyncActions();

            if (!model.Ready) { summary.Text = "Checking this PC…"; return; }
            int on = 0, usable = 0;
            foreach (TweakCard c in cards)
            {
                TweakStatus st = model.StatusOf(c.Tweak);
                if (st == null || st.State == TweakState.Unavailable) continue;
                usable++;
                if (st.State == TweakState.On) on++;
            }
            int staged = cards.Count(c => model.IsStaged(c.Tweak));
            summary.Text = usable == 0 ? "" : on + " of " + usable + " tweaks on this page are on" + (staged > 0 ? "  ·  " + staged + " ready to apply" : "") + ".";
        }

        void SelectRecommended()
        {
            var candidates = cards.Select(c => c.Tweak).Where(t => t.Recommended).ToList();
            int stagedNow = model.StageAllOn(candidates);
            int needAdmin = candidates.Count(t => model.StatusOf(t) != null && model.StatusOf(t).State != TweakState.On && model.StatusOf(t).State != TweakState.Unavailable && t.NeedsAdmin && !Ctx.IsElevated);
            if (stagedNow > 0)
                Ctx.SetStatus(stagedNow + (stagedNow == 1 ? " recommended change is" : " recommended changes are") + " ready. Check them in the bar below, then choose Review & apply." + (needAdmin > 0 ? " " + needAdmin + " more need administrator rights." : ""));
            else if (needAdmin > 0)
                Ctx.SetStatus(needAdmin + " recommended " + (needAdmin == 1 ? "tweak needs" : "tweaks need") + " administrator rights. Use Run as administrator to include them.");
            else
                Ctx.SetStatus("Every recommended tweak on this page is already on.");
        }

        public override void Shown() { Update(); }
        public override void TweaksChanged() { Update(); }
    }
}
