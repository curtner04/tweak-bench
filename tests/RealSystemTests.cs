using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TweakBench.Core;

namespace TweakBench.Tests
{
    /// <summary>
    /// Exercises RealSystem's registry code and ProcessRunner against the real PC, but ONLY inside the private key
    /// HKCU\Software\TweakBench.SelfTest (removed again at the end) and with harmless commands (cmd /c echo, ping to localhost).
    /// Nothing else on the PC is written. The catalog round trips in CatalogTests only prove internal consistency (their
    /// "Windows default" comes from the catalog itself); the --audit comparison on a real default PC is what checks windowsDefault.
    /// </summary>
    public static class RealSystemTests
    {
        const string SelfTest = @"Software\TweakBench.SelfTest";

        public static void Run(T t)
        {
            Registry(t);
            PowerParsing(t);
            Processes(t);
        }

        static void Registry(T t)
        {
            var sys = new RealSystem();
            sys.DeleteKey(Hive.User, SelfTest);
            t.Check(!sys.KeyExists(Hive.User, SelfTest), "the self-test key starts absent");
            try
            {
                string k = SelfTest + @"\Kinds";
                var cases = new List<KeyValuePair<RegKind, object>>
                {
                    new KeyValuePair<RegKind, object>(RegKind.Dword, 0u),
                    new KeyValuePair<RegKind, object>(RegKind.Dword, 0x80000000u),
                    new KeyValuePair<RegKind, object>(RegKind.Dword, 0xFFFFFFFFu),
                    new KeyValuePair<RegKind, object>(RegKind.Qword, ulong.MaxValue),
                    new KeyValuePair<RegKind, object>(RegKind.Qword, 5UL),
                    new KeyValuePair<RegKind, object>(RegKind.String, "plain text"),
                    new KeyValuePair<RegKind, object>(RegKind.String, ""),
                    new KeyValuePair<RegKind, object>(RegKind.ExpandString, @"%SystemRoot%\system32"),
                    new KeyValuePair<RegKind, object>(RegKind.MultiString, new[] { "one", "two", "three" }),
                    new KeyValuePair<RegKind, object>(RegKind.MultiString, new string[0]),
                    new KeyValuePair<RegKind, object>(RegKind.Binary, new byte[] { 0, 1, 2, 254, 255 }),
                    new KeyValuePair<RegKind, object>(RegKind.Binary, new byte[0]),
                };
                int n = 0;
                foreach (var c in cases)
                {
                    string name = "V" + (n++) + "_" + c.Key;
                    sys.WriteValue(Hive.User, k, name, c.Key, c.Value);
                    RegData d = sys.ReadValue(Hive.User, k, name);
                    t.Check(d != null && d.Kind == c.Key && Reg.Same(d.Kind, d.Data, c.Key, c.Value, true), name + " reads back with the same type and value");
                    t.Equal(Reg.Encode(c.Key, c.Value), d == null ? null : Reg.Encode(d.Kind, d.Data), name + " encodes the same after a round trip");
                }

                // Expand strings are stored unexpanded.
                RegData expand = sys.ReadValue(Hive.User, k, "V7_ExpandString");
                t.Equal(@"%SystemRoot%\system32", expand == null ? null : (string)expand.Data, "REG_EXPAND_SZ is read without expanding variables");

                // A value changes type: the old type is replaced, not kept.
                sys.WriteValue(Hive.User, k, "Retyped", RegKind.String, "7");
                sys.WriteValue(Hive.User, k, "Retyped", RegKind.Dword, 7u);
                RegData retyped = sys.ReadValue(Hive.User, k, "Retyped");
                t.Check(retyped != null && retyped.Kind == RegKind.Dword && (uint)retyped.Data == 7u, "a value can change type");

                // The (Default) value.
                sys.WriteValue(Hive.User, k + @"\Default", "", RegKind.String, "default text");
                RegData def = sys.ReadValue(Hive.User, k + @"\Default", "");
                t.Check(def != null && def.Kind == RegKind.String && (string)def.Data == "default text", "the (Default) value reads back");
                sys.DeleteValue(Hive.User, k + @"\Default", "");
                t.Check(sys.ReadValue(Hive.User, k + @"\Default", "") == null, "the (Default) value can be deleted");
                t.Check(sys.KeyExists(Hive.User, k + @"\Default") && sys.KeyIsEmpty(Hive.User, k + @"\Default"), "an empty key is reported empty");

                // Missing things read as missing and deleting them is not an error.
                t.Check(sys.ReadValue(Hive.User, k, "NoSuchValue") == null, "a missing value reads as null");
                t.Check(sys.ReadValue(Hive.User, SelfTest + @"\NoSuchKey", "X") == null, "a value under a missing key reads as null");
                sys.DeleteValue(Hive.User, k, "NoSuchValue");
                sys.DeleteValue(Hive.User, SelfTest + @"\NoSuchKey", "X");
                sys.DeleteKey(Hive.User, SelfTest + @"\NoSuchKey");
                t.Check(!sys.KeyExists(Hive.User, SelfTest + @"\NoSuchKey"), "deleting a missing key is not an error and creates nothing");

                // DeleteValue leaves the key; keys with content are not empty.
                sys.DeleteValue(Hive.User, k, "V0_Dword");
                t.Check(sys.ReadValue(Hive.User, k, "V0_Dword") == null && sys.KeyExists(Hive.User, k), "DeleteValue removes only the value");
                t.Check(!sys.KeyIsEmpty(Hive.User, k), "a key with values is not empty");
                t.Check(!sys.KeyIsEmpty(Hive.User, SelfTest + @"\NoSuchKey"), "a missing key is not 'empty'");

                // Kind-change restore through the real registry: snapshot, change, restore.
                var reg = Reg.Dword(Hive.User, k, "Retyped", 1u);
                sys.WriteValue(Hive.User, k, "Retyped", RegKind.String, "text first");
                Snapshot snap = reg.Capture(sys);
                reg.Apply(sys);
                t.Check(sys.ReadValue(Hive.User, k, "Retyped").Kind == RegKind.Dword, "apply wrote a DWORD over the text");
                reg.Restore(sys, snap);
                RegData restored = sys.ReadValue(Hive.User, k, "Retyped");
                t.Check(restored != null && restored.Kind == RegKind.String && (string)restored.Data == "text first", "restore puts the text value back");

                // Keys created by a change are removed again by restore.
                var fresh = Reg.Dword(Hive.User, SelfTest + @"\Fresh\Deep", "F", 1u);
                Snapshot freshSnap = fresh.Capture(sys);
                fresh.Apply(sys);
                t.Check(sys.KeyExists(Hive.User, SelfTest + @"\Fresh\Deep"), "apply created the key chain");
                fresh.Restore(sys, freshSnap);
                t.Check(!sys.KeyExists(Hive.User, SelfTest + @"\Fresh"), "restore removed the keys the change created");
                t.Check(sys.KeyExists(Hive.User, SelfTest), "restore left the parent that already existed");

                // KeyDefault: the key's existence is the setting.
                var kd = Reg.KeyDefault(Hive.User, SelfTest + @"\Classes\Clsid\InprocServer32", "", SelfTest + @"\Classes");
                kd.Apply(sys);
                t.Equal((bool?)true, kd.IsOn(sys), "a key-default change reads as on");
                kd.RestoreDefault(sys);
                t.Check(!sys.KeyExists(Hive.User, SelfTest + @"\Classes"), "restoring the default deletes the whole key tree");

                sys.DeleteKey(Hive.User, k);
                t.Check(!sys.KeyExists(Hive.User, k), "DeleteKey removes a key with values");
            }
            finally
            {
                sys.DeleteKey(Hive.User, SelfTest);
            }
            t.Check(!sys.KeyExists(Hive.User, SelfTest), "the self-test key is gone afterwards");
            t.Throws<ArgumentException>(() => sys.DeleteKey(Hive.User, ""), "DeleteKey refuses to delete a registry root");
        }

