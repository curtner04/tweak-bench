using System.Collections.Generic;

namespace TweakBench.Core
{
    // Start menu, taskbar and search tweaks. Every registry path and value below was checked against Microsoft Learn
    // (Policy CSP / ADMX reference) or a widely used reference, and against this PC's registry (read only).
    public static partial class Catalog
    {
        const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
        const string SearchSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\SearchSettings";
        const string ExplorerPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\Explorer";
        const string UserExplorerPolicyKey = @"Software\Policies\Microsoft\Windows\Explorer";

        static partial void AddStart(List<Tweak> list)
        {
            // ---------------- Taskbar ----------------

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests (AllowNewsAndInterests, Windows 11, Pro and up)
            // The per-user switch (TaskbarDa) is protected by Windows on current builds, so the supported device policy is used instead.
            list.Add(new Tweak
            {
                Id = "start.hide-widgets", Category = "start",
                Title = "Hide the Widgets button and turn Widgets off",
                Summary = "Removes the weather and news button from the taskbar and stops the Widgets board from loading.",
                Why = "Widgets keeps a news and weather feed running in the background. Hide it if you never open it. This uses the official Windows policy, because the normal taskbar switch is protected by Windows.",
                OffMeans = "The Widgets button shows on the taskbar and the board opens from it or from Win + W.",
                Caution = "Applies to every user on this PC. The Widgets switch in Settings no longer works while this is on.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "widgets", "news", "weather", "feed", "interests", "msn" },
                Changes = { Reg.Dword(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0) },
            });

            // Source: Taskbar "Task view" switch, Settings > Personalization > Taskbar.
            list.Add(new Tweak
            {
                Id = "start.hide-task-view", Category = "start",
                Title = "Hide the Task view button",
                Summary = "Removes the Task view button from the taskbar.",
                Why = "Frees a little room on the taskbar. Task view still opens with Win + Tab.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "task view", "desktops", "virtual desktops", "win tab" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "ShowTaskViewButton", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: HKCU Search\SearchboxTaskbarMode (Settings > Personalization > Taskbar > Search): 0 hide, 1 icon only, 2 box, 3 icon and label.
            // This is not the same numbering as the ConfigureSearchOnTaskbarMode policy.
            list.Add(new Tweak
            {
                Id = "start.search-icon-only", Category = "start",
                Title = "Show search as a small icon on the taskbar",
                Summary = "Replaces the wide search box with a single magnifier icon.",
                Why = "Saves a large amount of taskbar space. Search works the same way, and typing in the Start menu still searches.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                ExclusiveGroup = "taskbar-search",
                Keywords = new[] { "search box", "taskbar search", "magnifier", "search icon" },
                Changes = { Reg.Dword(Hive.User, SearchKey, "SearchboxTaskbarMode", 1) },
            });

            // Source: HKCU Search\SearchboxTaskbarMode: 0 hide (see start.search-icon-only for the full numbering; not the policy numbering).
            list.Add(new Tweak
            {
                Id = "start.search-hidden", Category = "start",
                Title = "Hide search from the taskbar",
                Summary = "Removes the search box and icon from the taskbar completely.",
                Why = "Gives the taskbar the most room. You can still search by opening Start and typing, or by pressing Win + S.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                ExclusiveGroup = "taskbar-search",
                Keywords = new[] { "search box", "taskbar search", "remove search", "hide search" },
                Changes = { Reg.Dword(Hive.User, SearchKey, "SearchboxTaskbarMode", 0) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (HKCU Explorer\Advanced TaskbarAl: 0 left, 1 center)
            list.Add(new Tweak
            {
                Id = "start.align-left", Category = "start",
                Title = "Align taskbar icons to the left",
                Summary = "Moves the Start button and app icons to the left edge, like Windows 10.",
                Why = "The Start button stays in one place no matter how many apps are open.",
                OffMeans = "Icons are centered on the taskbar.",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "taskbar alignment", "start button left", "center", "taskbaral" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "TaskbarAl", 0, windowsDefault: 1) },
            });

            // Source: Settings > System > For developers > "End Task" (TaskbarEndTask)
            list.Add(new Tweak
            {
                Id = "start.taskbar-end-task", Category = "start",
                Title = "Add End task to the taskbar right-click menu",
                Summary = "Right-click an app on the taskbar and choose End task to close it by force.",
                Why = "A quick way to close a frozen program without opening Task Manager. Unsaved work in that program is lost.",
                Caution = "End task closes the app at once, without asking to save.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately, MinBuild = 22621,
                Keywords = new[] { "end task", "kill", "force close", "frozen", "not responding", "developer settings" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1, windowsDefault: 0) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (ShowSecondsInSystemClock)
            list.Add(new Tweak
            {
                Id = "start.clock-seconds", Category = "start",
                Title = "Show seconds in the taskbar clock",
                Summary = "The clock in the corner of the taskbar counts seconds.",
                Why = "Handy when you time things or check that clocks agree. It adds a very small amount of screen updating.",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart, MinBuild = 22621,
                Keywords = new[] { "clock", "seconds", "time", "system tray" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "ShowSecondsInSystemClock", 1, windowsDefault: 0) },
            });

            // Source: Settings > Personalization > Taskbar > Taskbar behaviors > "Combine taskbar buttons and hide labels" (TaskbarGlomLevel: 0 always, 1 when full, 2 never; Windows 11 23H2 and later)
            list.Add(new Tweak
            {
                Id = "start.never-combine", Category = "start",
                Title = "Never combine taskbar buttons (main taskbar)",
                Summary = "Each open window gets its own labeled button instead of sharing one icon per app.",
                Why = "Makes it easy to see and pick one of many windows of the same app. The taskbar fills up faster.",
                OffMeans = "Windows of the same app share a single icon.",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart, MinBuild = 22631,
                Keywords = new[] { "combine", "labels", "group", "ungroup", "taskbar buttons", "glom" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "TaskbarGlomLevel", 2, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: https://winaero.com/enable-show-desktop-on-taskbar (TaskbarSd: 1 shows the corner button)
            list.Add(new Tweak
            {
                Id = "start.show-desktop-button", Category = "start",
                Title = "Show the Show desktop button at the far right",
                Summary = "Adds a thin button at the right end of the taskbar that minimizes everything to show the desktop.",
                Why = "One click to get to the desktop, as in older versions of Windows.",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "show desktop", "minimize all", "peek", "taskbarsd" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "TaskbarSd", 1, windowsDefault: 0) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (TaskbarBadges)
            list.Add(new Tweak
            {
                Id = "start.hide-taskbar-badges", Category = "start",
                Title = "Hide notification counters on taskbar icons",
                Summary = "Removes the small red numbers that apps put on their taskbar icons.",
                Why = "Fewer things asking for your attention. You can still see notifications in the notification center.",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "badges", "unread", "counter", "notification dot" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "TaskbarBadges", 0, windowsDefault: 1) },
            });

            // ---------------- Start menu ----------------

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-start (HideRecommendedSection, Windows 11 22H2, Pro and up)
            list.Add(new Tweak
            {
                Id = "start.hide-recommended-section", Category = "start",
                Title = "Hide the Recommended section in Start",
                Summary = "Removes the list of recent files and suggested apps from the bottom of the Start menu.",
                Why = "A cleaner, shorter Start menu with room for more pinned apps. Applies to every user on this PC.",
                OffMeans = "Start shows recent files and recommended apps below your pinned apps.",
                Caution = "On Windows 11 Pro this setting is sometimes ignored and the Recommended list stays. If nothing changes after restarting File Explorer, turn it off again. It needs administrator rights.",
                Risk = Risk.Moderate, TakesEffect = Takes.ExplorerRestart, MinBuild = 22621,
                Keywords = new[] { "recommended", "start menu", "recent files", "hide recommended" },
                Changes = { Reg.Dword(Hive.Machine, ExplorerPolicyKey, "HideRecommendedSection", 1) },
            });

            // Source: Settings > Personalization > Start > "Show recommendations for tips, shortcuts, new apps, and more" (Start_IrisRecommendations).
            list.Add(new Tweak
            {
                Id = "start.no-start-tips", Category = "start",
                Title = "Stop tips and promotions in Start recommendations",
                Summary = "Turns off the \"Show recommendations for tips, shortcuts, new apps, and more\" setting.",
                Why = "Start stops suggesting Microsoft tips and apps. Your own recent files can still appear unless you turn those off too.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22631,
                Keywords = new[] { "recommendations", "promotions", "ads in start", "iris" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "Start_IrisRecommendations", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: Settings > Personalization > Start "Show recently opened items in Start, Jump Lists, and File Explorer" (Start_TrackDocs).
            list.Add(new Tweak
            {
                Id = "start.no-recent-files", Category = "start",
                Title = "Do not list recently opened files in Start and jump lists",
                Summary = "Start, the right-click menus of taskbar apps and File Explorer stop listing the files you opened lately.",
                Why = "Nobody looking over your shoulder sees what you worked on. You lose the shortcut back to recent files.",
                Risk = Risk.Moderate, Presets = new[] { "privacy", "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "recent items", "jump lists", "recent documents", "start_trackdocs" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "Start_TrackDocs", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: Settings > Personalization > Start "Show account-related notifications" (Start_AccountNotifications).
            list.Add(new Tweak
            {
                Id = "start.no-account-notifications", Category = "start",
                Title = "Hide account notifications on the Start menu",
                Summary = "Stops the Start menu from nagging you about your Microsoft account, backup and storage.",
                Why = "Removes the dot and pop-up that ask you to finish account tasks or buy more OneDrive storage.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "account", "microsoft account", "onedrive storage", "start notifications" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "Start_AccountNotifications", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-start (HideRecentlyAddedApps, Pro and up; restart Explorer to apply)
            list.Add(new Tweak
            {
                Id = "start.no-recently-added-apps", Category = "start",
                Title = "Hide the Recently added list in Start",
                Summary = "Start stops showing the apps you installed lately.",
                Why = "A tidier All apps list. You find new apps by name in the alphabetical list or by search. Applies to every user on this PC.",
                Caution = "The Show recently added apps switch in Settings is greyed out while this is on.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart,
                Keywords = new[] { "recently added", "new apps", "start menu list" },
                Changes = { Reg.Dword(Hive.Machine, ExplorerPolicyKey, "HideRecentlyAddedApps", 1) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (Start_Layout: 0 default, 1 more pins, 2 more recommendations)
            list.Add(new Tweak
            {
                Id = "start.more-pins", Category = "start",
                Title = "Show more pinned apps in Start",
                Summary = "Gives the pinned apps area more rows and the recommended area fewer.",
                Why = "More of your favorite apps are visible without scrolling.",
                OffMeans = "Start uses the default balance between pinned apps and recommendations.",
                Risk = Risk.Safe, TakesEffect = Takes.ExplorerRestart, MinBuild = 22621,
                Keywords = new[] { "pins", "start layout", "more pins", "pinned apps" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "Start_Layout", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // ---------------- Search ----------------

            // Source: HKCU policy DisableSearchBoxSuggestions (Explorer.admx) and Search "BingSearchEnabled". https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search (DoNotUseWebResults) is not supported on Pro,
            // so the HKCU Explorer policy DisableSearchBoxSuggestions is used; BingSearchEnabled is kept as a secondary switch.
            list.Add(new Tweak
            {
                Id = "start.no-web-search", Category = "start",
                Title = "Keep web results out of Start search",
                Summary = "Searching from Start looks only at your apps, settings and files, not Bing.",
                Why = "Faster, quieter results, and what you type is not sent to Microsoft's web search. This works on Windows Pro, where the main web-search policy does not.",
                Caution = "This policy also turns off the list of recent searches in File Explorer's search box.",
                Risk = Risk.Safe, Presets = new[] { "privacy", "clean" }, TakesEffect = Takes.ExplorerRestart,
                Keywords = new[] { "bing", "web search", "search suggestions", "internet results", "cortana" },
                Changes =
                {
                    Reg.Dword(Hive.User, UserExplorerPolicyKey, "DisableSearchBoxSuggestions", 1),
                    Reg.Dword(Hive.User, SearchKey, "BingSearchEnabled", 0),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search (AllowSearchHighlights); per-user switch IsDynamicSearchBoxEnabled
            list.Add(new Tweak
            {
                Id = "start.no-search-highlights", Category = "start",
                Title = "Turn off search highlights",
                Summary = "Removes the daily pictures and \"today in history\" items from the search box and search panel.",
                Why = "Search stays a plain search. The highlights are fetched from the internet and change every day.",
                Risk = Risk.Safe, Presets = new[] { "privacy", "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "highlights", "doodle", "today in history", "search box picture" },
                Changes = { Reg.Dword(Hive.User, SearchSettingsKey, "IsDynamicSearchBoxEnabled", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: Settings > Privacy & security > Search permissions: "Microsoft account" and "Work or school account" cloud content search (IsMSACloudSearchEnabled, IsAADCloudSearchEnabled).
            list.Add(new Tweak
            {
                Id = "start.no-cloud-search", Category = "start",
                Title = "Do not search cloud accounts from Windows Search",
                Summary = "Windows Search stops looking in your Microsoft account and work or school account for mail, files and calendar items.",
                Why = "Keeps search on this PC and stops it from asking online services about what you type.",
                Caution = "Results from Outlook, OneDrive online files and similar services no longer show up in search.",
                Risk = Risk.Moderate, Presets = new[] { "privacy" }, TakesEffect = Takes.Immediately, MinBuild = 22000,
                Keywords = new[] { "cloud content search", "microsoft account", "work account", "onedrive search" },
                Changes =
                {
                    Reg.Dword(Hive.User, SearchSettingsKey, "IsMSACloudSearchEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                    Reg.Dword(Hive.User, SearchSettingsKey, "IsAADCloudSearchEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                },
            });

            // ---------------- Tips, suggestions and nags ----------------

            // Source: ContentDeliveryManager values used by Windows Spotlight tips on the lock screen. https://learn.microsoft.com/en-us/windows/configuration/manage-tips-and-suggestions
            list.Add(new Tweak
            {
                Id = "start.no-lockscreen-tips", Category = "start",
                Title = "Remove tips and fun facts from the lock screen",
                Summary = "The lock screen shows only the picture, without the \"Like what you see?\" and tips overlays.",
                Why = "A calmer lock screen. Nothing else changes about your lock screen picture.",
                Risk = Risk.Safe, Recommended = true, Presets = new[] { "privacy", "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "lock screen", "spotlight", "fun facts", "tips", "ads on lock screen" },
                Changes =
                {
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "RotatingLockScreenOverlayEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SubscribedContent-338387Enabled", 0, windowsDefault: 1).WhenMissing(1u),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/configuration/manage-tips-and-suggestions (Start suggestions)
            list.Add(new Tweak
            {
                Id = "start.no-start-suggestions", Category = "start",
                Title = "Turn off app suggestions in Start",
                Summary = "Windows stops suggesting apps and offers in the Start menu.",
                Why = "Removes sponsored suggestions that look like part of Windows.",
                Risk = Risk.Safe, Recommended = true, Presets = new[] { "privacy", "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "suggestions", "sponsored", "ads in start", "app suggestions", "advertising" },
                Changes =
                {
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SystemPaneSuggestionsEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SubscribedContent-338388Enabled", 0, windowsDefault: 1).WhenMissing(1u),
                },
            });

            // Source: https://learn.microsoft.com/en-us/windows/configuration/manage-tips-and-suggestions ("Get tips and suggestions when using Windows")
            list.Add(new Tweak
            {
                Id = "start.no-tips-notifications", Category = "start",
                Title = "Turn off \"tips and tricks\" notifications",
                Summary = "Windows stops popping up tips about how to use Windows.",
                Why = "Fewer pop-ups. Windows Help and the Tips app still work when you open them.",
                Risk = Risk.Safe, Recommended = true, Presets = new[] { "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "tips", "get started", "soft landing", "notifications", "suggestions" },
                Changes =
                {
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SubscribedContent-338389Enabled", 0, windowsDefault: 1).WhenMissing(1u),
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SoftLandingEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                },
            });

            // Source: ContentDeliveryManager silent-install values. https://learn.microsoft.com/en-us/windows/configuration/manage-tips-and-suggestions
            list.Add(new Tweak
            {
                Id = "start.no-silent-app-installs", Category = "start",
                Title = "Stop Windows from quietly installing suggested apps",
                Summary = "Apps that Microsoft or the PC maker promote are no longer added to your account in the background.",
                Why = "Keeps promotional apps and games off your Start menu. Apps already installed stay until you remove them.",
                Risk = Risk.Safe, Recommended = true, Presets = new[] { "privacy", "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "bloatware", "preinstalled", "candy crush", "silent install", "promoted apps", "oem" },
                Changes =
                {
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "SilentInstalledAppsEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "OemPreInstalledAppsEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                    Reg.Dword(Hive.User, Reg.ContentDelivery, "PreInstalledAppsEnabled", 0, windowsDefault: 1).WhenMissing(1u),
                },
            });

            // Source: ContentDeliveryManager SubscribedContent-310093 ("Suggest ways to get the most out of Windows" after updates). https://learn.microsoft.com/en-us/windows/configuration/manage-tips-and-suggestions
            list.Add(new Tweak
            {
                Id = "start.no-welcome-experience", Category = "start",
                Title = "Skip the \"welcome experience\" after updates",
                Summary = "Windows no longer opens a full-screen page about what is new after a big update.",
                Why = "Updates finish and you go straight back to work.",
                Risk = Risk.Safe, Recommended = true, Presets = new[] { "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "welcome", "what's new", "after update", "full screen", "310093" },
                Changes = { Reg.Dword(Hive.User, Reg.ContentDelivery, "SubscribedContent-310093Enabled", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: Settings > System > Notifications > "Suggest ways to get the most out of Windows and finish setting up this device" (ScoobeSystemSettingEnabled).
            list.Add(new Tweak
            {
                Id = "start.no-finish-setup", Category = "start",
                Title = "Stop the \"Finish setting up your device\" reminders",
                Summary = "Windows no longer shows full-screen and notification reminders to finish setup.",
                Why = "Removes the repeated nag to add a phone, sign in with a Microsoft account or turn on more Microsoft services.",
                Risk = Risk.Safe, Recommended = true, Presets = new[] { "clean" }, TakesEffect = Takes.Immediately, MinBuild = 22000,
                Keywords = new[] { "finish setup", "scoobe", "get the most out of windows", "nag", "let's finish setting up" },
                Changes = { Reg.Dword(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, windowsDefault: 1).WhenMissing(1u) },
            });
        }
    }
}
