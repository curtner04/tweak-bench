# Tweak Bench — design and build guide

Tweak Bench (app code **TB**) is a Windows power-user tweak app, in the spirit of Chris Titus Tech's WinUtil but
**easy to follow**. Source: `C:\dev\Windows`. Executable: `Tweak Bench.exe`. Data: `%LOCALAPPDATA%\Tweak Bench\data`.
Install folder (installer queue): `%LOCALAPPDATA%\Programs\Tweak Bench`. .NET Framework 4.8 WPF, compiled by Roslyn
(`C:\dev\Toolchains\VS2022\MSBuild\Current\Bin\Roslyn\csc.exe`, `-langversion:latest`) through `Build.ps1`. No NuGet, no extra DLLs:
one self-contained exe plus the shared `Configuration Transfer.exe` helper.

This file is a development guide; it is not shipped.

## Why WinUtil is hard to follow, and what Tweak Bench does instead

| WinUtil problem | Tweak Bench answer |
| --- | --- |
| A wall of terse checkboxes | One card per tweak: plain-English title, one-sentence summary, a "why" line, risk badge |
| No idea what is already set | Live status on every card ("On", "Off", "Partly on", "Not available here") read from the PC |
| "Run Tweaks" vs "Undo Selected" is confusing | Flip switches to say what you want, then **Review & apply** shows exactly what will change |
| Undo restores generic defaults | Every change saves the exact previous values; **History** undoes any single change or whole batch |
| Unclear what needs admin / restart | Shield marker for admin-only tweaks; "takes effect after…" note; Tweak Bench never restarts the PC |
| Technical details hidden or missing | "What it changes" expander lists every registry value / service / task with its current value |

## Hard rules (for everyone building this)

1. **Never change the real PC while developing or testing.** Read-only queries only (`reg query`, `Get-ItemProperty`,
   `Get-Service`, `schtasks /query`, `powercfg /q`, `Get-AppxPackage`, `winget list/show/search`, `Get-DnsClientServerAddress`).
   Do not run the app's apply/undo paths against `RealSystem`, do not run SFC/DISM repairs, winget installs, AppX removals,
   `sc config`, `reg add`, `netsh` changes or restore points. Tests use `MemorySystem` and a temp data folder only.
2. Never build or run an installer, and never edit `C:\dev\App Maintenance\Installers`. The 4:00 AM queue builds installers.
3. Never stop or restart running apps, Explorer or the PC.
4. Compile with `powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\Windows\Build.ps1 -OutputDirectory <your private scratch folder>`
   (add `-Test` for tests). Never build into `C:\dev\Windows` itself while other agents work. Errors in files you do not own are
   other agents' work in progress; your files must compile cleanly.
5. Only edit the files you own (see the ownership table). If you need a small API addition in a file you do not own,
   put it in your own file (extension methods, partial classes) and mention it in your final report.
6. C# style: `-langversion:latest` is fine, but keep it plain: no `dynamic` outside `RealSystem`, no reflection tricks. Messages to the
   user are short, plain English, no jargon in titles. Never use "simply"/"just".

## Architecture

```
src\Core      engine and contracts (no WPF): Model, Changes, ISystem, Catalog, Engine, History, Storage, ProcessRunner,
              RealSystem, MemorySystem, RestorePoint, SystemInfo, Audit
src\Catalog   the tweaks, one file per category (partial methods of Catalog)
src\Tools     non-UI logic for the tool pages: built-in apps, winget catalog, Windows features, repair tasks, DNS, legacy panels
src\App       WPF shell and pages (excluded from the test build)
tests         headless tests (TestRunner.cs finds every static class named *Tests with `public static void Run(T t)`)
```

Key contracts (already written — read them before coding): `src\Core\Model.cs` (Tweak, Change, Snapshot, enums),
`src\Core\Changes.cs` (RegValue, ServiceStart, TaskState, PowerValue, CustomChange and the `Reg`, `Svc`, `Sched`, `Power`, `Custom`
factories), `src\Core\ISystem.cs`, `src\Core\Engine.cs` (Status, Apply with rollback, Undo, profiles), `src\Core\History.cs`,
`src\Core\Storage.cs` (Paths, Json, AtomicFile, AppSettings), `src\Core\ProcessRunner.cs`, `src\App\UiContext.cs` (UiContext, PageBase),
`src\App\Ui.cs` (UI helpers, OutputConsole), `tests\TestRunner.cs`.

