# Flows & Business Rules

*Reconstructed from the live environment on 2026-08-24 — see
[03-solution-landscape.md](03-solution-landscape.md#how-this-was-produced) for method.
All classic workflows, business rules, business process flows, and Power Automate cloud
flows live in the same Dataverse `workflow` table, distinguished by `category` — that's
why they're documented together here.*

Across the 14 Time/Disbursement/Billing solutions there are **58 workflow-table
components**: 39 Modern Flows (cloud flows), 16 Business Rules, and 3 Business Process
Flows. **23 of the 58 (40%) are in Draft — turned off** — including some that sound
core to reconciliation and invoicing. Don't assume a flow does anything just because it
exists; check its state.

## Naming convention observed

- `fwp_auto_...` — event-triggered (fires on a Dataverse create/update/delete)
- `fwp_inst_...` / `fwp_instant_...` — manually triggered (a button/instant flow)
- `fwp_sched_...` — scheduled (runs on a timer)

Some flows don't follow this convention (older ones, or from a different author/era) —
treat it as a helpful hint, not a guarantee.

## Time entry value calculation — confirmed live status

`fwp_setTimeEntryValue_2` is a **Modern Flow, currently Activated** in production. This
confirms what [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md)
already described from the maintainers' notes: the legacy cloud flow is still the thing
computing `fwp_value` in production today. The plug-in replacement
(`SetTimeEntryValuePlugin`) has not been cut over here yet.

**New finding, not previously known**: there are at least two *other* flows that also
touch time-entry value in some way:

- `fwp_auto_setTimeValueWhenAddedManually` — **Activated**. Name suggests it's a second,
  parallel path for setting time entry value specifically when an entry is added
  manually (as opposed to however `fwp_setTimeEntryValue_2` triggers).
- `fwp_auto_setTimeEntryValue` — **Draft** (off). Possibly an earlier/retired version,
  given the naming similarity to `fwp_setTimeEntryValue_2`.

**This matters for the plugin cutover**: `SetTimeEntryValuePlugin`'s own documentation
describes itself as a "durable replacement for the `fwp_setTimeEntryValue_2` cloud
flow" — but if `fwp_auto_setTimeValueWhenAddedManually` is still Activated and also
writes to `fwp_value` under some condition, turning off `fwp_setTimeEntryValue_2` alone
would not make the plug-in the sole writer of that field. **Before cutover, confirm what
`fwp_auto_setTimeValueWhenAddedManually` actually does and whether it needs to be turned
off too.**

**Update 2026-08-25 — both flows' actual logic has now been read and this is confirmed,
not just suspected**: `fwp_auto_setTimeValueWhenAddedManually` is a genuine second
writer that also **silently forces every new time entry's charge band to Rate A**,
racing against `fwp_setTimeEntryValue_2` on every Create. Separately,
`fwp_setTimeEntryValue_2` itself has a self-retrigger bug (its "already correct" check
is hardcoded to Rate A) that's a plausible root cause of its reliability problems. See
[14-flow-logic-verification.md](14-flow-logic-verification.md#time-entry-value-calculation)
and [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md) for
the full detail — this is now the highest-priority item in the whole dataverse-customizations
domain.

## Reconciliation — the real mechanism

This is the answer to the open question raised in
[01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md#open-risk-two-different-reconciliation-fields) —
partially. `FWPReconciliationFix` contains exactly two flows:

| Flow | Category | State | What it does |
|---|---|---|---|
| "FWP: Set Unreconciled Value On Demand" | Modern Flow | **Activated** | Manual/button trigger → queries `fwp_timereconciliation` rows where `statecode = 0` and `fwp_TimeEntry` is set → for each, `HTTP POST`s to an external Azure Function |
| "FWP: Call Reconciliation balancer on change to time reconciliation" | Modern Flow | **Draft (off)** | Would trigger automatically on create/update/delete of `fwp_timereconciliation` → `HTTP POST`s the same Azure Function |

Both simply **hand the record ID off to an external Azure Function**:
`https://dha-reconciliation-balancer.azurewebsites.net/api/ProcessTimeReconciliation`.
**The actual reconciliation calculation logic lives in that Azure Function, not in
Dataverse** — it can't be inspected via FetchXML or any of the tools used for this wiki.

Two consequences worth knowing:

1. **Reconciliation today only runs when manually triggered.** The automatic
   "fires on any change" flow is switched off — only the on-demand/button flow is live.
   If reconciliation values look stale, this is the first thing to check: has anyone run
   the on-demand flow recently?
2. **The `fwp_calculatedreconciledvalue` vs `fwp_reconciledvaluedirect` question is
   partially resolved** — neither field name appears in either of these two flows (the
   actual reconciliation math is delegated to the Azure Function). But reading
   `fwp_setTimeEntryValue_2`'s own logic separately confirmed that **the legacy flow —
   and now the plug-in — both guard on `fwp_calculatedreconciledvalue`**, matching each
   other. The only remaining mismatch is between that pair and the WPF client's own lock
   (`fwp_reconciledvaluedirect`) — a pre-existing gap, not something introduced by this
   reconciliation mechanism or the plugin migration. See
   [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md#resolved-the-plug-ins-reconciliation-field-matches-the-flow-it-replaces).
   What still can't be confirmed from Dataverse: whether/how the Azure Function itself
   writes either field when it processes a `fwp_timereconciliation` record — **that
   still requires the Function App's own code/config.**

## Invoicing & Sage sync flows

A cluster of flows in `FWPTimeDisbursementFlows`/`FWPTimeDisbursementandBilling` handle
invoice generation and the Sage sync described in
[03-solution-landscape.md](03-solution-landscape.md#sage-integration-via-environment-variables).
**All of the flows below have since had their actual logic verified — see
[14-flow-logic-verification.md](14-flow-logic-verification.md) for the corrected detail;
several of the original name-based guesses turned out to be wrong or incomplete, noted
inline.**

**Activated:**
- `fwp_auto_generatepdfinvoice` — dispatches to an external Azure Function
  (`dhainvoicecreator.azurewebsites.net`) to generate the PDF; **does not reference the
  `fwp_creatingPDF` mutex flag** anywhere visible, contrary to the earlier assumption
- `fwp_auto_reconcileInvoiceOnRequestForApproval` — dispatches to a *different* Azure
  Function (`dhadataverseeditor...`) with a **hardcoded `"environment":"dev"` despite
  running in production** (a likely live bug), and directly resets every linked
  disbursement's `fwp_invoiced` flag to false
- `fwp_auto_setBillingContactDetailOnInvoice` — reads from the standard `contacts`
  table via a single lookup field, **not** `fwp_additionalbillingcontacts` as originally
  assumed
- `fwp_instant_AlignClientWithSage` — button-triggered, but confirmed to call a
  **third** distinct Azure Function (`dhasageinterface.azurewebsites.net`, a
  find-or-create tool), not the same backend as the automatic Sage sync flows
- `fwp_auto_copyInvoicePoToProject` — gated on `fwp_usethispoforfutureinvoices = true`,
  not unconditional
- `fwp_defaultDisbursementsToStandardTax`
- `fwp_auto_setInvoiceDueDate` — confirmed calculation: `fwp_duedate = fwp_invoicedate + 28 days`,
  flat offset, no conditions — see the duplicate-mechanism note below
- "FWP: Send Time Reminder Email" — **now believed to be dead/prototype code**, not part
  of the live reminder path; see [14-flow-logic-verification.md](14-flow-logic-verification.md#reminders--configuration)

**Draft (off):**
- `fwp_sched_updateValueSinceLastInvoice`
- `fwp_auto_publishInvoice` (exists **twice**, once per solution — see duplicates below)
- `fwp_callInvoiceReconciliation`
- `fwp_auto_wheninvoiceiscreatedreconciletime`
- `fwp_auto_createStandardDisbursement` / `fwp_auto_createstandarddisbursement` (case-variant duplicate, see below)

**Business rules on DHA Invoice** (mostly Activated): "Set Invoice Due Date", "Set
Default Values on Invoice", "Show or Hide PDF Details", plus some Draft ones ("Show
error and suggest negative value if Is Error = Yes AND PS Amount >= 0", an older
duplicate "Require PS Narrative...").

**Business Process Flows** (all Activated, standard-Project-Operations-shaped):
"Project Service - Opportunity to Project Process", "DHA Invoice Flow", "Project Service
- Quote to Project Process".

## Client-lock business rules

`FWPLockCustomerWhenPopulated` contains three Draft (off) business rules: "Lock
Completed Client Field on Opportunity", "Lock Client when populated on Project", "Lock
client when populated on Quote". All three are currently inactive — if the business
expectation is that the client/customer field becomes locked once set, **it currently
isn't being enforced**, since all three rules are Draft.

## Known duplicates / things worth a cleanup pass

These aren't necessarily bugs, but they're exactly the kind of thing that wastes a
developer's time figuring out "which one is real":

- **`Require PS Narrative if PS value > 0`** exists twice on DHA Invoice — one Activated,
  one Draft, differing only in capitalisation ("value" vs "Value"). Looks like a leftover
  from a rename.
- **`fwp_auto_setDisbursementMarginFromCategory`** exists twice (one Activated, one
  Draft) in `FWPTimeDisbursementandBilling`.
- **`fwp_auto_createStandardDisbursement`** (Draft, `FWPTimeDisbursementFlows`) vs
  **`fwp_auto_createstandarddisbursement`** (Draft, `FWPTimeDisbursementandBilling`) —
  same name, different casing, both inactive, packaged in two different solutions.
- **`fwp_auto_publishInvoice`** appears in both `FWPTimeDisbursementFlows` and
  `FWPTimeDisbursementandBilling` — both copies Draft.
- **Invoice due date may be set two ways**: a Business Rule ("Set Invoice Due Date",
  Activated) and a Modern Flow (`fwp_auto_setInvoiceDueDate`, Activated) both appear to
  target the same outcome. **Update**: the flow's calculation is now confirmed —
  `fwp_invoicedate + 28 days` flat, no conditions. Still not confirmed whether the
  Business Rule agrees with that offset or uses different logic; if they disagree, one
  will silently overwrite the other depending on execution order.

**Update 2026-08-25**: `fwp_auto_publishInvoice_v2` (Activated, in `FWPSageIntegration`)
is confirmed as the live replacement superseding all of the above — see
[14-flow-logic-verification.md](14-flow-logic-verification.md#sage-sync). Reading the
Sage/invoicing flows in full also surfaced that DHA's integration surface spans **at
least four separate external Azure Function Apps**
(`dha-reconciliation-balancer`, `dhainvoicecreator`, `dhadataverseeditor...`,
`dhasageinterface`), not one — each opaque from Dataverse, each a real gap in what this
wiki can verify without direct Azure access.

None of the above were investigated deeply enough to say definitively "this is dead" or
"this is a live bug" — they're flagged so a future pass (or whoever owns this solution
set) can make that call with actual business context.

## See also

- [00-overview.md](00-overview.md)
- [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md)
- [03-solution-landscape.md](03-solution-landscape.md)
- [14-flow-logic-verification.md](14-flow-logic-verification.md) — verified logic for every Activated flow named on this page
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
