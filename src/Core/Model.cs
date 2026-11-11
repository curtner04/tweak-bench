using System;
using System.Collections.Generic;
using System.Linq;

namespace TweakBench.Core
{
    /// <summary>How careful someone should be before turning a tweak on.</summary>
    public enum Risk
    {
        /// <summary>Harmless and easy to undo. Most people can turn it on without thinking about it.</summary>
        Safe,
        /// <summary>Changes how Windows behaves in a way some people will notice or miss.</summary>
        Moderate,
        /// <summary>For people who know why they want it. Can break a feature or an app.</summary>
        Advanced
    }

    /// <summary>When a change becomes visible after it is applied.</summary>
    public enum Takes
    {
        Immediately,
        /// <summary>File Explorer (taskbar, Start, desktop) must restart. The app offers a button; it never restarts Explorer silently.</summary>
        ExplorerRestart,
        /// <summary>Sign out and back in.</summary>
        SignOut,
        /// <summary>The PC must restart. Tweak Bench never restarts the PC itself.</summary>
        PcRestart
    }

    public enum TweakState
    {
        /// <summary>Every change of the tweak is in its "on" state.</summary>
        On,
        /// <summary>No change is in its "on" state.</summary>
        Off,
        /// <summary>Some changes are on, others are not.</summary>
        Partial,
        /// <summary>The tweak does not apply to this PC (wrong edition or build, missing service or task).</summary>
        Unavailable,
        /// <summary>The state could not be read.</summary>
        Unknown
    }

    /// <summary>A page of tweaks in the sidebar.</summary>
    public sealed class Category
    {
        public string Id;
        public string Title;
        /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets code point.</summary>
        public string Glyph;
        /// <summary>One sentence shown under the page title.</summary>
        public string Blurb;
    }

    /// <summary>A named starting set of tweaks ("Recommended basics", "Privacy", ...). Membership is <see cref="Tweak.Presets"/>.</summary>
    public sealed class Preset
    {
        public string Id;
        public string Title;
        public string Blurb;
    }

    /// <summary>
    /// One user-facing setting. Turning it on applies every <see cref="Changes"/> entry; turning it off restores the values
    /// that were there before Tweak Bench changed them (from history) or, when there is no history, the Windows defaults.
    /// </summary>
    public sealed class Tweak
    {
        /// <summary>Stable id, "category.short-name". Saved in history and profiles: never rename an existing id.</summary>
        public string Id;
        /// <summary><see cref="Category.Id"/>.</summary>
        public string Category;
        /// <summary>Short title phrased as what "on" means: "Show file extensions".</summary>
        public string Title;
        /// <summary>One plain sentence: what happens when it is on.</summary>
        public string Summary;
        /// <summary>Why someone would want it, and the trade-off. One or two sentences.</summary>
        public string Why;
        /// <summary>Optional: what the Windows default (off) is like, when that is not obvious.</summary>
        public string OffMeans;
        /// <summary>Optional warning shown highlighted (what can break).</summary>
        public string Caution;
        public Risk Risk = Risk.Safe;
        /// <summary>Shown with a "Recommended" badge and included in the "basics" preset.</summary>
        public bool Recommended;
        /// <summary>Preset ids this tweak belongs to (see <see cref="Catalog.Presets"/>). "basics" is implied by <see cref="Recommended"/>.</summary>
        public string[] Presets = new string[0];
        public Takes TakesEffect = Takes.Immediately;
        /// <summary>Tweaks with the same group are alternatives (for example "taskbar search: icon" and "taskbar search: hidden"). Only one can be selected in a batch.</summary>
        public string ExclusiveGroup;
        /// <summary>Extra words for search (synonyms, the old name of a setting).</summary>
        public string[] Keywords = new string[0];
        /// <summary>Lowest Windows build the tweak applies to (22000 = Windows 11). 0 = any.</summary>
        public int MinBuild;
        /// <summary>Highest Windows build the tweak applies to. 0 = any.</summary>
        public int MaxBuild;
        /// <summary>Optional extra check; returns a short reason when the tweak does not apply to this PC, otherwise null.</summary>
        public Func<ISystem, string> NotApplicable;
        public readonly List<Change> Changes = new List<Change>();

        public bool NeedsAdmin { get { return Changes.Any(c => c.NeedsAdmin); } }

        public bool InPreset(string presetId)
        {
            if (presetId == "basics" && Recommended) return true;
            return Presets != null && Presets.Contains(presetId);
        }

        public override string ToString() { return Id; }
    }

    /// <summary>The saved "before" state of one change, so it can be restored exactly. Stored in history.json.</summary>
    public sealed class Snapshot
    {
        /// <summary>Index of the change inside <see cref="Tweak.Changes"/>.</summary>
        public int Index;
        /// <summary><see cref="Change.Kind"/> of the change that produced it ("registry", "service", ...).</summary>
        public string Kind;
        /// <summary>Human-readable description of the change (for the History page).</summary>
        public string What;
        /// <summary>Human-readable description of the value before the change.</summary>
        public string Was;
        /// <summary>Kind-specific saved data. Strings only so it serializes cleanly.</summary>
        public Dictionary<string, string> Values = new Dictionary<string, string>();

        public string Get(string key) { string v; return Values != null && Values.TryGetValue(key, out v) ? v : null; }
        public void Set(string key, string value) { if (Values == null) Values = new Dictionary<string, string>(); Values[key] = value; }
    }

    /// <summary>One concrete system change inside a tweak (a registry value, a service start type, a scheduled task, ...).</summary>
    public abstract class Change
    {
        /// <summary>"registry", "registry-key", "service", "task", "power" or "custom".</summary>
        public abstract string Kind { get; }
        /// <summary>The "on" target in exact technical terms, for the "What it changes" details.</summary>
        public abstract string Describe();
        /// <summary>The current value in exact technical terms ("0x1", "not set", "Disabled").</summary>
        public abstract string DescribeCurrent(ISystem sys);
        /// <summary>True when this change is in its "on" state, false when not, null when it cannot be told.</summary>
        public abstract bool? IsOn(ISystem sys);
        /// <summary>A short reason when this change cannot be made on this PC (service or task missing), otherwise null.</summary>
        public virtual string UnavailableReason(ISystem sys) { return null; }
        /// <summary>Saves the current state so <see cref="Restore"/> can put it back exactly.</summary>
        public abstract Snapshot Capture(ISystem sys);
        public abstract void Apply(ISystem sys);
        /// <summary>Puts back exactly what <see cref="Capture"/> saved.</summary>
        public abstract void Restore(ISystem sys, Snapshot before);
        /// <summary>Returns to the Windows default (used when there is no history for this tweak on this PC).</summary>
        public abstract void RestoreDefault(ISystem sys);
        /// <summary>True when the change needs administrator rights (HKLM, services, tasks, power, most commands).</summary>
        public abstract bool NeedsAdmin { get; }
        /// <summary>When true, an unavailable change (missing service/task) is skipped instead of making the whole tweak unavailable.</summary>
        public bool Optional;

        /// <summary>Marks the change optional (fluent, for catalog entries).</summary>
        public Change AsOptional() { Optional = true; return this; }
    }
}