### Platform API (written by the Platform agent; UI and Tools code against exactly these signatures)

All in namespace `TweakBench.Core`, files under `src\Core`:

```csharp
public sealed class RealSystem : ISystem { public RealSystem(); }                 // talks to Windows (64-bit registry view)
public sealed class MemorySystem : ISystem                                         // in-memory; never touches the PC
{
    public MemorySystem(int build = 26300, string edition = "Professional", bool elevated = true);
    public static MemorySystem Demo();                // seeded with a realistic mix of on/off states for screenshots
    public List<string> Commands { get; }             // every Run() call as "file args"
    public Func<string, string, ProcessResult> RunHandler;   // optional fake for Run(file, args)
    public bool FailWrites;                           // tests: every write throws UnauthorizedAccessException
    public void AddService(string name, StartMode start, bool running, string displayName = null);
    public void AddTask(string path, bool enabled);
}
public sealed class RestorePointResult { public bool Ok; public string Message; }
public static class RestorePoint { public static RestorePointResult Create(ISystem sys, string description); }   // admin; runs PowerShell via sys.Run
public sealed class SystemInfo
{
    public string ProductName, DisplayVersion, Edition, ComputerName, UserName;   // ProductName says "Windows 11" on build >= 22000
    public int Build, Ubr; public bool IsElevated, ElevatedAsOtherUser;
    public TimeSpan Uptime; public DateTime LastBoot;
    public bool RestartPending; public List<string> RestartPendingReasons;
    public static SystemInfo Read(ISystem sys);
}
public static class Audit { public static void Write(ISystem sys, TextWriter w); }   // read-only status of every tweak
public static class ExplorerShell { public static bool Restart(out string message); }  // restarts File Explorer as the signed-in user (not elevated)
public static class AutoProfile                                                     // keeps profiles\My setup (automatic).json current
{
    public const string Name = "My setup (automatic)";
    public static void Update(Engine engine, ProfileStore profiles);   // every tweak this PC's history touched, with its current on/off state; never throws
}
```

### File ownership

| Owner | Files |
| --- | --- |
| Lead (already written) | `src\Core\{Model,Changes,ISystem,Catalog,Engine,History,Storage,ProcessRunner}.cs`, `src\App\{UiContext,Ui,AppIdentity,Release}.cs`, `tests\TestRunner.cs`, `Build.ps1`, `app.manifest`, `DESIGN.md` |
| Catalog: update | `src\Catalog\Update.cs` |
| Catalog: privacy | `src\Catalog\Privacy.cs` |
| Catalog: start & explorer | `src\Catalog\StartTaskbar.cs`, `src\Catalog\Explorer.cs` |
| Catalog: performance & system | `src\Catalog\Performance.cs`, `src\Catalog\SystemDesktop.cs`, `src\Catalog\Network.cs`, `src\Catalog\Services.cs` |
| Platform | `src\Core\{RealSystem,MemorySystem,RestorePoint,SystemInfo,Audit,ExplorerShell,AutoProfile}.cs`, `tests\EngineTests.cs`, `tests\CatalogTests.cs`, `tests\StorageTests.cs`, `tests\Test.ps1` |
| UI | `MainWindow.xaml`, `src\App\{Program,Theme,MainWindow,TweakCard,CategoryPage,SearchPage,StartPage,HistoryPage,ReviewDialog,SettingsDialog,ChangelogWindow,Render}.cs`, `src\App\Pages\` except the tool pages |
| Tools | `src\Tools\*.cs`, `src\App\Pages\{AppsPage,InstallPage,FeaturesPage,RepairPage,DnsPanel}.cs`, `tests\ToolsTests.cs` |
| Enrollment | `C:\dev\App Maintenance\…`, `C:\dev\App Management\…`, `assets\TB.ico`, `assets\TB.png`, `tools\Make-Icon.ps1` |

## Catalog authoring (src\Catalog\*.cs)

Each file implements one partial method, for example:

```csharp
namespace TweakBench.Core
{
    public static partial class Catalog
    {
        static partial void AddExplorer(List<Tweak> list)
        {
            // Source: https://learn.microsoft.com/... (or a well-known, verifiable reference)
            list.Add(new Tweak
            {
                Id = "explorer.show-extensions", Category = "explorer",
                Title = "Show file extensions",
                Summary = "File names show their type at the end, like report.pdf or setup.exe.",
                Why = "You can tell a document from a program at a glance, which also makes disguised malware (invoice.pdf.exe) easy to spot.",
                Recommended = true, Presets = new[] { "basics" }, TakesEffect = Takes.Immediately,
                Changes = { Reg.Dword(Hive.User, Reg.ExplorerAdvanced, "HideFileExt", 0, windowsDefault: 1).WhenMissing(1u) },
            });
        }
    }
}
```

Rules:

- **Accuracy first.** Every registry path, value name, type and value must be correct for current Windows 11 (this PC is
  Windows 11 Pro 26H2, build 26300). Put a `// Source:` comment with a Microsoft Learn / ADMX / well-known reference URL above each
  tweak. Verify paths against this PC read-only (`reg query`). When unsure, leave the tweak out rather than guess.
