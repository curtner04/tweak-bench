using System.Collections.Generic;

namespace TweakBench.Core
{
    /// <summary>Privacy tweaks: diagnostic data, advertising, activity history, location and Windows AI features.</summary>
    public static partial class Catalog
    {
        const string PrivacyPreset = "privacy";

        static partial void AddPrivacy(List<Tweak> list)
        {
            // ---------------------------------------------------------------- Diagnostic data

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#allowtelemetry
            list.Add(new Tweak
            {
                Id = "privacy.required-diagnostic-data", Category = "privacy",
                Title = "Send only required diagnostic data",
                Summary = "Windows sends Microsoft only the minimum diagnostic data it needs to stay secure and up to date.",
                Why = "Stops the optional extras (app usage, browsing and inking data, crash details) from being sent, and greys out the optional switch in Settings. Windows Home and Pro cannot turn diagnostic data off completely, and Microsoft treats the lowest setting on those editions as Required, so a value of 0 there behaves the same as this tweak.",
                OffMeans = "You choose in Settings > Privacy & security > Diagnostics & feedback whether to send optional data.",
                Caution = "Locks the optional diagnostic data switch, so you cannot join or stay in the Windows Insider Program while this is on.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "telemetry", "diagnostics", "AllowTelemetry", "basic", "optional diagnostic data" },
                Changes = { Reg.Dword(Hive.Machine, Reg.DataCollectionPolicy, "AllowTelemetry", 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#limitdumpcollection
            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#limitdiagnosticlogcollection
            list.Add(new Tweak
            {
                Id = "privacy.limit-crash-dumps-and-logs", Category = "privacy",
                Title = "Limit crash dumps and diagnostic logs sent to Microsoft",
                Summary = "If optional diagnostic data is ever on, Windows will not send diagnostic logs and will only send small crash dumps.",
                Why = "Full crash dumps and logs can contain pieces of what you were working on. This matters only if optional diagnostic data is on; it is a second layer of protection if it ever gets turned on.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately, MinBuild = 22000,
                Keywords = new[] { "dump", "crash", "logs", "LimitDumpCollection", "LimitDiagnosticLogCollection" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.DataCollectionPolicy, "LimitDumpCollection", 1),
                    Reg.Dword(Hive.Machine, Reg.DataCollectionPolicy, "LimitDiagnosticLogCollection", 1),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#disableonesettingsdownloads
            list.Add(new Tweak
            {
                Id = "privacy.no-onesettings-downloads", Category = "privacy",
                Title = "Stop Windows fetching remote configuration (OneSettings)",
                Summary = "Windows no longer contacts Microsoft's OneSettings service to download configuration changes.",
                Why = "OneSettings lets Microsoft switch features and experiments on or off on your PC without an update. Blocking it gives you more control but you may miss fixes that Microsoft delivers this way.",
                Caution = "Some Microsoft fixes and feature rollouts arrive through this service, so they may reach this PC late or not at all.",
                Risk = Risk.Advanced, TakesEffect = Takes.Immediately, MinBuild = 22000,
                Keywords = new[] { "onesettings", "remote config", "experiments", "feature flags" },
                Changes = { Reg.Dword(Hive.Machine, Reg.DataCollectionPolicy, "DisableOneSettingsDownloads", 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience (feedback notifications) and the Feedback Hub "Siuf" setting
            list.Add(new Tweak
            {
                Id = "privacy.no-feedback-prompts", Category = "privacy",
                Title = "Stop Windows asking for feedback",
                Summary = "Windows no longer pops up \"How likely are you to recommend...\" and similar feedback requests.",
                Why = "Nobody has to rate Windows. You can still send feedback yourself from the Feedback Hub.",
                Recommended = true, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "feedback", "survey", "SIUF", "notifications", "rate windows" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.DataCollectionPolicy, "DoNotShowFeedbackNotifications", 1),
                    Reg.Dword(Hive.User, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization (Connected User Experiences and Telemetry service)
            list.Add(new Tweak
            {
                Id = "privacy.disable-diagtrack", Category = "privacy",
                Title = "Turn off the Connected User Experiences and Telemetry service",
                Summary = "The background service that uploads diagnostic data (DiagTrack) is disabled and stopped.",
                Why = "This is the part of Windows that actually sends diagnostic data. Disabling it keeps the data on your PC. Nothing you use every day depends on it.",
                Caution = "Windows Insider builds and some Microsoft diagnostics tools expect this service. Windows Update still works.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "DiagTrack", "telemetry service", "connected user experiences" },
                Changes = { Svc.Start("DiagTrack", StartMode.Disabled, StartMode.Automatic) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services (dmwappushservice)
            list.Add(new Tweak
            {
                Id = "privacy.disable-wap-push", Category = "privacy",
                Title = "Turn off the Device Management WAP Push service",
                Summary = "The service that receives remote-management push messages (dmwappushservice) is disabled.",
                Why = "Only used by organisations that manage PCs with Intune or similar tools. A home PC does not need it.",
                Caution = "Do not turn this on for a PC managed by your work or school: it can stop device management messages arriving.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "dmwappushservice", "wap push", "mdm", "intune" },
                Changes = { Svc.Start("dmwappushservice", StartMode.Disabled, StartMode.Manual) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services (scheduled tasks)
            list.Add(new Tweak
            {
                Id = "privacy.disable-telemetry-tasks", Category = "privacy",
                Title = "Turn off Microsoft's telemetry scheduled tasks",
                Summary = "The compatibility appraiser, customer-experience (CEIP) and disk diagnostics collection tasks are disabled.",
                Why = "These tasks run in the background to gather app and hardware data for Microsoft. Tasks that do not exist on your Windows version are skipped.",
                Caution = "The Compatibility Appraiser also helps Windows judge whether a big update will work on your PC.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "scheduled tasks", "compatibility appraiser", "CEIP", "consolidator", "usbceip", "disk diagnostic" },
                Changes =
                {
                    Sched.Disable(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp"),
                    Sched.Disable(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"),
                    Sched.Disable(@"\Microsoft\Windows\Application Experience\ProgramDataUpdater"),
                    Sched.Disable(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"),
                    Sched.Disable(@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"),
                    Sched.Disable(@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services (Customer Experience Improvement Program)
            list.Add(new Tweak
            {
                Id = "privacy.opt-out-ceip", Category = "privacy",
                Title = "Opt out of the Customer Experience Improvement Program",
                Summary = "Windows stops sending anonymous usage statistics to the Customer Experience Improvement Program.",
                Why = "A policy-level opt-out that Windows features check before they send usage statistics. No visible change for you.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "CEIP", "SQM", "customer experience", "usage statistics" },
                Changes = { Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services (Application Compatibility: Inventory Collector, Application Telemetry)
            list.Add(new Tweak
            {
                Id = "privacy.no-app-inventory", Category = "privacy",
                Title = "Stop Windows listing your installed apps for Microsoft",
                Summary = "Turns off the application inventory collector and application telemetry that report your installed programs.",
                Why = "Windows normally builds a list of your installed programs and add-ins to check compatibility. Turning it off keeps that list on your PC.",
                Caution = "Windows has less information to warn you about incompatible programs before a major update.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "inventory", "application telemetry", "AITEnable", "DisableInventory", "compatibility" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory", 1),
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", 0),
                },
            });

            // ---------------------------------------------------------------- Advertising and personalisation

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy#disableadvertisingid (the Settings switch writes HKCU AdvertisingInfo\Enabled)
            list.Add(new Tweak
            {
                Id = "privacy.no-advertising-id", Category = "privacy",
                Title = "Turn off the advertising ID",
                Summary = "Apps can no longer use a personal advertising ID to show you tailored ads.",
                Why = "The advertising ID lets apps follow you across apps for ad targeting. You will still see ads in apps, but they are less targeted.",
                Recommended = true, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "advertising id", "ads", "AdvertisingInfo", "personalised ads", "tracking" },
                Changes = { Reg.Dword(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, windowsDefault: 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services (Tailored experiences; HKCU Privacy TailoredExperiencesWithDiagnosticDataEnabled)
            list.Add(new Tweak
            {
                Id = "privacy.no-tailored-experiences", Category = "privacy",
                Title = "Do not tailor tips and ads using diagnostic data",
                Summary = "Microsoft does not use your diagnostic data to personalise tips, recommendations and ads.",
                Why = "Keeps what Windows learns about how you use your PC out of the suggestions you see. You still get general tips.",
                Recommended = true, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "tailored experiences", "personalised", "recommendations", "TailoredExperiencesWithDiagnosticDataEnabled" },
                Changes = { Reg.Dword(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, windowsDefault: 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy#enableactivityfeed
            list.Add(new Tweak
            {
                Id = "privacy.no-activity-history", Category = "privacy",
                Title = "Turn off activity history",
                Summary = "Windows stops recording the apps, files and websites you open and stops sending that history to Microsoft.",
                Why = "Activity history powers the old Timeline and \"pick up where you left off\" features. Turning it off keeps a record of your activity from being collected.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.SignOut,
                Keywords = new[] { "timeline", "activity feed", "EnableActivityFeed", "PublishUserActivities", "UploadUserActivities" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.SystemPolicy, "EnableActivityFeed", 0),
                    Reg.Dword(Hive.Machine, Reg.SystemPolicy, "PublishUserActivities", 0),
                    Reg.Dword(Hive.Machine, Reg.SystemPolicy, "UploadUserActivities", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/powershell/module/international/set-winacceptlanguagefromlanguagelistoptout (same setting as the HKCU HttpAcceptLanguageOptOut value)
            list.Add(new Tweak
            {
                Id = "privacy.hide-language-list", Category = "privacy",
                Title = "Do not let websites read your language list",
                Summary = "Websites no longer receive the list of languages you have installed in Windows.",
                Why = "Your language list is a small fingerprint that sites can use to recognise you. Browsers still send the language they are set to.",
                Presets = new[] { PrivacyPreset }, TakesEffect = Takes.SignOut,
                Keywords = new[] { "language list", "HttpAcceptLanguageOptOut", "locally relevant content", "fingerprint" },
                Changes = { Reg.Dword(Hive.User, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1) },
            });

            // Source: Settings > Privacy & security > General ("Let Windows improve Start and search results by tracking app launches"); value Start_TrackProgs
            list.Add(new Tweak
            {
                Id = "privacy.no-app-launch-tracking", Category = "privacy",
                Title = "Stop tracking which apps you launch",
                Summary = "Windows no longer keeps count of the apps you open to improve Start and search.",
                Why = "Start and search stop learning which apps you open, so app suggestions are less personal.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.ExplorerRestart,
                Keywords = new[] { "Start_TrackProgs", "track app launches", "most used apps", "frequent apps" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "Start_TrackProgs", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/configuration/manage-tips-and-suggestions (ContentDeliveryManager subscribed content)
            list.Add(new Tweak
            {
                Id = "privacy.no-settings-suggestions", Category = "privacy",
                Title = "Hide suggested content in Settings",
                Summary = "The Settings app stops showing suggested apps, tips and offers.",
                Why = "Settings is for settings, not ads. Nothing you need is hidden.",
                Recommended = true, Presets = new[] { PrivacyPreset, "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "suggestions", "ads in settings", "subscribed content", "338393", "353694", "353696" },
                Changes =
                {
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SubscribedContent-338393Enabled", 0, windowsDefault: 1),
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SubscribedContent-353694Enabled", 0, windowsDefault: 1),
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SubscribedContent-353696Enabled", 0, windowsDefault: 1),
                },
            });

            // ---------------------------------------------------------------- Speech, inking, location, error reporting

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy#allowinputpersonalization (policy); HKCU HasAccepted is the Settings > Privacy > Speech switch
            list.Add(new Tweak
            {
                Id = "privacy.no-online-speech", Category = "privacy",
                Title = "Turn off online speech recognition",
                Summary = "Voice typing and dictation no longer send what you say to Microsoft's cloud speech service.",
                Why = "Your voice is not uploaded for recognition. Offline features such as Narrator keep working.",
                Caution = "Cloud-based dictation (Win + H voice typing) and voice assistants that rely on Microsoft's speech service stop working. The switch in Settings is locked.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "speech", "voice typing", "dictation", "AllowInputPersonalization", "OnlineSpeechPrivacy", "voice" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\InputPersonalization", "AllowInputPersonalization", 0),
                    Reg.Dword(Hive.User, @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services (inking and typing); values used by the Settings > Privacy > Inking & typing switch
            list.Add(new Tweak
            {
                Id = "privacy.no-inking-typing-personalization", Category = "privacy",
                Title = "Stop collecting your typing and handwriting to personalise suggestions",
                Summary = "Windows no longer learns from what you type and write, or from your contacts, to build a custom dictionary.",
                Why = "Typing suggestions, autocorrect and handwriting recognition stay generic. Your words are not collected to improve them.",
                Caution = "Suggestions and handwriting recognition may be slightly less tuned to your vocabulary.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.SignOut,
                Keywords = new[] { "inking", "typing", "handwriting", "personalization", "RestrictImplicitTextCollection", "HarvestContacts", "dictionary" },
                Changes =
                {
                    Reg.Dword(Hive.User, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, windowsDefault: 0),
                    Reg.Dword(Hive.User, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, windowsDefault: 0),
                    Reg.Dword(Hive.User, @"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, windowsDefault: 1),
                    Reg.Dword(Hive.User, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services (handwriting data sharing and error reports; ADMX TabletPC.admx, ErrorReporting)
            list.Add(new Tweak
            {
                Id = "privacy.no-handwriting-sharing", Category = "privacy",
                Title = "Do not share handwriting samples or handwriting error reports",
                Summary = "Windows does not send your handwriting samples or handwriting recognition error reports to Microsoft.",
                Why = "If you use a pen or touch screen, your handwriting stays on your PC. Handwriting recognition keeps working.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "handwriting", "pen", "ink", "tablet", "PreventHandwritingDataSharing", "PreventHandwritingErrorReports" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\TabletPC", "PreventHandwritingDataSharing", 1),
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports", "PreventHandwritingErrorReports", 1),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#allowlocation (the per-user switch is the CapabilityAccessManager consent store)
            list.Add(new Tweak
            {
                Id = "privacy.no-location", Category = "privacy",
                Title = "Block apps from using your location",
                Summary = "Your account denies location access to all apps and Windows features.",
                Why = "Apps and websites cannot see where you are. Good for a desktop that never moves; on a laptop it is worth thinking about.",
                Caution = "Maps, weather, the clock's automatic time zone and Find My Device cannot use your location while this is on.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "location", "gps", "maps", "ConsentStore", "geolocation" },
                Changes = { Reg.String(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", windowsDefault: "Allow") },
            });

            // Source: https://learn.microsoft.com/en-us/windows/win32/wer/wer-settings (Disabled value) and the "Disable Windows Error Reporting" Group Policy
            list.Add(new Tweak
            {
                Id = "privacy.no-error-reporting", Category = "privacy",
                Title = "Turn off Windows Error Reporting",
                Summary = "When an app crashes, Windows no longer offers to send a report to Microsoft.",
                Why = "Crash reports can include memory contents. Turning reporting off keeps them on your PC.",
                Caution = "Microsoft and app makers get less information to fix crashes, and Windows will not suggest solutions to problems it finds.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "WER", "crash report", "problem reporting", "error report" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1),
                    Sched.Disable(@"\Microsoft\Windows\Windows Error Reporting\QueueReporting"),
                },
            });

            // ---------------------------------------------------------------- Windows AI

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#disableaidataanalysis
            list.Add(new Tweak
            {
                Id = "privacy.no-recall-snapshots", Category = "privacy",
                Title = "Turn off saving Recall snapshots",
                Summary = "Windows does not save screenshots of your screen for Recall, and deletes any it already saved.",
                Why = "Recall takes pictures of what is on screen to build a searchable history. Turning it off means nothing you view is captured. Recall is off by default on managed PCs and needs you to opt in on others.",
                Caution = "Snapshots already saved on this PC are deleted. This works on Windows 11 24H2 and later.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately, MinBuild = 26100,
                Keywords = new[] { "recall", "snapshots", "screenshots", "DisableAIDataAnalysis", "copilot+", "AI" },
                Changes = { Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#allowrecallenablement
            list.Add(new Tweak
            {
                Id = "privacy.remove-recall", Category = "privacy",
                Title = "Make Recall unavailable and remove it",
                Summary = "The Recall component is removed from Windows so nobody can switch it on.",
                Why = "A stronger step than turning off snapshots: Recall's files are removed and it cannot be enabled in Settings.",
                Caution = "Needs a restart. Any saved Recall snapshots are deleted. If you later turn this off, Recall may need to be added back as an optional Windows feature before it can be used.",
                Risk = Risk.Moderate, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.PcRestart, MinBuild = 26100,
                Keywords = new[] { "recall", "AllowRecallEnablement", "copilot+", "AI", "remove" },
                Changes = { Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#disableclicktodo
            list.Add(new Tweak
            {
                Id = "privacy.disable-click-to-do", Category = "privacy",
                Title = "Turn off Click to Do",
                Summary = "The Click to Do screen overlay, which analyses what is on your screen, is switched off.",
                Why = "Click to Do takes a screenshot of your screen and analyses it on the PC to suggest actions. It is only available on some PCs (mainly Copilot+ PCs); on others this has no visible effect. This policy is documented for recent Windows 11 builds and may be ignored on older ones.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.SignOut, MinBuild = 26100,
                Keywords = new[] { "click to do", "DisableClickToDo", "AI", "copilot+", "screen overlay" },
                Changes = { Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#turnoffwindowscopilot
            list.Add(new Tweak
            {
                Id = "privacy.turn-off-copilot-policy", Category = "privacy",
                Title = "Turn off Windows Copilot (policy)",
                Summary = "Sets the Group Policy that turns off the older Windows Copilot side panel for your account.",
                Why = "Microsoft marks this policy deprecated and says it does not apply to the newer Copilot app, which is a normal app you can close or uninstall. It only affects the older side-panel version.",
                Caution = "This does not remove or hide the Copilot app on current Windows 11. To remove the app, use Remove built-in apps.",
                Risk = Risk.Safe, TakesEffect = Takes.SignOut, MinBuild = 22000,
                Keywords = new[] { "copilot", "TurnOffWindowsCopilot", "AI assistant", "bing chat" },
                Changes = { Reg.Dword(Hive.User, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#disablecocreator
            list.Add(new Tweak
            {
                Id = "privacy.no-paint-ai", Category = "privacy",
                Title = "Turn off the AI image features in Paint",
                Summary = "Paint's Cocreator, Image Creator and Generative fill are turned off.",
                Why = "These Paint features send your drawing or prompts to an AI service. Turning them off keeps Paint working offline and private.",
                Risk = Risk.Safe, Presets = new[] { PrivacyPreset }, TakesEffect = Takes.Immediately, MinBuild = 22621,
                Keywords = new[] { "paint", "cocreator", "image creator", "generative fill", "AI", "DisableCocreator" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableCocreator", 1),
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableGenerativeFill", 1),
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableImageCreator", 1),
                },
            });
        }
    }
}
