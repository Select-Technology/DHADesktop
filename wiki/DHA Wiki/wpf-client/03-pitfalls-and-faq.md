# Pitfalls & FAQ — WPF Client

---

**"I edited a file under `Utilities\Models\` or `Utilities\Services\` (or
`Utilities\DataAccess\`/`Utilities\ProjectProperties\`) and nothing changed."**

- **Cause**: those four folders are orphaned duplicates from an earlier reorganisation —
  files were promoted to the top-level `Models\`, `Services\`, `DataAccess\`,
  `ProjectProperties\` folders, and the old copies were left behind instead of deleted.
  They're not in the `.csproj` — the compiler never sees them.
- **Fix**: make the change in the top-level folder with the same filename instead.
  Confirm which one you're in before editing — this has genuinely diverged content (e.g.
  the orphaned `DisbursementService.cs` is 472 lines vs the real one's 1,717; the
  orphaned `Services\` folder is missing `QuoteService.cs` entirely), so it's easy to
  mistake the old copy for a smaller/simpler version of the current code rather than
  realising it's just dead.

---

**"I changed something in a ViewModel and the UI didn't react."**

- **Cause**: `CalendarViewModel`, `TimeEntryViewModel`, `DisbursementViewModel` are
  compiled but never instantiated — the app doesn't use them. All real UI logic is in
  `MainWindow.xaml.cs` code-behind.
- **Fix**: find the equivalent logic in `MainWindow.xaml.cs` or the relevant dialog's
  code-behind instead.

---

**"Does the app check for updates on its own?"**

- **No.** `AutoUpdateService` is fully implemented but never called from anywhere.
  Whatever the actual update-distribution process is today happens outside this code
  path — check with whoever manages rollout. See
  [02-deployment-and-updates.md](02-deployment-and-updates.md).

---

**"The app won't launch a second window / clicking the shortcut again does nothing visible."**

- **Cause**: by design. Single-instance enforcement via a named mutex — a second launch
  sends an "ACTIVATE" message over a named pipe to the running instance and exits.
- **Fix**: not a bug. If the running instance's window isn't coming to the foreground,
  check whether the pipe connection itself is failing (falls back to a Win32
  `ShowWindow` call on the existing process) — that fallback path is the thing to debug
  if activation isn't working.

---

**"Switching to the dev environment in the app, then restarting, put me back on prod."**

- **Cause**: `DataverseAuthService.SwitchEnvironment()` only overrides the environment
  URL in memory — it was never meant to persist across restarts. `App.config`/
  `Settings.settings` remain the source of truth for the next launch.
- **Fix**: not a bug — re-switch after restarting if needed, or change the setting in
  `App.config` for a persistent change.

---

**"A field silently isn't updating in the UI and no error appears anywhere."**

- **Cause**: `ErrorMessageHelper` deliberately suppresses WPF data-binding errors from
  ever surfacing to the user (to avoid noisy/confusing error boxes for binding issues).
- **Fix**: check the Debug output window in Visual Studio for binding errors — they're
  suppressed from the UI, not from the debug trace.
