using System;
using System.Collections.Generic;
using System.Linq;

namespace TweakBench.Core
{
    /// <summary>
    /// Every tweak, category and preset. Each category's tweaks live in their own file under src\Catalog and are added by
    /// implementing one of the partial methods below (an unimplemented partial method simply adds nothing).
    /// </summary>
    public static partial class Catalog
    {
        public static readonly List<Category> Categories = new List<Category>
        {
            new Category { Id = "update", Title = "Windows Update", Glyph = "", Blurb = "Decide when updates install and stop Windows from restarting your PC on its own." },
            new Category { Id = "privacy", Title = "Privacy", Glyph = "", Blurb = "Turn off advertising, tracking, data collection and AI features you did not ask for." },
            new Category { Id = "start", Title = "Start, taskbar & search", Glyph = "", Blurb = "Clean up the taskbar and Start menu and keep web results out of search." },
            new Category { Id = "explorer", Title = "File Explorer", Glyph = "", Blurb = "Show file extensions and hidden files, and make File Explorer quieter." },
            new Category { Id = "performance", Title = "Performance & gaming", Glyph = "", Blurb = "Power plans, animations, background activity and gaming settings." },
            new Category { Id = "system", Title = "System & desktop", Glyph = "", Blurb = "Look and feel, keyboard and mouse, sign-in and other everyday system settings." },
            new Category { Id = "network", Title = "Network", Glyph = "", Blurb = "DNS servers and network options." },
            new Category { Id = "services", Title = "Background services", Glyph = "", Blurb = "Optional Windows services you can switch to manual or off if you never use them." },
        };

        public static readonly List<Preset> Presets = new List<Preset>
        {
            new Preset { Id = "basics", Title = "Recommended basics", Blurb = "Safe changes almost everyone wants: no surprise restarts, no ads, file extensions shown." },
            new Preset { Id = "privacy", Title = "Privacy", Blurb = "Send Microsoft as little as Windows allows and turn off advertising and tracking." },
            new Preset { Id = "gaming", Title = "Gaming & performance", Blurb = "Less background work and input lag while you play. Uses the High performance power plan, which shortens laptop battery life." },
            new Preset { Id = "clean", Title = "Clean & quiet desktop", Blurb = "Fewer pop-ups, tips, widgets and suggestions." },
        };

        static List<Tweak> all;
        static readonly object gate = new object();

        static partial void AddUpdate(List<Tweak> list);       // src\Catalog\Update.cs
        static partial void AddPrivacy(List<Tweak> list);      // src\Catalog\Privacy.cs
        static partial void AddStart(List<Tweak> list);        // src\Catalog\StartTaskbar.cs
        static partial void AddExplorer(List<Tweak> list);     // src\Catalog\Explorer.cs
        static partial void AddPerformance(List<Tweak> list);  // src\Catalog\Performance.cs
        static partial void AddSystem(List<Tweak> list);       // src\Catalog\SystemDesktop.cs
        static partial void AddNetwork(List<Tweak> list);      // src\Catalog\Network.cs
        static partial void AddServices(List<Tweak> list);     // src\Catalog\Services.cs

        /// <summary>All tweaks in display order (category order, then the order each file adds them).</summary>
        public static IList<Tweak> All
        {
            get
            {
                lock (gate)
                {
                    if (all == null)
                    {
                        var list = new List<Tweak>();
                        AddUpdate(list); AddPrivacy(list); AddStart(list); AddExplorer(list);
                        AddPerformance(list); AddSystem(list); AddNetwork(list); AddServices(list);
                        var problems = Validate(list);
                        if (problems.Count > 0) throw new InvalidOperationException("The tweak catalog is invalid:\n" + string.Join("\n", problems));
                        all = list;
                    }
                    return all;
                }
            }
        }

        public static Tweak Find(string id) { return All.FirstOrDefault(t => t.Id == id); }

        public static Category FindCategory(string id) { return Categories.FirstOrDefault(c => c.Id == id); }

        public static IEnumerable<Tweak> InCategory(string id) { return All.Where(t => t.Category == id); }

        public static IEnumerable<Tweak> InPreset(string presetId) { return All.Where(t => t.InPreset(presetId)); }

        /// <summary>Catalog consistency rules. Tests call this too.</summary>
        public static List<string> Validate(IList<Tweak> list)
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            var presetIds = new HashSet<string>(Presets.Select(p => p.Id));
            foreach (Tweak t in list)
            {
                string name = t.Id ?? "(no id)";
                if (string.IsNullOrWhiteSpace(t.Id)) problems.Add("A tweak has no id.");
                else if (!ids.Add(t.Id)) problems.Add(name + ": duplicate id.");
                if (t.Id != null && t.Category != null && !t.Id.StartsWith(t.Category + ".", StringComparison.Ordinal)) problems.Add(name + ": id must start with its category id and a dot.");
                if (FindCategory(t.Category) == null) problems.Add(name + ": unknown category '" + t.Category + "'.");
                if (string.IsNullOrWhiteSpace(t.Title)) problems.Add(name + ": no title.");
                if (string.IsNullOrWhiteSpace(t.Summary)) problems.Add(name + ": no summary.");
                if (string.IsNullOrWhiteSpace(t.Why)) problems.Add(name + ": no 'why'.");
                if (t.Changes.Count == 0) problems.Add(name + ": no changes.");
                if (t.Changes.Any(c => c == null)) problems.Add(name + ": a change is null.");
                foreach (string p in t.Presets ?? new string[0]) if (!presetIds.Contains(p)) problems.Add(name + ": unknown preset '" + p + "'.");
                if (t.Recommended && t.Risk == Risk.Advanced) problems.Add(name + ": an Advanced tweak cannot be Recommended.");
                foreach (Change c in t.Changes)
                {
                    var r = c as RegValue;
                    if (r == null) continue;
                    if (string.IsNullOrWhiteSpace(r.Path) || r.Name == null) problems.Add(name + ": registry change without a path or name.");
                    if (r.Path != null && (r.Path.StartsWith("HK", StringComparison.OrdinalIgnoreCase) || r.Path.StartsWith("\\"))) problems.Add(name + ": registry path must not include the hive (" + r.Path + ").");
                    if (r.On == null) problems.Add(name + ": registry change without an 'on' value.");
                }
            }
            return problems;
        }
    }
}
