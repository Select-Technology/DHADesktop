# Time Entries

Entity: `fwp_timeentry`. Model: `Models\TimeEntry.cs`. Service: `Services\TimeEntryService.cs`.

## Fields

| Field | Meaning |
|---|---|
| `fwp_date` | The work date. **This, not `createdon`, drives locking** — see below |
| `fwp_notes` | Free-text comments |
| `fwp_minutes` | The **minutes component only** (0–59) — not a total |
| `fwp_durationhours` | The **whole-hours component only** — e.g. `1` for "1h 30m" |
| `fwp_decimalhours` | The **real total**: `Hours + Minutes/60`, e.g. `1.5` for "1h 30m". This is the field billing/rate math actually uses |
| `fwp_category` | `Chargeable=1`, `NonChargeable=2`, `Speculative=3`, `HourlyRate=4` |
| `fwp_classification` | `Project=800470000`, `Quote=800470001` |
| `fwp_chargerate` | `RateA=800470000`, `RateB=800470001` ("Rate B (Expert Witness)"), `RateC=800470002` |
| `fwp_project` / `fwp_quote` | Lookup to whichever matches `fwp_classification` — the other is always cleared |
| `fwp_teammember` | Lookup to `systemuser` |
| `createdon` | Display only (`CreatedDate`) — **not** used for locking, despite a model comment that suggests otherwise (see Pitfalls) |

`fwp_value` (the £ amount) deliberately does **not** appear in this list — see
[03-reconciliation-and-charge-bands.md](03-reconciliation-and-charge-bands.md).

**`fwp_project`/`fwp_quote` are mutually exclusive by app convention, not a Dataverse
constraint.** `TimeEntry.ToEntity()` writes whichever lookup matches `Classification`
and explicitly nulls the other. Nothing on the Dataverse side stops a record having both
(or neither) set if it's touched outside this app.

## Validation actually wired in

`TimeEntryViewModel.ValidateTimeEntry` runs on save:

- A Project must be selected if `Classification == Project`; a Quote if `Classification == Quote`.
- A team member is required.
- Hours and minutes can't both be zero ("Please enter time spent").
- Date can't be in the future.

**It does not call the daily-hours-limit validators at all** — those are wired in
separately, and only from specific dialogs (see below).

## Daily / single-entry hour limits

There are **two** validators for this, and only one is actually used:

- `Utilities\DailyTimeValidationHelper.cs` — **the live one.** `MAX_SINGLE_ENTRY_HOURS = 16`,
  `MAX_DAILY_HOURS = 24`, warns (but still allows) once a day's total reaches
  `NEAR_LIMIT_THRESHOLD = 22`. Called from `EditQuoteTimeEntryDialog.cs` and
  `MainWindow.xaml.cs` only.
- `Utilities\TimeEntryValidationHelper.ValidateDailyHoursLimit` — an older, simpler
  24-hour-only check. **Dead code** — nothing calls it. Don't extend this one; extend
  `DailyTimeValidationHelper`.

Because the daily-limit check is called from specific dialogs rather than from
`TimeEntryViewModel.ValidateTimeEntry`, **any other save path that bypasses those two
dialogs skips the daily-hours check entirely.**

There is no per-weekday expected-hours validation on time entries. The
`fwp_mondayexpectedhours`…`fwp_fridayexpectedhours` fields exist on
`fwp_colleagueconfiguration`, not on the time entry, and are used only for holiday-hours
calculations — see
[03-reconciliation-and-charge-bands.md](03-reconciliation-and-charge-bands.md).

## Locking rules

Keyed off `fwp_date`, evaluated in `Europe/London` time (handles BST/GMT automatically),
implemented in `Utilities\TimeEntryValidationHelper.cs`:

- Anything **more than 8 days old is always locked**, no exceptions.
- Tuesday through Sunday: everything back to (not including) the **previous Sunday** is
  editable.
- Monday **before noon**: the window stays open one extra morning — still back to
  8-days-ago Sunday.
- Monday **at/after noon**: the window snaps shut to just "yesterday onward."

`TimeEntry.IsLocked` / `IsEditable` are computed live from this, and the UI shows a
warning banner when less than 24 hours remain before an entry locks
(`ShouldShowLockWarning()`).

**Creating** a new entry is a separate, much simpler rule (`CanCreateTimeEntry`): it just
blocks future dates. There's no back-dating limit on creation — only on editing an
existing entry.

## Category vs. classification vs. charge band — three different choices

These are easy to conflate when reading the UI:

- **Category** (`fwp_category`) — is this billable work at all (Chargeable /
  Non-Chargeable / Speculative / Hourly Rate)?
- **Classification** (`fwp_classification`) — is it against a Job or a Quote?
- **Charge band** (`fwp_chargerate`) — for chargeable work, which of the colleague's
  three configured rates (A/B/C) applies?

None of these three fields imply or default from one another — each is chosen
independently in the entry dialog.

## See also

- [02-disbursements.md](02-disbursements.md)
- [03-reconciliation-and-charge-bands.md](03-reconciliation-and-charge-bands.md)
- [04-pitfalls-and-faq.md](04-pitfalls-and-faq.md)
