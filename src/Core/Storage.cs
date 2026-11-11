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
        /// <summary>Move Windows notification pop-ups to NotificationSpot (done by "Tweak Bench Notifications.exe").</summary>
        public bool MoveNotifications;
        /// <summary>Where notification pop-ups go. Null until the user picks a spot (a preset or "Choose position").</summary>
        public NotificationSpot NotificationSpot;
        /// <summary>Pop-up window titles learned on this PC for other display languages ("New notification" is always known).</summary>
        public List<string> NotificationTitles = new List<string>();

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
            if (NotificationSpot != null) NotificationSpot.Normalize();
            NotificationTitles = CleanTitles(NotificationTitles);
        }

        /// <summary>Trimmed, not blank, no duplicates (ignoring case), each at most 200 characters, at most 20 titles.</summary>
        static List<string> CleanTitles(List<string> list)
        {
            var result = new List<string>();
            foreach (string raw in list ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string title = raw.Trim();
                if (title.Length > 200) title = title.Substring(0, 200);
                if (result.Any(x => string.Equals(x, title, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(title);
                if (result.Count >= 20) break;
            }
            return result;
        }

        static List<string> Clean(List<string> list) { return (list ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(); }

        public void Save() { Save(Paths.SettingsFile); }

        public void Save(string file) { Normalize(); AtomicFile.WriteAllText(file, Json.SerializeIndented(this)); }
    }

    /// <summary>Where Windows notification pop-ups are placed: a spot on one monitor, anchored to a work-area edge.</summary>
    public sealed class NotificationSpot
    {
        /// <summary>Monitor device interface path (EnumDisplayDevices + EDD_GET_DEVICE_INTERFACE_NAME); stable across restarts.</summary>
        public string MonitorId;
        /// <summary>GDI device name such as \.\DISPLAY3 (fallback when the id is not found).</summary>
        public string MonitorDevice;
        /// <summary>Friendly monitor name for the UI, for example "DELL U2720Q".</summary>
        public string MonitorName;
        /// <summary>"left", "center" or "right": which edge of the card stays put horizontally.</summary>
        public string Horizontal = "right";
        /// <summary>"top" or "bottom": which edge stays put when the pop-up grows (a top spot grows downward).</summary>
        public string Vertical = "bottom";
        /// <summary>Distance in DIPs (1/96 inch) from the anchored work-area edge to the anchored card edge. For "center" it is the signed
        /// offset of the card centre from the work-area centre (positive = right / down).</summary>
        public double OffsetX = 12, OffsetY = 12;

        /// <summary>Largest offset kept, in DIPs.</summary>
        public const double MaxOffset = 100000;

        /// <summary>Unknown anchors become the defaults (right, bottom); offsets that are not numbers become 12 and huge ones are limited to +-100000.</summary>
        public void Normalize()
        {
            string h = (Horizontal ?? "").Trim().ToLowerInvariant();
            Horizontal = h == "left" || h == "center" || h == "right" ? h : "right";
            string v = (Vertical ?? "").Trim().ToLowerInvariant();
            Vertical = v == "top" || v == "bottom" ? v : "bottom";
            OffsetX = Offset(OffsetX); OffsetY = Offset(OffsetY);
        }

        static double Offset(double value)
        {
            if (double.IsNaN(value)) return 12;
            return Math.Max(-MaxOffset, Math.Min(MaxOffset, value));
        }

        public NotificationSpot Clone() { return (NotificationSpot)MemberwiseClone(); }
    }
}