- No placebo tweaks (no "speed up internet" myths, no `NetworkThrottlingIndex`, no "disable Nagle" for everyone). No tweaks that
  weaken security: never disable Defender, SmartScreen, UAC, the firewall, Windows Update entirely, VBS/Memory Integrity, or
  Spectre mitigations. Never delete user files.
- Ids are permanent: `"<category>.<kebab-name>"`. Category ids: `update`, `privacy`, `start`, `explorer`, `performance`, `system`,
  `network`, `services`.
- Title says what "on" means, positively phrased ("Hide the Widgets button", "Show file extensions"). Summary: one sentence of what
  happens. Why: who wants it and the trade-off. `Caution` only for real downsides. `OffMeans` when the default is not obvious.
- `Risk`: Safe (no downside for most people), Moderate (some people will miss it), Advanced (can break a feature/app; hidden unless
  "Show advanced tweaks" is on). An Advanced tweak cannot be Recommended.
- `Recommended = true` only for changes that are right for nearly everyone (implies the "basics" preset). Other presets:
  `privacy`, `gaming`, `clean`.
- `TakesEffect`: `Immediately`, `ExplorerRestart` (taskbar/Start/Explorer view changes that need Explorer restarted), `SignOut`,
  `PcRestart`.
- `windowsDefault`: give the explicit Windows default for settings that exist on a clean install (HKCU Explorer values);
  leave it null for policy values (`SOFTWARE\Policies\...`), which restore by deletion. Use `.WhenMissing(x)` when an absent
  value behaves like `x`.
- HKCU (`Hive.User`) changes do not need administrator rights; HKLM, services, tasks, power and commands do.
- Policies that only work on Enterprise/Education: set `NotApplicable = sys => sys.Edition == "Professional" ... ? null : "..."`
  or leave them out. Use `MinBuild = 22000` for Windows 11-only tweaks.
- Services: `Svc.Start("DiagTrack", StartMode.Disabled, StartMode.Automatic)`. Tasks: `Sched.Disable(@"\Microsoft\Windows\...\Name")`.
  Prefer Manual over Disabled for anything another feature might start.
- `CustomChange` for commands (powercfg, netsh, bcdedit, dism): use `Custom.Make(...)` and `Custom.Must(sys, file, args)` so all
  commands go through `ISystem.Run`. `Check` must be read-only and fast (it runs on every refresh).
- Mutually exclusive alternatives share an `ExclusiveGroup`.

## UI (src\App)

Look: a calm, modern Windows 11 style. Reuse the proven code-built WPF approach from `C:\dev\Anvil Aide` (`MainWindow.xaml` style
block, `src\App\Theme.cs`, `src\App\Ui.cs`): theme brushes as `DynamicResource`, light/dark following Windows. Accent is Windows blue.

Palette (Dark / Light): Bg #111318/#F3F5F8, Card #1A1D24/#FFFFFF, Card2 #22262F/#F6F8FB, Input #15181E/#FFFFFF, Border #2E333D/#D9DEE6,
Text #E8EAED/#1B1F27, Muted #A0A7B4/#5B6575, Accent #4CA8FF/#0A64C8, AccentFg #0B1220/#FFFFFF, AccentText #6CB8FF/#0A5BB5,
BtnBg #2A2F3A/#E8ECF2, Good #6CCB8A/#1E7D3C, GoodBg #1B3326/#E2F3E7, Warn #F0B44C/#8A5A00, WarnBg #3A2E14/#FFF2D6,
Bad #FF8A80/#B3261E, BadBg #45201E/#FBE4E1, Info #7FB7FF/#0A5BB5, InfoBg #1B2A40/#E4EEFB, Selected #243246/#E3ECF8.

