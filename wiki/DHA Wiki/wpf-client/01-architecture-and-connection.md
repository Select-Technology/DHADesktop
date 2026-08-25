# Architecture & Connection

## Authentication

OAuth via `Microsoft.Xrm.Tooling.Connector.CrmServiceClient`, owned by
`Services\DataverseAuthService.cs` — a private-constructor singleton (`.Instance`) that
holds the real connection. Key details:

- `ClientId` is **hardcoded** to Microsoft's own sample/test app ID
  (`51f81489-12ee-4a9e-aaae-a2591f45987d`) — it does **not** read the
  `DataverseClientId` app setting that exists in `App.config`/`Settings.settings` (which
  holds a different GUID entirely). That setting is effectively unused.
- Tenant ID and environment URL **do** come from `ProjectProperties.Settings.Default`
  (`DataverseTenantId`, `DataverseEnvironmentUrl`), resolved through `App.config`'s
  `applicationSettings` (prod default `https://dhapd.crm11.dynamics.com`). There's also
  an unused legacy `userSettings` block pointing at the dev URL — dead config, since the
  live code path only reads `applicationSettings`.
- Tokens are cached on disk at
  `%LOCALAPPDATA%\DHA\TimeManagement\TokenCache\msal_cache.dat`. If a cached token
  exists, login is silent (`LoginPrompt=Auto`, background refresh); an interactive popup
  only appears if that fails. No client secret / service-principal flow — always
  interactive-or-cached-token OAuth.
- `SwitchEnvironment()` lets the UI flip between dev/prod at runtime (clears the token
  cache, overrides the URL in memory) — but this **doesn't persist**; restarting the app
  reverts to whatever `App.config` says.

## Connection layering — what's actually live

Four classes look like a connection stack. Only two of them carry real traffic:

- **`DataverseAuthService`** (singleton) — owns the connection, token cache, `WhoAmI`
  validation (cached 5 minutes), impersonation (`SetCallerId`), environment switching.
  **Live.**
- **`DataverseConnector`** (`DataAccess\DataverseConnector.cs`) — a thin sync wrapper
  over `DataverseAuthService.Instance`, exposing `Retrieve(Multiple)/Create/Update/Delete`
  plus `ExecuteWithRetry<T>` — detects auth errors (by exception type or message text —
  "401", "unauthorized", token-expiry wording) and silently reconnects/retries once.
  **This is the class every entity service actually holds and calls.** **Live.**
- **`DataverseService`** (`Services\DataverseService.cs`) — an async CRUD façade over
  `DataverseConnector`, instantiated by `ServiceLocator.Initialize()`, but its only
  intended consumers are the dead ViewModels (see below) — so it's constructed but its
  methods are never actually invoked at runtime. **Dead weight.**
- **`DataverseConnectionService`** (`Services\DataverseConnectionService.cs`) — a
  near-duplicate async façade, wrapping `DataverseAuthService` directly instead of going
  through `DataverseConnector`. **Never instantiated anywhere** in the compiled tree.
  **Fully dead.**

Real traffic path: `UI code-behind → entity Service (TimeEntryService etc.) →
DataverseConnector → DataverseAuthService → CrmServiceClient`.

Not every call site necessarily goes through `ExecuteWithRetry` — some service methods
call `_connector.Connect()` directly instead, so token-expiry resilience isn't uniformly
guaranteed everywhere; check the specific call site if diagnosing an auth-drop symptom.

## ServiceLocator and SessionManager — two different, unrelated things

- **`Utilities\ServiceLocator.cs`** — a static manual singleton registry (no DI
  container). `Initialize()` builds one `DataverseConnector` and hands it to every entity
  service's constructor without connecting. `Connect(forceReconnect, showMessages)`
  verifies/refreshes the connection and resolves the current user
  (`CurrentUserId`/`Name`/`Email`, via a fallback cascade: OAuth user id → `GetMyCrmUserId()`
  → `WhoAmI` → `systemuser` lookup). It also owns **impersonation** — gated by a
  **hardcoded allow-list** (`_impersonationAuthorisedEmails = { "stsadmin@dhaplanning.co.uk" }`)
  — and exposes `EffectiveUserId`/`EffectiveUserName` so the rest of the app is
  impersonation-transparent.
