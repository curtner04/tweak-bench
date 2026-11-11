using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TweakBench.Core;

namespace TweakBench.Tests
{
    public static class StorageTests
    {
        public static void Run(T t)
        {
            JsonTests(t);
            AtomicFileTests(t);
            SettingsTests(t);
            HistoryTests(t);
            ProfileTests(t);
            PathTests(t);
        }

        public sealed class Sample
        {
            public string Text;
            public List<string> List = new List<string>();
            public Dictionary<string, string> Map = new Dictionary<string, string>();
            public Sample Child;
            public int Number;
            public bool Flag;
            public double Fraction;
        }

        static void JsonTests(T t)
        {
            var tricky = new Sample
            {
                Text = "He said \"hi\" and left a \\ backslash, a {brace}, a [bracket], a: colon, a, comma\r\nand a new line\tand a tab.",
                List = new List<string> { "a\"b", "c\\", "", "{\"nested\":[1,2]}" },
                Map = new Dictionary<string, string> { { "key \"quoted\"", "value: {x}" }, { "empty", "" } },
                Child = new Sample { Text = "child" },
                Number = -42,
                Flag = true,
                Fraction = 1.5,
            };
            string compact = Json.Serialize(tricky);
            string indented = Json.Indent(compact);
            t.Check(indented.Contains("\n"), "Indent adds line breaks");
            Sample back = Json.Deserialize<Sample>(indented);
            t.Equal(tricky.Text, back.Text, "text with quotes, backslashes and braces survives Indent");
            t.Check(back.List.SequenceEqual(tricky.List), "list with escaped quotes survives Indent");
            t.Equal("value: {x}", back.Map["key \"quoted\""], "dictionary with quoted key survives Indent");
            t.Equal("child", back.Child.Text, "nested object survives Indent");
            t.Equal(-42, back.Number, "number survives Indent");
            t.Check(back.Flag, "bool survives Indent");
            t.Equal(1.5, back.Fraction, "fraction survives Indent");
            t.Equal(compact, Json.Serialize(back), "Indent changes layout only, never content");

            t.Equal("{}", Json.Indent("{}"), "empty object stays on one line");
            t.Equal("[]", Json.Indent("[]"), "empty array stays on one line");
            string nested = Json.Indent("{\"a\":[],\"b\":{}}");
            t.Check(nested.Contains("\"a\": []") && nested.Contains("\"b\": {}"), "empty containers inside objects stay compact");
            string once = Json.SerializeIndented(tricky);
            t.Equal(once, Json.Indent(Json.Serialize(Json.Deserialize<Sample>(once))), "indenting is stable");
            t.Check(Json.SerializeIndented(new Sample()).Contains("  \"Text\""), "two-space indentation");

            t.Throws<Exception>(() => Json.Deserialize<Sample>("{not json"), "invalid JSON throws");
        }

        // Spaces added by Indent: leading spaces on lines.
        static int CountIndent(string indented)
        {
            int count = 0;
            foreach (string line in indented.Split('\n')) count += line.Length - line.TrimStart(' ').Length;
            return count;
        }

        static void AtomicFileTests(T t)
        {
            string dir = t.TempFolder();
            string file = Path.Combine(dir, "sub", "a.txt");
            t.Check(AtomicFile.ReadAllTextOrNull(file) == null, "missing file reads as null");
            AtomicFile.WriteAllText(file, "one");
            t.Equal("one", AtomicFile.ReadAllTextOrNull(file), "first write creates the folder and the file");
            t.Check(!File.Exists(file + ".previous"), "no .previous after the first write");
            t.Check(!File.Exists(file + ".tmp"), "no temporary file left behind");
            AtomicFile.WriteAllText(file, "two");
            t.Equal("two", AtomicFile.ReadAllTextOrNull(file), "second write replaces the file");
            t.Equal("one", File.ReadAllText(file + ".previous"), "the previous contents are kept as .previous");
            AtomicFile.WriteAllText(file, "three é中");
            t.Equal("three é中", AtomicFile.ReadAllTextOrNull(file), "UTF-8 text round-trips");
            t.Equal("two", File.ReadAllText(file + ".previous"), ".previous follows the last write");
            byte[] bytes = File.ReadAllBytes(file);
            t.Check(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "files are written without a byte order mark");
        }

