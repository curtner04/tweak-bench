using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace TweakBench.Core
{
    /// <summary>Runs command-line tools hidden (no console window ever appears) and captures or streams their output.</summary>
    public static class ProcessRunner
    {
        /// <summary>The console (OEM) code page that tools like DISM, netsh and powercfg write when redirected.</summary>
        public static Encoding Oem
        {
            get
            {
                // The system OEM code page (what console tools write), not the page of the current culture.
                try { return Encoding.GetEncoding((int)GetOEMCP()); }
                catch (Exception) { }
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch (Exception) { return Encoding.UTF8; }
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint GetOEMCP();

        /// <summary>True for powershell.exe and pwsh.exe, which <see cref="PowerShellArgs"/> switches to UTF-8 output.</summary>
        public static bool IsPowerShell(string file)
        {
            string name = Path.GetFileName(file ?? "").ToLowerInvariant();
            return name == "powershell.exe" || name == "pwsh.exe" || name == "powershell" || name == "pwsh";
        }

        /// <summary>Full path of a program in System32 (the 64-bit one; Tweak Bench is a 64-bit process).</summary>
        public static string System32(string exe) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe); }

        /// <summary>Windows PowerShell 5.1, always present.</summary>
        public static string PowerShell { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"); } }

        /// <summary>Arguments that run a PowerShell script block safely: no profile, no prompts, UTF-8 output.</summary>
        public static string PowerShellArgs(string script)
        {
            string prefix = "[Console]::OutputEncoding=[Text.Encoding]::UTF8;$ProgressPreference='SilentlyContinue';$ErrorActionPreference='Stop';";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prefix + script));
            return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded;
        }

        static ProcessStartInfo Info(string file, string arguments, Encoding encoding, string workingDirectory)
        {
            var psi = new ProcessStartInfo(file, arguments ?? "")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = encoding ?? Oem,
                StandardErrorEncoding = encoding ?? Oem,
            };
            if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
            return psi;
        }

        /// <summary>Runs and waits. A tool that does not finish within <paramref name="timeoutMs"/> is stopped and reported as timed out.</summary>
        public static ProcessResult Run(string file, string arguments, int timeoutMs = 120000, Encoding encoding = null, string workingDirectory = null)
        {
            var result = new ProcessResult();
            var output = new StringBuilder();
            var error = new StringBuilder();
            using (var p = new Process { StartInfo = Info(file, arguments, encoding, workingDirectory) })
            using (var outDone = new ManualResetEventSlim())
            using (var errDone = new ManualResetEventSlim())
            {
                p.OutputDataReceived += (s, e) => { if (e.Data == null) outDone.Set(); else lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data == null) errDone.Set(); else lock (error) error.AppendLine(e.Data); };
                p.Start();
                try { p.StandardInput.Close(); } catch (Exception) { }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    result.TimedOut = true;
                    KillTree(p);
                    p.WaitForExit(5000);
                }
                // No unbounded p.WaitForExit() here: it also waits for the output pipes to close, which a helper process
                // that inherited them can hold open long after the tool itself has finished. WaitForExit(int) returning
                // true already means the process exited, so ExitCode is valid; the bounded waits below drain the output.
                Drain(outDone, errDone, 3000);
                result.ExitCode = result.TimedOut ? -1 : p.ExitCode;
            }
            lock (output) result.Output = output.ToString();
            lock (error) result.Error = error.ToString();
            return result;
        }

        /// <summary>
        /// Runs a long tool (SFC, DISM, winget) and hands each output line to <paramref name="onLine"/> as it arrives
        /// (on a background thread). Blocks until the tool exits; returns its exit code, or -1 when cancelled (the tool is stopped).
        /// Call it from a background thread.
        /// </summary>
        public static int Stream(string file, string arguments, Action<string> onLine, CancellationToken cancel, Encoding encoding = null, string workingDirectory = null)
        {
            using (var p = new Process { StartInfo = Info(file, arguments, encoding, workingDirectory) })
            using (var outDone = new ManualResetEventSlim())
            using (var errDone = new ManualResetEventSlim())
            {
                p.OutputDataReceived += (s, e) => { if (e.Data == null) outDone.Set(); else if (onLine != null) onLine(Clean(e.Data)); };
                p.ErrorDataReceived += (s, e) => { if (e.Data == null) errDone.Set(); else if (onLine != null) onLine(Clean(e.Data)); };
                p.Start();
                try { p.StandardInput.Close(); } catch (Exception) { }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(250))
                {
                    if (cancel.IsCancellationRequested)
                    {
                        KillTree(p);
                        p.WaitForExit(5000);
                        Drain(outDone, errDone, 2000);
                        return -1;
                    }
                }
                // WaitForExit(int) returned true: the tool has exited. Do not call the unbounded WaitForExit() (see Run).
                Drain(outDone, errDone, 3000);
                return p.ExitCode;
            }
        }

        /// <summary>Waits for both output streams to finish, for at most <paramref name="totalMs"/> in all.</summary>
        static void Drain(ManualResetEventSlim outDone, ManualResetEventSlim errDone, int totalMs)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            outDone.Wait(totalMs);
            errDone.Wait(Math.Max(0, totalMs - (int)clock.ElapsedMilliseconds));
        }

        /// <summary>Removes NUL characters (SFC writes UTF-16 to redirected output) and trailing whitespace.</summary>
        public static string Clean(string line) { return line == null ? "" : line.Replace("\0", "").TrimEnd(); }

        static void KillTree(Process p)
        {
            try
            {
                using (var kill = Process.Start(new ProcessStartInfo(System32("taskkill.exe"), "/T /F /PID " + p.Id.ToString(CultureInfo.InvariantCulture)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                    kill.WaitForExit(5000);
            }
            catch (Exception) { }
            try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
        }
    }
}
