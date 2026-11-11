using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TweakBench.Core
{
    /// <summary>
    /// Keeps the profile "My setup (automatic)" current: every tweak this PC's history touched, with its current on/off state.
    /// Export / import configuration carries profiles, so a restored configuration on a new PC has this profile ready to review
    /// and apply. Windows settings themselves are not copied by Export / import.
    /// </summary>
    public static class AutoProfile
    {
        public const string Name = "My setup (automatic)";

        public static void Update(Engine engine, ProfileStore profiles)
        {
            Update(engine, profiles, null);
        }

        /// <summary>Same as <see cref="Update(Engine, ProfileStore)"/> with a custom tweak lookup (tests). Never throws.</summary>
        public static void Update(Engine engine, ProfileStore profiles, Func<string, Tweak> find)
        {
            try
            {
                if (engine == null || profiles == null) return;
                find = find ?? Catalog.Find;

                var touched = new List<string>();
                if (engine.History != null)
                {
                    foreach (HistoryBatch b in engine.History.Batches.ToList())
                    {
                        if (!engine.IsThisPc(b)) continue;
                        foreach (HistoryEntry e in b.Entries.ToList())
                        {
                            if (!e.Ok || string.IsNullOrEmpty(e.TweakId)) continue;
                            if (e.Action != "on" && e.Action != "off" && e.Action != "undo") continue;
                            if (!touched.Contains(e.TweakId)) touched.Add(e.TweakId);
                        }
                    }
                }

                // A profile restored from another PC keeps its entries until this PC's own history takes them over.
                Profile existing = null;
                string file = profiles.PathFor(Name);
                if (File.Exists(file)) { try { existing = ProfileStore.Read(file); } catch (Exception) { } }
                bool fromOtherPc = existing != null && !string.IsNullOrEmpty(existing.Machine) && !string.Equals(existing.Machine, engine.Machine, StringComparison.OrdinalIgnoreCase);

                var items = new List<ProfileItem>();
                foreach (string id in touched)
                {
                    Tweak t = find(id);
                    if (t == null) continue;
                    TweakState state = engine.Status(t).State;
                    if (state == TweakState.On) items.Add(new ProfileItem { Id = id, On = true });
                    else if (state == TweakState.Off) items.Add(new ProfileItem { Id = id, On = false });
                }

                string machine = engine.Machine;
                if (fromOtherPc)
                {
                    var carried = existing.Tweaks.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id) && !touched.Contains(p.Id)).ToList();
                    if (carried.Count > 0)
                    {
                        items.AddRange(carried.Select(p => new ProfileItem { Id = p.Id, On = p.On }));
                        machine = existing.Machine;
                    }
                }

                if (items.Count == 0 && existing == null) return;

                var order = new List<string>();
                try { order = Catalog.All.Select(t => t.Id).ToList(); } catch (Exception) { }
                items = items.Select((p, i) => new { p, i, rank = order.IndexOf(p.Id) })
                             .OrderBy(x => x.rank < 0 ? int.MaxValue : x.rank).ThenBy(x => x.i).Select(x => x.p).ToList();

                var profile = new Profile
                {
                    Name = Name,
                    CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Machine = machine,
                    Tweaks = items,
                };
                profiles.Save(profile);
            }
            catch (Exception) { }
        }
    }
}