        static void SettingsTests(T t)
        {
            string dir = t.TempFolder();
            string file = Path.Combine(dir, "settings.json");

            AppSettings d = AppSettings.Load(file);
            t.Equal(1280.0, d.Width, "default width");
            t.Equal(840.0, d.Height, "default height");
            t.Equal("home", d.LastPage, "default page");
            t.Equal("auto", d.Theme, "default theme");
            t.Check(d.RestorePointFirst && d.AskForAdmin && !d.ShowAdvanced, "default switches");
            t.Check(!File.Exists(file), "loading does not create the file");

            var s = new AppSettings { Width = 100, Height = 99999, Theme = "neon", LastPage = "  ", PendingOn = new List<string> { "a", "", "a", " ", "b" }, PendingOff = null };
            s.Save(file);
            AppSettings back = AppSettings.Load(file);
            t.Equal(980.0, back.Width, "width is raised to the minimum");
            t.Equal(2000.0, back.Height, "height is lowered to the maximum");
            t.Equal("auto", back.Theme, "unknown theme becomes auto");
            t.Equal("home", back.LastPage, "blank page becomes home");
            t.Check(back.PendingOn.SequenceEqual(new[] { "a", "b" }), "pending ids are cleaned and de-duplicated");
            t.Check(back.PendingOff != null && back.PendingOff.Count == 0, "missing lists become empty lists");

            var odd = new AppSettings { Width = double.NaN, Height = double.PositiveInfinity, Theme = "dark", ShowAdvanced = true, Maximized = true };
            odd.Normalize();
            t.Equal(1280.0, odd.Width, "NaN width falls back to the default");
            t.Equal(840.0, odd.Height, "infinite height falls back to the default");
            t.Equal("dark", odd.Theme, "valid theme kept");
            AppSettings full = new AppSettings { Theme = "light", ShowAdvanced = true, Maximized = true, RestorePointFirst = false, AskForAdmin = false, LastPage = "privacy", PendingInstall = new List<string> { "Mozilla.Firefox" }, PendingRemove = new List<string> { "Microsoft.BingNews" } };
            string f2 = Path.Combine(dir, "full.json");
            full.Save(f2);
            AppSettings loaded = AppSettings.Load(f2);
            t.Check(loaded.Theme == "light" && loaded.ShowAdvanced && loaded.Maximized && !loaded.RestorePointFirst && !loaded.AskForAdmin && loaded.LastPage == "privacy", "all settings round-trip");
            t.Check(loaded.PendingInstall.SequenceEqual(new[] { "Mozilla.Firefox" }) && loaded.PendingRemove.SequenceEqual(new[] { "Microsoft.BingNews" }), "staged installs and removals round-trip");

            // An unreadable file is never overwritten or moved.
            string bad = Path.Combine(dir, "bad.json");
            File.WriteAllText(bad, "{ this is not json");
            t.Throws<IOException>(() => AppSettings.Load(bad), "unreadable settings throw IOException");
            t.Equal("{ this is not json", File.ReadAllText(bad), "unreadable settings file is left untouched");
            t.Check(!File.Exists(bad + ".previous") && Directory.GetFiles(dir, "bad.json*").Length == 1, "no copies or moves of the unreadable file");

            string wrong = Path.Combine(dir, "wrong.json");
            File.WriteAllText(wrong, "{\"Schema\": 7, \"Width\": 1000}");
            t.Throws<IOException>(() => AppSettings.Load(wrong), "an unsupported schema throws IOException");
            t.Equal("{\"Schema\": 7, \"Width\": 1000}", File.ReadAllText(wrong), "unsupported settings file is left untouched");

            string nul = Path.Combine(dir, "null.json");
            File.WriteAllText(nul, "null");
            t.Throws<IOException>(() => AppSettings.Load(nul), "a null document throws IOException");
        }

