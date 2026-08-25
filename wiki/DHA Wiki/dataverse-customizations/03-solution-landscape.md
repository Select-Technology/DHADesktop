# Solution Landscape: Time, Disbursement & Billing

*Reconstructed from the live DHA Planning Production environment (`dhapd`) on 2026-08-24
via `pac env fetch` (FetchXML) queries against `solutioncomponent`, `workflow`,
`canvasapp`, `environmentvariabledefinition`, `systemform`, and entity metadata — not
from an exported solution file, since these solutions are installed as **managed** and
`pac solution export`/`clone` refuse to export a managed solution. If this page and the
live environment ever disagree, the environment is the source of truth; re-run the
queries (see [dataverse-customizations/pac-queries](#how-this-was-produced) below) to
refresh it.*

This page covers the **14 solutions directly tied to Time, Disbursement & Billing** (the
domain this wiki already documents from the WPF client side). The other ~36 solutions in
the environment (Sage/finance beyond this scope, the "IG" implementation-partner
solutions, misc/vendor apps) are **not yet covered** — see
[05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md) for the full list
and what's still outstanding.

## The architecture, in one paragraph

DHA's environment is **Dynamics 365 Project Operations** (`msdyn_project`,
`msdyn_purchaseorder`, `msdyn_taxcode` — Microsoft's standard project/billing tables) with
a large bespoke layer built on top: DHA's own `fwp_`-prefixed tables covering time entries,
disbursements, reconciliation, and invoicing, synced to an external **Sage** accounting
system. A separate `isc_`-prefixed table set (site location / sector / subsector / local
authority) looks like a construction- or public-sector-oriented ISV add-on layered in
alongside. The Opportunity → Quote → Project pipeline is standard Project Operations;
DHA's customisation concentrates on what happens once a Project exists: time and
disbursements are logged (via the WPF client — see [wpf-client/](../wpf-client/00-overview.md)),
reconciled, and turned into DHA-specific invoices that sync to Sage.

## The 14 solutions

| Solution | Version | What it appears to own (component-based inference — no solution descriptions exist; see note below) |
|---|---|---|
| `FWPTimeDisbursementTables` | 1.0.1.63 | Core schema: `fwp_timeentry`, `fwp_disbursement`, plus Quote/Contact forms, several business rules on DHA Invoice, the "DHA Invoice Flow" business process flow |
| `FWPTimeDisbursementFlows` | 1.0.0.45 | The bulk of the Power Automate flows — time value calculation, invoice generation, Sage sync, timesheet reminders |
| `FWPTimeDisbursementandBilling` | 1.0.0.18 | The largest solution by far: billing tables (`fwp_billingrate`, `fwp_bankaccount`, `fwp_feeextension`), invoice flows, the `isc_*` sector tables, both custom Canvas Apps, Field Security Profiles |
| `FWPReconciliationFix` | 1.0.0.5 | Two flows that dispatch to an external Azure Function — see [04-flows-and-business-rules.md](04-flows-and-business-rules.md#reconciliation-the-real-mechanism) |
| `FWPDisbursementRearchitect` | 1.0.0.4 | Disbursement-related form/attribute changes, "DHA Invoice Flow" BPF, `fwp_dhainvoiceflow` |
| `FWPDisbursementMode` | 1.0.0.2 | Small — one web resource, DHA Invoice entity shell reference |
| `FWPTimeOpeningBalances` | 1.0.0.2 | One flow (`TimeOpeningBalance`) — likely a one-off/setup migration flow for opening time balances |
| `FWPLockCustomerWhenPopulated` | 1.0.0.2 | Business rules that lock the Client field once populated on Opportunity/Project/Quote, plus several Opportunity/Quote forms and views |
| `ClientLockOverride` (friendly name: "FWP: Project Operations Super User") | 1.0.0.11 | The **Project Operations Admin** canvas app, a flow that sets Quote Customer, a flow that moves SharePoint files on client change, an Azure-Function-backed quote-closure flow |
| `FWPProjectOperationsAppOnly` | 1.0.0.13 | App-only customisations (sitemap, forms) for the Project Operations model-driven app |
| `FWPProjectOperationsTeamMemberMods` | 1.0.0.1 | Small — modifications to the Project Operations Team Member app |
| `FWPTypeofWorkModification` | 1.0.0.2 | Small — Opportunity-related attribute change |
| `FWPUpdateProjectNotificationEmails` | 1.0.0.1 | One flow: "IG-Projects-Alert Users of Project Live" |
| `FWPTimesheetReminder` | 1.0.0.6 | Timesheet reminder flow, the **Dynamics Finance for DHA** model-driven app + its sitemap, colleague-hours tables |

**Note on descriptions**: none of these 14 solutions have a `description` field populated
in Dataverse — the table above is inferred from their components, not authored
documentation. If whoever maintains these solutions can add real descriptions, that
would directly improve this page's accuracy for free.

## Custom tables (`fwp_` prefix)

| Table | Purpose (inferred) |
|---|---|
| `fwp_timeentry` | Time entries — see [time-management/01-time-entries.md](../time-management/01-time-entries.md) |
| `fwp_disbursement` | Disbursements — see [time-management/02-disbursements.md](../time-management/02-disbursements.md) |
| `fwp_disbursementcategory` | Disbursement type/category reference data |
| `fwp_disbursementreconciliation` | A **separate table** recording disbursement reconciliation, related to but distinct from the `fwp_disbursement` record itself |
| `fwp_timereconciliation` | A **separate table** recording time reconciliation — this is the entity the reconciliation-dispatch flows trigger on, see [04-flows-and-business-rules.md](04-flows-and-business-rules.md#reconciliation-the-real-mechanism) |
| `fwp_dhainvoice` | DHA's own invoice entity (distinct from standard Dynamics `invoice`) |
| `fwp_dhainvoiceline` | Invoice line items |
| `fwp_dhainvoiceflow` | Appears to track/drive the invoice generation process itself |
| `fwp_invoicenumberconfig` | Invoice numbering configuration (works with the `fwp_LastInvoiceNumber`/`fwp_LastCreditNumber` environment variables below) |
| `fwp_billingrate` | Billing rate reference data |
| `fwp_bankaccount` | Bank account records (for invoicing/payment details) |
| `fwp_feeextension` | Unclear from name alone — worth investigating in a future pass |
| `fwp_additionalbillingcontacts` | Additional billing contacts on a project/account |
| `fwp_timesheetreminder` | Backing table for the timesheet reminder flow |
| `fwp_colleaguehours` | Related to but distinct from `fwp_colleagueconfiguration` (see [time-management/03-reconciliation-and-charge-bands.md](../time-management/03-reconciliation-and-charge-bands.md)) — worth reconciling which one is authoritative for what in a future pass |
| `fwp_colleagueconfiguration` | Per-colleague rate/expected-hours config — already documented from the WPF client side |

## ISV/sector extension tables (`isc_` prefix)

`isc_sitelocation`, `isc_sector`, `isc_subsector`, `isc_localauthority` — all live in
`FWPTimeDisbursementandBilling`. These look like a construction- or public-sector-focused
ISV solution (the `isc_projectnumbervisible` field from the
[George quote-visibility bug](../time-management/04-pitfalls-and-faq.md) is also an `isc_`
field, on the `quote` table — same vendor/naming family). Not investigated further in
this pass.

## Sage integration (via Environment Variables)

Several environment variables, all in `FWPTimeDisbursementandBilling`/`FWPTimeDisbursementFlows`,
directly configure a **live sync to Sage**:

| Variable | Current value | What it controls |
|---|---|---|
| `fwp_BillToProduction` | **`yes`** | Whether generated invoices go to Sage's **production** company or its **test** company. Currently routes to production |
| `fwp_SageAPIServer_Planning` | `https://dha.hypersage.co.uk:50027/` | Sage API endpoint — Planning company |
| `fwp_SageAPIServer_Test` | `https://dha.hypersage.co.uk:40027/` | Sage API endpoint — Test company |
| `fwp_PlanningAuthKey` | *(a plaintext key)* | Auth key for the production Sage endpoint |
| `fwp_TestAuthKey` | *(a plaintext key)* | Auth key for the test Sage endpoint |
| `fwp_LastInvoiceNumber` | `2500503` | Running counter driving the next invoice number |
| `fwp_LastCreditNumber` | `C250005` | Running counter driving the next credit note number |
| `fwp_creatingPDF` | `no` | Mutex flag guarding against two concurrent PDF-invoice generation runs |
| `fwp_automateInvoiceReconciliation` | `no` | Feature flag — invoice reconciliation is not currently automated |
| `fwp_LastSyncDate` | `2025-07-01T00:00:00Z` | Last Sage sync timestamp |
| `fwp_dv_env` | **`prod`** (see correction below) | Per-environment (dev/test/prod) differentiation flag |

**Correction (2026-08-25, from [12-integrations-and-misc.md](12-integrations-and-misc.md#environment-variables--a-correction-and-a-reinforcement))**:
`fwp_dv_env` was originally reported here as unconfigured — that checked only the
Definition's `defaultvalue`, which is genuinely blank. It's actually set via a separate
**`environmentvariablevalue`** override record to `"prod"`. Live values always come from
`environmentvariablevalue` when one exists; the Definition's default is only a fallback.
Query the Value table, not just the Definition, when checking what an environment
variable is actually doing.

⚠️ **Security finding**: `fwp_PlanningAuthKey` and `fwp_TestAuthKey` are stored as plain
**String**-type environment variable default values, not as the **Secret** type Dataverse
supports (which stores the value in Key Vault and doesn't expose it in plaintext to
solution viewers). Anyone with read access to these solutions in the maker portal can see
both Sage API keys in plaintext, including the **production** one. This should be raised
with whoever owns the Sage integration — migrating these to Secret-type environment
variables is a standard, low-effort fix. **Reinforced by a later pass**: both keys also
have explicit `environmentvariablevalue` override records duplicating the same plaintext
value — the secret exists in cleartext in two places, not one. See
[12-integrations-and-misc.md](12-integrations-and-misc.md#environment-variables--a-correction-and-a-reinforcement).

**Further correction (2026-08-25, from [14-flow-logic-verification.md](14-flow-logic-verification.md))**:
reading the actual Sage-calling flows shows this table is **not the full picture**.
`fwp_auto_copyClientToSage` and `fwp_auto_publishInvoice_v2` both hardcode the production
Sage URL and auth token **directly in the flow definition** — they don't reference
`fwp_SageAPIServer_Planning`, `fwp_SageAPIServer_Test`, `fwp_PlanningAuthKey`,
`fwp_TestAuthKey`, or `fwp_BillToProduction` at all. That means: (1) the plaintext secret
now exists in **at least three places** — the environment variable default, its value
override, and inside two flow definitions; and (2) `fwp_BillToProduction` does not
appear to actually control anything in the flows read so far — there's no
environment-variable-driven prod/test branching in what was verified, despite that
being the variable's documented purpose. Whether some other, unaudited flow does use
these variables is unknown. See
[14-flow-logic-verification.md](14-flow-logic-verification.md#sage-sync) for the
verified flow-by-flow detail.

⚠️ **Operational note**: `fwp_LastInvoiceNumber`/`fwp_LastCreditNumber` are exactly the
kind of environment variable that looks harmless to "just fix" but will duplicate or skip
invoice numbers if edited without understanding what process increments them. Treat as
read-only outside of the invoicing flow itself.

## Canvas Apps

Most Canvas App components found are Power Apps' auto-generated
`*_DefaultCommandLibrary` apps (created automatically whenever a grid command-bar
customisation is saved — not bespoke UI). Two are genuinely custom, standalone canvas
apps layered onto an otherwise model-driven system:

- **TimeAndDisbursementPage** (`FWPTimeDisbursementandBilling`) — created 2025-06-17
- **Project Operations Admin** (`ClientLockOverride`) — created 2025-02-14

Neither is investigated further in this pass — worth opening both in the maker portal to
understand what they're for and who uses them, since a canvas app existing outside the
WPF client and the model-driven app is a third UI surface to be aware of.

## Model-driven apps in this domain

From `pac model list` (20 model-driven apps total in the environment; only the DHA-custom
ones are relevant here):

- **Dynamics Finance for DHA** (`fwp_DynamicsFinanceforDHA`) — has its own sitemap,
  referenced by `FWPTimesheetReminder` and `FWPProjectOperationsAppOnly`
- **DHA Land CRM** (`fwp_DHALandCRM`) — separate app, not investigated in this pass
- **Time Test** (`fwp_TimeTest`) — name suggests a sandbox/testing app, not investigated

The rest are standard Microsoft apps (Project, Project Operations, Project Operations
Team Member, Sales Hub, Customer Service Hub, etc.) that ship with the platform/Project
Operations and aren't DHA customisations.

## System Forms

Form customisation is concentrated on the **Opportunity → Quote → Project →
Disbursement/Invoice** pipeline — Account, Contact, Opportunity, Quote, Project,
Disbursement, and DHA Invoice all have customised Main forms. The one distinctly
purpose-built form (not a relabelled default) is **"Invoice (Finance)"** on
`fwp_dhainvoice`, described in its own metadata as "an extended version with additional
capabilities for Finance" — likely the primary screen Finance staff use for invoices,
worth a walkthrough in a future pass.

## How this was produced

Via `pac env fetch --xmlFile <query>.xml` against the authenticated `DHAProd` profile,
querying `solutioncomponent` (joined to `solution` by `uniquename`) for the 14 solutions
above, then resolving each component type's objectid against its own table
(`workflow`, `canvasapp`, `environmentvariabledefinition`, `systemform`, and entity
metadata via the `entity` FetchXML pseudo-table). See
[04-flows-and-business-rules.md](04-flows-and-business-rules.md) for the flow/business-rule
detail and [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md) for
what's outside this pass's scope.

## See also

- [00-overview.md](00-overview.md)
- [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md)
- [04-flows-and-business-rules.md](04-flows-and-business-rules.md)
- [14-flow-logic-verification.md](14-flow-logic-verification.md) — the Sage-flow hardcoded-credentials finding above
- [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md)
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
