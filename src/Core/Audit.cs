using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TweakBench.Core
{
    /// <summary>
    /// A read-only report of every tweak: its state on this PC and the current value of each change. It never writes
    /// anything (no registry, service, task or power change, no commands other than the read-only powercfg query).
    /// </summary>
    public static class Audit
    {
        public static void Write(ISystem sys, TextWriter w)
        {
            SystemInfo info = SystemInfo.Read(sys);
            w.WriteLine("Tweak Bench audit (read-only)");
            w.WriteLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            w.WriteLine("PC: " + info.ComputerName + "   User: " + info.UserName);
            w.WriteLine("Windows: " + info.ProductName + " " + info.DisplayVersion + " (" + info.Edition + "), build " + info.Build + "." + info.Ubr);
            w.WriteLine("Administrator rights: " + (info.IsElevated ? "yes" : "no") + "   Restart pending: " + (info.RestartPending ? "yes (" + string.Join(" ", info.RestartPendingReasons) + ")" : "no"));
            w.WriteLine();

            System.Collections.Generic.IList<Tweak> all;
            try { all = Catalog.All; }
            catch (Exception ex) { w.WriteLine("The tweak catalog could not be loaded: " + ex.Message); return; }

            // No history: the audit only reads.
            var engine = new Engine(sys, null);
            var counts = new int[Enum.GetValues(typeof(TweakState)).Length];
            string category = null;
            foreach (Tweak t in all)
            {
                if (t.Category != category)
                {
                    category = t.Category;
                    Category c = Catalog.FindCategory(category);
                    w.WriteLine("== " + (c == null ? category : c.Title) + " ==");
                }
                TweakStatus st;
                try { st = engine.Status(t); }
                catch (Exception ex) { st = new TweakStatus { Tweak = t, State = TweakState.Unknown, Reason = ex.Message }; }
                counts[(int)st.State]++;
                w.WriteLine(t.Id + "  [" + st.State + "]" + (st.Reason != null ? "  " + st.Reason : "") + (t.NeedsAdmin ? "  (needs administrator)" : ""));
                foreach (ChangeStatus cs in st.Changes)
                {
                    string flag = cs.Unavailable != null ? "skipped: " + cs.Unavailable : cs.On == true ? "on" : cs.On == false ? "off" : "unknown";
                    string now;
                    try { now = cs.Current ?? cs.Change.DescribeCurrent(sys); } catch (Exception ex) { now = "unknown (" + ex.Message + ")"; }
                    w.WriteLine("    - " + cs.Change.Describe());
                    w.WriteLine("        now: " + now + "   [" + flag + "]");
                }
            }

            w.WriteLine();
            w.WriteLine("Summary: " + all.Count + " tweaks");
            foreach (TweakState s in Enum.GetValues(typeof(TweakState)).Cast<TweakState>())
                w.WriteLine("  " + s + ": " + counts[(int)s]);
        }
    }
}
