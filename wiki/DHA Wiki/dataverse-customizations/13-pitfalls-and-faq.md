# Pitfalls & FAQ — Dataverse Customisations

---

**"Time entries suddenly all have £0 value / no value at all, across many people."**

- **Cause**: this is the known failure mode of the cloud flow `fwp_setTimeEntryValue_2`
  — Microsoft has repeatedly auto-disabled it. If the plug-in migration
  (`SetTimeEntryValuePlugin`) hasn't been cut over in this environment yet, the flow is
  still the thing computing `fwp_value`, and it can silently stop running. Confirmed
  live in production as of 2026-08-24 (Activated) — see
  [04-flows-and-business-rules.md](04-flows-and-business-rules.md#time-entry-value-calculation--confirmed-live-status).
- **Fix**: check whether the flow is turned on in the affected environment (dev vs
  prod — see [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md)).
  A "Watchdog" detection process is referenced in the plug-in's deployment notes as
  already covering this (flagging value-less entries) — check whether it fired.

---

**"We're about to cut the plug-in over to production — what's the checklist?"**

Updated 2026-08-25, now that the actual flow logic has been read:

1. ~~Confirm whether `fwp_calculatedreconciledvalue` and `fwp_reconciledvaluedirect` are
   the same field.~~ **Resolved**: the legacy flow and the plug-in both guard on
   `fwp_calculatedreconciledvalue` — they match each other. The plug-in doesn't make
   anything worse here than the flow it replaces. See
   [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md#resolved-the-plug-ins-reconciliation-field-matches-the-flow-it-replaces).
2. **`fwp_auto_setTimeValueWhenAddedManually` is now confirmed as a real, active
   problem, not just a suspected one.** It's a second writer of `fwp_value` that also
   silently forces every new time entry to Rate A, racing against
   `fwp_setTimeEntryValue_2` on every Create. **This flow must be turned off (or fixed)
   as part of the cutover** — see
   [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md#open-risk-fwp_auto_settimevaluewhenaddedmanually--confirmed-second-writer-confirmed-race-condition).
   This is now the single most important item on this checklist.

---

**"A Rate B (Expert Witness) or Rate C time entry shows a Rate A value instead of the band I selected."**

- **Cause, confirmed 2026-08-25**: `fwp_auto_setTimeValueWhenAddedManually` fires on
  every new time entry and unconditionally forces `fwp_chargerate` back to Rate A plus
  recalculates `fwp_value` at the Rate A rate — with no awareness of what band the user
  actually picked. It races against `fwp_setTimeEntryValue_2` (which does respect the
  selected band); whichever flow happens to run last on a given record wins. See
  [14-flow-logic-verification.md](14-flow-logic-verification.md#time-entry-value-calculation).
- **Fix**: not something the desktop app can work around — this needs the flow itself
  investigated/disabled. Flag any suspected instances (an entry that should be Band B/C
  but shows a Rate A value) for whoever owns this flow.

---

**"An invoice's approval-stage reconciliation looks wrong / like it used different data than expected."**

- **Cause**: `fwp_auto_reconcileInvoiceOnRequestForApproval` hardcodes
  `"environment": "dev"` in its call to the reconciliation Azure Function, despite being
  an Activated flow in the Production environment. If the Function branches on that
  value, approval-stage reconciliation may be silently running against dev
  data/config even for real production invoices. See
  [14-flow-logic-verification.md](14-flow-logic-verification.md#invoicing).
- **Fix**: needs confirming with whoever owns `dhadataverseeditor...azurewebsites.net`
  — this looks like a genuine bug, not intentional behaviour.

---

**"Timesheet reminder emails look inconsistent, or a 'send reminder' flow doesn't seem to do anything when run."**

- **Cause**: there are two unrelated reminder mechanisms. `fwp_sched_timesheetReminder`
  is the real, live one — a daily job with an escalating Mon/Tue/Wed sequence
  (CC'ing the manager from Tuesday). `FWP: Send Time Reminder Email` is a separate,
  Activated-but-apparently-dead flow with unwired placeholder content that nothing
  currently calls. See
  [14-flow-logic-verification.md](14-flow-logic-verification.md#reminders--configuration).
- **Fix**: if investigating reminder behaviour, look at `fwp_sched_timesheetReminder`
  first — the other flow being Activated doesn't mean it's doing anything.

---

**"There are duplicate `fwp_colleagueconfiguration` records for some colleagues."**

- **Cause**: `fwp_inst_pushAllUserConfigurations` creates a brand-new configuration
  record for every eligible colleague with **no check for an existing one** — it's a
  plain Create, not an upsert. If it's been run more than once against the same user
  base, duplicates are the expected result. See
  [14-flow-logic-verification.md](14-flow-logic-verification.md#reminders--configuration).
- **Fix**: don't re-run this flow as a "refresh" — it's a one-time seeding tool. Clean
  up duplicates manually (keep whichever record has the colleague's real configured
  rates, not the hardcoded default £200 Rate A one this flow creates).

---

**"Which external Azure Function actually handles X?"**

- DHA's integrations span **at least four separate Azure Function Apps**, easily
  confused for one another: `dha-reconciliation-balancer` (time reconciliation),
  `dhainvoicecreator` (PDF generation, Sage PO sync trigger), `dhadataverseeditor...`
  (quote customer/closure operations, invoice-approval reconciliation), and
  `dhasageinterface` (Sage client find-or-create). None are visible from Dataverse —
  see [14-flow-logic-verification.md](14-flow-logic-verification.md#headline-findings)
  for which flow calls which.

---

**"Reconciliation values look stale / haven't updated even though I changed a time entry."**

- **Cause**: the flow that would automatically reconcile on every change
  (`FWP: Call Reconciliation balancer on change to time reconciliation`) is currently
  **Draft (off)**. Only the manual/on-demand flow (`FWP: Set Unreconciled Value On
  Demand`) is Activated. Reconciliation via the Azure Function currently only runs when
  someone manually triggers it.
- **Fix**: run the on-demand flow, or ask whoever owns the reconciliation process to run
  it. If reconciliation is meant to be automatic, the Draft flow needs activating — but
  confirm with the business first, since something may have led to it being turned off
  deliberately.

---

**"The Client/Customer field on an Opportunity/Project/Quote can be changed even though it's already set — I thought that was locked once populated."**

- **Cause**: all three "Lock ... when populated" business rules
  (`FWPLockCustomerWhenPopulated` solution) are currently **Draft (off)**. The lock isn't
  being enforced today, regardless of what the solution's name implies.
- **Fix**: not a bug to silently "fix" — confirm with the business whether the lock
  should be live before activating these rules; something may have led to them being
  turned off.

---

**"Are the Sage API keys visible to people who shouldn't see them?"**

- **Yes, currently.** `fwp_PlanningAuthKey` and `fwp_TestAuthKey` (environment variables
  in `FWPTimeDisbursementandBilling`) are stored as plain **String**-type values, not as
  Dataverse's **Secret** type — meaning anyone with read access to that solution in the
  maker portal can see both Sage API keys in plaintext, including the production one.
  See [03-solution-landscape.md](03-solution-landscape.md#sage-integration-via-environment-variables).
- **Fix**: migrate both to Secret-type environment variables (Dataverse stores these in
  Key Vault and doesn't expose the value to solution viewers). This is a genuine security
  finding, not just a documentation gap — raise it with whoever owns the Sage
  integration.

---

**"An invoice number looks wrong / numbers got skipped or duplicated."**

- **Cause**: invoice/credit-note numbering is driven by two environment variables,
  `fwp_LastInvoiceNumber` and `fwp_LastCreditNumber`, which some process increments.
  Editing these directly (e.g. "just bump it back") will cause duplicate or skipped
  numbers if done without understanding what increments them and when.
- **Fix**: treat these as read-only outside of the invoicing flow itself; investigate
  which flow increments them (see [04-flows-and-business-rules.md](04-flows-and-business-rules.md#invoicing--sage-sync-flows))
  before touching the value.

---

**"Which flow actually publishes invoices to Sage — I found several with similar names."**

- **Cause**: `fwp_auto_publishInvoice_v2` (in `FWPSageIntegration`) is the live,
  Activated one. There are **three** older, inactive copies of `fwp_auto_publishInvoice`
  (no `_v2`) spread across `FWPTimeDisbursementFlows`, `FWPTimeDisbursementandBilling`,
  and `FWPSageIntegration` itself — all superseded, none deleted. See
  [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md#duplicate-proliferation-continued).
- **Fix**: always check `statecode` before assuming a flow with a plausible name is the
  one actually running. When in doubt, `_v2`/latest-looking name + Activated wins.

---

**"Purchase order sync looks stale / hasn't run in ages."**

- **Cause**: `fwp_sched_getPurchaseOrders` (in `FWPSageIntegration`) is **Draft (off)**.
  The `fwp_FWPLastPOCheck` environment variable it presumably updates is not advancing
  as a result. See [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md#the-sage-integration-concretely).
- **Fix**: confirm with the business whether PO sync is meant to be live; if so, the
  flow needs reactivating (and probably investigating why it was turned off first).

---

**"A user can see/do something DHA-1→6 doesn't seem to explain."**

- **Cause**: the environment still has **10 legacy roles from the previous
  implementation partner** ("IG"/Infinity Software Consulting) — `DHA - Project
  Manager`, `DHA - Team Project Manager`, `DHA - Admin`, `DHA - Management KPIs`,
  `View Append Projects`, `DHA - Copy of Account Manager`, `DHA - Land Dashboard`,
  `DHA - Managing Director`, `DHA - Copy of Project Manager`, `DHA - Finance`. These
  predate and are structurally unrelated to DHA-1→6, but they're still live role
  definitions that can still be assigned. See
  [08-ig-legacy-layer.md](08-ig-legacy-layer.md#the-legacy-security-model-dha---role-not-dha-1→6).
- **Fix**: check the user's full role assignment list, not just their DHA-1→6 levels —
  effective permissions are the union of everything they hold.

---

**"Time/billing data looks like it's not syncing anywhere outside Dynamics — is there another system involved?"**

- **Likely legacy, not current**: an earlier integration synced Quote/Contact/Client/
  Project data to an external system called **Coretime** (`IGDHAEnvVarsConRefs`,
  `IGDHAFlows`) — this appears to have been superseded by the native FWP time/
  disbursement/billing tables, and one of its flows is now Draft. If something still
  expects Coretime data, that's worth raising — it may be a genuine loose end from the
  migration rather than intentional. See
  [08-ig-legacy-layer.md](08-ig-legacy-layer.md#the-coretime-bridge--superseded-by-the-fwp-layer).

---

**"A fee/project amount is hidden on a dashboard when I'd expect to see it (or vice versa)."**

- **Cause**: `FWPDashboardVisibility`/`FWPDashboardVisibilityOverride` install a JS web
  resource, `fwp_isc_projectamount_visibility` ("FWP: DHA Fee Visibility Script"), that
  controls this client-side. The name deliberately continues the legacy `isc_`
  visibility-field convention (see the George bug and the IG-era Field Security
  Profiles) — likely gated on the same kind of "has this passed client approval" logic.
  See [10-ui-security-visibility.md](10-ui-security-visibility.md#the-feeamount-visibility-script--connects-to-the-george-bug).
- **Fix**: not resolved in this pass — the script's actual source wasn't read. If this
  becomes a live ticket, that's the next thing to pull.

---

**"Time entries/disbursements can't be created directly on a Project/Quote form, only through the app."**

- **Likely cause**: `FWPHideSubgridButtons` appears to hide the create/delete buttons on
  `fwp_timeentry`/`fwp_disbursement` subgrids somewhere in the model-driven app — by
  design, if the intent is to force all time/disbursement entry through the WPF client.
  Not confirmed without the ribbon workbench. See
  [10-ui-security-visibility.md](10-ui-security-visibility.md#form-adjustments-and-hide-subgrid-buttons).
- **Fix**: if this is unexpected, check the ribbon customisation on the relevant
  form/subgrid directly in the maker portal.

---

**"Checking an environment variable's value in the maker portal and getting a different answer than expected."**

- **Cause**: Dataverse environment variables have two layers — a Definition (with a
  `defaultvalue`) and an optional separate `environmentvariablevalue` override record.
  The override, when present, always wins. `fwp_dv_env` looked "unconfigured" until this
  was checked properly — its real value (`"prod"`) only exists as an override. See
  [12-integrations-and-misc.md](12-integrations-and-misc.md#environment-variables--a-correction-and-a-reinforcement).
- **Fix**: always check `environmentvariablevalue` for the actual live value, not just
  the Definition's default.

---

**"Site location mapping looks inconsistent, or a map control seems to be missing/duplicated."**

- **Cause**: a migration from Bing Maps (`BingsMapFix`, legacy IG/ISC) to Azure Maps
  (`AzureMaps`, current team, created 2026-02-03 — very recently) is in progress, built
  on the same underlying `isc_sitelocation` table. The related "Weekly New Site
  Locations" flow is currently Draft/off. See
  [12-integrations-and-misc.md](12-integrations-and-misc.md#bing-maps--azure-maps-a-migration-in-progress).
- **Fix**: check whether both the old and new map controls are active on the same form,
  and whether the new flow needs activating.

---

**"Someone was given DHA-4 but not DHA-1/2/3, and things are broken/missing for them."**

- **Cause**: roles are cumulative, not tiered independently. DHA-4 alone doesn't imply
  the access DHA-1–3 would normally provide underneath it.
- **Fix**: assign the full ladder (DHA-1 through DHA-4). See
  [02-security-roles.md](02-security-roles.md).

---

**"I changed the DHA-1 role definition to fix one user's access, and now it's affected everyone."**

- **Cause**: role **definitions** are shared — editing one changes it for every user
  holding that role.
- **Fix**: for a one-off need, assign an additional role to that specific user instead of
  editing a shared role definition. Escalate genuine definition changes rather than
  making them ad hoc for a single ticket.

---

**"Has anyone documented [some other solution/flow/app] yet?"**

- Check [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md) first —
  it tracks what's been covered so far (Time/Disbursement/Billing, then
  Finance/Billing/Invoicing) and what's still outstanding (the "IG"
  implementation-partner solutions, UI/security/visibility, integrations/misc, and
  vendor/platform solutions), so this doesn't get re-investigated from scratch by
  someone who doesn't know a prior pass already looked.
