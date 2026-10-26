# Pitfalls & FAQ — Time Management

Symptom → cause → fix, for the fastest lookup.

---

**"George (or anyone) sees zero quotes, but quotes definitely exist."**

- **Cause**: their security role lacks field-level-security read access to
  `isc_projectnumbervisible`. When that column isn't readable, Dataverse evaluates an
  `IS NULL` filter on it as matching **zero rows** for that user — not "unknown," not
  "the visible-but-null rows," zero.
- **Fix**: already handled in code. `QuoteService.GetQuotes()` retries without the
  `isc_projectnumbervisible IS NULL` condition if the first query returns nothing, and
  does that filtering in C# instead. `SearchQuotes()` never adds the server-side
  condition in the first place, for the same reason. If this resurfaces, use
  `QuoteService.GetQuoteDebugInfo()` for a full diagnostic dump (impersonation state,
  WhoAmI, unfiltered baseline query, status distribution, field-value analysis).
  See `DiagnoseForGeorge.md` for the original investigation.
- **Likely root cause, found in a later live-environment pass**: three legacy Field
  Security Profiles from the previous implementation partner —
  `DHA - Client Approver`, `DHA - Non-Client Approver`, `Project Admin Team` — are the
  probable mechanism gating `isc_projectnumbervisible`. Not confirmed definitively (see
  [dataverse-customizations/08-ig-legacy-layer.md](../dataverse-customizations/08-ig-legacy-layer.md#field-security-profiles--likely-mechanism-behind-the-george-bug)
  for the specific profile IDs and the exact follow-up query that would close this out).

---

**"Time looks right in the app but the invoiced/reported value is £0."**

- **Cause**: the colleague has no `fwp_colleagueconfiguration` record. With none,
  `ColleagueConfiguration.CreateDefault()` returns £0 for Rate A/B/C — the time entry's
  hours are all correct, but the value computed from them is zero, with no error
  surfaced anywhere.
- **Fix**: create a colleague configuration record for that person with their actual
  rates.

---

**"I can only see my own time entries on a job, not everyone else's."**

- **Cause**: security role only grants **Own**-level access to time entries (DHA-1 only).
- **Fix**: see [dataverse-customizations/02-security-roles.md](../dataverse-customizations/02-security-roles.md)
  — add DHA-2 (keeping DHA-1) for team-wide visibility.

---

**"An old time entry I need to fix says it's locked, but it's not reconciled."**

- **Cause**: two independent locks exist. The date-based lock
  (`TimeEntryValidationHelper`) fires roughly a week after the work date regardless of
  reconciliation status — it's not the same thing as the reconciliation lock.
- **Fix**: this is by design (stop indefinite editing of old timesheets). If a genuine
  correction is needed, it has to go through whoever can edit reconciled/locked data
  directly in Dynamics — the desktop app has no override.

---

**"I picked Rate B on my last entry, but the next one defaulted back to Rate A."**

- **Cause**: not a bug. Every new time entry defaults to Rate A; the app never
  remembers the last band chosen, per the functional requirement that a charge-band
  selection applies only to the entry it was made on.
- **Fix**: none needed — re-select the band each time it's not the default.

---

**"The calendar view shows an empty month, but I know I logged time then."**

- **Cause**: `CalendarService.GetMonthlyTimeEntries` calls the time entry query with no
  explicit date range, which defaults to the last 90 days. Navigate further back than
  ~3 months and the query silently returns nothing for that period.
- **Fix**: known limitation, not a data-loss bug — the entries still exist in Dataverse.
  Needs a code fix to pass an explicit range for the month being viewed if this needs
  supporting properly.

---

**"Someone edited/deleted a time entry that should have been locked because it's reconciled."**

- **Cause**: `ReconciliationValidationHelper` fails **open** — if the reconciliation
  check itself errors (connection drop, record not found, any exception), the edit is
  allowed rather than blocked.
- **Fix**: check for connectivity issues around the time of the edit. This is a
  deliberate design trade-off (don't block legitimate work on a transient error), not a
  bug to "fix" without changing that trade-off deliberately.

---

**"A disbursement's mileage/printing rate looks different from what I expected."**

- **Cause**: two different hard-coded fallback tables exist for unit charges
  (`GetDefaultDisbursementTypes()` vs `GetUnitChargeForDisbursementTypeName()`), with
  different values for the same type (e.g. Printing/Colour: `0.45` vs `0.42`). Which one
  applies depends on whether `fwp_unitcharge` is actually populated on the
  `fwp_disbursementtype` record.
- **Fix**: check the `fwp_unitcharge` value on the actual Dataverse record first.

---

**"I unchecked 'billable to client' on a disbursement, but it's back to checked next time."**

- **Cause**: `fwp_billabletoclient` doesn't exist as a Dataverse column anymore. The
  field is UI-only and always reloads as `true`.
- **Fix**: none available in the current build — this needs a schema/code change if the
  business genuinely needs this to persist.
