# Disbursements

Entity: `fwp_disbursement`. Model: `Models\Disbursement.cs`. Service: `Services\DisbursementService.cs`.

## Fields

| Field | Meaning |
|---|---|
| `fwp_date` | The date of the expense |
| `fwp_description` | Free text |
| `fwp_value` | The £ amount (Money) — for disbursements, unlike time entries, **the app does write this directly** |
| `fwp_units` / `fwp_unitcharge` | For unit-based types (mileage, printing) — only written when `UnitCharge > 0` (`IsUnitBased`) |
| `fwp_disbursementtype` | Lookup to `fwp_disbursementtype`. `fwp_type` (an option set) is a legacy fallback used only if no lookup GUID is set |
| `fwp_project` / `fwp_quote` | Same mutually-exclusive-by-convention pattern as time entries, keyed off `Classification` |
| `BillableToClient` (model property) | **Not backed by any Dataverse column.** `fwp_billabletoclient` was removed from the schema; the app always treats it as `true` on load and never persists it. If someone asks "why can't I mark this non-billable and have it stick" — this is why |

## Where the Amount comes from

For unit-based disbursement types (mileage, printing), `Amount` is calculated as
`Units × UnitCharge` — but that multiplication happens in the **dialog code-behind**
(`EditDisbursementDialog.xaml.cs`, `MainWindow.xaml.cs`), not in the Model or Service
layer. Nothing below the UI re-derives or checks that `Amount` actually equals
`Units × UnitCharge` — if a future code path sets them inconsistently, nothing will
catch it.

## Disbursement types have a hidden write side-effect on read

`DisbursementService.GetAllDisbursementTypesAsync()` calls
`EnsureMileageDisbursementTypesExist()` on **every load**. If a "Mileage (Personal Car)"
type doesn't already exist, it **creates one in Dataverse** by cloning an existing
mileage type. A method that looks like a simple "get the list" can write a new record.

Type names are also silently normalised for display, without touching the underlying
Dataverse record:

- `"Mileage"` → shown as `"Mileage (Company Car)"`
- `"Photocopying (B&W/Colour)"` → shown as `"Printing (B&W/Colour)"`

Anyone inspecting the raw `fwp_disbursementtype` record directly (e.g. in Dynamics) will
see a different name than what the app displays for the same record.

## Hard-coded fallback unit charges are inconsistent

When `fwp_unitcharge` on a disbursement type is `≤ 0`, the app falls back to a
client-side hard-coded rate. There are **two separate fallback tables** with different
values for the same type — e.g. `GetDefaultDisbursementTypes()` lists Printing (Colour)
at `0.45`, while `GetUnitChargeForDisbursementTypeName()` returns `0.42` for the same
type. Which one applies depends on which code path is hit, which in turn depends on
whether `fwp_unitcharge` was ever actually populated on the Dataverse record. If a rate
looks "wrong," check that first before assuming a bug.

## Validation

`Utilities\DisbursementValidationHelper.cs` is an **empty stub** — literally
`internal class DisbursementValidationHelper { }`, referenced nowhere. Don't look here
for disbursement business rules.

The real validation lives in `DisbursementViewModel.ValidateDisbursement`:

- `ProjectId > 0`, `TeamMemberId > 0`, `DisbursementTypeId > 0`, `Amount > 0`
- Non-empty description
- Date not in the future

**Note the gap**: this check always requires `ProjectId > 0`, regardless of
`Classification`. A disbursement classified against a Quote legitimately has
`ProjectId == 0` (it uses `QuoteId` instead) — as written, this validation doesn't branch
on classification the way the equivalent time-entry check does. Confirm against the live
UI path before relying on quote-classified disbursements validating correctly.

## See also

- [01-time-entries.md](01-time-entries.md)
- [03-reconciliation-and-charge-bands.md](03-reconciliation-and-charge-bands.md)
- [04-pitfalls-and-faq.md](04-pitfalls-and-faq.md)
