using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TweakBench.Tests
{
    /// <summary>
    /// Tiny headless test runner. Any public static class whose name ends in "Tests" and has a method
    /// <c>public static void Run(T t)</c> is run automatically. Tests must never touch the real PC: use MemorySystem and a
    /// private data folder (<see cref="T.TempFolder"/>). Exit code 0 = all passed.
    /// </summary>
    public sealed class T
    {
        public int Passed, Failed;
        public string Suite = "";
        readonly List<string> failures = new List<string>();
        readonly List<string> temps = new List<string>();

        public void Check(bool condition, string what)
        {
            if (condition) Passed++;
            else { Failed++; failures.Add(Suite + ": " + what); Console.WriteLine("  FAIL " + what); }
        }

        public void Equal<TV>(TV expected, TV actual, string what)
        {
            bool same = EqualityComparer<TV>.Default.Equals(expected, actual);
            Check(same, what + (same ? "" : " (expected " + Show(expected) + ", got " + Show(actual) + ")"));
        }

        static string Show(object o) { return o == null ? "null" : "'" + o + "'"; }

        /// <summary>Passes when <paramref name="action"/> throws (any exception, or <typeparamref name="TE"/> when given).</summary>
        public void Throws<TE>(Action action, string what) where TE : Exception
        {
            try { action(); Check(false, what + " (no exception)"); }
            catch (TE) { Check(true, what); }
            catch (Exception ex) { Check(false, what + " (threw " + ex.GetType().Name + ": " + ex.Message + ")"); }
        }

        /// <summary>A new empty folder under %TEMP%\TweakBench.Tests, removed after the run.</summary>
        public string TempFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "TweakBench.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            temps.Add(dir);
            return dir;
        }

        public static int Main(string[] args)
        {
            var t = new T();
            string filter = args.Length > 0 ? args[0] : null;
            var suites = typeof(T).Assembly.GetTypes()
                .Where(x => x.IsClass && x.IsAbstract && x.IsSealed && x.Name.EndsWith("Tests", StringComparison.Ordinal))
                .Select(x => new { Type = x, Run = x.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(T) }, null) })
                .Where(x => x.Run != null && (filter == null || x.Type.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(x => x.Type.Name, StringComparer.Ordinal).ToList();
            foreach (var s in suites)
            {
                t.Suite = s.Type.Name;
                Console.WriteLine(s.Type.Name);
                int before = t.Failed;
                try { s.Run.Invoke(null, new object[] { t }); }
                catch (TargetInvocationException ex) { t.Check(false, "crashed: " + ex.InnerException); }
                if (t.Failed == before) Console.WriteLine("  ok");
            }
            foreach (string dir in t.temps) { try { Directory.Delete(dir, true); } catch (Exception) { } }
            Console.WriteLine();
            Console.WriteLine(t.Passed + " passed, " + t.Failed + " failed in " + suites.Count + " suites.");
            foreach (string f in t.failures) Console.WriteLine("FAILED: " + f);
            return t.Failed == 0 && suites.Count > 0 ? 0 : 1;
        }
    }
}
