# Deployment & Updates

## Installer

`DHA.TimeManagement.Installer` (sibling project to the app) builds the MSI using
**WiX 3.x**, driven by `BuildInstaller.bat`:

1. MSBuild the WPF app in Release.
2. `heat.exe` harvests the Release output directory into `AppFiles.wxs` (auto-generated
   — component IDs are content hashes, not hand-authored; regenerated every run, don't
   hand-edit it).
3. `candle.exe` compiles.
4. `light.exe` links into `DHA.TimeManagement.Installer\Release\DHA.TimeManagement.msi`.

`Product.wxs` defines the MSI shell: per-machine install under
`Program Files\Select Technology\DHA Time Management`, Start Menu + Desktop shortcuts,
and a fixed `UpgradeCode` (`65694575-FAE5-4EB3-B371-61A715B8ED24`) that matches an older,
now-unused `.vdproj`-based installer, kept specifically so upgrades from that older
installer still work correctly.

The batch script includes an explicit OneDrive work-around (`attrib -r`) because the
repo lives inside a OneDrive-synced folder, and OneDrive's dehydrated placeholder files
break MSBuild's incremental clean if not forced read-write first.

There is also a `.vdproj` file (`DHA.TimeManagement.Installer.vdproj`) sitting in the
same folder — this is the **legacy** Visual Studio Installer project, no longer used to
build. Don't edit it expecting it to affect the shipped MSI.

## Auto-update — built, wired to nothing

`Updates\AutoUpdateService.cs` / `UpdateModels.cs` are fully implemented and compiled:

- `CheckForUpdatesAsync()` fetches a JSON manifest
  (`{LatestVersion, DownloadUrl, ReleaseNotes, MinimumVersion, ForceUpdate}`) from a
  SharePoint-hosted URL configured in `App.config`
  (`https://dhaplanningcouk.sharepoint.com/sites/FutureWorkplace/Installation/apps/TimeDesktop/version.json`).
- `DownloadAndInstallUpdateAsync` downloads the referenced MSI to a temp path and shells
  out to `msiexec.exe /i ... /quiet /norestart REBOOT=ReallySuppress`.

**Nothing in the app calls any of this.** There's no startup check, no "check for
updates" menu item, no scheduled task — `App.xaml.cs` and `MainWindow.xaml.cs` never
reference `AutoUpdateService`. It compiles cleanly and would presumably work if wired
up, but as of this writing it's inert scaffolding. (The identical copy under
`Utilities\Updates\` is a byte-for-byte duplicate of this one — not diverged, just the
same orphaned-duplicate pattern described in
[00-overview.md](00-overview.md), applied here too.)

If updates are currently distributed to users at all, it isn't through this mechanism —
check with whoever manages rollout for the actual current process (manual MSI
distribution via the SharePoint site referenced above is the most likely candidate,
given `version.json`'s location and the existence of `DHA-TimeManagement-ReleaseNotes.html`
as a standalone "what's new" page in the app project — that HTML file is also not opened
by any compiled code; it looks like something distributed alongside a release rather
than something the app displays itself).

## See also

- [00-overview.md](00-overview.md)
- [01-architecture-and-connection.md](01-architecture-and-connection.md)
- [03-pitfalls-and-faq.md](03-pitfalls-and-faq.md)
