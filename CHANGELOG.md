# Tweak Bench changelog

## 1.0.0 - 2026-11-11

- First release: Windows power-user tweaks explained in plain English, in the spirit of WinUtil but easy to follow.
- 118 tweaks in 8 categories (Windows Update, Privacy, Start/taskbar/search, File Explorer, Performance & gaming, System & desktop, Network, Background services). Every card shows its live state on this PC, a summary, why you might want it, its risk, when it takes effect and exactly what it changes.
- Headline tweak **Stop automatic restarts for updates**: updates keep installing automatically, but Windows is set not to restart the PC while you are signed in.
- Flip switches to stage changes, then **Review & apply** with an optional System Restore point. Presets: Recommended basics, Privacy, Gaming & performance, Clean & quiet desktop.
- **History** keeps the exact previous values of every change: undo one change or a whole batch. A change that fails part-way is rolled back. Profiles save and re-apply a setup; "My setup (automatic)" is kept current.
- Tools: Remove built-in apps, Install apps (winget, with update checks), Windows features (DISM), Repair & tools (SFC, DISM, Windows Update and network resets, temporary files, classic Windows tools) and a DNS chooser with undo.
- Never restarts the PC. Asks for administrator rights at start and works read-only for system-wide settings without them.
- Restart button, changelog, Export / import configuration (shared Configuration Transfer helper, app code TB), single instance, light and dark themes.
- Enrolled in the Daily App Installer Queue; the first installer is produced by the scheduled run.