Window (default 1280×840, min 980×640):

- **Sidebar** (≈250 px): app icon + "Tweak Bench" + version; a search box ("Search all tweaks"); nav: **Start here**; "TWEAKS":
  the 8 categories (with a small count of pending changes); "TOOLS": Remove built-in apps, Install apps, Windows features, Repair &
  tools; **History**. Bottom: administrator status chip, a visible **Restart** button, and a "⋯" menu with Changelog, Export /
  import configuration, Settings.
- **Admin banner** when not elevated: "Running without administrator rights. You can change your own settings; system-wide ones
  are read-only." [Run as administrator].
- **Page header**: title, one-line subtitle, and on category pages: "Select recommended" and a "Show advanced tweaks" switch.
- **Tweak card** (`TweakCard`): title (semibold) + badges (Recommended, Moderate/Advanced, shield "Admin" when not elevated);
  summary; "Why: …" muted; caution note (Warn); a status pill (On = Good, Off = Muted, Partly on = Warn, Not available = Muted with
  reason, Unknown); a **switch** showing the *desired* state. Flipping the switch stages a change (card shows "Will turn on/off"
  and a highlight); flipping back un-stages it. Disabled switch + tooltip when unavailable or when it needs admin and the app is
  not elevated. "What it changes ▸" expander lists each `Change.Describe()` with "now: " + `DescribeCurrent()`. Effect note:
  "Takes effect after restarting File Explorer / signing out / restarting the PC". "Turned on by Tweak Bench — off restores your
  previous values" when `HasUndo`.
- **Pending bar** (bottom of the content, only when something is staged): "N changes ready" [Discard] [Review & apply].
- **Review dialog**: every staged item ("Turn on: …" / "Turn off: …") with its risk and effect, a checkbox to drop items,
  "Create a restore point first" (default from settings; needs admin), [Apply N changes] / [Cancel]. Applying runs on a background
  thread with progress; then a results list (✓ done / – skipped with reason / ✗ failed with message) and follow-ups:
  [Restart File Explorer now] when needed, "Restart your PC when convenient — Tweak Bench never restarts it for you" when needed.