        static void PowerParsing(T t)
        {
            string sample =
                "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n" +
                "  GUID Alias: SCHEME_BALANCED\r\n" +
                "  Subgroup GUID: 238c9fa8-0aad-41ed-83f4-97be242c8f20  (Sleep)\r\n" +
                "    Power Setting GUID: bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d  (Allow wake timers)\r\n" +
                "      Possible Setting Index: 000\r\n" +
                "      Possible Setting Friendly Name: Disable\r\n" +
                "      Possible Setting Index: 001\r\n" +
                "      Possible Setting Friendly Name: Enable\r\n" +
                "    Current AC Power Setting Index: 0x00000001\r\n" +
                "    Current DC Power Setting Index: 0x00000000\r\n";
            uint?[] v = RealSystem.ParsePower(sample);
            t.Equal((uint?)1u, v[0], "plugged-in value parsed");
            t.Equal((uint?)0u, v[1], "battery value parsed");
            uint?[] big = RealSystem.ParsePower("x\r\nCurrent AC Power Setting Index: 0xffffffff\r\nCurrent DC Power Setting Index: 0x00000708\r\n");
            t.Equal((uint?)uint.MaxValue, big[0], "a large hexadecimal value parses");
            t.Equal((uint?)0x708u, big[1], "a second value parses");
            uint?[] none = RealSystem.ParsePower("The power scheme, subgroup or setting specified does not exist.");
            t.Check(none[0] == null && none[1] == null, "output without values gives null");
            t.Check(RealSystem.ParsePower(null)[0] == null, "null output gives null");
        }

