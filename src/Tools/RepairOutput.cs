using System;

namespace TweakBench.Tools
{
    /// <summary>Turns the output and exit codes of SFC and DISM into one plain sentence.</summary>
    public static class RepairOutput
    {
        public sealed class Verdict
        {
            public bool Ok;
            public bool RestartNeeded;
            public string Message = "";
        }

        /// <summary>Reads what System File Checker printed (any language falls back to the exit code).</summary>
        public static Verdict SummarizeSfc(string output, int exitCode)
        {
            string text = (output ?? "").Replace("\0", "");
            if (Has(text, "did not find any integrity violations"))
                return new Verdict { Ok = true, Message = "No problems found. Your Windows system files are intact." };
            if (Has(text, "successfully repaired"))
                return new Verdict { Ok = true, RestartNeeded = true, Message = "Problems were found and repaired. Restart your PC when convenient." };
            if (Has(text, "unable to fix"))
                return new Verdict { Ok = false, Message = "Some damaged files could not be repaired. Run \"Repair the Windows image\" next, then run this check again. Details are in C:\\Windows\\Logs\\CBS\\CBS.log." };
            if (Has(text, "pending system repair") || Has(text, "restart"))
                return new Verdict { Ok = false, RestartNeeded = true, Message = "Windows has a repair waiting for a restart. Restart the PC, then run the check again." };
            if (Has(text, "could not perform the requested operation") || exitCode != 0)
                return new Verdict { Ok = false, Message = "The check could not run (code " + exitCode + "). Try again after restarting the PC." };
            return new Verdict { Ok = true, Message = "The check finished." };
        }

        /// <summary>DISM exit codes: 0 done, 3010 done with restart needed, others are failures.</summary>
        public static Verdict SummarizeDism(int exitCode, bool isRepair)
        {
            switch (exitCode)
            {
                case 0: return new Verdict { Ok = true, Message = isRepair ? "The Windows image is healthy or was repaired." : "Cleanup finished." };
                case 3010: return new Verdict { Ok = true, RestartNeeded = true, Message = "Done. Restart your PC when convenient to finish." };
                case 740: return new Verdict { Ok = false, Message = "This needs administrator rights." };
                case -2146498529:
                case -2146498530:
                    return new Verdict { Ok = false, Message = "Windows could not find healthy source files. Check your internet connection and try again." };
                case -1: return new Verdict { Ok = false, Message = "Stopped." };
                default: return new Verdict { Ok = false, Message = "DISM stopped with code " + exitCode + ". Details are in C:\\Windows\\Logs\\DISM\\dism.log." };
            }
        }

        static bool Has(string text, string phrase) { return text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0; }
    }
}
