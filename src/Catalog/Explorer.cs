using System.Collections.Generic;

namespace TweakBench.Core
{
    // File Explorer tweaks. Values were checked against Microsoft Learn or widely used references and against this PC (read only).
    public static partial class Catalog
    {
        const string GalleryClsid = @"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}";
        const string OneDriveClsid = @"Software\Classes\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}";
        const string ClassicMenuClsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        const string AllFoldersShell = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell";

        static partial void AddExplorer(List<Tweak> list)
        {
            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (HideFileExt: 0 shows extensions)
            list.Add(new Tweak
            {
                Id = "explorer.show-extensions", Category = "explorer",
                Title = "Show file extensions",
                Summary = "File names show their type at the end, like report.pdf or setup.exe.",
                Why = "You can tell a document from a program at a glance, which also makes disguised malware (invoice.pdf.exe) easy to spot.",
                Recommended = true, Presets = new[] { "basics" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "file extension", "file type", "hidefileext", "suffix", ".exe", ".txt" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "HideFileExt", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (Hidden: 1 show, 2 do not show)
            list.Add(new Tweak
            {
                Id = "explorer.show-hidden-files", Category = "explorer",
                Title = "Show hidden files and folders",
                Summary = "Files and folders marked as hidden, like AppData, appear in File Explorer (slightly faded).",
                Why = "Needed to reach program settings and some app data. The files are shown faded so you can tell them apart.",
                Risk = Risk.Safe, Presets = new[] { "basics" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "hidden", "appdata", "show hidden", "dotfiles" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "Hidden", 1, windowsDefault: 2).WhenMissing(2u) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (ShowSuperHidden)
            list.Add(new Tweak
            {
                Id = "explorer.show-protected-files", Category = "explorer",
                Title = "Show protected operating system files",
                Summary = "Also shows the system files Windows hides by default, such as pagefile.sys and desktop.ini.",
                Why = "Useful when you troubleshoot or clean up a drive. Most people never need it.",
                Caution = "Deleting or renaming a protected file can stop Windows from starting. Look, but do not delete.",
                Risk = Risk.Advanced, TakesEffect = Takes.Immediately,
                Keywords = new[] { "super hidden", "system files", "protected", "pagefile", "desktop.ini" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "ShowSuperHidden", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (LaunchTo: 1 = This PC)
            list.Add(new Tweak
            {
                Id = "explorer.open-this-pc", Category = "explorer",
                Title = "Open File Explorer to This PC",
                Summary = "File Explorer starts at your drives and folders instead of Home.",
                Why = "Your drives are one click away. Home, with its recent and pinned items, is the default.",
                OffMeans = "File Explorer opens to Home.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "launch to", "start folder", "home", "quick access", "this pc", "computer" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "LaunchTo", 1) },
            });

            // Source: Folder Options > Privacy: "Show recently used files" and "Show frequently used folders" (Explorer ShowRecent, ShowFrequent).
            list.Add(new Tweak
            {
                Id = "explorer.no-recent-frequent", Category = "explorer",
                Title = "Hide recent files and frequent folders on Home",
                Summary = "The Home page and Quick access stop listing files you opened lately and folders you use often.",
                Why = "Keeps what you worked on out of sight. Your pinned folders stay.",
                Risk = Risk.Safe, Presets = new[] { "privacy", "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "recent", "frequent", "quick access", "home", "privacy", "showrecent", "showfrequent" },
                Changes =
                {
                    Reg.Dword(Hive.User, Reg.Explorer, "ShowRecent", 0, windowsDefault: 1).WhenMissing(1u),
                    Reg.Dword(Hive.User, Reg.Explorer, "ShowFrequent", 0, windowsDefault: 1).WhenMissing(1u),
                },
            });

            // Source: https://winaero.com/remove-gallery-from-file-explorer (System.IsPinnedToNameSpaceTree = 0 under the Gallery CLSID; Windows 11 23H2 and later)
            list.Add(new Tweak
            {
                Id = "explorer.hide-gallery", Category = "explorer",
                Title = "Hide Gallery from the navigation pane",
                Summary = "Removes the Gallery entry from the left side of File Explorer.",
                Why = "One less item in the list if you do not use the photo Gallery. Your pictures are not touched.",
                Caution = "A large Windows update can bring Gallery back. Apply the tweak again if that happens.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.ExplorerRestart, MinBuild = 22621,
                Keywords = new[] { "gallery", "navigation pane", "sidebar", "photos" },
                Changes = { Reg.Dword(Hive.User, GalleryClsid, "System.IsPinnedToNameSpaceTree", 0) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (System.IsPinnedToNameSpaceTree under the OneDrive CLSID)
            list.Add(new Tweak
            {
                Id = "explorer.hide-onedrive-nav", Category = "explorer",
                Title = "Hide personal OneDrive from the navigation pane",
                Summary = "Removes the personal OneDrive entry (a work or school OneDrive entry stays).",
                Why = "Declutters the list if you reach OneDrive another way. OneDrive keeps syncing and the folder stays on disk.",
                Caution = "You will need to open your OneDrive folder from your user folder or the notification area icon.",
                Risk = Risk.Moderate, TakesEffect = Takes.ExplorerRestart,
                NotApplicable = sys => sys.KeyExists(Hive.User, OneDriveClsid) ? null : "A personal OneDrive entry is not present for this user.",
                Keywords = new[] { "onedrive", "cloud", "navigation pane", "sidebar" },
                Changes = { Reg.Dword(Hive.User, OneDriveClsid, "System.IsPinnedToNameSpaceTree", 0, windowsDefault: 1) },
            });

            // Source: Folder Options > View > "Show sync provider notifications" (ShowSyncProviderNotifications).
            list.Add(new Tweak
            {
                Id = "explorer.no-sync-ads", Category = "explorer",
                Title = "Hide OneDrive and Microsoft 365 ads in File Explorer",
                Summary = "Stops the banners that offer storage upgrades and Microsoft 365 inside File Explorer.",
                Why = "File Explorer is for files, not for advertising. Nothing useful is lost.",
                Risk = Risk.Safe, Recommended = true, Presets = new[] { "basics", "clean" }, TakesEffect = Takes.Immediately,
                Keywords = new[] { "ads", "sync provider", "banner", "onedrive", "microsoft 365", "promotion", "notifications" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "ShowSyncProviderNotifications", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: Folder Options > View > "Decrease space between items (compact view)" (UseCompactMode).
            list.Add(new Tweak
            {
                Id = "explorer.compact-view", Category = "explorer",
                Title = "Use compact view in File Explorer",
                Summary = "Reduces the space between rows so more files fit on screen.",
                Why = "Handy on small screens or with long lists. Rows are a bit harder to hit with a finger on a touch screen.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately, MinBuild = 22621,
                Keywords = new[] { "compact", "spacing", "dense", "usecompactmode", "decrease space" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "UseCompactMode", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch ("link" binary value; 00 00 00 00 removes the suffix)
            list.Add(new Tweak
            {
                Id = "explorer.no-shortcut-suffix", Category = "explorer",
                Title = "Do not add \"- Shortcut\" to new shortcut names",
                Summary = "New shortcuts keep the name of the file, not \"Report - Shortcut\".",
                Why = "Shorter, cleaner names. Existing shortcuts are not renamed.",
                OffMeans = "New shortcuts get \"- Shortcut\" added to their names.",
                Risk = Risk.Safe, Presets = new[] { "clean" }, TakesEffect = Takes.SignOut,
                Keywords = new[] { "shortcut", "link", "suffix", "rename shortcut" },
                Changes = { Reg.Binary(Hive.User, Reg.Explorer, "link", new byte[] { 0, 0, 0, 0 }) },
            });

            // Source: https://winaero.com/how-to-change-file-explorer-options-in-the-registry and the matching Settings or Folder Options switch (empty default value under the {86ca1aa0-...} InprocServer32 key)
            list.Add(new Tweak
            {
                Id = "explorer.classic-context-menu", Category = "explorer",
                Title = "Use the classic right-click menu",
                Summary = "Right-clicking shows the full Windows 10 style menu at once, without \"Show more options\".",
                Why = "Every command is in one place. The short Windows 11 menu hides many of them behind an extra click.",
                OffMeans = "The Windows 11 short menu shows, with Show more options at the bottom.",
                Risk = Risk.Moderate, TakesEffect = Takes.ExplorerRestart, MinBuild = 22000,
                Keywords = new[] { "context menu", "right click", "show more options", "windows 10 menu", "shift f10" },
                Changes = { Reg.KeyDefault(Hive.User, ClassicMenuClsid + @"\InprocServer32", "", ClassicMenuClsid) },
            });

            // Source: https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation (LongPathsEnabled)
            list.Add(new Tweak
            {
                Id = "explorer.long-paths", Category = "explorer",
                Title = "Allow file paths longer than 260 characters",
                Summary = "Programs that support it can open files in deeply nested folders.",
                Why = "Developers and people with deep folder trees stop seeing \"path too long\" errors. Older programs that do not support long paths are unaffected.",
                Risk = Risk.Safe, TakesEffect = Takes.PcRestart, MinBuild = 14393,
                Keywords = new[] { "long path", "260", "max_path", "path too long", "longpathsenabled", "developer" },
                Changes = { Reg.Dword(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: Folder Options > View > "Use check boxes to select items" (AutoCheckSelect).
            list.Add(new Tweak
            {
                Id = "explorer.item-checkboxes", Category = "explorer",
                Title = "Show check boxes to select files",
                Summary = "A check box appears next to each file, so you can select several by clicking them.",
                Why = "Selecting many files without holding Ctrl is easy, especially on touch screens.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "checkbox", "select", "multiple files", "autocheckselect", "touch" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "AutoCheckSelect", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: https://geoffchappell.com/notes/windows/shell/drivenames.htm (ShowDriveLettersFirst: 4 = letters before names)
            list.Add(new Tweak
            {
                Id = "explorer.drive-letters-first", Category = "explorer",
                Title = "Show drive letters before drive names",
                Summary = "Drives read \"C: Windows\" instead of \"Windows (C:)\".",
                Why = "Drives line up by letter, which is easier to scan when you have many drives.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "drive letter", "this pc", "drive name", "label" },
                Changes = { Reg.Dword(Hive.User, Reg.Explorer, "ShowDriveLettersFirst", 4, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: Folder Options > View > "Hide empty drives" (HideDrivesWithNoMedia).
            list.Add(new Tweak
            {
                Id = "explorer.show-empty-drives", Category = "explorer",
                Title = "Show empty drives, like card readers",
                Summary = "Drives with no disk or card in them still appear in This PC.",
                Why = "Card readers and optical drives are always visible, so you know they are there. They stay empty until you insert media.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "empty drives", "card reader", "cd drive", "removable", "hidedriveswithnomedia" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "HideDrivesWithNoMedia", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: Folder Options > View > "Hide folder merge conflicts" (HideMergeConflicts).
            list.Add(new Tweak
            {
                Id = "explorer.show-merge-conflicts", Category = "explorer",
                Title = "Warn when merging folders with the same name",
                Summary = "File Explorer asks before it combines two folders that have the same name.",
                Why = "Stops a copy or move from silently mixing a folder into another. You get a prompt first.",
                OffMeans = "Folders with the same name are merged without a separate warning.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "merge", "folder conflict", "same name", "hidemergeconflicts" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "HideMergeConflicts", 0, windowsDefault: 1).WhenMissing(1u) },
            });

            // Source: Folder Options > General > "Expand to open folder" (NavPaneExpandToCurrentFolder).
            list.Add(new Tweak
            {
                Id = "explorer.expand-nav-to-folder", Category = "explorer",
                Title = "Expand the navigation pane to the open folder",
                Summary = "The left side of File Explorer opens up to show where the current folder sits.",
                Why = "You always see the folders above and beside the one you are in.",
                Risk = Risk.Safe, TakesEffect = Takes.Immediately,
                Keywords = new[] { "navigation pane", "expand", "tree", "sidebar", "current folder" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "NavPaneExpandToCurrentFolder", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: Folder Options > View > "Launch folder windows in a separate process" (SeparateProcess).
            list.Add(new Tweak
            {
                Id = "explorer.separate-process", Category = "explorer",
                Title = "Try opening File Explorer windows in their own process",
                Summary = "May keep a crash in one File Explorer window from affecting the others.",
                Why = "Older versions of File Explorer used this to keep a crash in one window from affecting the taskbar. Newer tabbed File Explorer may ignore it. Uses a little more memory.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "separate process", "explorer crash", "stability", "memory" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "SeparateProcess", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: Folder Options > View > "Always show icons, never thumbnails" (IconsOnly).
            list.Add(new Tweak
            {
                Id = "explorer.icons-not-thumbnails", Category = "explorer",
                Title = "Show icons instead of picture previews",
                Summary = "Pictures and videos show a generic icon, not a small preview of their content.",
                Why = "Folders with many large pictures open faster and use less disk and memory. You lose the previews.",
                Risk = Risk.Moderate, TakesEffect = Takes.Immediately,
                Keywords = new[] { "thumbnails", "previews", "icons only", "slow folder", "iconsonly" },
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "IconsOnly", 1, windowsDefault: 0).WhenMissing(0u) },
            });

            // Source: https://winaero.com/disable-folder-type-discovery-windows-10 (FolderType = NotSpecified under Shell\Bags\AllFolders\Shell)
            list.Add(new Tweak
            {
                Id = "explorer.no-folder-discovery", Category = "explorer",
                Title = "Stop File Explorer from guessing each folder's type",
                Summary = "Every folder uses the same general view, instead of Windows picking Pictures, Music or Documents views by looking inside.",
                Why = "Large folders open faster because Windows no longer scans their contents to choose a layout.",
                Caution = "Folders you have already opened keep the view they were saved with. Columns may differ from what you are used to.",
                Risk = Risk.Advanced, TakesEffect = Takes.ExplorerRestart,
                Keywords = new[] { "folder type", "automatic folder type discovery", "slow folder", "folder template", "foldertype" },
                Changes = { Reg.String(Hive.User, AllFoldersShell, "FolderType", "NotSpecified") },
            });
        }
    }
}