        static HistoryBatch SampleBatch()
        {
            var batch = new HistoryBatch { Id = "b1", TimeUtc = DateTime.UtcNow.ToString("o"), Label = "Applied \"2\" changes", Machine = "PC-A", User = "me" };
            var entry = new HistoryEntry { TweakId = "explorer.show-extensions", Title = "Show file extensions", Action = "on", Ok = true };
            var snap = new Snapshot { Index = 0, Kind = "registry", What = @"HKCU\Software\X  V", Was = "1 (DWORD)" };
            snap.Set("exists", "1"); snap.Set("data", "quote \" and \\ slash");
            entry.Before.Add(snap);
            batch.Entries.Add(entry);
            return batch;
        }

        static void HistoryTests(T t)
        {
            string dir = t.TempFolder();
            string file = Path.Combine(dir, "history.json");

            HistoryStore empty = HistoryStore.Load(file);
            t.Equal(0, empty.Batches.Count, "missing history is empty");
            t.Check(empty.LoadWarning == null, "missing history has no warning");

            empty.Add(SampleBatch());
            HistoryStore back = HistoryStore.Load(file);
            t.Equal(1, back.Batches.Count, "saved batch is loaded");
            HistoryEntry e = back.Batches[0].Entries[0];
            t.Equal("explorer.show-extensions", e.TweakId, "tweak id round-trips");
            t.Equal("Applied \"2\" changes", back.Batches[0].Label, "label with quotes round-trips");
            t.Equal("quote \" and \\ slash", e.Before[0].Get("data"), "snapshot values round-trip");
            t.Equal("1 (DWORD)", e.Before[0].Was, "snapshot text round-trips");
            t.Check(e.CanUndo, "entry can be undone after loading");
            t.Check(back.Batches[0].Time > DateTime.MinValue, "batch time parses");
            t.Check(back.LatestOpenOn("explorer.show-extensions", "pc-a") != null, "LatestOpenOn finds it (case-insensitive PC name)");
            t.Check(back.LatestOpenOn("explorer.show-extensions", "PC-B") == null, "LatestOpenOn ignores other PCs");
            t.Check(back.LatestOpenOn("nope", "PC-A") == null, "LatestOpenOn ignores other tweaks");
            e.UndoneUtc = "x";
            t.Check(back.LatestOpenOn("explorer.show-extensions", "PC-A") == null, "an undone entry is not open");
            t.Equal("b1", back.FindBatch("b1").Id, "FindBatch");
            t.Check(back.FindBatch("zzz") == null, "FindBatch returns null when missing");

            // Unreadable history is set aside, never deleted.
            string badFile = Path.Combine(dir, "bad", "history.json");
            Directory.CreateDirectory(Path.GetDirectoryName(badFile));
            File.WriteAllText(badFile, "][ definitely not json");
            HistoryStore bad = HistoryStore.Load(badFile);
            t.Check(bad.LoadWarning != null && bad.LoadWarning.Contains("kept as"), "unreadable history sets a warning");
            t.Equal(0, bad.Batches.Count, "unreadable history starts empty");
            t.Check(!File.Exists(badFile), "the unreadable file was moved away");
            string[] aside = Directory.GetFiles(Path.GetDirectoryName(badFile), "history.json.unreadable-*");
            t.Equal(1, aside.Length, "the unreadable file is kept under a new name");
            t.Equal("][ definitely not json", aside.Length == 1 ? File.ReadAllText(aside[0]) : "", "the kept file is unchanged");
            bad.Add(SampleBatch());
            t.Equal(1, HistoryStore.Load(badFile).Batches.Count, "a new history can be written afterwards");

            string wrongSchema = Path.Combine(dir, "schema", "history.json");
            Directory.CreateDirectory(Path.GetDirectoryName(wrongSchema));
            File.WriteAllText(wrongSchema, "{\"Schema\": 9, \"Batches\": []}");
            HistoryStore ws = HistoryStore.Load(wrongSchema);
            t.Check(ws.LoadWarning != null, "an unsupported history schema is set aside with a warning");

            // A torn newest save falls back to the previous good copy.
            string torn = Path.Combine(dir, "torn", "history.json");
            var tornStore = new HistoryStore(torn);
            tornStore.Add(SampleBatch());
            var second = SampleBatch(); second.Id = "b-second";
            tornStore.Add(second);
            File.WriteAllText(torn, "{ \"Schema\": 1, \"Batches\": [ { \"Id\": ");
            HistoryStore recovered = HistoryStore.Load(torn);
            t.Equal(1, recovered.Batches.Count, "the previous save is used when the newest one is unreadable");
            t.Check(recovered.LoadWarning != null && recovered.LoadWarning.Contains("previous save"), "the fallback is explained");
            t.Check(Directory.GetFiles(Path.GetDirectoryName(torn), "history.json.unreadable-*").Length == 1, "the unreadable file is still kept");

            // Old files without a machine id load, and a new id round-trips.
            var withId = SampleBatch(); withId.MachineId = "guid-1";
            string idFile = Path.Combine(dir, "ids", "history.json");
            new HistoryStore(idFile).Add(withId);
            t.Equal("guid-1", HistoryStore.Load(idFile).Batches[0].MachineId, "the machine id is saved");

            // Missing lists in a hand-edited file do not crash.
            string sparse = Path.Combine(dir, "sparse", "history.json");
            Directory.CreateDirectory(Path.GetDirectoryName(sparse));
            File.WriteAllText(sparse, "{\"Schema\": 1, \"Batches\": [{\"Id\": \"x\", \"Machine\": \"PC-A\"}]}");
            HistoryStore sp = HistoryStore.Load(sparse);
            t.Check(sp.LoadWarning == null && sp.Batches.Count == 1 && sp.Batches[0].Entries != null, "a batch without entries loads");

            // Saving twice keeps a .previous copy.
            empty.Add(new HistoryBatch { Id = "b2", Machine = "PC-A", TimeUtc = DateTime.UtcNow.ToString("o") });
            t.Check(File.Exists(file + ".previous"), "history keeps a .previous copy");
        }

