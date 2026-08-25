# UI, Security & Visibility

*Reconstructed from DHA Planning Production on 2026-08-24 via `pac env fetch`, same
method as [03-solution-landscape.md](03-solution-landscape.md#how-this-was-produced).
Fourth incremental pass — see [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md).*

This page covers the 4 solutions in the "UI / Security / Visibility" group:
`FWPDashboardVisibility`, `FWPDashboardVisibilityOverride`, `FWPHideSubgridButtons`,
`FWPFormAdjustments`. All four are **current work**, not legacy — every one was created
in 2025, and all publish under Select Technology (see below), unlike the IG/ISC layer
in [08-ig-legacy-layer.md](08-ig-legacy-layer.md).

## The 4 solutions

| Solution | Version | Created | Publisher | What it actually contains |
|---|---|---|---|---|
| `FWPDashboardVisibility` | 1.0.0.3 | 2025-10-24 07:55 | SelectTechnology | One JS web resource controlling fee/amount visibility (see below) |
| `FWPDashboardVisibilityOverride` | 1.0.0.0 | 2025-10-24 08:16 (**21 minutes after the base solution**) | SelectTechnology | References the same JS web resource as `FWPDashboardVisibility` — reads as an immediate follow-up/patch to it, not an independent feature |
| `FWPHideSubgridButtons` | 1.0.0.2 | 2025-08-01 | SelectTechnology | Only shell-only references to `fwp_timeentry`/`fwp_disbursement` visible via this method — the actual change is almost certainly a ribbon/command-bar customisation on those subgrids (hiding the +New/Delete buttons), which — like `FWPDHAInvoiceRibbon` in [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md) — doesn't produce its own `solutioncomponent` rows |
| `FWPFormAdjustments` | 1.0.0.1 | 2025-05-08 | **Select Technology Future Workplace** (a *different* publisher record, unique name `sts_future_workplace`, from the other three's `SelectTechnology`) | Form/dashboard/quick-create adjustments on Opportunity and Account, plus a batch of standard Opportunity/Client views pulled in alongside |

**Publisher note**: three of these four solutions publish as `SelectTechnology`, but
`FWPFormAdjustments` publishes as `Select Technology Future Workplace`
(`sts_future_workplace`) — two different publisher records for what's presumably the
same team. Not a problem, just an inconsistency worth knowing about if you're ever
setting up a new solution and wondering which publisher to use.

## The fee/amount visibility script — connects to the George bug

`FWPDashboardVisibility` and `FWPDashboardVisibilityOverride` both reference the exact
same JS web resource: **`fwp_isc_projectamount_visibility`**, display name **"FWP: DHA
Fee Visibility Script"**. The name is notable for mixing the *current* team's `fwp_`
prefix with the *legacy* `isc_` naming convention in one identifier — this isn't an
accident of naming, it's evidence that whoever wrote it deliberately continued the
`isc_` visibility-field convention rather than replacing it.

This directly connects to two things already documented elsewhere in this wiki:

- The **George quote-visibility bug**
  ([time-management/04-pitfalls-and-faq.md](../time-management/04-pitfalls-and-faq.md))
  centred on a field called `isc_projectnumbervisible`.
- The **legacy Field Security Profiles** from the IG/ISC layer —
  `DHA - Client Approver` / `DHA - Non-Client Approver`
  ([08-ig-legacy-layer.md](08-ig-legacy-layer.md#field-security-profiles--likely-mechanism-behind-the-george-bug))
  — which look like exactly the kind of construct that would gate a field named
  `isc_projectamountvisible` or similar the same way.

**Working theory**: there's a small family of `isc_*visible` boolean fields (at least
`isc_projectnumbervisible`, and presumably one behind this fee-visibility script) that
control whether client-facing amounts/numbers are shown, likely tied to whether a
quote/project has passed a client-approval stage. The current FWP team appears to be
actively building *more* visibility logic (this JS web resource) on top of that same
convention, via client-side scripting on the dashboard rather than field-level security
on the form. **This script itself wasn't read in this pass** (it's a JS file, not a
Dataverse record with inspectable metadata via FetchXML) — pulling its actual source
would confirm the exact field name and logic, and is the natural next step for closing
out the George bug thread for good.

## Form Adjustments and Hide Subgrid Buttons

`FWPFormAdjustments` touches a cluster of Opportunity and Account ("Client") forms and
dashboards — Information, AI for Sales, Project Information, Opportunity, In Context
Form, and the Opportunities Dashboard on Opportunity; Account Quick Create, Accounts
Dashboard, Account for Multisession experience, Information, and Account on the Client
side — plus a full set of standard Opportunity/Client views (Won/Lost/Open/Closed/All
Opportunities, Active/Inactive Clients). The views are very likely incidental —
pulled in because whoever built the solution added the entity broadly rather than
picking specific forms — rather than deliberately customised themselves.

`FWPHideSubgridButtons` targets `fwp_timeentry` and `fwp_disbursement` specifically —
consistent with its name, this is almost certainly hiding the create/delete buttons on
time entry and disbursement subgrids somewhere (likely on the Project or Quote form),
so that time/disbursement records can only be created through the WPF client rather
than directly in the model-driven app. Not confirmed without opening the ribbon
workbench directly.

## What's still not answered here

- The actual logic/field name inside `fwp_isc_projectamount_visibility` — needs the web
  resource's JS source, not just its metadata.
- Which form(s)/ribbon `FWPHideSubgridButtons` actually modifies — needs the ribbon
  workbench, same limitation as `FWPDHAInvoiceRibbon`.
- Why `FWPFormAdjustments` uses a different publisher from the other three — likely
  just historical, not investigated further.

## See also

- [00-overview.md](00-overview.md)
- [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md) — the other place a ribbon customisation was found invisible to this method
- [08-ig-legacy-layer.md](08-ig-legacy-layer.md) — the legacy Field Security Profiles this page's visibility script likely parallels
- [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md)
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
- [time-management/04-pitfalls-and-faq.md](../time-management/04-pitfalls-and-faq.md) — the George bug this page's finding connects to
