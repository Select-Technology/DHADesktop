# Reconciliation and Charge Bands

## Reconciliation (as enforced by the WPF client)

`Utilities\ReconciliationValidationHelper.cs` is what actually blocks edits/deletes in
this app. **The field it checks is `fwp_reconciledvaluedirect`** — not
`fwp_calculatedreconciledvalue` (that name doesn't appear anywhere in the compiled
client code; it only shows up in the new plug-in project — see the open risk noted in
[dataverse-customizations/01-value-calculation-flow-vs-plugin.md](../dataverse-customizations/01-value-calculation-flow-vs-plugin.md#open-risk-two-different-reconciliation-fields)).

Mechanics:

- Any non-zero value in `fwp_reconciledvaluedirect` (handles `Money`, `decimal`, `int`,
  or a parseable string) blocks the edit or delete.
- **Fails open**: if the connection drops, the record can't be found, or any exception
  occurs while checking, the helper returns "allowed" rather than "blocked" — a
  deliberate choice ("don't block someone's work because of a transient error"), but it
  means a Dataverse hiccup can silently let an edit through on a record that should have
  been locked.
- **`Create` is never checked** — a brand-new time entry or disbursement obviously can't
  already be reconciled, so `CreateTimeEntry`/`AddDisbursementAsync` skip the check
  entirely.
- Where it's enforced: `TimeEntryService.UpdateTimeEntry(Async)`,
  `DeleteTimeEntry(Async)`, and the disbursement equivalents in `DisbursementService.cs`.

What the user sees on a blocked edit/delete: a message box titled e.g. "Cannot Modify
Time Entry" / "Cannot Delete Disbursement", with the reconciled value and date, and a
line telling them to contact their supervisor.

There is also a separate `fwp_reconciledvaluedirect` **entity** (not just a field),
joined to `systemuser`, used by `GetTimeEntryReconciliationInfo` /
`GetDisbursementReconciliationInfo` to show who reconciled a record and when — so there's
both a mirrored field on the time entry/disbursement record and a related
reconciliation record.

## Charge bands and rates

Each colleague has one `fwp_colleagueconfiguration` record (`Services\ColleagueConfigurationService.cs`)
holding:

- `fwp_ratea` / `fwp_rateb` / `fwp_ratec` — that person's £ rate for each band
- `fwp_mondayexpectedhours` … `fwp_fridayexpectedhours` — expected weekday hours, used
  **only** by the holiday-hours calculator (a hard-coded 09:00–17:00 day minus a
  hard-coded 13:00–14:00 lunch). Saturday/Sunday aren't real Dataverse fields at all —
  they're hard-coded to `0` in the model, since the schema has no weekend columns.
  **None of this is used to validate or cap actual logged time.**

If a colleague has **no** configuration record, `ColleagueConfiguration.CreateDefault()`
assumes 8h/weekday and **£0 for all three rates** — so an unconfigured person's
chargeable time will compute to £0 value with no error shown anywhere. If someone's
invoiced time looks wrong, check they have a configuration record before looking
anywhere else.

**Which band applies is chosen per time entry, and never remembers the last choice** —
every new entry defaults to Rate A, even if the previous entry (in the same session,
same day, same job) was Rate B or C. This matches the functional requirement
("charge band selection... shall apply to only that time entry") but regularly surprises
users who expect it to stick.

The rate **value itself** is maintained only in Dynamics by admins — end users can pick a
band in the app, but can't see or edit the £ figure behind it. The app writes the chosen
**band** (`fwp_chargerate`) to the time entry; it does **not** write the resulting
**value** (`fwp_value`) — that's computed server-side. See
[dataverse-customizations/01-value-calculation-flow-vs-plugin.md](../dataverse-customizations/01-value-calculation-flow-vs-plugin.md)
for how.

## See also

- [01-time-entries.md](01-time-entries.md)
- [02-disbursements.md](02-disbursements.md)
- [04-pitfalls-and-faq.md](04-pitfalls-and-faq.md)
