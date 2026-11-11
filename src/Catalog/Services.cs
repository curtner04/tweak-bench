using System;
using System.Collections.Generic;

namespace TweakBench.Core
{
    public static partial class Catalog
    {
        // Default start types below were checked on Windows 11 Pro build 26300 (Win32_Service) and against
        // https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server
        static partial void AddServices(List<Tweak> list)
        {
            // Source: same page, "Print Spooler": OK to disable if the PC is not a print server. Default Automatic.
            list.Add(new Tweak
            {
                Id = "services.print-spooler-off", Category = "services",
                Title = "Turn off the Print Spooler service",
                Summary = "The service that sends documents to printers is switched off and does not start with Windows.",
                Why = "If you never print from this PC, switching it off removes a service that has been the target of several serious security flaws. Turn it back on in one click when you need a printer.",
                OffMeans = "The Print Spooler starts with Windows (Automatic).",
                Caution = "Printing stops, including Print to PDF and XPS. Printers disappear from Settings until the service is turned back on.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "printer", "printing", "spooler", "printnightmare", "pdf", "security" },
                Changes = { Svc.Start("Spooler", StartMode.Disabled, StartMode.Automatic) },
            });

            // Source: same page, "Windows Search"; see also https://learn.microsoft.com/en-us/windows/win32/search/windows-search. Default Automatic (delayed start).
            list.Add(new Tweak
            {
                Id = "services.windows-search-off", Category = "services",
                Title = "Turn off Windows Search indexing",
                Summary = "Windows stops building its file index in the background.",
                Why = "Saves disk activity and a little memory, which helps on slow hard drives and older PCs. The trade-off is that file and mail search gets slower or stops returning results.",
                OffMeans = "Windows Search runs in the background and keeps an index of your files (Automatic, delayed start).",
                Caution = "File and mail search becomes slow or stops returning results, and apps that use the index (Outlook, OneNote, Mail) can lose search. Start menu results for files may be limited.",
                Risk = Risk.Advanced, TakesEffect = Takes.Immediately,
                Keywords = new[] { "indexing", "wsearch", "searchindexer", "search", "index", "disk usage" },
                Changes = { Svc.Start("WSearch", StartMode.Disabled, StartMode.AutomaticDelayed) },
            });

            // Source: https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server  (SysMain, "Superfetch"). Default Automatic on Windows 11 Pro.
            list.Add(new Tweak
            {
                Id = "services.sysmain-off", Category = "services",
                Title = "Turn off SysMain (Superfetch)",
                Summary = "Windows stops pre-loading frequently used programs into memory.",
                Why = "Only worth trying if SysMain shows high disk or memory use in Task Manager. On a PC with a fast SSD and plenty of memory, pre-loading brings little either way. On a hard drive, programs can start more slowly.",
                OffMeans = "SysMain runs in the background and learns which programs you use (Automatic).",
                Caution = "On a PC with a mechanical hard drive, apps and Windows can feel slower to open.",
                Risk = Risk.Advanced, TakesEffect = Takes.Immediately,
                Keywords = new[] { "superfetch", "prefetch", "memory", "disk usage 100%", "ssd", "sysmain" },
                Changes = { Svc.Start("SysMain", StartMode.Disabled, StartMode.Automatic) },
            });

            // Source: same page, "Downloaded Maps Manager": started on demand by apps that use downloaded maps. Default Automatic (delayed start).
            list.Add(new Tweak
            {
                Id = "services.downloaded-maps-manual", Category = "services",
                Title = "Do not start the Downloaded Maps service with Windows",
                Summary = "The service for offline maps no longer starts with Windows. It is set to Manual.",
                Why = "If you do not use the Maps app or offline maps, there is no reason for it to run all the time. Nothing starts a Manual service for you, so if an app needs it, offline maps may not work until you turn this off.",
                OffMeans = "The service is set to start with Windows (Automatic, delayed start).",
                Caution = "Offline maps may not work until you turn this card off.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "maps", "mapsbroker", "offline maps", "downloaded maps" },
                Changes = { Svc.Start("MapsBroker", StartMode.Manual, StartMode.AutomaticDelayed) },
            });

            // Source: https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server  (Distributed Link Tracking Client). Default Automatic.
            list.Add(new Tweak
            {
                Id = "services.link-tracking-manual", Category = "services",
                Title = "Do not start the Distributed Link Tracking service with Windows",
                Summary = "The service that repairs shortcuts when a file moves between drives or network PCs no longer starts with Windows. It is set to Manual.",
                Why = "Used mostly in office networks. On a home PC it runs for nothing. Nothing starts a Manual service for you, so shortcut repair effectively stops.",
                OffMeans = "The service starts with Windows (Automatic).",
                Caution = "Shortcut repair stops: a shortcut to a file that you later move to another drive or PC will not follow it.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "trkwks", "shortcuts", "link tracking", "ntfs", "broken shortcut" },
                Changes = { Svc.Start("TrkWks", StartMode.Manual, StartMode.Automatic) },
            });

            // Source: https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server  (Connected Devices Platform Service). Default Automatic (delayed start) on Windows 11 Pro.
            list.Add(new Tweak
            {
                Id = "services.connected-devices-manual", Category = "services",
                Title = "Start the Connected Devices Platform service only when needed",
                Summary = "The service behind Nearby sharing, Phone Link and Dynamic lock starts only when something asks for it.",
                Why = "If you do not share with nearby PCs or link a phone, the service has nothing to do. Windows features that need it start it again.",
                OffMeans = "The service starts with Windows (Automatic, delayed start).",
                Caution = "Nearby sharing, Phone Link, Dynamic lock and some device features can stop working or be slow to start.",
                Risk = Risk.Advanced, TakesEffect = Takes.Immediately,
                Keywords = new[] { "cdpsvc", "nearby sharing", "phone link", "dynamic lock", "cross-device", "connected devices" },
                Changes = { Svc.Start("CDPSvc", StartMode.Manual, StartMode.AutomaticDelayed) },
            });
        }
    }
}
