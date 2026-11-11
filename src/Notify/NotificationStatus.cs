using System;
using System.IO;
using System.Text;
using TweakBench.Core;

namespace TweakBench.Notify
{
    /// <summary>What the notification helper reports about itself (shown by the Tweak Bench page). Times are ISO 8601 UTC strings, rectangles "left,top,right,bottom".</summary>
    public sealed class NotificationStatus
    {
        public int Pid;
        /// <summary>The helper's version (equals the Tweak Bench release it was built with).</summary>
        public string Version;
        public string ExePath;
        public string StartedUtc;
        /// <summary>"moving" (watching for pop-ups), "waiting" (Windows' shell host is not running yet), "off", "stopped" or "error".</summary>
        public string State;
        /// <summary>Pop-ups moved since the helper started.</summary>
        public int Moved;
        public string LastMovedUtc;
        public string LastToastTitle;
        public string LastToastRect;
        public string LastTargetRect;
        public string LastError;
        /// <summary>True while the process that draws pop-ups was found and is being watched.</summary>
        public bool HostFound;
        public string UpdatedUtc;
        /// <summary>Size of the last pop-up card seen, in DIPs (0 until one was seen); the position chooser uses it for its card.</summary>
        public double LastCardWidthDip, LastCardHeightDip;

        public const string StateMoving = "moving", StateWaiting = "waiting", StateOff = "off", StateStopped = "stopped", StateError = "error";

        public static string Stamp(DateTime utc) { return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture); }
    }

    /// <summary>The status file: Paths.Logs\notifications-status.json (machine-local). Written atomically by the helper, read by the app.</summary>
    public static class NotificationStatusFile
    {
        public const string FileName = "notifications-status.json";

        public static string FilePath { get { return System.IO.Path.Combine(Paths.Logs, FileName); } }

        /// <summary>The current status, or null when the file is missing or unreadable. Never throws.</summary>
        public static NotificationStatus Read() { return Read(FilePath); }

        public static NotificationStatus Read(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
                string text = File.ReadAllText(file, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return Json.Deserialize<NotificationStatus>(text);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Stamps UpdatedUtc and writes the file (temporary file, then swap). Returns false when it could not be written. Never throws.</summary>
        public static bool Write(NotificationStatus status) { return Write(FilePath, status); }

        public static bool Write(string file, NotificationStatus status)
        {
            string tmp = null;
            try
            {
                if (status == null || string.IsNullOrEmpty(file)) return false;
                status.UpdatedUtc = NotificationStatus.Stamp(DateTime.UtcNow);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                tmp = file + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
                File.WriteAllText(tmp, Json.SerializeIndented(status), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
                return true;
            }
            catch (Exception)
            {
                // The swap fails while the app reads the file: do not leave the temporary copy behind.
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }
        }
    }
}
