using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>Search results across every category, shown with the same cards as the category pages.</summary>
    public sealed class SearchPage : PageBase
    {
        const int MaxResults = 60;
        readonly TweakModel model;
        readonly List<TweakCard> cards = new List<TweakCard>();
        StackPanel list;
        string query = "";
        int found, hiddenAdvanced;

        public SearchPage(UiContext ctx, TweakModel model) : base(ctx) { this.model = model; }

        public override string Id { get { return "search"; } }
        public override string Title { get { return "Search results"; } }
        public override string Glyph { get { return Icons.Search; } }
        public override string Subtitle
        {
            get
            {
                if (string.IsNullOrWhiteSpace(query)) return "Type in the search box to find a tweak by what it does.";
                if (found == 0) return "No tweak matches \"" + query.Trim() + "\".";
                return found + (found == 1 ? " tweak matches" : " tweaks match") + " \"" + query.Trim() + "\".";
            }
        }

        protected override FrameworkElement Build()
        {
            list = new StackPanel();
            model.Changed += Refresh;
            Search(query);
            return list;
        }

        /// <summary>Shows the tweaks that match every word of <paramref name="text"/> (title, summary, why, keywords, category).</summary>
        public void Search(string text)
        {
            query = text ?? "";
            if (list == null) return;
            string[] words = query.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var matches = new List<Tweak>();
            hiddenAdvanced = 0;
            if (words.Length > 0)
            {
                int index = 0;
                var ranked = new List<KeyValuePair<int, Tweak>>();
                foreach (Tweak t in Catalog.All)
                {
                    index++;
                    Category c = Catalog.FindCategory(t.Category);
                    string title = (t.Title ?? "").ToLowerInvariant();
                    string haystack = title + " " + (t.Summary ?? "").ToLowerInvariant() + " " + (t.Why ?? "").ToLowerInvariant() + " " + (t.OffMeans ?? "").ToLowerInvariant() + " "
                        + string.Join(" ", t.Keywords ?? new string[0]).ToLowerInvariant() + " " + (c == null ? "" : c.Title.ToLowerInvariant()) + " " + t.Id.Replace('-', ' ').Replace('.', ' ');
                    if (!words.All(w => haystack.Contains(w))) continue;
                    if (t.Risk == Risk.Advanced && !Ctx.Settings.ShowAdvanced) { hiddenAdvanced++; continue; }
                    int rank = words.All(w => title.Contains(w)) ? 0 : 1;
                    ranked.Add(new KeyValuePair<int, Tweak>(rank * 100000 + index, t));
                }
                matches = ranked.OrderBy(r => r.Key).Select(r => r.Value).ToList();
            }
            found = matches.Count;

            list.Children.Clear();
            cards.Clear();
            foreach (Tweak t in matches.Take(MaxResults))
            {
                var card = new TweakCard(Ctx, model, t, true);
                cards.Add(card);
                list.Children.Add(card.Root);
            }
            if (found > MaxResults)
                list.Children.Add(Ui.Para("Showing the first " + MaxResults + " matches. Add a word to narrow the search.", "Muted", 12.5));
            if (hiddenAdvanced > 0)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 0, 0) };
                row.Children.Add(Ui.Text(hiddenAdvanced + (hiddenAdvanced == 1 ? " advanced match is hidden." : " advanced matches are hidden."), "Muted"));
                var show = Ui.Button("Show them", () => { Ctx.Settings.ShowAdvanced = true; Ctx.SaveSettings(); model.Notify(); }, "Link");
                show.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(show);
                list.Children.Add(row);
            }
            if (found == 0 && words.Length > 0 && hiddenAdvanced == 0)
                list.Children.Add(Ui.Note("Try a shorter or different word, for example \"restart\", \"ads\", \"taskbar\" or \"dns\".", "Info"));
        }

        void Refresh()
        {
            if (list == null) return;
            if (hiddenAdvanced > 0 && Ctx.Settings.ShowAdvanced) { Search(query); return; }
            foreach (TweakCard c in cards) c.Update();
        }

        public override void Shown() { Refresh(); }
        public override void TweaksChanged() { Refresh(); }
    }
}