        static void ProfileTests(T t)
        {
            string dir = Path.Combine(t.TempFolder(), "profiles");
            var store = new ProfileStore(dir);
            t.Equal(0, store.List().Count, "no folder: no profiles");

            var p = new Profile { Name = "Gaming PC", CreatedUtc = DateTime.UtcNow.ToString("o"), Machine = "PC-A", Tweaks = new List<ProfileItem> { new ProfileItem { Id = "a.one", On = true }, new ProfileItem { Id = "a.two", On = false } } };
            store.Save(p);
            t.Check(File.Exists(Path.Combine(dir, "Gaming PC.json")), "profile saved under its name");
            List<Profile> list = store.List();
            t.Equal(1, list.Count, "profile listed");
            t.Equal("Gaming PC", list[0].Name, "name round-trips");
            t.Check(list[0].Tweaks.Count == 2 && list[0].Tweaks[0].On && !list[0].Tweaks[1].On, "items round-trip");

            store.Save(new Profile { Name = "Another", Tweaks = new List<ProfileItem>() });
            t.Check(store.List().Select(x => x.Name).SequenceEqual(new[] { "Another", "Gaming PC" }), "profiles are listed by file name");

            // Broken files are skipped.
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ nope");
            File.WriteAllText(Path.Combine(dir, "wrongschema.json"), "{\"Schema\": 3, \"Name\": \"x\", \"Tweaks\": []}");
            File.WriteAllText(Path.Combine(dir, "noid.json"), "{\"Schema\": 1, \"Name\": \"x\", \"Tweaks\": [{\"Id\": \"\"}]}");
            t.Equal(2, store.List().Count, "unreadable and invalid profiles are skipped, not fatal");
            t.Throws<InvalidDataException>(() => ProfileStore.Read(Path.Combine(dir, "wrongschema.json")), "Read rejects an unsupported schema");
            t.Throws<InvalidDataException>(() => ProfileStore.Read(Path.Combine(dir, "noid.json")), "Read rejects a profile item without an id");

            // The name falls back to the file name.
            File.WriteAllText(Path.Combine(dir, "Nameless.json"), "{\"Schema\": 1, \"Tweaks\": []}");
            t.Equal("Nameless", ProfileStore.Read(Path.Combine(dir, "Nameless.json")).Name, "a profile without a name uses its file name");

            // Safe file names.
            t.Equal("a-b-c", ProfileStore.SafeFileName("a/b\\c"), "path separators are replaced");
            t.Equal("x-y-z--", ProfileStore.SafeFileName("x:y*z?\""), "reserved characters are replaced");
            t.Equal("Profile", ProfileStore.SafeFileName("   "), "blank names become 'Profile'");
            t.Equal("Profile", ProfileStore.SafeFileName(null), "null names become 'Profile'");
            t.Equal(80, ProfileStore.SafeFileName(new string('n', 200)).Length, "long names are cut to 80 characters");
            t.Equal("My setup (automatic)", ProfileStore.SafeFileName(AutoProfile.Name), "the automatic profile name is a valid file name");
            t.Check(store.PathFor("../evil").StartsWith(dir, StringComparison.OrdinalIgnoreCase), "a name cannot escape the profiles folder");

            // Export to any path and delete.
            string export = Path.Combine(t.TempFolder(), "shared.json");
            ProfileStore.SaveTo(p, export);
            t.Equal("Gaming PC", ProfileStore.Read(export).Name, "SaveTo writes a readable profile anywhere");
            store.Delete("Gaming PC");
            t.Check(!File.Exists(Path.Combine(dir, "Gaming PC.json")), "Delete removes the profile");
            store.Delete("Gaming PC");
            t.Check(true, "deleting a missing profile is not an error");

            // Replacing a profile keeps the old copy.
            store.Save(new Profile { Name = "Another", Tweaks = new List<ProfileItem> { new ProfileItem { Id = "x.y", On = true } } });
            t.Check(File.Exists(Path.Combine(dir, "Another.json.previous")), "saving over a profile keeps a .previous copy");
        }

