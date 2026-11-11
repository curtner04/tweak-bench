using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TweakBench.Core;

namespace TweakBench.Tests
{
    /// <summary>Small helpers shared by the test suites (not itself a suite).</summary>
    public static class TestKit
    {
        public const string Key = @"Software\TweakBenchTest";

        public static Tweak Make(string id, params Change[] changes)
        {
            var t = new Tweak { Id = id, Category = id.Substring(0, id.IndexOf('.')), Title = "Test " + id, Summary = "A test tweak.", Why = "Because tests." };
            t.Changes.AddRange(changes);
            return t;
        }

        public static Func<string, Tweak> Finder(params Tweak[] tweaks)
        {
            return id => tweaks.FirstOrDefault(t => t.Id == id);
        }

        public static Engine NewEngine(MemorySystem sys, string historyFile, params Tweak[] tweaks)
        {
            return new Engine(sys, new HistoryStore(historyFile), Finder(tweaks)) { Machine = "PC-A", User = "tester" };
        }

        public static BatchResult On(Engine e, Tweak t) { return e.Apply(new[] { new PlanItem(t, true) }, "test on"); }
        public static BatchResult Off(Engine e, Tweak t) { return e.Apply(new[] { new PlanItem(t, false) }, "test off"); }

        public static uint? Dword(ISystem sys, string name, Hive hive = Hive.User, string path = Key)
        {
            RegData d = sys.ReadValue(hive, path, name);
            return d == null ? (uint?)null : (uint)d.Data;
        }

