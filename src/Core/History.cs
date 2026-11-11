using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TweakBench.Core
{
    /// <summary>One apply/undo session ("Applied 4 changes", "Undo: Show file extensions").</summary>
    public sealed class HistoryBatch
    {
        public string Id;
        /// <summary>UTC time, ISO 8601 ("o").</summary>
        public string TimeUtc;
        public string Label;
        /// <summary>Computer name the batch ran on. Undo is only offered on the same PC.</summary>
        public string Machine;
        /// <summary>Stable id of the PC (Windows MachineGuid) so renaming the PC does not disable Undo. Null in older files and when unreadable.</summary>
        public string MachineId;
        public string User;
        public List<HistoryEntry> Entries = new List<HistoryEntry>();

        public DateTime Time { get { DateTime t; return DateTime.TryParse(TimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t) ? t.ToLocalTime() : DateTime.MinValue; } }
    }

    /// <summary>One tweak turned on or off (or an undo), with the state saved just before it.</summary>
    public sealed class HistoryEntry
    {
        public string TweakId;
        public string Title;
        /// <summary>"on", "off" or "undo".</summary>
        public string Action;
        public bool Ok;
        public string Error;
        /// <summary>True when the change failed part-way and the parts already made were put back.</summary>
        public bool RolledBack;
        /// <summary>State of each change just before this action; undoing the entry restores these.</summary>
        public List<Snapshot> Before = new List<Snapshot>();
        /// <summary>Set when this entry was undone (or consumed by turning the tweak off again).</summary>
        public string UndoneUtc;
        /// <summary>Id of the batch that undid it.</summary>
        public string UndoneBy;

        public bool CanUndo { get { return Ok && UndoneUtc == null && Action != "undo" && Before != null && Before.Count > 0; } }
    }

    public sealed class HistoryFileData
    {
        public int Schema = 1;
        public List<HistoryBatch> Batches = new List<HistoryBatch>();
    }

    /// <summary>history.json: every change Tweak Bench made, newest last. Saved after each batch.</summary>
    public sealed class HistoryStore
    {
        public readonly string FilePath;
        public HistoryFileData Data = new HistoryFileData();
        /// <summary>Set when an unreadable history.json was set aside on load (it is renamed, never deleted).</summary>
        public string LoadWarning;
        readonly object gate = new object();

        public HistoryStore(string filePath) { FilePath = filePath; }

        public static HistoryStore Load() { return Load(Paths.HistoryFile); }

        public static HistoryStore Load(string filePath)
        {
            var store = new HistoryStore(filePath);
            string text = AtomicFile.ReadAllTextOrNull(filePath);
            if (text == null) return store;
            try
            {
                store.Data = Parse(text);
            }
            catch (Exception ex)
            {
                string aside = filePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                try { File.Move(filePath, aside); } catch (Exception) { aside = filePath; }
                // AtomicFile keeps the save before the last one as "*.previous": use it rather than losing every undo entry.
                HistoryFileData older = null;
                try
                {
                    string previous = AtomicFile.ReadAllTextOrNull(filePath + ".previous");
                    if (previous != null) older = Parse(previous);
                }
                catch (Exception) { }
                if (older != null)
                {
                    store.Data = older;
                    store.LoadWarning = "The newest change history could not be read (" + ex.Message + "). It was kept as " + aside + ", and the previous save was used instead, so the very last changes may be missing from History.";
                }
                else
                    store.LoadWarning = "The change history could not be read (" + ex.Message + "). It was kept as " + aside + " and a new history was started. Turning tweaks off still works; it restores Windows defaults.";
            }
            return store;
        }

        static HistoryFileData Parse(string text)
        {
            var data = Json.Deserialize<HistoryFileData>(text);
            if (data == null || data.Schema != 1 || data.Batches == null) throw new InvalidDataException("unsupported format");
            foreach (var b in data.Batches) { if (b.Entries == null) b.Entries = new List<HistoryEntry>(); foreach (var e in b.Entries) if (e.Before == null) e.Before = new List<Snapshot>(); }
            return data;
        }

        public IList<HistoryBatch> Batches { get { return Data.Batches; } }

        public void Add(HistoryBatch batch)
        {
            lock (gate) { Data.Batches.Add(batch); Save(); }
        }

        public void Save()
        {
            lock (gate) { AtomicFile.WriteAllText(FilePath, Json.SerializeIndented(Data)); }
        }

        /// <summary>The newest successful "on" entry for a tweak on this PC that has not been undone, with its batch.</summary>
        public HistoryEntry LatestOpenOn(string tweakId, string machine, out HistoryBatch batch)
        {
            return LatestOpenOn(tweakId, b => string.Equals(b.Machine, machine, StringComparison.OrdinalIgnoreCase), out batch);
        }

        /// <summary>As above, with the caller deciding which batches belong to this PC (see <see cref="Engine.IsThisPc"/>).</summary>
        public HistoryEntry LatestOpenOn(string tweakId, Func<HistoryBatch, bool> isThisPc, out HistoryBatch batch)
        {
            lock (gate)
            {
                for (int i = Data.Batches.Count - 1; i >= 0; i--)
                {
                    HistoryBatch b = Data.Batches[i];
                    if (!isThisPc(b)) continue;
                    for (int j = b.Entries.Count - 1; j >= 0; j--)
                    {
                        HistoryEntry e = b.Entries[j];
                        if (e.TweakId == tweakId && e.Action == "on" && e.Ok && e.UndoneUtc == null) { batch = b; return e; }
                    }
                }
            }
            batch = null;
            return null;
        }

        public HistoryEntry LatestOpenOn(string tweakId, string machine) { HistoryBatch b; return LatestOpenOn(tweakId, machine, out b); }

        public HistoryEntry LatestOpenOn(string tweakId, Func<HistoryBatch, bool> isThisPc) { HistoryBatch b; return LatestOpenOn(tweakId, isThisPc, out b); }

        public HistoryBatch FindBatch(string id) { lock (gate) return Data.Batches.FirstOrDefault(b => b.Id == id); }
    }

    /// <summary>A saved set of tweak choices ("my setup"). Applying it on any PC turns the listed tweaks on or off.</summary>
    public sealed class Profile
    {
        public int Schema = 1;
        public string Name;
        public string CreatedUtc;
        public string Machine;
        public List<ProfileItem> Tweaks = new List<ProfileItem>();
    }

    public sealed class ProfileItem
    {
        public string Id;
        public bool On;
    }

    /// <summary>Profiles live in data\profiles\*.json so Export / import configuration carries them.</summary>
    public sealed class ProfileStore
    {
        public readonly string Folder;
        public ProfileStore(string folder) { Folder = folder; }
        public ProfileStore() : this(Paths.ProfilesFolder) { }

        public static string SafeFileName(string name)
        {
            string s = new string((name ?? "").Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());
            if (s.Length == 0) s = "Profile";
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }

        public string PathFor(string name) { return Path.Combine(Folder, SafeFileName(name) + ".json"); }

        public List<Profile> List()
        {
            var list = new List<Profile>();
            if (!Directory.Exists(Folder)) return list;
            foreach (string file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try { list.Add(Read(file)); } catch (Exception) { }
            }
            return list;
        }

        public static Profile Read(string file)
        {
            var p = Json.Deserialize<Profile>(File.ReadAllText(file));
            if (p == null || p.Schema != 1 || p.Tweaks == null || p.Tweaks.Any(t => t == null || string.IsNullOrWhiteSpace(t.Id))) throw new InvalidDataException("Not a Tweak Bench profile: " + file);
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = Path.GetFileNameWithoutExtension(file);
            return p;
        }

        public void Save(Profile p) { AtomicFile.WriteAllText(PathFor(p.Name), Json.SerializeIndented(p)); }

        /// <summary>Writes a profile to any path (used for "Export profile..." to share with another PC).</summary>
        public static void SaveTo(Profile p, string file) { AtomicFile.WriteAllText(file, Json.SerializeIndented(p)); }

        public void Delete(string name)
        {
            string file = PathFor(name);
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
