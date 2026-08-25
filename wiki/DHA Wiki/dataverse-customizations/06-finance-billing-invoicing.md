# Finance, Billing & Invoicing

*Reconstructed from DHA Planning Production on 2026-08-24 via `pac env fetch`, same
method as [03-solution-landscape.md](03-solution-landscape.md#how-this-was-produced).
This is the second incremental pass — see
[05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md) for what's still
outstanding after this one.*

This page covers the 6 solutions in the "Finance / Billing / Invoicing" group:
`FWPBillingTables`, `FWPBillingTablesv2`, `FWPDynamicsFinance`, `FWPSageIntegration`,
`FWPDHAInvoiceRibbon`, `JustInvoice`. Compared to the Time/Disbursement/Billing group,
this batch is much smaller (107 solutioncomponent rows vs. 720) — these solutions are
narrower, incremental add-ons rather than the main bulk of the customisation.

## The 6 solutions

| Solution | Version | What it actually contains |
|---|---|---|
| `FWPBillingTables` | 1.0.0.1 | Entity ownership for the core billing-adjacent tables: `fwp_dhainvoice`, `fwp_dhainvoiceflow`, `fwp_disbursement`, `fwp_disbursementcategory`, `fwp_disbursementreconciliation`, `fwp_billingrate`, `fwp_colleaguehours`, `fwp_colleagueconfiguration`, `fwp_timeentry` — plus one workflow (the shared "DHA Invoice Flow" BPF) and a web resource |
| `FWPBillingTablesv2` | 1.0.0.4 | A smaller, later increment on the same table set: `fwp_timeentry`, `fwp_timereconciliation`, `fwp_dhainvoice`, plus the same "DHA Invoice Flow" BPF again |
| `FWPDynamicsFinance` | 1.0.0.4 | Backs the **Dynamics Finance for DHA** model-driven app: its sitemap, several Quote-related saved queries/views/forms, the `opportunity` and `fwp_dhainvoice` entities (partially), one business rule, one flow, and the `ProjectService_DefaultCommandLibrary` canvas app (an auto-generated command-bar app, not bespoke) |
| `FWPSageIntegration` | 1.0.0.48 | The **actual Sage sync logic** — see below. By far the most mature/iterated solution in this group (version 1.0.0.48 vs. everything else at 1.0.0.1–1.0.0.4) |
| `FWPDHAInvoiceRibbon` | 1.0.0.2 | Contributed only a single "Include As Shell Only" reference to `fwp_dhainvoice` in this query — ribbon/command-bar customisations on an entity's form don't generate their own `solutioncomponent` rows the way fields or flows do, so this solution's actual content (a ribbon customisation on the DHA Invoice form, per its name) isn't independently visible via this method. Would need the maker portal's ribbon workbench, or a solution-diff approach, to see what it actually changed |
| `JustInvoice` | 1.0.0.1 | Also tiny — one entity reference (`fwp_dhainvoice`, Include Subcomponents). Likely a narrow, single-purpose customisation; not investigated further |

## The Sage integration, concretely

`FWPSageIntegration` (v1.0.0.48) is confirmed as the solution containing the real Sage
sync flows referenced by the environment variables in
[03-solution-landscape.md](03-solution-landscape.md#sage-integration-via-environment-variables).
Its flows:

| Flow | State | Purpose (verified 2026-08-25 — see [14-flow-logic-verification.md](14-flow-logic-verification.md#sage-sync)) |
|---|---|---|
| `fwp_auto_copyClientToSage` | **Activated** | Triggers on `account` create/update. POSTs to `dha.hypersage.co.uk:50027/api/customer/` with a **hardcoded** URL and auth token (not via the environment variables), writes the returned ledger code to `account.isc_ledgercode` |
| `fwp_auto_publishInvoice_v2` | **Activated** | Triggers on the invoice BPF reaching a "ready to publish" stage. Builds a full Sage sales-invoice payload (incl. one line per disbursement), POSTs to the same hardcoded endpoint, emails the customer a PDF on success. Confirmed as the live replacement |
| `fwp_auto_publishInvoice` | **Draft** | Superseded by `_v2`. (A same-named Draft flow also exists in `FWPTimeDisbursementFlows`/`FWPTimeDisbursementandBilling` — see [04-flows-and-business-rules.md](04-flows-and-business-rules.md#known-duplicates--things-worth-a-cleanup-pass) — so there are now **three** inactive copies of "fwp_auto_publishInvoice" across three solutions, all superseded by this one `_v2` flow) |
| `fwp_sched_getPurchaseOrders` | **Draft** | Currently off. Its confirmed live counterpart, `fwp_sched_callSagePOSync` (see below), runs hourly regardless — the naming similarity between the two is a documentation trap, they're not the same flow |

**Correction**: neither `fwp_auto_copyClientToSage` nor `fwp_auto_publishInvoice_v2`
reference `fwp_SageAPIServer_Planning/Test`, `fwp_PlanningAuthKey`/`fwp_TestAuthKey`, or
`fwp_BillToProduction` — the URL and auth token are hardcoded directly in each flow's
definition. See the correction in
[03-solution-landscape.md](03-solution-landscape.md#sage-integration-via-environment-variables)
— this means the plaintext Sage key exists in at least three places, not one, and the
documented prod/test switch (`fwp_BillToProduction`) doesn't appear to control anything
in the flows verified so far.

**Also confirmed**: `fwp_sched_callSagePOSync` (Activated, hourly recurrence — not
found in the original component scan for this page but resolved during the flow
verification pass) is a thin dispatcher to a **third** Azure Function
(`dhainvoicecreator.azurewebsites.net`), separate from the `dha.hypersage.co.uk` direct
calls above — DHA's Sage integration spans multiple independent backends, not one. See
[14-flow-logic-verification.md](14-flow-logic-verification.md#sage-sync).

Also customises `msdyn_taxcode` and `msdyn_taxcodedetail` (standard Project Operations
tax tables) — consistent with tax codes needing to round-trip to Sage correctly.

**New environment variable found**: `fwp_FWPLastPOCheck` = `30/06/2025 00:00:00` (String)
— a timestamp presumably meant to track the last purchase-order sync. Since
`fwp_sched_getPurchaseOrders` (the flow that would presumably update it) is Draft/off,
**this timestamp is almost 2 months stale as of this wiki page's date** and will keep
falling further behind until that flow is either reactivated or this mechanism is
retired deliberately.

## Duplicate proliferation, continued

The pattern already flagged in
[04-flows-and-business-rules.md](04-flows-and-business-rules.md#known-duplicates--things-worth-a-cleanup-pass)
extends into this group:

- **A business rule literally named "FWP: BR: Disbursement Classification" exists in
  both `FWPTimeDisbursementandBilling` (from the first pass) and `FWPDynamicsFinance`**
  — same name, different solution, not confirmed whether it's the same underlying record
  referenced twice or two separate records. Worth a direct check in the maker portal.
- **`fwp_auto_publishInvoice` now has three known inactive copies** spread across
  `FWPTimeDisbursementFlows`, `FWPTimeDisbursementandBilling`, and `FWPSageIntegration`,
  all apparently superseded by the Activated `fwp_auto_publishInvoice_v2` in
  `FWPSageIntegration`. None of the old copies appear to have been deleted after the
  `_v2` rebuild.

This reinforces the same conclusion as the first pass: whoever maintains these solutions
tends to leave the old copy behind when building a replacement, rather than deleting it.
Worth a dedicated cleanup pass at some point — not urgent, but it's the kind of debt that
makes "which one is live" an ongoing question for every new developer.

## What's still not answered here

- What `FWPDHAInvoiceRibbon` actually changes (see note above — needs the ribbon
  workbench, not FetchXML).
- What `JustInvoice` actually does beyond referencing `fwp_dhainvoice`.
- What `fwp_feeextension` (found in the first pass, `FWPTimeDisbursementandBilling`) is
  for — still unresolved.
- The Azure Function behind Sage/reconciliation isn't source-controlled anywhere visible
  from Dataverse — same caveat as the reconciliation balancer in
  [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md).

## See also

- [00-overview.md](00-overview.md)
- [03-solution-landscape.md](03-solution-landscape.md)
- [04-flows-and-business-rules.md](04-flows-and-business-rules.md)
- [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md)
- [14-flow-logic-verification.md](14-flow-logic-verification.md) — verified logic for the Sage flows above
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