        /// <summary>The captured state of every available change of a tweak, as comparable text.</summary>
        public static string State(ISystem sys, Tweak t)
        {
            var parts = new List<string>();
            foreach (Change c in t.Changes)
            {
                string unavailable = null;
                try { unavailable = c.UnavailableReason(sys); } catch (Exception) { }
                if (unavailable != null && c.Optional) { parts.Add("(skipped)"); continue; }
                Snapshot s = c.Capture(sys);
                parts.Add(c.Kind + ":" + string.Join(",", s.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)));
            }
            return string.Join(" | ", parts);
        }
    }

    public static class EngineTests
    {
        public static void Run(T t)
        {
            Status(t);
            ApplyAndRestore(t);
            RestoreKinds(t);
            UndoAndConsume(t);
            Rollback(t);
            NeedsAdmin(t);
            MachineMismatch(t);
            Plans(t);
            AutoProfileTests(t);
            MemorySystemBehaviour(t);
            StrictTypes(t);
            WrongUser(t);
            HistorySaveFailure(t);
            UnreadableBeforeState(t);
            DeniedTask(t);
            PowerPlanChanged(t);
            RenamedPc(t);
            ProcessEncoding(t);
        }

        static string History(T t) { return Path.Combine(t.TempFolder(), "history.json"); }

        // ------------------------------------------------------------------------------------------------ status

        static void Status(T t)
        {
            var sys = new MemorySystem();
            Tweak simple = TestKit.Make("test.simple", Reg.Dword(Hive.User, TestKit.Key, "A", 1, 0));
            var engine = TestKit.NewEngine(sys, History(t), simple);

            t.Equal(TweakState.Off, engine.Status(simple).State, "absent value is Off");
            sys.Seed(Hive.User, TestKit.Key, "A", RegKind.Dword, 1u);
            t.Equal(TweakState.On, engine.Status(simple).State, "matching value is On");
            sys.Seed(Hive.User, TestKit.Key, "A", RegKind.Dword, 0u);
            t.Equal(TweakState.Off, engine.Status(simple).State, "other value is Off");
            sys.Seed(Hive.User, TestKit.Key, "A", RegKind.String, "1");
            t.Equal(TweakState.On, engine.Status(simple).State, "a number stored as text still counts");

            Tweak whenMissing = TestKit.Make("test.missing", Reg.Dword(Hive.User, TestKit.Key, "Absent", 1, 1).WhenMissing(1u));
            t.Equal(TweakState.On, engine.Status(whenMissing).State, "WhenMissing: absent value behaves as on");

            Tweak partial = TestKit.Make("test.partial", Reg.Dword(Hive.User, TestKit.Key, "P1", 1), Reg.Dword(Hive.User, TestKit.Key, "P2", 1));
            sys.Seed(Hive.User, TestKit.Key, "P1", RegKind.Dword, 1u);
            t.Equal(TweakState.Partial, engine.Status(partial).State, "one of two changes on is Partial");
            sys.Seed(Hive.User, TestKit.Key, "P2", RegKind.Dword, 1u);
            t.Equal(TweakState.On, engine.Status(partial).State, "both on is On");

            // Optional changes (services, tasks) that do not exist are skipped.
            Tweak optional = TestKit.Make("test.optional", Reg.Dword(Hive.User, TestKit.Key, "O", 1), Svc.Start("NoSuchService", StartMode.Disabled, StartMode.Manual), Sched.Disable(@"\Test\NoSuchTask"));
            TweakStatus os = engine.Status(optional);
            t.Equal(TweakState.Off, os.State, "optional missing service/task are ignored");
            t.Equal(3, os.Changes.Count, "all changes are listed");
            t.Check(os.Changes[1].Unavailable != null, "missing service reports a reason");

            Tweak onlyOptional = TestKit.Make("test.onlyoptional", Svc.Start("NoSuchService", StartMode.Disabled, StartMode.Manual));
            TweakStatus oo = engine.Status(onlyOptional);
            t.Equal(TweakState.Unavailable, oo.State, "only missing optional changes: Unavailable");
            t.Check(!string.IsNullOrEmpty(oo.Reason), "Unavailable has a reason");

            Tweak required = TestKit.Make("test.required", Custom.Make("Needs a thing", s => true, s => { }, s => { }, true, null, s => "The thing is missing."));
            TweakStatus rs = engine.Status(required);
            t.Equal(TweakState.Unavailable, rs.State, "required unavailable change makes the tweak Unavailable");
            t.Equal("The thing is missing.", rs.Reason, "reason is passed through");

            Tweak unknown = TestKit.Make("test.unknown", Custom.Make("Cannot tell", s => null, s => { }, s => { }));
            t.Equal(TweakState.Unknown, engine.Status(unknown).State, "unreadable state is Unknown");
            Tweak throwing = TestKit.Make("test.throwing", Custom.Make("Check throws", s => { throw new InvalidOperationException("boom"); }, s => { }, s => { }));
            t.Equal(TweakState.Unknown, engine.Status(throwing).State, "a throwing Check is Unknown, not a crash");
            Tweak offUnknown = TestKit.Make("test.offunknown", Reg.Dword(Hive.User, TestKit.Key, "OU", 1), Custom.Make("Cannot tell", s => null, s => { }, s => { }));
            t.Equal(TweakState.Off, engine.Status(offUnknown).State, "off plus unknown is Off");

            var old = new MemorySystem(build: 19045);
            var oldEngine = TestKit.NewEngine(old, History(t));
            Tweak win11 = TestKit.Make("test.win11", Reg.Dword(Hive.User, TestKit.Key, "W", 1)); win11.MinBuild = 22000;
            TweakStatus ws = oldEngine.Status(win11);
            t.Equal(TweakState.Unavailable, ws.State, "MinBuild: Windows 10 build is Unavailable");
            t.Check(ws.Reason != null && ws.Reason.Contains("Windows 11"), "MinBuild reason names Windows 11");
            Tweak legacy = TestKit.Make("test.legacy", Reg.Dword(Hive.User, TestKit.Key, "L", 1)); legacy.MaxBuild = 19045;
            t.Equal(TweakState.Unavailable, engine.Status(legacy).State, "MaxBuild: newer build is Unavailable");

            Tweak proOnly = TestKit.Make("test.pro", Reg.Dword(Hive.User, TestKit.Key, "PRO", 1));
            proOnly.NotApplicable = s => s.Edition == "Core" ? "Not in Windows Home." : null;
            t.Equal(TweakState.Off, engine.Status(proOnly).State, "NotApplicable returning null is available");
            t.Equal(TweakState.Unavailable, TestKit.NewEngine(new MemorySystem(edition: "Core"), History(t)).Status(proOnly).State, "NotApplicable returning a reason is Unavailable");
            proOnly.NotApplicable = s => { throw new InvalidOperationException("bad check"); };
            t.Equal(TweakState.Unavailable, engine.Status(proOnly).State, "a throwing NotApplicable is Unavailable");

            Tweak service = TestKit.Make("test.service", Svc.Start("TestSvc", StartMode.Disabled, StartMode.Manual));
            sys.AddService("TestSvc", StartMode.Manual, true);
            t.Equal(TweakState.Off, engine.Status(service).State, "service with the default start type is Off");
            sys.AddService("TestSvc", StartMode.Disabled, false);
            t.Equal(TweakState.On, engine.Status(service).State, "disabled service is On");

            Tweak task = TestKit.Make("test.task", Sched.Disable(@"\Test\Task1"));
            sys.AddTask(@"\Test\Task1", true);
            t.Equal(TweakState.Off, engine.Status(task).State, "enabled task is Off");
            sys.AddTask(@"\Test\Task1", false);
            t.Equal(TweakState.On, engine.Status(task).State, "disabled task is On");

            Tweak powerTweak = TestKit.Make("test.power", Power.Value("Test setting", "SUB_TEST", "SETTING_TEST", 0, 0, 1, 1));
            sys.AddPower("SUB_TEST", "SETTING_TEST", 1, 1);
            t.Equal(TweakState.Off, engine.Status(powerTweak).State, "power default is Off");
            sys.AddPower("SUB_TEST", "SETTING_TEST", 0, 0);
            t.Equal(TweakState.On, engine.Status(powerTweak).State, "power at the on value is On");

            t.Check(!engine.Status(simple).HasUndo, "no undo without history");
        }

        // ------------------------------------------------------------------------------------------------ apply and restore

        static void ApplyAndRestore(T t)
        {
            var sys = new MemorySystem();
            Tweak tw = TestKit.Make("test.apply", Reg.Dword(Hive.User, TestKit.Key, "A", 1, 0), Reg.String(Hive.User, TestKit.Key, "B", "on", "default"));
            string file = History(t);
            var engine = TestKit.NewEngine(sys, file, tw);
            sys.Seed(Hive.User, TestKit.Key, "A", RegKind.Dword, 7u);
            sys.Seed(Hive.User, TestKit.Key, "B", RegKind.String, "mine");

            BatchResult r = TestKit.On(engine, tw);
            t.Equal(1, r.Succeeded, "turning on succeeds");
            t.Equal(1u, TestKit.Dword(sys, "A").Value, "value A written");
            t.Equal("on", (string)sys.ReadValue(Hive.User, TestKit.Key, "B").Data, "value B written");
            t.Equal(1, engine.History.Batches.Count, "one batch in history");
            t.Equal("on", engine.History.Batches[0].Entries[0].Action, "entry action is on");
            t.Equal(2, engine.History.Batches[0].Entries[0].Before.Count, "both snapshots saved");
            t.Check(engine.Status(tw).HasUndo, "HasUndo after turning on");
            t.Check(engine.History.Batches[0].Entries[0].Before[0].Was == "7 (DWORD)", "snapshot describes the previous value");

            BatchResult again = TestKit.On(engine, tw);
            t.Equal(1, again.SkippedCount, "turning on twice is skipped");
            t.Equal(1, engine.History.Batches.Count, "skipped items add no history");

            BatchResult off = TestKit.Off(engine, tw);
            t.Equal(1, off.Succeeded, "turning off succeeds");
            t.Equal(7u, TestKit.Dword(sys, "A").Value, "off restores the exact previous number (not the Windows default)");
            t.Equal("mine", (string)sys.ReadValue(Hive.User, TestKit.Key, "B").Data, "off restores the exact previous text");
            t.Check(off.Items[0].Message.Contains("before"), "message says previous values were restored");
            t.Check(!engine.Status(tw).HasUndo, "the on entry is consumed by turning off");
            t.Check(engine.History.Batches[0].Entries[0].UndoneUtc != null, "on entry marked as consumed");

            // Persisted.
            var reloaded = HistoryStore.Load(file);
            t.Equal(2, reloaded.Batches.Count, "history saved to disk");
            t.Equal(2, reloaded.Batches[0].Entries[0].Before.Count, "snapshots saved to disk");

            // No history: Windows default (and deletion when there is none).
            var sys2 = new MemorySystem();
            var engine2 = TestKit.NewEngine(sys2, History(t), tw);
            sys2.Seed(Hive.User, TestKit.Key, "A", RegKind.Dword, 1u);
            sys2.Seed(Hive.User, TestKit.Key, "B", RegKind.String, "on");
            BatchResult d = TestKit.Off(engine2, tw);
            t.Equal(1, d.Succeeded, "off without history succeeds");
            t.Equal(0u, TestKit.Dword(sys2, "A").Value, "off without history writes the Windows default");
            t.Equal("default", (string)sys2.ReadValue(Hive.User, TestKit.Key, "B").Data, "off without history writes the text default");
            t.Check(d.Items[0].Message.Contains("default"), "message says the default was restored");

            // A value with no default is deleted when turned off without history.
            Tweak policy = TestKit.Make("test.policy", Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "TestPolicy", 1));
            var sys3 = new MemorySystem();
            var engine3 = TestKit.NewEngine(sys3, History(t), policy);
            sys3.Seed(Hive.Machine, Reg.AutoUpdatePolicy, "TestPolicy", RegKind.Dword, 1u);
            TestKit.Off(engine3, policy);
            t.Check(sys3.ReadValue(Hive.Machine, Reg.AutoUpdatePolicy, "TestPolicy") == null, "policy value without default is deleted");

            // DefaultDeletesKey removes the whole key.
            const string clsid = @"Software\Classes\CLSID\{test}\InprocServer32";
            Tweak menu = TestKit.Make("test.menu", Reg.KeyDefault(Hive.User, clsid, "", @"Software\Classes\CLSID\{test}"));
            var sys4 = new MemorySystem();
            var engine4 = TestKit.NewEngine(sys4, History(t), menu);
            TestKit.On(engine4, menu);
            t.Check(sys4.KeyExists(Hive.User, clsid), "key created by turning on");
            t.Equal(TweakState.On, engine4.Status(menu).State, "key-based tweak is On");
            TestKit.Off(engine4, menu);
            t.Check(!sys4.KeyExists(Hive.User, @"Software\Classes\CLSID\{test}"), "turning off removes the key tree it created");
        }

        static void RestoreKinds(T t)
        {
            var sys = new MemorySystem();
            string k = TestKit.Key + @"\Kinds";

            // Values and keys that did not exist are deleted again, keys Tweak Bench created are removed, existing parents stay.
            sys.Seed(Hive.User, TestKit.Key, "Keep", RegKind.Dword, 5u);
            Tweak fresh = TestKit.Make("test.fresh", Reg.Dword(Hive.User, k + @"\Deep\Er", "X", 1));
            var engine = TestKit.NewEngine(sys, History(t), fresh);
            TestKit.On(engine, fresh);
            t.Check(sys.KeyExists(Hive.User, k + @"\Deep\Er"), "keys created when turning on");
            TestKit.Off(engine, fresh);
            t.Check(!sys.KeyExists(Hive.User, k), "keys the tweak created are removed on off");
            t.Check(sys.KeyExists(Hive.User, TestKit.Key), "the parent that already existed stays");
            t.Equal(5u, TestKit.Dword(sys, "Keep").Value, "unrelated values stay");

            // A key that got other content in between is left alone.
            TestKit.On(engine, fresh);
            sys.Seed(Hive.User, k + @"\Deep", "Other", RegKind.Dword, 9u);
            TestKit.Off(engine, fresh);
            t.Check(sys.KeyExists(Hive.User, k + @"\Deep"), "a key with other values is not removed");
            t.Check(!sys.KeyExists(Hive.User, k + @"\Deep\Er"), "the empty key below it is removed");

            // Every registry kind round-trips exactly.
            var sys2 = new MemorySystem();
            sys2.Seed(Hive.User, k, "Q", RegKind.Qword, 0x1122334455667788UL);
            sys2.Seed(Hive.User, k, "M", RegKind.MultiString, new[] { "one", "two" });
            sys2.Seed(Hive.User, k, "B", RegKind.Binary, new byte[] { 1, 2, 3, 255 });
            sys2.Seed(Hive.User, k, "E", RegKind.ExpandString, "%SystemRoot%\\x");
            sys2.Seed(Hive.User, k, "S", RegKind.String, "quote \" and \\ slash");
            Tweak kinds = TestKit.Make("test.kinds",
                Reg.Qword(Hive.User, k, "Q", 1), Reg.Multi(Hive.User, k, "M", new[] { "x" }), Reg.Binary(Hive.User, k, "B", new byte[] { 0 }),
                Reg.Expand(Hive.User, k, "E", "y"), Reg.String(Hive.User, k, "S", "z"));
            var engine2 = TestKit.NewEngine(sys2, History(t), kinds);
            string before = TestKit.State(sys2, kinds);
            TestKit.On(engine2, kinds);
            t.Check(TestKit.State(sys2, kinds) != before, "state changed after turning on");
            // The history file round-trips through JSON before it is used.
            var reloaded = HistoryStore.Load(engine2.History.FilePath);
            var engine2b = new Engine(sys2, reloaded, TestKit.Finder(kinds)) { Machine = "PC-A", User = "tester" };
            TestKit.Off(engine2b, kinds);
            t.Equal(before, TestKit.State(sys2, kinds), "all registry kinds restored exactly through a saved history file");
            t.Equal(0x1122334455667788UL, (ulong)sys2.ReadValue(Hive.User, k, "Q").Data, "QWORD restored");
            t.Check(((string[])sys2.ReadValue(Hive.User, k, "M").Data).SequenceEqual(new[] { "one", "two" }), "multi-string restored");
            t.Check(((byte[])sys2.ReadValue(Hive.User, k, "B").Data).SequenceEqual(new byte[] { 1, 2, 3, 255 }), "binary restored");

            // Services, tasks and power values.
            var sys3 = new MemorySystem();
            sys3.AddService("TestSvc", StartMode.Automatic, true);
            sys3.AddTask(@"\Test\T", true);
            sys3.AddPower("SUB_TEST", "SET", 5, 7);
            Tweak other = TestKit.Make("test.other", Svc.Start("TestSvc", StartMode.Disabled, StartMode.Manual), Sched.Disable(@"\Test\T"), Power.Value("Test", "SUB_TEST", "SET", 0, 0, 1, 1));
            var engine3 = TestKit.NewEngine(sys3, History(t), other);
            TestKit.On(engine3, other);
            ServiceInfo si = sys3.GetService("TestSvc");
            t.Equal(StartMode.Disabled, si.Start, "service start type set");
            t.Check(!si.Running, "service stopped when disabled");
            t.Equal(false, sys3.GetTaskEnabled(@"\Test\T").Value, "task disabled");
            t.Equal(0u, sys3.GetPowerValue("SUB_TEST", "SET", true).Value, "power value set");
            TestKit.Off(engine3, other);
            si = sys3.GetService("TestSvc");
            t.Equal(StartMode.Automatic, si.Start, "service start type restored exactly");
            t.Check(si.Running, "service started again because it was running before");
            t.Equal(true, sys3.GetTaskEnabled(@"\Test\T").Value, "task re-enabled");
            t.Equal(5u, sys3.GetPowerValue("SUB_TEST", "SET", true).Value, "power value (plugged in) restored exactly");
            t.Equal(7u, sys3.GetPowerValue("SUB_TEST", "SET", false).Value, "power value (battery) restored exactly");

            // A custom change replays the state it saw.
            bool flag = true;
            Tweak custom = TestKit.Make("test.custom", Custom.Make("Flag", s => flag, s => flag = true, s => flag = false));
            flag = false;
            var engine4 = TestKit.NewEngine(new MemorySystem(), History(t), custom);
            TestKit.On(engine4, custom);
            t.Check(flag, "custom TurnOn ran");
            TestKit.Off(engine4, custom);
            t.Check(!flag, "custom change returns to the state captured before");
        }

        // ------------------------------------------------------------------------------------------------ undo

        static void UndoAndConsume(T t)
        {
            var sys = new MemorySystem();
            Tweak tw = TestKit.Make("test.undo", Reg.Dword(Hive.User, TestKit.Key, "U", 1, 0));
            var engine = TestKit.NewEngine(sys, History(t), tw);
            sys.Seed(Hive.User, TestKit.Key, "U", RegKind.Dword, 5u);

            // on -> off -> undo the off (back on) -> off again restores the original 5, not the default 0.
            BatchResult onBatch = TestKit.On(engine, tw);
            BatchResult offBatch = TestKit.Off(engine, tw);
            t.Equal(5u, TestKit.Dword(sys, "U").Value, "after off the original value is back");
            HistoryBatch hb = offBatch.Batch;
            HistoryEntry offEntry = hb.Entries[0];
            t.Check(engine.CannotUndo(hb, offEntry) == null, "the off entry can be undone");
            ItemResult undo = engine.Undo(hb, offEntry);
            t.Check(undo.Ok, "undo of off succeeds");
            t.Equal(1u, TestKit.Dword(sys, "U").Value, "undoing the off turns it back on");
            t.Equal(TweakState.On, engine.Status(tw).State, "state is On after undoing the off");
            t.Check(engine.Status(tw).HasUndo, "the on entry is open again");
            t.Check(engine.CannotUndo(hb, offEntry) != null, "an undone entry cannot be undone twice");
            ItemResult twice = engine.Undo(hb, offEntry);
            t.Check(twice.Skipped && !twice.Ok, "second undo is refused");
            TestKit.Off(engine, tw);
            t.Equal(5u, TestKit.Dword(sys, "U").Value, "off after undo-of-off still restores the original value");

            // Undo of an "on" entry.
            var sys2 = new MemorySystem();
            var engine2 = TestKit.NewEngine(sys2, History(t), tw);
            sys2.Seed(Hive.User, TestKit.Key, "U", RegKind.Dword, 3u);
            BatchResult b = TestKit.On(engine2, tw);
            ItemResult u2 = engine2.Undo(b.Batch, b.Batch.Entries[0]);
            t.Check(u2.Ok, "undo of on succeeds");
            t.Equal(3u, TestKit.Dword(sys2, "U").Value, "undo of on restores the previous value");
            t.Check(!engine2.Status(tw).HasUndo, "an undone on entry is no longer open");
            t.Equal("undo", engine2.History.Batches.Last().Entries[0].Action, "the undo is recorded in history");
            HistoryEntry undoEntry = engine2.History.Batches.Last().Entries[0];
            t.Check(engine2.CannotUndo(engine2.History.Batches.Last(), undoEntry) != null, "an undo entry cannot itself be undone");

            // UndoBatch: several tweaks, newest first.
            var sys3 = new MemorySystem();
            Tweak a = TestKit.Make("test.a", Reg.Dword(Hive.User, TestKit.Key, "Same", 1, 0));
            Tweak c = TestKit.Make("test.c", Reg.Dword(Hive.User, TestKit.Key, "Same", 2, 0));
            sys3.Seed(Hive.User, TestKit.Key, "Same", RegKind.Dword, 9u);
            var engine3 = TestKit.NewEngine(sys3, History(t), a, c);
            BatchResult both = engine3.Apply(new[] { new PlanItem(a, true), new PlanItem(c, true) }, "two");
            t.Equal(2u, TestKit.Dword(sys3, "Same").Value, "second tweak wrote last");
            List<ItemResult> results = engine3.UndoBatch(both.Batch);
            t.Equal(2, results.Count, "both entries undone");
            t.Equal(9u, TestKit.Dword(sys3, "Same").Value, "undoing newest first restores the very first value");

            // A tweak removed from the catalog cannot be undone.
            var engine4 = new Engine(sys3, engine3.History, id => null) { Machine = "PC-A" };
            HistoryEntry anyEntry = engine3.History.Batches[0].Entries[0];
            anyEntry.UndoneUtc = null;
            t.Check(engine4.CannotUndo(engine3.History.Batches[0], anyEntry).Contains("no longer"), "unknown tweak id cannot be undone");

            // Progress callbacks.
            var seen = new List<string>();
            var sys5 = new MemorySystem();
            var engine5 = TestKit.NewEngine(sys5, History(t), tw);
            engine5.Apply(new[] { new PlanItem(tw, true) }, "p", (done, total, title) => seen.Add(done + "/" + total + ":" + title));
            t.Equal(2, seen.Count, "progress reported before the item and at the end");
            t.Equal("1/1:", seen[1], "final progress has no title");
        }

        // ------------------------------------------------------------------------------------------------ rollback

        static void Rollback(T t)
        {
            var sys = new MemorySystem();
            sys.Seed(Hive.User, TestKit.Key, "R1", RegKind.Dword, 10u);
            sys.AddService("TestSvc", StartMode.Automatic, true);
            Tweak failing = TestKit.Make("test.rollback",
                Reg.Dword(Hive.User, TestKit.Key, "R1", 1, 0),
                Reg.Dword(Hive.User, TestKit.Key + @"\New", "R2", 1),
                Svc.Start("TestSvc", StartMode.Disabled, StartMode.Manual),
                Custom.Make("Always fails", s => false, s => { throw new InvalidOperationException("The command failed."); }, s => { }));
            var engine = TestKit.NewEngine(sys, History(t), failing);
            string before = TestKit.State(sys, failing);
            BatchResult r = TestKit.On(engine, failing);
            t.Equal(1, r.Failed, "the failing tweak is reported as failed");
            t.Check(r.Items[0].Message.Contains("command failed"), "the failure message is shown");
            t.Check(r.Items[0].Message.Contains("Nothing was left changed"), "the user is told nothing was left changed");
            t.Equal(before, TestKit.State(sys, failing), "everything made before the failure was put back");
            t.Check(!sys.KeyExists(Hive.User, TestKit.Key + @"\New"), "keys created before the failure were removed again");
            HistoryEntry e = engine.History.Batches[0].Entries[0];
            t.Check(!e.Ok && e.RolledBack, "history records a rolled back failure");
            t.Check(!e.CanUndo, "a rolled back entry cannot be undone");
            t.Check(!engine.Status(failing).HasUndo, "a failed attempt leaves no open undo");

            // A write that fails half-way (FailWhen) is rolled back too.
            var sys2 = new MemorySystem();
            sys2.Seed(Hive.User, TestKit.Key, "F1", RegKind.Dword, 4u);
            Tweak two = TestKit.Make("test.half", Reg.Dword(Hive.User, TestKit.Key, "F1", 1), Reg.Dword(Hive.User, TestKit.Key, "F2", 1));
            sys2.FailWhen = what => what.StartsWith("write reg", StringComparison.Ordinal) && what.EndsWith("F2", StringComparison.Ordinal);
            var engine2 = TestKit.NewEngine(sys2, History(t), two);
            BatchResult r2 = TestKit.On(engine2, two);
            t.Equal(1, r2.Failed, "partial failure is reported");
            t.Equal(4u, TestKit.Dword(sys2, "F1").Value, "the first write was undone");
            t.Check(TestKit.Dword(sys2, "F2") == null, "the failed write left nothing");

            // FailWrites: nothing can be written, nothing may change.
            var sys3 = new MemorySystem();
            sys3.Seed(Hive.User, TestKit.Key, "W", RegKind.Dword, 2u);
            Tweak one = TestKit.Make("test.denied", Reg.Dword(Hive.User, TestKit.Key, "W", 1));
            var engine3 = TestKit.NewEngine(sys3, History(t), one);
            sys3.FailWrites = true;
            BatchResult r3 = TestKit.On(engine3, one);
            t.Equal(1, r3.Failed, "FailWrites: the tweak fails");
            t.Equal(2u, TestKit.Dword(sys3, "W").Value, "FailWrites: value unchanged");
            t.Check(!r3.Items[0].Ok && !string.IsNullOrEmpty(r3.Items[0].Message), "FailWrites: the user gets a message");
            t.Check(!engine3.Status(one).HasUndo, "FailWrites: no open undo entry");

            // Failure while turning off restores the state from before the attempt.
            var sys4 = new MemorySystem();
            sys4.Seed(Hive.User, TestKit.Key, "Z1", RegKind.Dword, 6u);
            Tweak off2 = TestKit.Make("test.offfail", Reg.Dword(Hive.User, TestKit.Key, "Z1", 1, 0), Reg.Dword(Hive.User, TestKit.Key, "Z2", 1, 0));
            var engine4 = TestKit.NewEngine(sys4, History(t), off2);
            TestKit.On(engine4, off2);
            string onState = TestKit.State(sys4, off2);
            sys4.FailWhen = what => what.StartsWith("write reg", StringComparison.Ordinal) && what.EndsWith("Z1", StringComparison.Ordinal);
            BatchResult r4 = TestKit.Off(engine4, off2);
            t.Equal(1, r4.Failed, "a failing turn-off is reported");
            t.Equal(onState, TestKit.State(sys4, off2), "a failing turn-off leaves the tweak on");
            sys4.FailWhen = null;
            t.Check(engine4.Status(off2).HasUndo, "the on entry stays open after a failed off");
        }

        // ------------------------------------------------------------------------------------------------ admin and machine

        static void NeedsAdmin(T t)
        {
            var sys = new MemorySystem(elevated: false);
            Tweak machine = TestKit.Make("test.hklm", Reg.Dword(Hive.Machine, @"SOFTWARE\TweakBenchTest", "M", 1));
            Tweak user = TestKit.Make("test.hkcu", Reg.Dword(Hive.User, TestKit.Key, "M", 1));
            Tweak service = TestKit.Make("test.svc", Svc.Start("TestSvc", StartMode.Disabled, StartMode.Manual));
            sys.AddService("TestSvc", StartMode.Manual, false);
            var engine = TestKit.NewEngine(sys, History(t), machine, user, service);

            BatchResult r = engine.Apply(new[] { new PlanItem(machine, true), new PlanItem(user, true), new PlanItem(service, true) }, "mixed");
            t.Equal(2, r.SkippedCount, "admin-only items are skipped without administrator rights");
            t.Equal(1, r.Succeeded, "the user-level item still works");
            t.Check(r.Items[0].Message.Contains("administrator"), "the skip message mentions administrator rights");
            t.Check(sys.ReadValue(Hive.Machine, @"SOFTWARE\TweakBenchTest", "M") == null, "nothing written to HKLM");
            t.Equal(StartMode.Manual, sys.GetService("TestSvc").Start, "service untouched");
            t.Equal(1, engine.History.Batches[0].Entries.Count, "only the real change is in history");

            var none = TestKit.NewEngine(new MemorySystem(elevated: false), History(t), machine);
            BatchResult skipped = TestKit.On(none, machine);
            t.Equal(0, none.History.Batches.Count, "a batch with nothing done adds no history");
            t.Equal(1, skipped.SkippedCount, "skipped");

            var elevated = TestKit.NewEngine(new MemorySystem(elevated: true), History(t), machine);
            t.Equal(1, TestKit.On(elevated, machine).Succeeded, "elevated: the HKLM change is made");

            // Unavailable tweaks are skipped with their reason.
            Tweak missing = TestKit.Make("test.gone", Svc.Start("NoSuchService", StartMode.Disabled, StartMode.Manual));
            BatchResult m = TestKit.On(TestKit.NewEngine(new MemorySystem(), History(t), missing), missing);
            t.Equal(1, m.SkippedCount, "unavailable tweak is skipped");
            t.Check(m.Items[0].Message.Contains("NoSuchService"), "the reason names the missing service");
        }

        static void MachineMismatch(T t)
        {
            var sys = new MemorySystem();
            Tweak tw = TestKit.Make("test.machine", Reg.Dword(Hive.User, TestKit.Key, "MM", 1, 0));
            string file = History(t);
            var pcA = TestKit.NewEngine(sys, file, tw);
            sys.Seed(Hive.User, TestKit.Key, "MM", RegKind.Dword, 3u);
            BatchResult b = TestKit.On(pcA, tw);

            var reloaded = HistoryStore.Load(file);
            var pcB = new Engine(sys, reloaded, TestKit.Finder(tw)) { Machine = "PC-B", User = "tester" };
            HistoryBatch batch = reloaded.Batches[0];
            string why = pcB.CannotUndo(batch, batch.Entries[0]);
            t.Check(why != null && why.Contains("PC-A"), "undo on another PC is refused and names the original PC");
            ItemResult refused = pcB.Undo(batch, batch.Entries[0]);
            t.Check(refused.Skipped && !refused.Ok, "Undo returns a skipped result");
            t.Equal(1u, TestKit.Dword(sys, "MM").Value, "nothing was changed by the refused undo");
            t.Check(!pcB.Status(tw).HasUndo, "the other PC's history is not an undo here");

            // On PC-B, turning it off uses the Windows default, not PC-A's saved values.
            TestKit.Off(pcB, tw);
            t.Equal(0u, TestKit.Dword(sys, "MM").Value, "off on another PC restores the Windows default");

            t.Equal(null, pcA.CannotUndo(batch, batch.Entries[0]), "the same PC can undo");
            var ci = new Engine(sys, reloaded, TestKit.Finder(tw)) { Machine = "pc-a" };
            t.Equal(null, ci.CannotUndo(batch, batch.Entries[0]), "computer names compare case-insensitively");
        }

        // ------------------------------------------------------------------------------------------------ review fixes

        static void StrictTypes(T t)
        {
            var sys = new MemorySystem();
            string policy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
            Tweak pol = TestKit.Make("test.policy", Reg.Dword(Hive.Machine, policy, "NoAutoRebootWithLoggedOnUsers", 1));
            var engine = TestKit.NewEngine(sys, History(t), pol);
            sys.Seed(Hive.Machine, policy, "NoAutoRebootWithLoggedOnUsers", RegKind.String, "1");
            t.Equal(TweakState.Off, engine.Status(pol).State, "a policy stored as text is not on (Windows ignores it)");
            BatchResult r = TestKit.On(engine, pol);
            t.Check(r.Items[0].Ok && !r.Items[0].Skipped, "applying rewrites the wrongly typed policy");
            RegData d = sys.ReadValue(Hive.Machine, policy, "NoAutoRebootWithLoggedOnUsers");
            t.Check(d != null && d.Kind == RegKind.Dword && (uint)d.Data == 1u, "the policy is now a real DWORD");
            TestKit.Off(engine, pol);
            RegData back = sys.ReadValue(Hive.Machine, policy, "NoAutoRebootWithLoggedOnUsers");
            t.Check(back != null && back.Kind == RegKind.String && (string)back.Data == "1", "turning off restores the text value exactly");

            Tweak machineValue = TestKit.Make("test.hklm", Reg.Dword(Hive.Machine, @"SOFTWARE\TweakBenchTest", "M", 1));
            sys.Seed(Hive.Machine, @"SOFTWARE\TweakBenchTest", "M", RegKind.String, "1");
            t.Equal(TweakState.Off, engine.Status(machineValue).State, "system-wide values must have the right type too");
            Tweak userValue = TestKit.Make("test.hkcu", Reg.Dword(Hive.User, TestKit.Key, "U", 1));
            sys.Seed(Hive.User, TestKit.Key, "U", RegKind.String, "1");
            t.Equal(TweakState.On, engine.Status(userValue).State, "per-user values still accept a number stored as text");
        }

        static void WrongUser(T t)
        {
            var sys = new MemorySystem();
            Tweak user = TestKit.Make("test.user", Reg.Dword(Hive.User, TestKit.Key, "WU", 1, 0));
            Tweak machine = TestKit.Make("test.machine2", Reg.Dword(Hive.Machine, @"SOFTWARE\TweakBenchTest", "WM", 1, 0));
            var engine = TestKit.NewEngine(sys, History(t), user, machine);
            t.Equal(null, engine.WrongUserReason, "WrongUserReason starts empty");
            engine.WrongUserReason = "Running as a different account.";
            BatchResult r = engine.Apply(new[] { new PlanItem(user, true), new PlanItem(machine, true) }, "wrong user");
            t.Check(r.Items[0].Skipped && r.Items[0].Message == "Running as a different account.", "a current-user tweak is skipped with the reason");
            t.Check(!sys.KeyExists(Hive.User, TestKit.Key), "nothing was written to the current user's hive");
            t.Check(r.Items[1].Ok && !r.Items[1].Skipped, "a system-wide tweak still applies");
            engine.WrongUserReason = null;
            t.Check(TestKit.On(engine, user).Items[0].Ok, "once cleared the tweak applies");
        }

        static void HistorySaveFailure(T t)
        {
            var sys = new MemorySystem();
            string folder = t.TempFolder();
            string blocker = Path.Combine(folder, "blocker");
            File.WriteAllText(blocker, "a file where a folder is needed");
            string file = Path.Combine(blocker, "history.json"); // cannot be created: "blocker" is a file
            Tweak tw = TestKit.Make("test.histfail", Reg.Dword(Hive.User, TestKit.Key, "HF", 1, 0));
            var engine = TestKit.NewEngine(sys, file, tw);
            BatchResult r = TestKit.On(engine, tw);
            t.Check(r.Items.Count == 1 && r.Items[0].Ok, "the batch result is still returned");
            t.Equal(1u, TestKit.Dword(sys, "HF").Value, "the change was made");
            t.Check(r.HistoryError != null && r.HistoryError.Contains("could not be saved"), "the failure is reported");
            t.Equal(1, engine.History.Batches.Count, "the batch stays in memory");
            HistoryBatch b = engine.History.Batches[0];
            ItemResult u = engine.Undo(b, b.Entries[0]);
            t.Check(u.Ok, "undo still works this session");
            t.Check(u.HistoryError != null, "the undo reports the failed save too");
            t.Check(TestKit.Dword(sys, "HF") == null, "undo removed the value it had added");
        }

        static void UnreadableBeforeState(T t)
        {
            var sys = new MemorySystem();
            bool flag = false;
            Tweak tw = TestKit.Make("test.unknown", Custom.Make("Unreadable thing", s => null, s => flag = true, s => flag = false));
            var engine = TestKit.NewEngine(sys, History(t), tw);
            BatchResult r = TestKit.On(engine, tw);
            t.Check(r.Items[0].Ok && r.Items[0].Message.Contains("could not be read"), "the user is told Undo cannot restore an unreadable state");
            HistoryBatch b = engine.History.Batches[0];
            ItemResult u = engine.Undo(b, b.Entries[0]);
            t.Check(!u.Ok, "undo does not claim success when nothing could be restored");
            t.Check(u.Message.Contains("could not be read") && flag, "undo says why, and the change is still in place");

            var sys2 = new MemorySystem();
            Tweak pw = TestKit.Make("test.power-unknown", Power.Value("Unknown", "SUB_NONE", "SET_NONE", 0, 0, 1, 1));
            var snap = new Snapshot { Kind = "power" };
            t.Throws<InvalidOperationException>(() => pw.Changes[0].Restore(sys2, snap), "a power snapshot with no values cannot be restored");
        }

        /// <summary>Wraps a MemorySystem and makes reading one scheduled task fail with "access denied".</summary>
        sealed class DenyingSystem : ISystem
        {
            readonly ISystem inner;
            public string DeniedTask;
            public DenyingSystem(ISystem inner) { this.inner = inner; }
            public bool IsElevated { get { return inner.IsElevated; } }
            public int Build { get { return inner.Build; } }
            public string Edition { get { return inner.Edition; } }
            public RegData ReadValue(Hive hive, string path, string name) { return inner.ReadValue(hive, path, name); }
            public bool KeyExists(Hive hive, string path) { return inner.KeyExists(hive, path); }
            public bool KeyIsEmpty(Hive hive, string path) { return inner.KeyIsEmpty(hive, path); }
            public void WriteValue(Hive hive, string path, string name, RegKind kind, object data) { inner.WriteValue(hive, path, name, kind, data); }
            public void DeleteValue(Hive hive, string path, string name) { inner.DeleteValue(hive, path, name); }
            public void DeleteKey(Hive hive, string path) { inner.DeleteKey(hive, path); }
            public ServiceInfo GetService(string name) { return inner.GetService(name); }
            public void SetServiceStart(string name, StartMode mode) { inner.SetServiceStart(name, mode); }
            public void StartService(string name) { inner.StartService(name); }
            public void StopService(string name) { inner.StopService(name); }
            public bool? GetTaskEnabled(string path)
            {
                if (path == DeniedTask) throw new UnauthorizedAccessException("Windows did not allow reading the scheduled task " + path + ".");
                return inner.GetTaskEnabled(path);
            }
            public void SetTaskEnabled(string path, bool enabled) { inner.SetTaskEnabled(path, enabled); }
            public uint? GetPowerValue(string subgroup, string setting, bool ac) { return inner.GetPowerValue(subgroup, setting, ac); }
            public void SetPowerValue(string subgroup, string setting, bool ac, uint value) { inner.SetPowerValue(subgroup, setting, ac, value); }
            public ProcessResult Run(string file, string arguments, int timeoutMs = 120000) { return inner.Run(file, arguments, timeoutMs); }
        }

        static void DeniedTask(T t)
        {
            var mem = new MemorySystem();
            mem.AddTask(@"\Test\Open", true);
            var sys = new DenyingSystem(mem) { DeniedTask = @"\Test\Locked" };
            Tweak tw = TestKit.Make("test.denied", Sched.Disable(@"\Test\Open"), Sched.Disable(@"\Test\Locked"));
            var engine = new Engine(sys, new HistoryStore(History(t)), TestKit.Finder(tw)) { Machine = "PC-A", User = "tester" };
            TweakStatus st = engine.Status(tw);
            t.Equal(2, st.Changes.Count, "both tasks are kept in the status");
            t.Check(st.Changes.All(c => c.Unavailable == null), "an unreadable task is not treated as missing");
            t.Check(st.State != TweakState.On, "the tweak is not reported On while one task cannot be read");
            BatchResult r = TestKit.On(engine, tw);
            t.Check(!r.Items[0].Ok && r.Items[0].Message.Contains("nothing was changed"), "nothing is changed when a task cannot be read first");
            t.Equal((bool?)true, mem.GetTaskEnabled(@"\Test\Open"), "the readable task was left alone");
        }

        static void PowerPlanChanged(T t)
        {
            var sys = new MemorySystem();
            sys.AddPower("SUB_TEST", "SETTING_PLAN", 1, 1);
            Tweak tw = TestKit.Make("test.plan", Power.Value("Plan test", "SUB_TEST", "SETTING_PLAN", 0, 0, 1, 1));
            var engine = TestKit.NewEngine(sys, History(t), tw);
            TestKit.On(engine, tw);
            HistoryBatch b = engine.History.Batches[0];
            t.Check(b.Entries[0].Before[0].Get("scheme") != null, "the power plan is recorded");
            // Switch to a different plan before undoing.
            string[] known = { "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "381b4222-f694-41f0-9685-ff5bb260df2e" };
            string now = sys.Run(ProcessRunner.System32("powercfg.exe"), "/getactivescheme").Output;
            string other = known.First(g => now.IndexOf(g, StringComparison.OrdinalIgnoreCase) < 0);
            sys.Run(ProcessRunner.System32("powercfg.exe"), "/setactive " + other);
            ItemResult refused = engine.Undo(b, b.Entries[0]);
            t.Check(!refused.Ok && refused.Message.Contains("power plan"), "undo refuses to write into a different power plan");
            t.Equal(0u, sys.GetPowerValue("SUB_TEST", "SETTING_PLAN", true).Value, "the other plan was not touched");
        }

        static void RenamedPc(T t)
        {
            var sys = new MemorySystem();
            sys.Seed(Hive.Machine, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid", RegKind.String, "11111111-2222-3333-4444-555555555555");
            Tweak tw = TestKit.Make("test.rename", Reg.Dword(Hive.User, TestKit.Key, "RN", 1, 0));
            string file = History(t);
            var before = TestKit.NewEngine(sys, file, tw);
            t.Equal("11111111-2222-3333-4444-555555555555", before.MachineId, "the engine reads the machine id");
            sys.Seed(Hive.User, TestKit.Key, "RN", RegKind.Dword, 3u);
            TestKit.On(before, tw);

            var reloaded = HistoryStore.Load(file);
            t.Equal(before.MachineId, reloaded.Batches[0].MachineId, "the batch stores the machine id");
            var renamed = new Engine(sys, reloaded, TestKit.Finder(tw)) { Machine = "PC-NEW-NAME", User = "tester" };
            HistoryBatch batch = reloaded.Batches[0];
            t.Equal(null, renamed.CannotUndo(batch, batch.Entries[0]), "a renamed PC can still undo");
            t.Check(renamed.Status(tw).HasUndo, "a renamed PC still has the saved values");

            var other = new MemorySystem();
            other.Seed(Hive.Machine, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid", RegKind.String, "99999999-2222-3333-4444-555555555555");
            var elsewhere = new Engine(other, reloaded, TestKit.Finder(tw)) { Machine = "PC-A", User = "tester" };
            t.Check(elsewhere.CannotUndo(batch, batch.Entries[0]) != null, "another PC with the same name is still refused");

            // An older history file has no machine id: the computer name decides.
            batch.MachineId = null;
            var sameName = new Engine(sys, reloaded, TestKit.Finder(tw)) { Machine = "PC-A", User = "tester" };
            t.Equal(null, sameName.CannotUndo(batch, batch.Entries[0]), "an old batch without an id matches by name");
            var diffName = new Engine(sys, reloaded, TestKit.Finder(tw)) { Machine = "PC-Z", User = "tester" };
            t.Check(diffName.CannotUndo(batch, batch.Entries[0]) != null, "an old batch on another name is refused");
        }

        static void ProcessEncoding(T t)
        {
            t.Check(ProcessRunner.Oem != null, "the OEM encoding resolves");
            t.Check(ProcessRunner.IsPowerShell(ProcessRunner.PowerShell) && !ProcessRunner.IsPowerShell(@"C:\Windows\System32\cmd.exe"), "PowerShell is recognised by file name");
        }

        // ------------------------------------------------------------------------------------------------ plans and profiles

        static void Plans(T t)
        {
            var sys = new MemorySystem();
            Tweak a = TestKit.Make("test.p1", Reg.Dword(Hive.User, TestKit.Key, "P1", 1, 0));
            Tweak b = TestKit.Make("test.p2", Reg.Dword(Hive.User, TestKit.Key, "P2", 1, 0));
            Tweak c = TestKit.Make("test.p3", Reg.Dword(Hive.User, TestKit.Key, "P3", 1, 0));
            Tweak gone = TestKit.Make("test.p4", Svc.Start("NoSuchService", StartMode.Disabled, StartMode.Manual));
            sys.Seed(Hive.User, TestKit.Key, "P1", RegKind.Dword, 1u);
            sys.Seed(Hive.User, TestKit.Key, "P2", RegKind.Dword, 0u);
            var engine = TestKit.NewEngine(sys, History(t), a, b, c, gone);

            Profile p = engine.CaptureProfile("Mine", new[] { a, b, c, gone }, false);
            t.Equal(1, p.Tweaks.Count, "CaptureProfile lists only tweaks that are on");
            t.Equal("test.p1", p.Tweaks[0].Id, "the on tweak is listed");
            t.Equal("PC-A", p.Machine, "profile records the PC");
            Profile withOff = engine.CaptureProfile("Mine", new[] { a, b, c, gone }, true);
            t.Equal(3, withOff.Tweaks.Count, "includeOff adds tweaks that are off (not unavailable ones)");
            t.Check(withOff.Tweaks.Single(x => x.Id == "test.p2").On == false, "off entries have On = false");

            var profile = new Profile { Name = "Plan", Tweaks = new List<ProfileItem>
            {
                new ProfileItem { Id = "test.p1", On = true },     // already on: no plan
                new ProfileItem { Id = "test.p2", On = true },     // off -> turn on
                new ProfileItem { Id = "test.p3", On = false },    // already off: no plan
                new ProfileItem { Id = "test.p1", On = false },    // contradicts the first: turn off
                new ProfileItem { Id = "test.p4", On = true },     // unavailable: skipped
                new ProfileItem { Id = "test.unknown", On = true } // not in this version: skipped
            } };
            List<PlanItem> plan = engine.PlanFor(profile);
            t.Equal(2, plan.Count, "PlanFor includes only differences");
            t.Check(plan.Any(x => x.Tweak.Id == "test.p2" && x.TurnOn), "plan turns p2 on");
            t.Check(plan.Any(x => x.Tweak.Id == "test.p1" && !x.TurnOn), "plan turns p1 off");
            t.Equal("Turn on: Test test.p2", plan.First(x => x.Tweak.Id == "test.p2").ToString(), "PlanItem text");
        }

        static void AutoProfileTests(T t)
        {
            var sys = new MemorySystem();
            Tweak a = TestKit.Make("test.ap1", Reg.Dword(Hive.User, TestKit.Key, "AP1", 1, 0));
            Tweak b = TestKit.Make("test.ap2", Reg.Dword(Hive.User, TestKit.Key, "AP2", 1, 0));
            Tweak c = TestKit.Make("test.ap3", Reg.Dword(Hive.User, TestKit.Key, "AP3", 1, 0), Reg.Dword(Hive.User, TestKit.Key, "AP3b", 1, 0));
            Tweak untouched = TestKit.Make("test.ap4", Reg.Dword(Hive.User, TestKit.Key, "AP4", 1, 0));
            var engine = TestKit.NewEngine(sys, History(t), a, b, c, untouched);
            var profiles = new ProfileStore(Path.Combine(t.TempFolder(), "profiles"));
            Func<string, Tweak> find = TestKit.Finder(a, b, c, untouched);

            AutoProfile.Update(engine, profiles, find);
            t.Equal(0, profiles.List().Count, "no history: no profile is written");

            TestKit.On(engine, a);
            TestKit.On(engine, b);
            TestKit.Off(engine, b);
            sys.Seed(Hive.User, TestKit.Key, "AP3", RegKind.Dword, 1u);      // c becomes Partial
            TestKit.On(engine, c);                                           // already partial: applied
            sys.Seed(Hive.User, TestKit.Key, "AP3b", RegKind.Dword, 0u);     // and drifts back to partial
            AutoProfile.Update(engine, profiles, find);
            List<Profile> list = profiles.List();
            t.Equal(1, list.Count, "one automatic profile");
            Profile auto = list[0];
            t.Equal(AutoProfile.Name, auto.Name, "profile name");
            t.Equal("PC-A", auto.Machine, "profile records the PC");
            t.Check(auto.Tweaks.Any(x => x.Id == "test.ap1" && x.On), "a tweak that is on is listed as on");
            t.Check(auto.Tweaks.Any(x => x.Id == "test.ap2" && !x.On), "a tweak turned off again is listed as off");
            t.Check(!auto.Tweaks.Any(x => x.Id == "test.ap3"), "partial tweaks are skipped");
            t.Check(!auto.Tweaks.Any(x => x.Id == "test.ap4"), "tweaks never touched are not listed");

            // Undo entries count as touches and the profile follows the current state.
            BatchResult oneMore = TestKit.On(engine, untouched);
            engine.Undo(oneMore.Batch, oneMore.Batch.Entries[0]);
            AutoProfile.Update(engine, profiles, find);
            auto = profiles.List().Single();
            t.Check(auto.Tweaks.Any(x => x.Id == "test.ap4" && !x.On), "an undone tweak is listed with its current (off) state");

            // History of another PC is ignored.
            var otherSys = new MemorySystem();
            var other = new Engine(otherSys, new HistoryStore(Path.Combine(t.TempFolder(), "h.json")), find) { Machine = "PC-Z", User = "x" };
            var otherProfiles = new ProfileStore(Path.Combine(t.TempFolder(), "profiles"));
            TestKit.On(other, a);
            var foreign = new Engine(otherSys, other.History, find) { Machine = "PC-Q", User = "x" };
            AutoProfile.Update(foreign, otherProfiles, find);
            t.Equal(0, otherProfiles.List().Count, "another PC's history does not create a profile");

            // A profile restored from another PC is kept until this PC's history takes entries over.
            var restored = new ProfileStore(Path.Combine(t.TempFolder(), "profiles"));
            restored.Save(new Profile { Name = AutoProfile.Name, Machine = "OLD-PC", Tweaks = new List<ProfileItem> { new ProfileItem { Id = "test.ap1", On = true }, new ProfileItem { Id = "test.ap2", On = true } } });
            var sys2 = new MemorySystem();
            var newPc = TestKit.NewEngine(sys2, History(t), a, b, c, untouched);
            AutoProfile.Update(newPc, restored, find);
            Profile kept = restored.List().Single();
            t.Equal(2, kept.Tweaks.Count, "restored profile survives an update before anything is applied");
            TestKit.On(newPc, a);
            AutoProfile.Update(newPc, restored, find);
            kept = restored.List().Single();
            t.Check(kept.Tweaks.Any(x => x.Id == "test.ap1" && x.On), "the tweak applied here takes over");
            t.Check(kept.Tweaks.Any(x => x.Id == "test.ap2" && x.On), "the not yet applied entry from the old PC is still there");
            t.Equal("OLD-PC", kept.Machine, "profile keeps the old PC name while it still carries its entries");
            TestKit.On(newPc, b);
            AutoProfile.Update(newPc, restored, find);
            kept = restored.List().Single();
            t.Equal("PC-A", kept.Machine, "once everything was applied here the profile belongs to this PC");

            // Never throws, even when the profile folder is unusable.
            string blocker = Path.Combine(t.TempFolder(), "file");
            File.WriteAllText(blocker, "x");
            try { AutoProfile.Update(engine, new ProfileStore(Path.Combine(blocker, "sub")), find); t.Check(true, "unusable folder does not throw"); }
            catch (Exception ex) { t.Check(false, "AutoProfile.Update threw: " + ex.Message); }
            try { AutoProfile.Update(null, null); t.Check(true, "null arguments do not throw"); }
            catch (Exception ex) { t.Check(false, "AutoProfile.Update(null) threw: " + ex.Message); }
        }

        // ------------------------------------------------------------------------------------------------ MemorySystem itself

        static void MemorySystemBehaviour(T t)
        {
            var sys = new MemorySystem();
            sys.WriteValue(Hive.User, @"Software\A\B\C", "V", RegKind.Dword, 5u);
            t.Check(sys.KeyExists(Hive.User, @"software\a\b"), "keys are case-insensitive and parents exist");
            t.Check(sys.ReadValue(Hive.User, @"SOFTWARE\A\B\C", "v") != null, "value names are case-insensitive");
            t.Check(!sys.KeyIsEmpty(Hive.User, @"Software\A\B"), "a key with a subkey is not empty");
            sys.DeleteValue(Hive.User, @"Software\A\B\C", "V");
            t.Check(sys.KeyIsEmpty(Hive.User, @"Software\A\B\C"), "a key without values is empty");
            sys.DeleteKey(Hive.User, @"Software\A");
            t.Check(!sys.KeyExists(Hive.User, @"Software\A\B\C"), "DeleteKey removes the whole tree");
            sys.DeleteKey(Hive.User, @"Software\Missing\Key");
            sys.DeleteValue(Hive.User, @"Software\Missing", "x");
            t.Check(true, "deleting what is missing is not an error");

            sys.WriteValue(Hive.User, "Software\\Arr", "M", RegKind.MultiString, new[] { "a" });
            ((string[])sys.ReadValue(Hive.User, "Software\\Arr", "M").Data)[0] = "changed";
            t.Equal("a", ((string[])sys.ReadValue(Hive.User, "Software\\Arr", "M").Data)[0], "reads return copies");
            sys.WriteValue(Hive.User, "Software\\Arr", "D", RegKind.Dword, 3);
            t.Check(sys.ReadValue(Hive.User, "Software\\Arr", "D").Data is uint, "DWORD data is stored as uint");

            var user = new MemorySystem(elevated: false);
            t.Throws<UnauthorizedAccessException>(() => user.WriteValue(Hive.Machine, "SOFTWARE\\X", "v", RegKind.Dword, 1u), "HKLM write without administrator rights throws");
            user.WriteValue(Hive.User, "Software\\X", "v", RegKind.Dword, 1u);
            t.Throws<UnauthorizedAccessException>(() => { var f = new MemorySystem { FailWrites = true }; f.WriteValue(Hive.User, "Software\\X", "v", RegKind.Dword, 1u); }, "FailWrites throws");

            sys.RunHandler = (file, args) => new ProcessResult { ExitCode = args == "bad" ? 3 : 0, Output = "out:" + args };
            t.Equal(0, sys.Run("tool.exe", "good").ExitCode, "RunHandler answers");
            t.Equal(3, sys.Run("tool.exe", "bad").ExitCode, "RunHandler exit code");
            t.Equal("tool.exe good", sys.Commands[0], "commands are recorded as file + args");
            t.Equal(0, new MemorySystem().Run("x.exe", "y").ExitCode, "no handler: exit code 0");

            SystemInfo info = SystemInfo.Read(new MemorySystem(26300, "Professional", true));
            t.Equal("Windows 11 Pro", info.ProductName, "ProductName says Windows 11 on build 22000 or newer");
            t.Equal(26300, info.Build, "build");
            t.Check(!info.RestartPending, "no restart pending on a fresh MemorySystem");
            var pending = new MemorySystem();
            pending.Seed(Hive.Machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", "x", RegKind.Dword, 1u);
            pending.Seed(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations", RegKind.MultiString, new[] { @"\??\C:\old.tmp", "" });
            SystemInfo p = SystemInfo.Read(pending);
            t.Check(p.RestartPending && p.RestartPendingReasons.Count == 2, "restart pending reasons are read");
            t.Equal("Windows 10 Pro", SystemInfo.Read(new MemorySystem(19045)).ProductName, "older builds keep the registry product name");
            t.Check(info.Uptime > TimeSpan.Zero, "uptime is read");

            // Restore points.
            var rp = new MemorySystem(elevated: false);
            t.Check(!RestorePoint.Create(rp, "x").Ok, "restore point needs administrator rights");
            t.Equal(0, rp.Commands.Count, "nothing was run without administrator rights");
            var ok = new MemorySystem();
            RestorePointResult good = RestorePoint.Create(ok, "Tweak Bench: it's a test");
            t.Check(good.Ok, "restore point created");
            t.Check(ok.Commands.Count == 1 && ok.Commands[0].Contains("powershell"), "restore point runs PowerShell through sys.Run");
            var recent = new MemorySystem { RunHandler = (f, a) => new ProcessResult { ExitCode = 0, Output = "TB-WARNING: A restore point was already created within the past 1440 minutes." } };
            RestorePointResult rr = RestorePoint.Create(recent, "x");
            t.Check(rr.Ok && rr.Message.Contains("24 hours"), "the once-a-day warning counts as protected");
            var off = new MemorySystem { RunHandler = (f, a) => new ProcessResult { ExitCode = 1, Error = "Checkpoint-Computer : System Restore is disabled." } };
            RestorePointResult offResult = RestorePoint.Create(off, "x");
            t.Check(!offResult.Ok && offResult.Message.Contains("SystemPropertiesProtection.exe"), "System Protection off explains how to turn it on");
            var broken = new MemorySystem { RunHandler = (f, a) => new ProcessResult { ExitCode = 1, Error = "Something odd happened." } };
            RestorePointResult br = RestorePoint.Create(broken, "x");
            t.Check(!br.Ok && br.Message.Contains("Something odd"), "other failures show Windows' message");
        }
    }
}
