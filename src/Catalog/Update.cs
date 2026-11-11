using System;
using System.Collections.Generic;

namespace TweakBench.Core
{
    // Windows Update tweaks. Everything here is a policy value (restored by deleting it) except the settings that are
    // documented as plain Windows Update / Control Panel settings. No tweak turns Windows Update off.
    public static partial class Catalog
    {
        const string UpdGroupInstall = "update.install-mode";

        /// <summary>Windows Home ignores the Windows Update policies, so those tweaks are hidden there.</summary>
        static string UpdNeedsPro(ISystem sys)
        {
            string e = sys.Edition ?? "";
            return e.StartsWith("Core", StringComparison.OrdinalIgnoreCase)
                ? "Windows Home ignores this Windows Update policy. It works on Windows Pro, Enterprise and Education."
                : null;
        }

        static partial void AddUpdate(List<Tweak> list)
        {
            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-restart  (sections "Delay automatic restart" and
            //   "Registry keys used to manage restart": AUOptions = 4 with NoAutoRebootWithLoggedOnUsers = 1 stops automatic restarts while a user is signed in)
            // Note: the same page calls NoAutoRebootWithLoggedOnUsers a legacy setting that "doesn't work exactly as per description" (it can result in no quality-update restarts at all), and says a restart the user schedules from the notification overrides it. The wording below therefore never promises more than "set not to restart".
            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings  (NoAutoUpdate 0 = Automatic Updates enabled, ScheduledInstallDay 0 = every day, ScheduledInstallTime 0-23)
            // Note: Microsoft says the "Always automatically restart at the scheduled time" policy is legacy and not used on Windows 11, so it is explicitly set to 0 here to override any old leftover value.
            list.Add(new Tweak
            {
                Id = "update.no-auto-restart", Category = "update",
                Title = "Stop automatic restarts for updates",
                Summary = "Windows keeps downloading and installing updates by itself, but is set not to restart your PC while you are signed in.",
                Why = "Nobody loses open work to a surprise restart. Updates finish installing after you restart the PC yourself, whenever it suits you.",
                OffMeans = "Windows decides when to restart: it picks a time outside your active hours and can restart on its own after a few days.",
                Caution = "This strongly reduces surprise restarts while you are signed in. Microsoft calls it a legacy setting that may not behave exactly as described, so it is not a guarantee. It does not cancel a restart Windows already scheduled, and a restart you schedule yourself still happens. A restart can still happen while nobody is signed in. Updates install at about 3 AM, or when the PC is next awake. Windows keeps asking you to restart until you do, so restart now and then so security fixes finish installing. Settings may say some options are managed by your organization.",
                Recommended = true, Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                ExclusiveGroup = UpdGroupInstall,
                NotApplicable = UpdNeedsPro,
                Keywords = new[] { "reboot", "restart", "force restart", "auto reboot", "stop restarting", "never reboot", "surprise restart", "NoAutoRebootWithLoggedOnUsers", "AUOptions", "active hours" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "NoAutoUpdate", 0),
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "AUOptions", 4),
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "ScheduledInstallDay", 0),
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "ScheduledInstallTime", 3),
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "NoAutoRebootWithLoggedOnUsers", 1),
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "AlwaysAutoRebootAtScheduledTime", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings  (AUOptions 3 = "Auto download and notify for install"; NoAutoUpdate 0 = enabled)
            list.Add(new Tweak
            {
                Id = "update.notify-before-install", Category = "update",
                Title = "Ask me before installing updates",
                Summary = "Windows downloads updates in the background and tells you when they are ready, then waits for you to install them.",
                Why = "You choose the moment for every update, which suits a PC you rely on for long jobs. It is the alternative to \"Stop automatic restarts for updates\": turning one on turns the other off.",
                OffMeans = "Windows installs updates on its own.",
                Caution = "Nothing installs until you do it in Settings > Windows Update, so security fixes wait for you. Windows can still schedule a restart after you start an install, so keep the restart tweak in mind too.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                ExclusiveGroup = UpdGroupInstall,
                NotApplicable = UpdNeedsPro,
                Keywords = new[] { "manual updates", "notify", "ask first", "download only", "AUOptions", "install later" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "NoAutoUpdate", 0),
                    Reg.Dword(Hive.Machine, Reg.AutoUpdatePolicy, "AUOptions", 3),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-restart  (Configure active hours: SetActiveHours, ActiveHoursStart, ActiveHoursEnd under HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate; max range 18 hours)
            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update  (ActiveHoursStart/End range 0-23, ActiveHoursMaxRange 8-18)
            list.Add(new Tweak
            {
                Id = "update.long-active-hours", Category = "update",
                Title = "Treat 6 AM to midnight as active hours",
                Summary = "Windows will not restart for updates between 6 AM and midnight, the longest span it allows (18 hours).",
                Why = "A lighter option for people who do not want the full restart block: automatic restarts can only happen overnight.",
                OffMeans = "Active hours are whatever you set in Settings (8 AM to 5 PM unless you changed them), and Windows may restart outside them.",
                Caution = "Microsoft documents that this has no effect while \"Stop automatic restarts for updates\" is on, because that setting already blocks restarts while you are signed in. A restart can still happen overnight.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                NotApplicable = UpdNeedsPro,
                Keywords = new[] { "active hours", "restart window", "SetActiveHours", "working hours", "quiet hours" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "SetActiveHours", 1),
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "ActiveHoursStart", 6),
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "ActiveHoursEnd", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-restart  (Control restart notifications; this setting is the "Notify me when a restart is required to finish updating" switch in Settings)
            // Source: https://www.elevenforum.com/t/enable-or-disable-auto-restart-notifications-for-windows-update-in-windows-11.2840/  (RestartNotificationsAllowed2 under HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings, 1 = on)
            list.Add(new Tweak
            {
                Id = "update.restart-notifications", Category = "update",
                Title = "Tell me when a restart is needed",
                Summary = "Windows shows a notification when an update is waiting for a restart.",
                Why = "If you stop automatic restarts, this is how you find out that an update still needs one. Restarting now and then keeps your security fixes current.",
                OffMeans = "Windows does not show the extra restart notification (the Settings page still shows the pending restart).",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "restart reminder", "notify restart", "RestartNotificationsAllowed2", "finish updating" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdateUx, "RestartNotificationsAllowed2", 1),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-msched  (Automatic Maintenance WakeUp policy: when not forced on, the Control Panel wake setting applies, so the Control Panel value is the one switched off here)
            // Source: https://learn.microsoft.com/en-us/windows/win32/taskschd/task-maintenence  (Automatic Maintenance can wake the computer; Control Panel > Security and Maintenance setting stored under Schedule\Maintenance WakeUp)
            // Allow wake timers values (verified with powercfg /q on this PC): 0 = Disable, 1 = Enable, 2 = Important wake timers only.
            list.Add(new Tweak
            {
                Id = "update.no-wake-for-updates", Category = "update",
                Title = "Do not wake the PC to install updates",
                Summary = "A sleeping PC stays asleep: automatic maintenance and updates cannot wake it up on a timer.",
                Why = "No more PC that wakes up in the night and spins its fans. Updates run when the PC is next awake.",
                OffMeans = "Windows can wake the PC from sleep for maintenance and update work.",
                Caution = "Turning off wake timers also stops other timed wake-ups, such as a scheduled backup or task set to \"wake the computer\", and some alarm apps. This changes the power plan you are using now; another plan keeps its own wake timer setting.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "wake timers", "wake from sleep", "wake up at night", "RTCWAKE", "maintenance", "sleep", "WakeUp" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "WakeUp", 0),
                    Power.Value("Allow wake timers", "SUB_SLEEP", "RTCWAKE", 0, 0, 1, 1),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows-server/security/windows-authentication/winlogon-automatic-restart-sign-on-arso
            //   (policy "Sign-in and lock last interactive user automatically after a restart": HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\DisableAutomaticRestartSignOn, 1 = disabled)
            list.Add(new Tweak
            {
                Id = "update.no-auto-sign-in-after-restart", Category = "update",
                Title = "Do not sign in automatically after a restart",
                Summary = "After a restart you did not sign out for, Windows waits at the sign-in screen instead of quietly signing you in and locking the PC.",
                Why = "Windows keeps your sign-in details to do this and reopens apps behind the lock screen. Turning it off is safer on a shared or portable PC.",
                OffMeans = "After a restart you did not sign out for, such as one for a Windows Update, Windows signs you in and locks the PC so your apps can reopen.",
                Caution = "You will have to sign in yourself after a restart, and apps that used to reopen on their own will not. If Windows restarts while you are away, your apps will not reopen on their own.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Presets = new[] { "privacy" },
                Keywords = new[] { "ARSO", "automatic restart sign-on", "DisableAutomaticRestartSignOn", "auto login", "lock screen apps" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.MachinePolicySystem, "DisableAutomaticRestartSignOn", 1),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update  (DeferFeatureUpdatesPeriodInDays, range 0-365, Pro and above)
            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-configure-wufb  (DeferFeatureUpdates / DeferFeatureUpdatesPeriodInDays under HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate)
            list.Add(new Tweak
            {
                Id = "update.delay-feature-updates", Category = "update",
                Title = "Wait a year before big Windows upgrades",
                Summary = "Large yearly Windows upgrades are offered 365 days after release instead of right away.",
                Why = "Early versions of a big upgrade often have bugs that get fixed later. Waiting lets other people find them first. Regular security updates are not delayed by this.",
                OffMeans = "Windows offers new feature upgrades as soon as it considers your PC ready.",
                Caution = "You will not get new Windows features for a year. This does not block updates forever: when your Windows version reaches the end of its support, Windows still offers the next one.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                NotApplicable = UpdNeedsPro,
                Keywords = new[] { "defer", "postpone", "feature update", "upgrade", "25H2", "26H2", "DeferFeatureUpdates" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "DeferFeatureUpdates", 1),
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "DeferFeatureUpdatesPeriodInDays", 365),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update  (DeferQualityUpdatesPeriodInDays, range 0-30, Pro and above)
            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-configure-wufb  (DeferQualityUpdates / DeferQualityUpdatesPeriodInDays under HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate)
            list.Add(new Tweak
            {
                Id = "update.delay-quality-updates", Category = "update",
                Title = "Wait 7 days before monthly updates install",
                Summary = "Monthly fixes and security updates are held back for 7 days after they come out.",
                Why = "If a monthly update turns out to be faulty, Microsoft often pulls or fixes it within a week, so you miss the problem.",
                OffMeans = "Monthly updates install as soon as they are available.",
                Caution = "Your PC stays exposed to newly fixed security holes for an extra week. Skip this if the PC handles anything sensitive.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                NotApplicable = UpdNeedsPro,
                Keywords = new[] { "defer", "postpone", "quality update", "patch tuesday", "cumulative update", "DeferQualityUpdates" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "DeferQualityUpdates", 1),
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "DeferQualityUpdatesPeriodInDays", 7),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update  (ExcludeWUDriversInQualityUpdate: 1 = exclude drivers; HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate)
            // Source: https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings  ("Do not include drivers with Windows Updates")
            list.Add(new Tweak
            {
                Id = "update.no-driver-updates", Category = "update",
                Title = "Do not install drivers through Windows Update",
                Summary = "Windows Update stops adding hardware drivers to its updates.",
                Why = "Drivers from Windows Update are sometimes older or different from the ones your PC maker or graphics card maker provides, and a swap can break things. You keep control of your drivers.",
                OffMeans = "Windows Update installs drivers along with its other updates.",
                Caution = "You must update drivers yourself, for example from the maker's website, or new hardware might not work until you do.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                NotApplicable = UpdNeedsPro,
                Keywords = new[] { "drivers", "graphics driver", "nvidia", "amd", "ExcludeWUDriversInQualityUpdate", "driver swap" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdatePolicy, "ExcludeWUDriversInQualityUpdate", 1),
                },
            });

            // Source: https://www.elevenforum.com/t/enable-or-disable-get-latest-updates-as-soon-as-available-in-windows-11.13821/
            //   (IsContinuousInnovationOptedIn under HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings: 1 = on, 0 = off)
            list.Add(new Tweak
            {
                Id = "update.no-early-updates", Category = "update",
                Title = "Do not get new features early",
                Summary = "Turns off \"Get the latest updates as soon as they're available\" in Windows Update settings.",
                Why = "New features and fixes then arrive with the normal monthly rollout, after more people have tried them.",
                OffMeans = "Windows can deliver new non-security features and fixes as soon as Microsoft releases them.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                MinBuild = 22621,
                Keywords = new[] { "latest updates", "as soon as available", "early", "optional updates", "continuous innovation", "IsContinuousInnovationOptedIn" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, Reg.WindowsUpdateUx, "IsContinuousInnovationOptedIn", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-deliveryoptimization  (DODownloadMode 0 = HTTP only, no peering; HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization)
            // Source: https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization  (Delivery Optimization shares update files with other PCs)
            list.Add(new Tweak
            {
                Id = "update.no-peer-sharing", Category = "update",
                Title = "Do not share updates with other PCs",
                Summary = "Windows downloads updates only from Microsoft and never uploads parts of them to other computers.",
                Why = "Saves your upload bandwidth and stops your PC sending update files to other computers. The only cost is that PCs on your own home network no longer help each other download.",
                OffMeans = "Windows can fetch update pieces from nearby PCs and share what it has downloaded with them.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Presets = new[] { "privacy" },
                NotApplicable = UpdNeedsPro,
                Keywords = new[] { "delivery optimization", "peer to peer", "p2p", "upload", "bandwidth", "DODownloadMode" },
                Changes =
                {
                    Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0),
                },
            });
        }
    }
}
