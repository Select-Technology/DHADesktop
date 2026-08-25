# WPF Client — Overview

## What this domain is

"DHA Time Management" (project folder `DHA - Quotes into disbursements`) is the desktop
app staff use day-to-day. This section covers its own plumbing — how it connects to
Dataverse, how it manages a session, how it stops itself running twice, and how it's
packaged and updated. It does **not** cover the business rules for time entries/
disbursements themselves — that's [time-management/](../time-management/00-overview.md).

## The most important thing to know before touching this codebase

**A large amount of code in this project is not actually built or not actually called.**
Before changing behaviour, confirm you're editing a file that's actually live:

- `Utilities\Models\`, `Utilities\Services\`, `Utilities\DataAccess\`,
  `Utilities\ProjectProperties\` are **orphaned duplicates** of the real, compiled
  `Models\`, `Services\`, `DataAccess\`, `ProjectProperties\` folders — same class
  names, older/divergent content, **not included in the `.csproj`**, never built. Editing
  a file in one of these silently does nothing.
- `Services\DataverseService.cs` and `Services\DataverseConnectionService.cs` **are**
  compiled but are dead weight at runtime — nothing in the live code path calls them.
- `ViewModels\CalendarViewModel.cs`, `TimeEntryViewModel.cs`, `DisbursementViewModel.cs`
  are compiled but **never instantiated** — the app doesn't use MVVM in practice; all UI
  logic lives in code-behind (`MainWindow.xaml.cs`, ~8,800 lines).
- `Updates\AutoUpdateService.cs` is compiled, fully functional-looking, but **never
  called from anywhere in the app** — no startup check, no "check for updates" button.

See [01-architecture-and-connection.md](01-architecture-and-connection.md) for exactly
what *is* live, and [03-pitfalls-and-faq.md](03-pitfalls-and-faq.md) for the "I edited
X and nothing happened" symptom directly.

## Where to go next

- [01-architecture-and-connection.md](01-architecture-and-connection.md) — auth, connection layering, session/single-instance handling
- [02-deployment-and-updates.md](02-deployment-and-updates.md) — the installer and the (unwired) auto-update mechanism
- [03-pitfalls-and-faq.md](03-pitfalls-and-faq.md) — symptom → cause → fix
