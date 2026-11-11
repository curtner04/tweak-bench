using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using TweakBench.Core;

namespace TweakBench.Tools
{
    /// <summary>Small helpers shared by the tool pages: quoting, PowerShell, JSON reading and sizes. No WPF in here.</summary>
    public static class Cmd
    {
        /// <summary>Quotes text as a PowerShell single-quoted string literal.</summary>
        public static string PsQuote(string text) { return "'" + (text ?? "").Replace("'", "''") + "'"; }

        /// <summary>Quotes one argument for a Windows command line (CommandLineToArgvW rules).</summary>
        public static string Arg(string text)
        {
            text = text ?? "";
            if (text.Length > 0 && text.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return text;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in text)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
                sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }

        /// <summary>Runs a PowerShell script hidden and waits. Output is read as UTF-8.</summary>
        public static ProcessResult RunPowerShell(string script, int timeoutMs = 120000)
        {
            return ProcessRunner.Run(ProcessRunner.PowerShell, ProcessRunner.PowerShellArgs(script), timeoutMs, Encoding.UTF8);
        }

        /// <summary>Full path of winget.exe, or null when App Installer is not installed for this user.</summary>
        public static string FindWinget()
        {
            try
            {
                string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(alias)) return alias;
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        string candidate = Path.Combine(dir.Trim().Trim('"'), "winget.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>"1.4 GB", "230 MB", "12 KB".</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            double v = bytes;
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            if (u == 0) return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
            return v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[u];
        }

        public static string Plural(int n, string one, string many)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
        }

        // ---- JSON helpers (JavaScriptSerializer.DeserializeObject gives Dictionary / object[]) ----

        /// <summary>Parses JSON into a list of objects: accepts one object, an array of objects, or empty text.</summary>
        public static List<Dictionary<string, object>> JsonObjects(string json)
        {
            var list = new List<Dictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            int start = json.IndexOfAny(new[] { '{', '[' });
            if (start < 0) return list;
            object root;
            try { root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json.Substring(start)); }
            catch (Exception) { return list; }
            Collect(root, list);
            return list;
        }

        static void Collect(object node, List<Dictionary<string, object>> list)
        {
            var dict = node as Dictionary<string, object>;
            if (dict != null) { list.Add(dict); return; }
            var items = node as System.Collections.IEnumerable;
            if (items != null && !(node is string)) foreach (object item in items) Collect(item, list);
        }

        public static string Str(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        public static bool Bool(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            bool b;
            return bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out b) && b;
        }

        public static int Int(Dictionary<string, object> o, string key, int fallback = 0)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return fallback;
            int n;
            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }

        /// <summary>A JSON value that may be missing, a single string or an array of strings, as a list of non-empty strings.</summary>
        public static List<string> StrList(Dictionary<string, object> o, string key)
        {
            var list = new List<string>();
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return list;
            var items = v as System.Collections.IEnumerable;
            if (items != null && !(v is string)) { foreach (object item in items) if (item != null) list.Add(Convert.ToString(item, CultureInfo.InvariantCulture)); }
            else list.Add(Convert.ToString(v, CultureInfo.InvariantCulture));
            return list.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        /// <summary>Splits tool output into lines, dropping carriage returns, NULs and the progress-spinner debris winget writes.</summary>
        public static List<string> Lines(string text)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (string raw in text.Replace("\0", "").Split('\n'))
            {
                string line = raw.TrimEnd('\r', ' ', '\t');
                // winget redraws a spinner with carriage returns: keep only what follows the last one.
                int cr = line.LastIndexOf('\r');
                if (cr >= 0) line = line.Substring(cr + 1);
                lines.Add(line);
            }
            return lines;
        }
    }
}
