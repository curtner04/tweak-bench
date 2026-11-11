using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>A page that wants buttons or switches on the right side of the page header (the window shows them).</summary>
    public interface IHeaderActions
    {
        System.Windows.FrameworkElement HeaderActions { get; }
    }

    /// <summary>
    /// The shared state of the tweak pages: the live status of every tweak (read on a background thread) and the single set of
    /// staged choices (tweak id -> wanted on/off) that cards, the pending bar, presets, profiles and the review dialog all use.
    /// All members except the status computation run on the UI thread.
    /// </summary>
    public sealed class TweakModel
    {
        readonly UiContext ctx;
        readonly Dictionary<string, bool> staged = new Dictionary<string, bool>();
        // Tweak id -> id of the tweak whose choice switched it off automatically (exclusive alternatives).
        readonly Dictionary<string, string> becauseOf = new Dictionary<string, string>();
        // Ids of tweaks that were just applied or undone and whose live state is being read again.
        readonly HashSet<string> updating = new HashSet<string>();
        Dictionary<string, TweakStatus> statuses = new Dictionary<string, TweakStatus>();
        bool computing, again;

        /// <summary>True once the statuses have been read at least once.</summary>
        public bool Ready { get; private set; }
        /// <summary>True while statuses are being read.</summary>
        public bool Computing { get { return computing; } }
        /// <summary>Raised on the UI thread when the staged set or the statuses changed.</summary>
        public event Action Changed = () => { };
        /// <summary>Raised on the UI thread after the statuses were read again (not for staging changes).</summary>
        public event Action StatusesRefreshed = () => { };

        public TweakModel(UiContext ctx) { this.ctx = ctx; }

        public TweakStatus StatusOf(Tweak t)
        {
            TweakStatus s;
            return statuses.TryGetValue(t.Id, out s) ? s : null;
        }

        // ---------------------------------------------------------------- staged set

        public int Count { get { return staged.Count; } }

        /// <summary>The wanted state when the tweak is staged, otherwise null.</summary>
        public bool? StagedValue(Tweak t)
        {
            bool v;
            return staged.TryGetValue(t.Id, out v) ? (bool?)v : null;
        }

        public bool IsStaged(Tweak t) { return staged.ContainsKey(t.Id); }

        /// <summary>What the switch of a card shows: the staged choice, otherwise the live state.</summary>
        public bool Desired(Tweak t)
        {
            bool? s = StagedValue(t);
            if (s.HasValue) return s.Value;
            TweakStatus st = StatusOf(t);
            return st != null && st.State == TweakState.On;
        }

        /// <summary>True when the tweak can be changed from here: its state is known, it applies to this PC and the app has the rights it needs.</summary>
        public bool CanStage(Tweak t)
        {
            TweakStatus st = StatusOf(t);
            if (st == null || st.State == TweakState.Unavailable) return false;
            if (t.NeedsAdmin && !ctx.IsElevated) return false;
            if (WrongUserReason(t) != null) return false;
            return true;
        }

        /// <summary>
        /// Why a tweak that only changes the signed-in user's own settings must not be changed from here (Tweak Bench runs as a different
        /// Windows account than the one signed in), otherwise null.
        /// </summary>
        public string WrongUserReason(Tweak t)
        {
            string reason = ctx.Engine == null ? null : ctx.Engine.WrongUserReason;
            if (string.IsNullOrEmpty(reason) || t.Changes.Count == 0) return null;
            foreach (Change c in t.Changes)
            {
                var r = c as RegValue;
                if (r == null || r.Hive != Hive.User) return null;
            }
            return reason;
        }

        /// <summary>The title of the tweak whose choice switched this one off automatically (only one alternative can be on), or null.</summary>
        public string BecauseOf(Tweak t)
        {
            string chooser;
            bool wanted, chosen;
            if (!becauseOf.TryGetValue(t.Id, out chooser)) return null;
            if (!staged.TryGetValue(t.Id, out wanted) || wanted) return null;
            if (!staged.TryGetValue(chooser, out chosen) || !chosen) return null;
            Tweak c = Catalog.Find(chooser);
            return c == null ? null : c.Title;
        }

        /// <summary>True while the live state of this tweak is being read again after it was applied or undone.</summary>
        public bool IsUpdating(Tweak t) { return updating.Contains(t.Id); }

        /// <summary>Marks tweaks as "being read again" until the next status read finishes (call before RefreshTweaks).</summary>
        public void MarkUpdating(IEnumerable<string> ids)
        {
            bool any = false;
            foreach (string id in ids) if (id != null && updating.Add(id)) any = true;
            if (any) Raise();
        }

        /// <summary>Stages "turn on" or "turn off". Choosing the state the tweak already has simply un-stages it. Alternatives in the same group are adjusted too.</summary>
        public void Stage(Tweak t, bool on)
        {
            StageQuietly(t, on);
            Raise();
        }

        void StageQuietly(Tweak t, bool on)
        {
            becauseOf.Remove(t.Id);
            TweakStatus st = StatusOf(t);
            bool alreadyThere = st != null && ((on && st.State == TweakState.On) || (!on && st.State == TweakState.Off));
            if (alreadyThere) staged.Remove(t.Id); else staged[t.Id] = on;
            if (t.ExclusiveGroup == null) return;
            foreach (Tweak other in Catalog.All.Where(o => o.ExclusiveGroup == t.ExclusiveGroup && o.Id != t.Id))
            {
                if (on)
                {
                    // Only one alternative can be selected: drop the others, and switch off the one that is on now.
                    staged.Remove(other.Id);
                    TweakStatus os = StatusOf(other);
                    if (os != null && os.State == TweakState.On && !(other.NeedsAdmin && !ctx.IsElevated)) { staged[other.Id] = false; becauseOf[other.Id] = t.Id; }
                }
                else
                {
                    // Taking back a choice also takes back the automatic "turn off" of the alternative that was on.
                    bool v;
                    if (staged.TryGetValue(other.Id, out v) && !v) staged.Remove(other.Id);
                }
            }
        }

        public void Unstage(Tweak t)
        {
            bool wanted;
            if (!staged.TryGetValue(t.Id, out wanted)) return;
            staged.Remove(t.Id);
            if (wanted && t.ExclusiveGroup != null)
            {
                // Taking back a choice also takes back the automatic "turn off" of the alternative that was on.
                foreach (Tweak other in Catalog.All.Where(o => o.ExclusiveGroup == t.ExclusiveGroup && o.Id != t.Id))
                {
                    bool v;
                    if (staged.TryGetValue(other.Id, out v) && !v) staged.Remove(other.Id);
                }
            }
            Raise();
        }

        public void Clear()
        {
            if (staged.Count == 0) return;
            staged.Clear();
            becauseOf.Clear();
            Raise();
        }

        /// <summary>
        /// Keeps at most one tweak per exclusive group (alternatives such as the power plans cannot be on together): the recommended one,
        /// otherwise the first in the order given. A group that already has a member switched on is left alone. Other tweaks pass through.
        /// </summary>
        List<Tweak> OnePerGroup(List<Tweak> candidates)
        {
            var best = new Dictionary<string, Tweak>();
            foreach (var group in candidates.Where(t => t.ExclusiveGroup != null).GroupBy(t => t.ExclusiveGroup))
            {
                string key = group.Key;
                bool anyOn = Catalog.All.Any(o => o.ExclusiveGroup == key && StatusOf(o) != null && StatusOf(o).State == TweakState.On);
                best[key] = anyOn ? null : group.OrderByDescending(t => t.Recommended).First();
            }
            return candidates.Where(t => t.ExclusiveGroup == null || ReferenceEquals(best[t.ExclusiveGroup], t)).ToList();
        }

        /// <summary>Stages every tweak of a list that can be turned on and is not on yet (one per exclusive group). Returns how many were staged.</summary>
        public int StageAllOn(IEnumerable<Tweak> tweaks)
        {
            int n = 0;
            var todo = new List<Tweak>();
            foreach (Tweak t in tweaks)
            {
                TweakStatus st = StatusOf(t);
                if (!CanStage(t) || st.State == TweakState.On) continue;
                todo.Add(t);
            }
            foreach (Tweak t in OnePerGroup(todo))
            {
                StageQuietly(t, true);
                n++;
            }
            if (n > 0) Raise();
            return n;
        }

        /// <summary>Stages a plan (for example from a profile).</summary>
        public int StagePlan(IEnumerable<PlanItem> plan)
        {
            int n = 0;
            foreach (PlanItem p in plan) { StageQuietly(p.Tweak, p.TurnOn); n++; }
            if (n > 0) Raise();
            return n;
        }

        /// <summary>The tweaks of a preset that would change when it is applied (not on yet, available here), and how many more need administrator rights.</summary>
        public List<Tweak> PresetTargets(string presetId, out int needAdmin)
        {
            needAdmin = 0;
            var list = new List<Tweak>();
            foreach (Tweak t in Catalog.InPreset(presetId))
            {
                TweakStatus st = StatusOf(t);
                if (st == null || st.State == TweakState.Unavailable || st.State == TweakState.On) continue;
                if (t.NeedsAdmin && !ctx.IsElevated) { needAdmin++; continue; }
                if (WrongUserReason(t) != null) continue;
                list.Add(t);
            }
            return OnePerGroup(list);
        }

        /// <summary>The staged changes in the order they are applied: catalog order, with "turn off" of exclusive alternatives first.</summary>
        public List<PlanItem> Plan()
        {
            var order = new Dictionary<string, int>();
            int i = 0;
            foreach (Tweak t in Catalog.All) order[t.Id] = i++;
            var items = new List<PlanItem>();
            foreach (var kv in staged)
            {
                Tweak t = Catalog.Find(kv.Key);
                if (t != null) items.Add(new PlanItem(t, kv.Value));
            }
            return items.OrderBy(p => p.TurnOn && p.Tweak.ExclusiveGroup != null ? 1 : 0).ThenBy(p => order[p.Tweak.Id]).ToList();
        }

        public int CountIn(string categoryId)
        {
            int n = 0;
            foreach (string id in staged.Keys)
            {
                Tweak t = Catalog.Find(id);
                if (t != null && t.Category == categoryId) n++;
            }
            return n;
        }

        /// <summary>Removes the staged entries of finished plan items (done, or already in the wanted state). Failed and skipped-for-rights ones stay staged.</summary>
        public void Completed(IEnumerable<ItemResult> results)
        {
            foreach (ItemResult r in results)
            {
                bool done = r.Ok;
                bool unavailable = r.Skipped && !r.Ok && (r.Message ?? "").IndexOf("administrator", StringComparison.OrdinalIgnoreCase) < 0;
                if (done || unavailable) staged.Remove(r.Item.Tweak.Id);
                if (r.Ok && !r.Skipped) updating.Add(r.Item.Tweak.Id);
            }
            Raise();
        }

        // ---------------------------------------------------------------- saving

        public void Save(AppSettings settings)
        {
            settings.PendingOn = staged.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            settings.PendingOff = staged.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        }

        public void Load(AppSettings settings)
        {
            staged.Clear();
            becauseOf.Clear();
            foreach (string id in settings.PendingOn) if (Catalog.Find(id) != null) staged[id] = true;
            foreach (string id in settings.PendingOff) if (Catalog.Find(id) != null) staged[id] = false;
        }

        // ---------------------------------------------------------------- statuses

        Dictionary<string, TweakStatus> Compute()
        {
            var d = new Dictionary<string, TweakStatus>();
            foreach (Tweak t in Catalog.All)
            {
                try { d[t.Id] = ctx.Engine.Status(t); }
                catch (Exception ex) { d[t.Id] = new TweakStatus { Tweak = t, State = TweakState.Unknown, Reason = "Could not read it: " + ex.Message }; }
            }
            return d;
        }

        /// <summary>Reads every tweak's state again. Asynchronous unless <paramref name="synchronous"/> (screenshot mode).</summary>
        public void Refresh(bool synchronous = false)
        {
            if (synchronous)
            {
                statuses = Compute();
                updating.Clear();
                Ready = true;
                Prune();
                Raise();
                StatusesRefreshed();
                return;
            }
            if (computing) { again = true; return; }
            computing = true;
            Raise();
            Task.Run(() =>
            {
                Dictionary<string, TweakStatus> d = null;
                try { d = Compute(); } catch (Exception) { }
                ctx.OnUi(() => Finish(d));
            });
        }

        void Finish(Dictionary<string, TweakStatus> d)
        {
            computing = false;
            if (d != null) { statuses = d; Ready = true; Prune(); }
            // A newer read is already queued: its result is the one that shows what the last change did.
            if (!again) updating.Clear();
            Raise();
            if (d != null) StatusesRefreshed();
            if (again) { again = false; Refresh(false); }
        }

        /// <summary>Drops staged choices that no longer make sense: the tweak is now in the wanted state, or does not apply to this PC.</summary>
        void Prune()
        {
            foreach (string id in staged.Keys.ToList())
            {
                Tweak t = Catalog.Find(id);
                TweakStatus st = t == null ? null : StatusOf(t);
                if (t == null) { staged.Remove(id); continue; }
                if (st == null) continue;
                bool on = staged[id];
                if (st.State == TweakState.Unavailable || (on && st.State == TweakState.On) || (!on && st.State == TweakState.Off)) staged.Remove(id);
            }
        }

        /// <summary>Tells the pages to refresh (for example after the "show advanced" option changed).</summary>
        public void Notify() { Raise(); }

        void Raise() { Changed(); }
    }
}