        static void Processes(T t)
        {
            string cmd = ProcessRunner.System32("cmd.exe");

            ProcessResult echo = ProcessRunner.Run(cmd, "/c echo hello world", 20000);
            t.Check(echo.Ok && !echo.TimedOut, "cmd /c echo succeeds");
            t.Equal("hello world", (echo.Output ?? "").Trim(), "output is captured");

            ProcessResult fail = ProcessRunner.Run(cmd, "/c exit 7", 20000);
            t.Equal(7, fail.ExitCode, "the exit code is returned");
            t.Check(!fail.Ok, "a non-zero exit code is not Ok");

            ProcessResult err = ProcessRunner.Run(cmd, "/c echo problem 1>&2", 20000);
            t.Equal("problem", (err.Error ?? "").Trim(), "error output is captured separately");

            // A tool that runs too long is stopped and reported as timed out.
            var clock = Stopwatch.StartNew();
            ProcessResult slow = ProcessRunner.Run(ProcessRunner.System32("ping.exe"), "-n 5 127.0.0.1", 1000);
            clock.Stop();
            t.Check(slow.TimedOut, "a long tool is reported as timed out");
            t.Equal(-1, slow.ExitCode, "a timed-out tool has exit code -1");
            t.Check(clock.ElapsedMilliseconds < 4500, "the timeout is honoured (took " + clock.ElapsedMilliseconds + " ms)");

            // A helper that keeps the output pipes open after the tool exits must not defeat the timeout.
            clock.Restart();
            ProcessResult held = ProcessRunner.Run(cmd, "/c start /b ping -n 12 127.0.0.1 > nul", 2000);
            ProcessResult heldPipe = ProcessRunner.Run(cmd, "/c start /b ping -n 12 127.0.0.1", 2000);
            clock.Stop();
            t.Check(!held.TimedOut && !heldPipe.TimedOut, "the tool itself finished");
            t.Check(clock.ElapsedMilliseconds < 9000, "a helper holding the pipe does not block past the drain limit (took " + clock.ElapsedMilliseconds + " ms)");

            // Stream: lines arrive, the exit code comes back, cancelling stops the tool.
            var lines = new List<string>();
            int code = ProcessRunner.Stream(cmd, "/c echo one& echo two", l => { lock (lines) lines.Add(l); }, CancellationToken.None);
            t.Equal(0, code, "Stream returns the exit code");
            lock (lines) t.Check(lines.Contains("one") && lines.Contains("two"), "Stream delivers each line");

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(600);
                clock.Restart();
                int cancelled = ProcessRunner.Stream(ProcessRunner.System32("ping.exe"), "-n 20 127.0.0.1", l => { }, cts.Token);
                clock.Stop();
                t.Equal(-1, cancelled, "a cancelled Stream returns -1");
                t.Check(clock.ElapsedMilliseconds < 8000, "cancelling stops the tool (took " + clock.ElapsedMilliseconds + " ms)");
            }

            // PowerShell output comes back as UTF-8 through RealSystem.Run.
            ProcessResult ps = new RealSystem().Run(ProcessRunner.PowerShell, ProcessRunner.PowerShellArgs("'caf' + [char]0xE9"), 60000);
            t.Check(ps.Ok && (ps.Output ?? "").Trim() == "café", "PowerShell non-ASCII output is decoded as UTF-8");

            t.Equal("abc", ProcessRunner.Clean("a\0b\0c   "), "Clean removes NUL characters and trailing spaces");
        }
    }
}
