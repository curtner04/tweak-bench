using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TweakBench.Core
{
    /// <summary>
    /// Where Tweak Bench keeps its files. Everything the user would want on a new PC is under <see cref="Data"/>
    /// (%LOCALAPPDATA%\Tweak Bench\data), which Export / import configuration carries as a whole. Logs are machine-local.
    /// Tests and the demo mode point TWEAKBENCH_DATA at a private folder.
    /// </summary>
    public static class Paths
    {
        public static string AppFolder
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tweak Bench"); }
        }

        public static string Data
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("TWEAKBENCH_DATA");
                return string.IsNullOrWhiteSpace(over) ? System.IO.Path.Combine(AppFolder, "data") : System.IO.Path.GetFullPath(over);
            }
        }

        /// <summary>Machine-local logs (never transferred): a sibling of the data folder.</summary>
        public static string Logs { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Data.TrimEnd('\\')), "logs"); } }

        public static string SettingsFile { get { return System.IO.Path.Combine(Data, "settings.json"); } }
        public static string HistoryFile { get { return System.IO.Path.Combine(Data, "history.json"); } }
        public static string ProfilesFolder { get { return System.IO.Path.Combine(Data, "profiles"); } }
    }

    public static class Json
    {
        static JavaScriptSerializer Make() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 }; }

        public static string Serialize(object value) { return Make().Serialize(value); }

        /// <summary>Indented JSON (two spaces) so saved files are readable.</summary>
        public static string SerializeIndented(object value) { return Indent(Serialize(value)); }

        public static T Deserialize<T>(string text) { return Make().Deserialize<T>(text); }

        public static string Indent(string json)
        {
            var sb = new StringBuilder(json.Length + json.Length / 4);
            int depth = 0; bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') quoted = false;
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        depth++; sb.Append('\n').Append(' ', depth * 2); break;
                    case '}':
                    case ']': depth--; sb.Append('\n').Append(' ', depth * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', depth * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Crash-safe text files: write a temporary file, then swap it in, keeping the previous copy as "*.previous".</summary>
    public static class AtomicFile
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string ReadAllTextOrNull(string path) { return File.Exists(path) ? File.ReadAllText(path, Utf8) : null; }

        public static void WriteAllText(string path, string text)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Utf8);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".previous");
            else File.Move(tmp, path);
        }
    }

    /// <summary>Per-user preferences in settings.json. Staged (not yet applied) selections survive a Restart.</summary>
    public sealed class AppSettings
    {
        public int Schema = 1;
        public double Width = 1280, Height = 840;
        public bool Maximized;
        /// <summary>Page id shown at start ("home", a category id, "apps", "install", "features", "repair", "history").</summary>
        public string LastPage = "home";
        /// <summary>Show Advanced tweaks (hidden by default to keep pages simple).</summary>
        public bool ShowAdvanced;
        /// <summary>Create a System Restore point before applying a batch of changes.</summary>
        public bool RestorePointFirst = true;
        /// <summary>Ask for administrator rights when Tweak Bench opens.</summary>
        public bool AskForAdmin = true;
        /// <summary>"auto", "light" or "dark".</summary>
        public string Theme = "auto";
        /// <summary>Tweak ids selected to turn on / off but not applied yet.</summary>
        public List<string> PendingOn = new List<string>();
        public List<string> PendingOff = new List<string>();
        /// <summary>winget package ids selected for installation but not installed yet.</summary>
        public List<string> PendingInstall = new List<string>();
        /// <summary>Built-in app package names selected for removal but not removed yet.</summary>
        public List<string> PendingRemove = new List<string>();

        public static AppSettings Load() { return Load(Paths.SettingsFile); }

        /// <summary>Missing file: defaults. Unreadable file: throws, leaving the file untouched.</summary>
        public static AppSettings Load(string file)
        {
            string text = AtomicFile.ReadAllTextOrNull(file);
            if (text == null) return new AppSettings();
            AppSettings s;
            try { s = Json.Deserialize<AppSettings>(text); }
            catch (Exception ex) { throw new IOException("Tweak Bench settings could not be read (" + file + "). The file was left unchanged. " + ex.Message, ex); }
            if (s == null || s.Schema != 1) throw new IOException("Tweak Bench settings are not in a supported format (" + file + "). The file was left unchanged.");
            s.Normalize();
            return s;
        }

        public void Normalize()
        {
            if (double.IsNaN(Width) || double.IsInfinity(Width)) Width = 1280;
            if (double.IsNaN(Height) || double.IsInfinity(Height)) Height = 840;
            Width = Math.Max(980, Math.Min(3000, Width));
            Height = Math.Max(640, Math.Min(2000, Height));
            if (string.IsNullOrWhiteSpace(LastPage)) LastPage = "home";
            if (Theme != "light" && Theme != "dark") Theme = "auto";
            PendingOn = Clean(PendingOn); PendingOff = Clean(PendingOff); PendingInstall = Clean(PendingInstall); PendingRemove = Clean(PendingRemove);
        }

        static List<string> Clean(List<string> list) { return (list ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(); }

        public void Save() { Save(Paths.SettingsFile); }

        public void Save(string file) { Normalize(); AtomicFile.WriteAllText(file, Json.SerializeIndented(this)); }
    }
}
