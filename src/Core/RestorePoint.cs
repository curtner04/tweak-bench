using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TweakBench.Core
{
    public sealed class RestorePointResult
    {
        public bool Ok;
        public string Message;
    }

    /// <summary>Creates a System Restore point (needs administrator rights). Everything goes through <see cref="ISystem.Run"/>.</summary>
    public static class RestorePoint
    {
        const string ProtectionHelp = "System Protection is turned off, so Windows cannot create restore points. To turn it on, open System Properties > System Protection (SystemPropertiesProtection.exe), choose your Windows drive, select Configure and turn on system protection.";

        public static RestorePointResult Create(ISystem sys, string description)
        {
            if (!sys.IsElevated)
                return new RestorePointResult { Ok = false, Message = "Creating a restore point needs administrator rights. Choose \"Run as administrator\" and try again." };

            string text = string.IsNullOrWhiteSpace(description) ? "Tweak Bench" : description.Trim();
            if (text.Length > 100) text = text.Substring(0, 100);
            string quoted = "'" + text.Replace("'", "''") + "'";

            // The "once per 24 hours" notice is a warning, not an error: capture it so it can be told apart from a failure.
            string script = "$w=$null; Checkpoint-Computer -Description " + quoted + " -RestorePointType MODIFY_SETTINGS -WarningVariable w -WarningAction SilentlyContinue; if ($w) { Write-Output ('TB-WARNING: ' + (($w | ForEach-Object { $_.ToString() }) -join ' ')) }";

            ProcessResult r;
            try { r = sys.Run(ProcessRunner.PowerShell, ProcessRunner.PowerShellArgs(script), 180000); }
            catch (Exception ex) { return new RestorePointResult { Ok = false, Message = "Could not start the restore point: " + ex.Message }; }

            if (r.TimedOut) return new RestorePointResult { Ok = false, Message = "Windows took too long to create the restore point, so it was stopped." };

            string output = ((r.Output ?? "") + "\n" + (r.Error ?? "")).Trim();
            string warning = null;
            Match m = Regex.Match(output, "TB-WARNING:(.*)", RegexOptions.Singleline);
            if (m.Success) warning = m.Groups[1].Value.Trim();

            if (r.ExitCode == 0)
            {
                // Windows allows one automatic restore point per 1440 minutes (24 hours). The number is the same in every language.
                if (warning != null && warning.Contains("1440"))
                    return new RestorePointResult { Ok = true, Message = "Windows already made a restore point in the last 24 hours, so that one protects you. Windows allows one new restore point a day." };
                if (warning != null && LooksDisabled(warning))
                    return new RestorePointResult { Ok = false, Message = ProtectionHelp };
                return new RestorePointResult { Ok = true, Message = "Restore point created." + (warning != null ? " Windows said: " + FirstLine(warning) : "") };
            }

            if (LooksDisabled(output)) return new RestorePointResult { Ok = false, Message = ProtectionHelp };
            if (output.Contains("1440")) return new RestorePointResult { Ok = true, Message = "Windows already made a restore point in the last 24 hours, so that one protects you. Windows allows one new restore point a day." };
            string detail = FirstLine(output);
            return new RestorePointResult { Ok = false, Message = "Windows could not create a restore point" + (detail.Length > 0 ? ": " + detail : " (error " + r.ExitCode + ").") };
        }

        static bool LooksDisabled(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string[] hints = { "disabled", "turned off", "not enabled", "enable-computerrestore", "80070422", "system restore is off", "system protection is off" };
            return hints.Any(h => text.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static string FirstLine(string text)
        {
            foreach (string line in (text ?? "").Split('\n'))
            {
                string s = line.Trim();
                if (s.Length > 0 && !s.StartsWith("At line", StringComparison.OrdinalIgnoreCase)) return s.Length > 240 ? s.Substring(0, 240) + "..." : s;
            }
            return "";
        }
    }
}