- **`Utilities\SessionManager.cs`** — unrelated to Dataverse auth. A static JSON-file
  persistence helper (`%LOCALAPPDATA%\DHA\TimeManagement\session.json`) for **UI/window
  state** across restarts: selected team member, last time-entry date, date-range
  filters, selected tab, window position/state, and preferences like start-minimized and
  auto-refresh interval.

## Single-instance enforcement

`App.xaml.cs` uses a named `Mutex` (`DHA_DSTC_WPF_SingleInstance`) to detect a second
launch. If the mutex already exists, the new process sends an activation message and
exits immediately; the first instance instead starts a pipe server.

`Utilities\NamedPipeServer.cs` is a minimal named-pipe listener
(`DHA_DSTC_WPF_Pipe`, one connection at a time) — not general IPC, just a single hardcoded
`"ACTIVATE"` message. A second launch connects as a pipe client and sends that string; if
the pipe connection fails, it falls back to `Process.GetProcessesByName` +
`WindowHelper.ShowWindow` (Win32) to force the existing window to the foreground. On
receipt, the first instance dispatches to the UI thread and restores the main window.

## Other things worth knowing

- **Intel GPU workaround**: `App.xaml.cs` queries `Win32_VideoController` via WMI at
  startup; if it detects (or fails to determine — it fails open, assuming Intel) an
  Intel GPU, it forces software rendering and sets `WPF_DISABLE_GPU_ACCELERATION`/
  `INTEL_DISABLE_ALL_OPTIMIZATIONS` — a real rendering-bug workaround, not decorative.
- **Global error handling**: `AppDomain.UnhandledException`, `DispatcherUnhandledException`,
  and `TaskScheduler.UnobservedTaskException` are all wired to a file logger.
  `DispatcherUnhandledException` marks itself handled, so a UI-thread exception shows a
  message box but **doesn't crash the app** — which also means the app can keep running
  in a partially broken state after an unhandled error rather than restarting clean.
- **`ErrorMessageHelper`** translates known Dataverse validation-error substrings (e.g.
  "minutes > 59", leaked `fwp_`/`twp_` field names) into user-friendly text, and
  deliberately **suppresses WPF data-binding errors** from ever reaching the user — worth
  knowing if a UI field silently doesn't update and no error appears anywhere.

## MVVM: mostly abandoned

`ViewModels\CalendarViewModel.cs`, `TimeEntryViewModel.cs`, `DisbursementViewModel.cs`
are compiled but never instantiated anywhere outside their own files — `MainWindow.xaml.cs`
does everything directly via `ServiceLocator.<Service>` calls in code-behind. None of the
three implement `INotifyPropertyChanged` (they're plain service wrappers, not bindable
view-models), so even a revival attempt wouldn't get reactive UI updates for free. There's
also a namespace inconsistency if anyone does revive this: `CalendarViewModel`/
`TimeEntryViewModel` live in `DHA.DSTC.ViewModels` (missing `.WPF`), while
`DisbursementViewModel` lives in `DHA.DSTC.WPF.ViewModels`.

`MainWindow` itself **does** implement `INotifyPropertyChanged`, but mostly drives the UI
through direct calls to helper methods (`UpdateStatus`, `UpdateDailyProgress`, etc.)
rather than property-changed-driven bindings — a pragmatic hybrid, not pure MVVM either
way.

## See also

- [00-overview.md](00-overview.md)
- [02-deployment-and-updates.md](02-deployment-and-updates.md)
- [03-pitfalls-and-faq.md](03-pitfalls-and-faq.md)