- **Start here**: welcome + 3-step "how it works"; a highlighted card for **"Stop automatic restarts for updates"**
  (tweak `update.no-auto-restart`) with its status and a one-click "Review & apply"; the 4 preset cards (title, blurb, "N changes to
  make", [Review]); a "This PC" summary (Windows edition/version/build, uptime, administrator status, restart pending); quick
  actions (Create restore point, Restart File Explorer, Open History).
- **Search results** page across all categories.
- **History**: batches newest first (time, label, items with ✓/✗, "Undo" per entry when `Engine.CannotUndo` is null, "Undo this
  batch"), entry details (each snapshot's What / Was). **Profiles**: "Save my current setup as a profile…", list with
  [Review & apply] [Export…] [Delete], [Import profile…].
- **Settings dialog**: theme (Auto/Light/Dark), create restore point before applying, ask for administrator rights at start,
  show advanced tweaks.
- Status line at the very bottom.

Behaviour:

- Startup (`Program.cs`): `--version` / `--changelog` print and exit. `--audit [file]` prints `Audit.Write` (read-only) and exits.
  `--render <folder> [--theme light|dark]` = demo mode (MemorySystem.Demo(), temp data folder) that renders every page to PNG and
  exits; it never touches the real PC. Otherwise: single-instance mutex `Local\TweakBench.App` (an `UnauthorizedAccessException`
  opening it means an elevated copy owns it: treat as "already running"); bring an existing window forward and exit;
  refuse while Configuration Transfer is open (`SingleInstance.TransferWindowOpen()`). If not elevated and `Settings.AskForAdmin`
  and no `--no-elevate`/`--restart-after`: release the mutex, start itself with `Verb = "runas"` and `--elevated-after <pid> <ticks>`,
  and exit; if the user declines the UAC prompt (Win32Exception 1223), continue without admin. `--elevated-after` /
  `--restart-after` call `AppShared.AppRestart.WaitForParent` before taking the mutex.
- **Restart** button: refuses while `UiContext.IsBusy` (says what is running); otherwise every page's `SavePending()`, settings
  saved, `AppRestart.Prepare()` (keeps the same elevation), close. Never runs an installer.
- **Run as administrator**: same save, then `runas` with `--elevated-after`; on decline stay open.
- **Export / import configuration**: confirm that Tweak Bench will close, save, start
  `ConfigurationTransfer.Start(ConfigurationTransfer.Find(appFolder, true), appFolder, Paths.Data, true)`, close.
- **Changelog** window shows `AppShared.ReleaseNotes.Read()` with the version in the title.
- Staged selections (`Settings.PendingOn/PendingOff/PendingInstall/PendingRemove`) are restored after Restart / elevation.
- After every batch and every undo call `AutoProfile.Update(ctx.Engine, ctx.Profiles)`, so a restored configuration on a new PC
  has "My setup (automatic)" ready to review & apply (Windows settings themselves are not copied by Export / import).
- Status reads can be slow (services, tasks, powercfg): compute statuses on a background thread and update cards when ready.

## Tools (src\Tools + src\App\Pages)

- **Remove built-in apps** (`AppsPage`): curated list of removable inbox/Store apps (friendly name, what it is, "safe to remove"
  note), installed status from `Get-AppxPackage` (current user), select → confirm → `Remove-AppxPackage` for the current user;
  optional "also stop it being installed for new user accounts" (`Remove-AppxProvisionedPackage -Online`, admin). Never offer
  frameworks, the Store, App Installer (winget), Windows Security, Settings, Edge, Shell/system packages. Reinstall hint: Microsoft Store.
- **Install apps** (`InstallPage`): curated winget catalog (browsers, utilities, development, media, gaming, communication, office)
  with verified winget ids; installed status from `winget export` (or `winget list`); select → install one by one with live output;
  "Check for updates" (`winget upgrade`) and "Update everything" (`winget upgrade --all`). Missing winget → explain App Installer.
- **Windows features** (`FeaturesPage`): curated optional features (Windows Sandbox, Hyper-V, Virtual Machine Platform, WSL,
  .NET 3.5, Telnet client, SMB 1.0 (recommend off), legacy Media Player, NFS client, PowerShell 2.0 (recommend off)…) with state and
  enable/disable via DISM `/norestart`. Exit code 3010 = done, restart needed. Never restart.
- **Repair & tools** (`RepairPage`): System File Checker, DISM RestoreHealth, component store cleanup, reset Windows Update
  components, reset network stack, flush DNS, clear temporary files (with size), create restore point, restart Explorer, open
  Windows Update settings; a grid of classic Control Panel tools (Control Panel, Network Connections, Power Options, Sound, System
  Properties, Programs and Features, Device Manager, Disk Management, Services, Event Viewer, Task Scheduler, Registry Editor,
  User Accounts (netplwiz), Environment Variables, …). Long tools stream into an `OutputConsole` with a Stop button. Confirm
  before anything that resets or deletes.
- **DNS** (`DnsPanel`, top of the Network page): current DNS per active adapter; choose Automatic (DHCP), Cloudflare, Cloudflare
  malware blocking, Google, Quad9, AdGuard, OpenDNS; apply to all active adapters (admin) and keep the previous servers so the
  change can be undone.

## Testing

- `Build.ps1 -OutputDirectory <scratch> -Test` builds `build\TweakBench.Tests.exe`; run it with `TWEAKBENCH_DATA` pointing at a temp
  folder. Tests never touch the real PC.
- `"<scratch>\Tweak Bench.exe" --audit <file>` reads every tweak's state on this PC (read-only) — use it to confirm detection works.
- `--render <folder>` screenshots every page from demo data; run it on a hidden desktop:
  `& 'C:\dev\App Maintenance\shared\Invoke-HiddenDesktop.ps1' -File <script> -Log <log>` (or `-Command`).

## App standards

Visible Restart button; version in `src\App\Release.cs` + dated `CHANGELOG.md` embedded and shown in the app; Export / import
configuration through the shared helper (whole `data` folder: settings.json, history.json, profiles\); installer only through the
4:00 AM queue; App Management entry; never interrupt active work (Restart waits while busy).
