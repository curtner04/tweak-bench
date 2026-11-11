# Tweak Bench

Windows power-user tweaks explained in plain English, with live status, a review step before anything changes, and exact
undo. It is in the spirit of Chris Titus Tech's WinUtil, built to be easy to follow.

**Tweak Bench never restarts your PC.** When a change needs a restart, sign-out or File Explorer restart, it says so and
leaves the timing to you.

## Download

[**Download Tweak Bench for Windows**](https://github.com/curtner04/tweak-bench-downloads/releases/latest/download/TB-Setup.exe)
(64-bit Windows 11) · [release history](https://github.com/curtner04/tweak-bench-downloads/releases)

The installer puts Tweak Bench in your user profile without asking for administrator rights; Tweak Bench itself asks when it
opens. If the link says "Not Found", the first installer has not been published yet.

## How it works

1. **See what is set.** Every tweak is a card with a plain title, one sentence on what it does, a "Why" line, a risk badge
   and its current state on this PC (On, Off, Partly on, Not available here). "What it changes" lists every registry
   value, service, scheduled task or power setting with its current value.
2. **Flip the switches.** Nothing changes yet. Your choices wait in the bar at the bottom of the window ("N changes ready").
   Presets (Recommended basics, Privacy, Gaming & performance, Clean & quiet desktop) pick a set for you.
3. **Review and apply.** The review lists exactly what will be turned on or off, offers a System Restore point first, then
   shows the result of each change. **History** saves the exact previous values of every change, so you can undo one change
   or a whole batch at any time.

Start with **Start here**: the highlighted card is **Stop automatic restarts for updates**.

## Pages

| Page | What it has |
| --- | --- |
| Start here | How it works, the automatic-restart card, presets, a summary of this PC (Windows version, uptime, restart pending) and quick actions |
| Windows Update (11) | Stop automatic restarts, ask before installing, active hours, restart notices, no waking the PC for updates, delays for feature/quality updates, no driver updates, no peer sharing |
| Privacy (25) | Diagnostic data, telemetry service and tasks, advertising ID, activity history, location, speech and typing, Recall / Windows AI and other data collection |
| Start, taskbar & search (25) | Widgets, Task View, search box, alignment, End task, Start recommendations, web results in search, lock-screen tips, suggested apps and nags |
| File Explorer (20) | File extensions, hidden files, open to This PC, Home clean-up, classic right-click menu, OneDrive ads, long paths |
| Performance & gaming (14) | Power plans, hibernation, Fast Startup, Game Bar recording, GPU scheduling, mouse acceleration, animations, sleep and USB power |
| System & desktop (13 + pop-up position) | Notification pop-up position (put pop-ups anywhere on any screen), dark mode, accessibility shortcut pop-ups, Num Lock, sign-in status messages, crash screen details, snapping and other everyday settings |
| Network (4 + DNS) | DNS provider for all connected adapters (with undo), Teredo, IPv4 preference, LLMNR |
| Background services (6) | Optional services many people never use (Advanced ones are hidden until you turn on "Show advanced tweaks") |
| Remove built-in apps | A curated list of removable inbox and Store apps with what each one is; removal for your account, optionally for new accounts too |
| Install apps | About 100 popular apps through winget, with installed status, "Check for updates" and "Update everything" |
| Windows features | Windows Sandbox, Hyper-V, WSL, .NET 3.5, SMB 1.0 and other optional features, enabled or disabled with DISM (no automatic restart) |
| Repair & tools | System File Checker, DISM repair and clean-up, Windows Update reset, network reset, DNS flush, temporary files, restore point, File Explorer restart, and 27 classic Windows tools |
| History | Every change with its previous values, undo per change or per batch, and saved profiles |

## Notification pop-up position

Windows shows notification pop-ups in the bottom-right corner of your main screen and has no setting to move them. On
**System & desktop**, the **Notification pop-ups** panel moves them to any spot on any screen: pick a quick spot (top left,
top centre, top right, bottom left, bottom centre, bottom right) or **Choose a spot on any screen** and drag a card to where
you want pop-ups to appear. **Send a test notification** shows a sample.

A small helper, `Tweak Bench Notifications.exe`, does the moving. It has no window, runs without administrator rights, starts
when you sign in (a `Tweak Bench Notifications` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) and watches for
the Windows pop-up window, moving it to your spot each time Windows places it. To turn it off, switch **Move notification
pop-ups** off: the sign-in value is removed, the helper stops and Windows places pop-ups itself again. Nothing in Windows is
changed.

Limits:

- A pop-up can flash in the corner for a moment before it moves.
- The Windows pop-up window has a transparent edge that still catches mouse clicks while the pop-up shows. On a screen with a
  different scaling than your main screen (for example 100% next to a 150% main screen) that edge above the card is taller, about
  the height of the card itself.
- Pop-ups that an app draws itself (instead of Windows) are not moved.
- With Do Not Disturb on, Windows shows no pop-ups at all.
- It works with the current Windows 11 design of pop-ups and may need an update if Windows changes it.

## Administrator rights

Tweak Bench asks for administrator rights when it opens. Say no and it opens anyway: settings for your own account still
work, and system-wide ones are shown read-only with a **Run as administrator** button. You can turn the prompt off in Settings.

## Profiles and moving to a new PC

**History > Profiles** saves your setup as a profile you can review and apply on any PC, export or import. Tweak Bench also
keeps **My setup (automatic)** up to date after every change.

**Export / import configuration** (in the ⋯ menu, and as a Start menu shortcut after installation) carries Tweak Bench's
whole data folder (`%LOCALAPPDATA%\Tweak Bench\data`: settings, staged choices, history and profiles) in a
password-encrypted archive. It does not copy the Windows settings themselves: on the new PC open **History > Profiles >
My setup (automatic) > Review & apply**. Undo of old history entries only works on the PC where they were made. Removed apps,
installed apps and Windows features are not repeated automatically.

## Safety

- Every change saves the exact previous state first, and a tweak that fails part-way is put back as it was.
- Turning a tweak off restores the values from before Tweak Bench changed it, or the Windows default when there is no history.
- Nothing weakens Windows security: Defender, SmartScreen, UAC, the firewall and Windows Update itself are never turned off.
- Repairs that reset or delete something ask first; the Windows Update reset renames folders instead of deleting them.
- Restart waits while a change, repair or install is running, and never runs an installer.

## Command line

| Argument | Effect |
| --- | --- |
| `--version` | Prints the version |
| `--changelog` | Prints the changelog |
| `--audit [file]` | Read-only report of every tweak's state on this PC |
| `--no-elevate` | Opens without asking for administrator rights |
| `--render <folder> [--theme light\|dark]` | Development: renders every page from demo data to PNG files; never touches the PC |

## Building and testing

- `powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1` builds `Tweak Bench.exe` and the helper
  `Tweak Bench Notifications.exe` here (Roslyn from
  `C:\dev\Toolchains\VS2022`, .NET Framework 4.8). `-OutputDirectory <folder>` builds elsewhere and also refreshes the exe
  here; `-Test` also builds `TweakBench.Tests.exe`.
- The build also compiles a few shared sources (`AppRestart.cs`, `SingleInstance.cs`, `ConfigurationTransfer.cs`,
  `ReleaseNotes.cs`, `ReleaseAttributes.cs` and `Build.Common.ps1`) from `C:\dev\App Maintenance\shared`, or from a `_shared`
  folder next to `Build.ps1`. They are not part of this repository, so a fresh clone does not build on its own yet.
- `tests\Test.ps1` builds and runs the tests in a temporary folder. Tests use an in-memory PC; the only real writes are a
  private `HKCU\Software\TweakBench.SelfTest` key that the registry test deletes again.
- Installers are made only by the 4:00 AM Daily App Installer Queue (`C:\dev\App Maintenance`, app code **TB**).
- `DESIGN.md` describes the architecture, catalog rules and file layout.

Data: `%LOCALAPPDATA%\Tweak Bench\data` (transferred; `settings.json` also holds the notification option, spot and learned pop-up
names) and `%LOCALAPPDATA%\Tweak Bench\logs` (this PC only; includes `notifications.log` and `notifications-status.json`
from the helper).
