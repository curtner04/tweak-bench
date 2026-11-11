using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TweakBench.Core
{
    public sealed class ChangeStatus
    {
        public Change Change;
        public bool? On;
        public string Current;
        /// <summary>Set when this change cannot be made here (skipped when optional).</summary>
        public string Unavailable;
    }

    public sealed class TweakStatus
    {
        public Tweak Tweak;
        public TweakState State;
        /// <summary>Why the tweak is unavailable or unknown.</summary>
        public string Reason;
        public List<ChangeStatus> Changes = new List<ChangeStatus>();
        /// <summary>History holds the values from before Tweak Bench turned it on, so "off" restores those exactly.</summary>
        public bool HasUndo;
    }

    public sealed class PlanItem
    {
        public Tweak Tweak;
        public bool TurnOn;
        public PlanItem() { }
        public PlanItem(Tweak tweak, bool turnOn) { Tweak = tweak; TurnOn = turnOn; }
        public override string ToString() { return (TurnOn ? "Turn on: " : "Turn off: ") + Tweak.Title; }
    }

    public sealed class ItemResult
    {
        public PlanItem Item;
        public bool Ok;
        /// <summary>Nothing was attempted (already in that state, unavailable, or needs administrator rights).</summary>
        public bool Skipped;
        public string Message;
        /// <summary>Set when the change was made but History could not be saved to disk (Undo then only works until Tweak Bench is closed).</summary>
        public string HistoryError;
    }

    public sealed class BatchResult
    {
        public HistoryBatch Batch;
        public List<ItemResult> Items = new List<ItemResult>();
        public bool NeedsExplorerRestart, NeedsSignOut, NeedsPcRestart;
        /// <summary>
        /// Set when the changes were made but history.json could not be saved. The batch is still in the in-memory History,
        /// so Undo works until Tweak Bench is closed. Show this text to the user.
        /// </summary>
        public string HistoryError;
        public int Succeeded { get { return Items.Count(i => i.Ok && !i.Skipped); } }
        public int Failed { get { return Items.Count(i => !i.Ok && !i.Skipped); } }
        public int SkippedCount { get { return Items.Count(i => i.Skipped); } }
    }

    /// <summary>
    /// Reads tweak states and applies or undoes them. Every action saves the exact previous state in history.json first,
    /// and a tweak whose changes fail part-way is rolled back to that state. Nothing here restarts Explorer or the PC.
    /// Thread-safe for one batch at a time (callers run batches on a background thread, one after another).
    /// </summary>
    public sealed class Engine
    {
        public readonly ISystem System;
        public readonly HistoryStore History;
        public string Machine = Environment.MachineName;
        /// <summary>Stable id of this PC (Windows MachineGuid), or null when unreadable. History matches on it, so renaming the PC keeps Undo working.</summary>
        public string MachineId;
        public string User = Environment.UserName;
        /// <summary>
        /// Set by the app when it runs as a different account than the one signed in (see SystemInfo.ElevatedAsOtherUser).
        /// While set, tweaks that change the current user's registry hive are skipped with this text. Null means fine.
        /// </summary>
        public string WrongUserReason;
        readonly Func<string, Tweak> find;
        readonly object gate = new object();

        public Engine(ISystem system, HistoryStore history, Func<string, Tweak> findTweak = null)
        {
            System = system;
            History = history;
            find = findTweak ?? Catalog.Find;
            MachineId = ReadMachineId(system);
        }

        /// <summary>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid (read-only), or null.</summary>
        public static string ReadMachineId(ISystem system)
        {
            try
            {
                RegData d = system == null ? null : system.ReadValue(Hive.Machine, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
                string id = d == null ? null : d.Data as string;
                return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            }
            catch (Exception) { return null; }
        }

        /// <summary>True when the batch was made on this PC: by PC id when both sides have one, otherwise by computer name.</summary>
        public bool IsThisPc(HistoryBatch batch)
        {
            if (batch == null) return false;
            if (!string.IsNullOrEmpty(MachineId) && !string.IsNullOrEmpty(batch.MachineId))
                return string.Equals(MachineId, batch.MachineId, StringComparison.OrdinalIgnoreCase);
            return string.Equals(batch.Machine, Machine, StringComparison.OrdinalIgnoreCase);
        }

        static string Now() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }

        public static string BuildText(int build) { return build >= 22000 ? "Windows 11" : "Windows build " + build; }

        public TweakStatus Status(Tweak t)
        {
            var st = new TweakStatus { Tweak = t };
            if (t.MinBuild > 0 && System.Build < t.MinBuild) { st.State = TweakState.Unavailable; st.Reason = "Needs " + BuildText(t.MinBuild) + (t.MinBuild == 22000 ? "" : " or newer") + "; this PC has build " + System.Build + "."; return st; }
            if (t.MaxBuild > 0 && System.Build > t.MaxBuild) { st.State = TweakState.Unavailable; st.Reason = "Only applies to Windows builds up to " + t.MaxBuild + "; this PC has build " + System.Build + "."; return st; }
            if (t.NotApplicable != null)
            {
                string reason = null;
                try { reason = t.NotApplicable(System); } catch (Exception ex) { reason = "Could not check: " + ex.Message; }
                if (reason != null) { st.State = TweakState.Unavailable; st.Reason = reason; return st; }
            }
            int on = 0, off = 0, unknown = 0;
            foreach (Change c in t.Changes)
            {
                var cs = new ChangeStatus { Change = c };
                st.Changes.Add(cs);
                try { cs.Unavailable = c.UnavailableReason(System); } catch (Exception ex) { cs.Unavailable = ex.Message; }
                if (cs.Unavailable != null)
                {
                    if (c.Optional) continue;
                    st.State = TweakState.Unavailable; st.Reason = cs.Unavailable; return st;
                }
                try { cs.On = c.IsOn(System); } catch (Exception) { cs.On = null; }
                try { cs.Current = c.DescribeCurrent(System); } catch (Exception ex) { cs.Current = "unknown (" + ex.Message + ")"; }
                if (cs.On == true) on++; else if (cs.On == false) off++; else unknown++;
            }
            int total = on + off + unknown;
            if (total == 0) { st.State = TweakState.Unavailable; st.Reason = st.Changes.Select(c => c.Unavailable).FirstOrDefault(r => r != null) ?? "Nothing to change on this PC."; return st; }
            if (on == total) st.State = TweakState.On;
            else if (off == total) st.State = TweakState.Off;
            else if (on == 0 && off == 0) { st.State = TweakState.Unknown; st.Reason = "Its current state could not be read."; }
            else if (on == 0) st.State = TweakState.Off;
            else st.State = TweakState.Partial;
            st.HasUndo = History != null && History.LatestOpenOn(t.Id, IsThisPc) != null;
            return st;
        }

        /// <summary>Changes that exist on this PC (optional ones that are unavailable are left out), in catalog order with their index.</summary>
        List<KeyValuePair<int, Change>> Available(Tweak t)
        {
            var list = new List<KeyValuePair<int, Change>>();
            for (int i = 0; i < t.Changes.Count; i++)
            {
                Change c = t.Changes[i];
                string reason = null;
                try { reason = c.UnavailableReason(System); } catch (Exception ex) { reason = ex.Message; }
                if (reason != null && c.Optional) continue;
                list.Add(new KeyValuePair<int, Change>(i, c));
            }
            return list;
        }

        List<Snapshot> CaptureAll(List<KeyValuePair<int, Change>> changes)
        {
            var snaps = new List<Snapshot>();
            foreach (var kv in changes)
            {
                Snapshot s = kv.Value.Capture(System);
                s.Index = kv.Key;
                s.Kind = kv.Value.Kind;
                if (string.IsNullOrEmpty(s.What)) s.What = kv.Value.Describe();
                snaps.Add(s);
            }
            return snaps;
        }

        /// <summary>Restores snapshots newest-first. Returns the errors (empty when everything was put back).</summary>
        List<string> RestoreAll(Tweak t, IList<Snapshot> snaps)
        {
            var errors = new List<string>();
            for (int i = snaps.Count - 1; i >= 0; i--)
            {
                Snapshot s = snaps[i];
                if (s.Index < 0 || s.Index >= t.Changes.Count || t.Changes[s.Index].Kind != s.Kind) { errors.Add("The saved state of \"" + s.What + "\" no longer matches this version of the tweak."); continue; }
                try { t.Changes[s.Index].Restore(System, s); }
                catch (Exception ex) { errors.Add(s.What + ": " + ex.Message); }
            }
            return errors;
        }

        /// <summary>Applies a plan in order. <paramref name="progress"/> gets (done, total, current title).</summary>
        public BatchResult Apply(IList<PlanItem> plan, string label, Action<int, int, string> progress = null)
        {
            lock (gate)
            {
                var result = new BatchResult();
                var batch = new HistoryBatch { Id = Guid.NewGuid().ToString("N"), TimeUtc = Now(), Label = label ?? "Applied changes", Machine = Machine, MachineId = MachineId, User = User };
                result.Batch = batch;
                var consumed = new List<HistoryEntry>();
                int done = 0;
                foreach (PlanItem item in plan)
                {
                    if (progress != null) progress(done, plan.Count, item.Tweak.Title);
                    ItemResult r = item.TurnOn ? TurnOn(item, batch) : TurnOff(item, batch, consumed);
                    result.Items.Add(r);
                    if (r.Ok && !r.Skipped)
                    {
                        if (item.Tweak.TakesEffect == Takes.ExplorerRestart) result.NeedsExplorerRestart = true;
                        if (item.Tweak.TakesEffect == Takes.SignOut) result.NeedsSignOut = true;
                        if (item.Tweak.TakesEffect == Takes.PcRestart) result.NeedsPcRestart = true;
                    }
                    done++;
                }
                if (progress != null) progress(done, plan.Count, null);
                if (batch.Entries.Count > 0 && History != null)
                {
                    foreach (HistoryEntry e in consumed) { e.UndoneUtc = batch.TimeUtc; e.UndoneBy = batch.Id; }
                    result.HistoryError = AddToHistory(batch);
                }
                return result;
            }
        }

        /// <summary>
        /// Adds the batch to History. If saving history.json fails, tries once more; if that fails too the batch stays in memory
        /// (so Undo still works this session) and the returned text says what happened. Null when saved.
        /// </summary>
        string AddToHistory(HistoryBatch batch)
        {
            try { History.Add(batch); return null; }
            catch (Exception first)
            {
                // Add put the batch in memory before the save failed: only the save needs another go.
                try { History.Save(); return null; }
                catch (Exception)
                {
                    return "The changes were made, but they could not be saved to History (" + first.Message + "). Undo only works until Tweak Bench is closed.";
                }
            }
        }

        static bool ChangesCurrentUser(Tweak t)
        {
            foreach (Change c in t.Changes) { var r = c as RegValue; if (r != null && r.Hive == Hive.User) return true; }
            return false;
        }

        ItemResult Precheck(PlanItem item)
        {
            TweakStatus st = Status(item.Tweak);
            if (st.State == TweakState.Unavailable) return new ItemResult { Item = item, Ok = false, Skipped = true, Message = st.Reason ?? "Not available on this PC." };
            if (!string.IsNullOrEmpty(WrongUserReason) && ChangesCurrentUser(item.Tweak)) return new ItemResult { Item = item, Ok = false, Skipped = true, Message = WrongUserReason };
            if (item.Tweak.NeedsAdmin && !System.IsElevated) return new ItemResult { Item = item, Ok = false, Skipped = true, Message = "Needs administrator rights. Choose \"Run as administrator\" and try again." };
            if (item.TurnOn && st.State == TweakState.On) return new ItemResult { Item = item, Ok = true, Skipped = true, Message = "Already on." };
            if (!item.TurnOn && st.State == TweakState.Off) return new ItemResult { Item = item, Ok = true, Skipped = true, Message = "Already off." };
            return null;
        }

        /// <summary>True for a snapshot that holds nothing Restore could use (the state could not be read before the change).</summary>
        static bool Unreadable(Snapshot s)
        {
            if (s.Kind == "custom") return s.Get("state") == "unknown";
            if (s.Kind == "power") return s.Get("ac") == null && s.Get("dc") == null;
            return false;
        }

        ItemResult TurnOn(PlanItem item, HistoryBatch batch)
        {
            ItemResult pre = Precheck(item);
            if (pre != null) return pre;
            Tweak t = item.Tweak;
            var entry = new HistoryEntry { TweakId = t.Id, Title = t.Title, Action = "on" };
            List<KeyValuePair<int, Change>> changes = Available(t);
            try { entry.Before = CaptureAll(changes); }
            catch (Exception ex) { return new ItemResult { Item = item, Ok = false, Message = "Could not read the current state, so nothing was changed: " + ex.Message }; }
            int applied = 0;
            try
            {
                foreach (var kv in changes) { applied++; kv.Value.Apply(System); }
                entry.Ok = true;
                batch.Entries.Add(entry);
                string note = entry.Before.Any(Unreadable) ? " Part of its previous state could not be read, so Undo cannot put that part back." : "";
                return new ItemResult { Item = item, Ok = true, Message = "Turned on." + note };
            }
            catch (Exception ex)
            {
                // Put back the parts already made, including the one that failed half-way.
                List<string> errors = RestoreAll(t, entry.Before.Take(applied).ToList());
                entry.Ok = false; entry.RolledBack = errors.Count == 0; entry.Error = ex.Message + (errors.Count > 0 ? " Rollback problems: " + string.Join("; ", errors) : "");
                batch.Entries.Add(entry);
                return new ItemResult { Item = item, Ok = false, Message = ex.Message + (errors.Count == 0 ? " Nothing was left changed." : " Some parts could not be put back: " + string.Join("; ", errors)) };
            }
        }

        ItemResult TurnOff(PlanItem item, HistoryBatch batch, List<HistoryEntry> consumed)
        {
            ItemResult pre = Precheck(item);
            if (pre != null) return pre;
            Tweak t = item.Tweak;
            var entry = new HistoryEntry { TweakId = t.Id, Title = t.Title, Action = "off" };
            List<KeyValuePair<int, Change>> changes = Available(t);
            try { entry.Before = CaptureAll(changes); }
            catch (Exception ex) { return new ItemResult { Item = item, Ok = false, Message = "Could not read the current state, so nothing was changed: " + ex.Message }; }
            HistoryEntry prior = History == null ? null : History.LatestOpenOn(t.Id, IsThisPc);
            try
            {
                string how;
                if (prior != null)
                {
                    List<string> errors = RestoreAll(t, prior.Before);
                    if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
                    // Parts added to the tweak after it was turned on have no saved value: give them the Windows default.
                    var covered = new HashSet<int>(prior.Before.Select(s => s.Index));
                    for (int i = changes.Count - 1; i >= 0; i--) if (!covered.Contains(changes[i].Key)) changes[i].Value.RestoreDefault(System);
                    consumed.Add(prior);
                    how = "Turned off (restored the values from before it was turned on).";
                }
                else
                {
                    for (int i = changes.Count - 1; i >= 0; i--) changes[i].Value.RestoreDefault(System);
                    how = "Turned off (Windows default restored).";
                }
                entry.Ok = true;
                batch.Entries.Add(entry);
                return new ItemResult { Item = item, Ok = true, Message = how };
            }
            catch (Exception ex)
            {
                List<string> errors = RestoreAll(t, entry.Before);
                entry.Ok = false; entry.RolledBack = errors.Count == 0; entry.Error = ex.Message + (errors.Count > 0 ? " Rollback problems: " + string.Join("; ", errors) : "");
                batch.Entries.Add(entry);
                return new ItemResult { Item = item, Ok = false, Message = ex.Message + (errors.Count == 0 ? " Nothing was left changed." : " Some parts could not be put back: " + string.Join("; ", errors)) };
            }
        }

        /// <summary>Why an entry cannot be undone here, or null when it can.</summary>
        public string CannotUndo(HistoryBatch batch, HistoryEntry entry)
        {
            if (entry.Action == "undo") return "This entry is itself an undo.";
            if (!entry.Ok) return entry.RolledBack ? "This change failed and was already put back." : "This change failed; there is nothing to undo.";
            if (entry.UndoneUtc != null) return "Already undone.";
            if (!IsThisPc(batch)) return "Made on " + batch.Machine + ". Undo is only possible on that PC.";
            Tweak t = find(entry.TweakId);
            if (t == null) return "This version of Tweak Bench no longer has that tweak.";
            if (t.NeedsAdmin && !System.IsElevated) return "Needs administrator rights.";
            return null;
        }

        /// <summary>Restores the state saved in <paramref name="entry"/> and records the undo as its own batch.</summary>
        public ItemResult Undo(HistoryBatch batch, HistoryEntry entry)
        {
            lock (gate)
            {
                Tweak t = find(entry.TweakId);
                var item = new PlanItem(t, entry.Action == "off");
                string why = CannotUndo(batch, entry);
                if (why != null) return new ItemResult { Item = item, Ok = false, Skipped = true, Message = why };
                var undo = new HistoryBatch { Id = Guid.NewGuid().ToString("N"), TimeUtc = Now(), Label = "Undo: " + entry.Title, Machine = Machine, MachineId = MachineId, User = User };
                var record = new HistoryEntry { TweakId = t.Id, Title = t.Title, Action = "undo" };
                try { record.Before = CaptureAll(Available(t)); } catch (Exception) { record.Before = new List<Snapshot>(); }
                List<string> errors = RestoreAll(t, entry.Before);
                record.Ok = errors.Count == 0;
                if (!record.Ok) record.Error = string.Join("; ", errors);
                undo.Entries.Add(record);
                if (record.Ok)
                {
                    entry.UndoneUtc = undo.TimeUtc; entry.UndoneBy = undo.Id;
                    // Undoing an "off" brings the tweak back on: the "on" entry that the "off" consumed is open again,
                    // so a later "off" restores the original pre-Tweak-Bench values rather than the Windows default.
                    if (entry.Action == "off" && History != null)
                        foreach (HistoryBatch b in History.Batches)
                            foreach (HistoryEntry e in b.Entries)
                                if (e.TweakId == entry.TweakId && e.Action == "on" && e.UndoneBy == batch.Id) { e.UndoneUtc = null; e.UndoneBy = null; }
                }
                string historyError = History == null ? null : AddToHistory(undo);
                return new ItemResult { Item = item, Ok = record.Ok, Message = record.Ok ? "Undone." : "Undo failed: " + record.Error, HistoryError = historyError };
            }
        }

        /// <summary>Undoes every undoable entry of a batch, newest first.</summary>
        public List<ItemResult> UndoBatch(HistoryBatch batch)
        {
            var results = new List<ItemResult>();
            for (int i = batch.Entries.Count - 1; i >= 0; i--)
            {
                HistoryEntry e = batch.Entries[i];
                if (!e.CanUndo) continue;
                results.Add(Undo(batch, e));
            }
            return results;
        }

        /// <summary>The plan that makes this PC match a profile (only tweaks whose state differs, in catalog order).</summary>
        public List<PlanItem> PlanFor(Profile profile)
        {
            var plan = new List<PlanItem>();
            foreach (ProfileItem p in profile.Tweaks)
            {
                Tweak t = find(p.Id);
                if (t == null) continue;
                TweakStatus st = Status(t);
                if (st.State == TweakState.Unavailable) continue;
                if (p.On && st.State != TweakState.On) plan.Add(new PlanItem(t, true));
                if (!p.On && st.State != TweakState.Off) plan.Add(new PlanItem(t, false));
            }
            return plan;
        }

        /// <summary>A profile of the tweaks that are currently on (and, optionally, the ones explicitly off).</summary>
        public Profile CaptureProfile(string name, IEnumerable<Tweak> tweaks, bool includeOff)
        {
            var p = new Profile { Name = name, CreatedUtc = Now(), Machine = Machine };
            foreach (Tweak t in tweaks)
            {
                TweakState s = Status(t).State;
                if (s == TweakState.On) p.Tweaks.Add(new ProfileItem { Id = t.Id, On = true });
                else if (includeOff && s == TweakState.Off) p.Tweaks.Add(new ProfileItem { Id = t.Id, On = false });
            }
            return p;
        }
    }
}