        static void PathTests(T t)
        {
            string old = Environment.GetEnvironmentVariable("TWEAKBENCH_DATA");
            try
            {
                string dir = t.TempFolder();
                Environment.SetEnvironmentVariable("TWEAKBENCH_DATA", Path.Combine(dir, "data"));
                t.Equal(Path.Combine(dir, "data"), Paths.Data, "TWEAKBENCH_DATA overrides the data folder");
                t.Equal(Path.Combine(dir, "data", "settings.json"), Paths.SettingsFile, "settings file is in the data folder");
                t.Equal(Path.Combine(dir, "data", "history.json"), Paths.HistoryFile, "history file is in the data folder");
                t.Equal(Path.Combine(dir, "data", "profiles"), Paths.ProfilesFolder, "profiles folder is in the data folder");
                t.Equal(Path.Combine(dir, "logs"), Paths.Logs, "logs are next to, not inside, the data folder");
                Environment.SetEnvironmentVariable("TWEAKBENCH_DATA", null);
                t.Check(Paths.Data.EndsWith(Path.Combine("Tweak Bench", "data"), StringComparison.OrdinalIgnoreCase), "default data folder is %LOCALAPPDATA%\\Tweak Bench\\data");
            }
            finally { Environment.SetEnvironmentVariable("TWEAKBENCH_DATA", old); }
        }
    }
}
