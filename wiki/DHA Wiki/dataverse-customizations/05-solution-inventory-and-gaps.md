# Solution Inventory & Documentation Gaps

The DHA Planning Production environment (`dhapd`) contains **50 solutions** as of
2026-08-24 (`pac solution list`). This wiki documents them **incrementally, by domain**
— going deep on one group at a time rather than shallowly on all 50 at once. This page
is the tracking list: what's covered, what isn't yet, and where to pick up next.

Use this page to answer "has anyone looked at solution X yet?" before assuming
something is undocumented — and update it whenever a new pass covers another group.

## ✅ Documented

**Time / Disbursement / Billing** (2026-08-24) — see [03-solution-landscape.md](03-solution-landscape.md)
and [04-flows-and-business-rules.md](04-flows-and-business-rules.md):

`FWPTimeDisbursementTables`, `FWPTimeDisbursementFlows`, `FWPTimeDisbursementandBilling`,
`FWPReconciliationFix`, `FWPDisbursementRearchitect`, `FWPDisbursementMode`,
`FWPTimeOpeningBalances`, `FWPLockCustomerWhenPopulated`, `ClientLockOverride`,
`FWPProjectOperationsAppOnly`, `FWPProjectOperationsTeamMemberMods`,
`FWPTypeofWorkModification`, `FWPUpdateProjectNotificationEmails`, `FWPTimesheetReminder`

**Finance / Billing / Invoicing** (2026-08-24) — see [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md):

`FWPBillingTables`, `FWPBillingTablesv2`, `FWPDynamicsFinance`, `FWPSageIntegration`,
`FWPDHAInvoiceRibbon`, `JustInvoice`

**IG/ISC legacy layer** (2026-08-24, previous implementation partner) — see [08-ig-legacy-layer.md](08-ig-legacy-layer.md):

`IGDHACustomisations`, `IGDHAEnvVarsConRefs`, `IGDHAFlows`, `IGDHAWorkflows`,
`IGQuoteUpgrade`, `IG_2024`, `IG_870880`, `IG_AUG_Fix`, `IG_Opportunity_Enhancements`

**UI / Security / Visibility** (2026-08-24) — see [10-ui-security-visibility.md](10-ui-security-visibility.md):

`FWPDashboardVisibility`, `FWPDashboardVisibilityOverride`, `FWPHideSubgridButtons`,
`FWPFormAdjustments`

**Integrations / misc** (2026-08-25) — see [12-integrations-and-misc.md](12-integrations-and-misc.md):

`FWPDocusignIntegrationPt1`, `FWPWebResources`, `FWPEnvironmentVariables`,
`FWPLandCRM`, `FWPContactCleanse` (partially — see note in that page), `AzureMaps`,
`BingsMapFix` (turned out to be IG/ISC legacy, not FWP — see that page)

Plus the plugin/flow migration (`SetTimeEntryValuePlugin`) and the DHA-1→6 security
roles, from earlier passes — see [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md)
and [02-security-roles.md](02-security-roles.md).

## ⏳ Not yet documented

### Other / vendor / platform

| Solution | Friendly name | Version |
|---|---|---|
| `CoreLeaveRequest` | Core - Leave Request App | 1.2.2.7 |
| `OrganizationSettingsEditor` | Organization Settings Editor (Dynamics 365) | 1.0.2011.1201 |
| `OpeningBalanceImport` | Opening Balance Import | 1.0.0.0 (unmanaged) |
| `ProjectChangeLog` | Project Change Log | 1.0.0.3 |
| `TEMPTimeExporter` | TEMP: Time Exporter | 1.0.0.0 (unmanaged) |
| `FileClearance` | File Clearance | 1.0.0.0 |
| `FileStorageSaver` | File Storage Saver | 1.0.0.0 |
| `DynamicsMKT_SendOptimization` | Dynamics Marketing Send Optimization | 1.1.61220.60 |
| `EnvironmentVariableTester` | Environment Variable Tester | 1.0.0.0 (unmanaged) |
| `Cr7ff2c` | Common Data Services Default Solution | 1.0.0.0 (unmanaged) |
| `Default` | Default Solution | 1.0 (unmanaged) |

`OrganizationSettingsEditor` and `DynamicsMKT_SendOptimization` are recognisable
Microsoft/ISV-provided solutions, not DHA-authored — low priority for this wiki.
`TEMPTimeExporter`, `OpeningBalanceImport`, `EnvironmentVariableTester` sound like
one-off migration/testing tools — worth a quick check for whether they're still needed
before deciding they need documentation at all.

## How to pick up the next pass

1. Only "Other / vendor / platform" remains. Most of it is low priority — recognisable
   Microsoft/ISV solutions (`OrganizationSettingsEditor`, `DynamicsMKT_SendOptimization`)
   or one-off migration/testing tools (`TEMPTimeExporter`, `OpeningBalanceImport`,
   `EnvironmentVariableTester`) that may not be worth a full write-up. `CoreLeaveRequest`,
   `ProjectChangeLog`, `FileClearance`, `FileStorageSaver` are the more plausible
   DHA-specific candidates if this group is picked up.
2. Also worth closing out from prior passes rather than starting a new solution group:
   the `fieldpermission` lookup on the two legacy Field Security Profiles (now relevant
   to *two* current-team solutions, not just the George bug —
   [08-ig-legacy-layer.md](08-ig-legacy-layer.md#field-security-profiles--likely-mechanism-behind-the-george-bug)),
   and pulling the actual JS source of `fwp_isc_projectamount_visibility`
   ([10-ui-security-visibility.md](10-ui-security-visibility.md)) and
   `fwp_updateValueSinceLastInvoice` ([12-integrations-and-misc.md](12-integrations-and-misc.md)).
3. Re-run the same method: `pac env fetch` against `solutioncomponent` filtered to the
   target solutions' `uniquename`s (see the query in
   [03-solution-landscape.md](03-solution-landscape.md#how-this-was-produced)), then
   resolve each component type against its own table.
4. Move the covered solutions from "Not yet documented" to "Documented" on this page,
   and link the new wiki page(s) in from [README.md](../README.md) and
   [00-overview.md](00-overview.md).

## See also

- [00-overview.md](00-overview.md)
- [03-solution-landscape.md](03-solution-landscape.md)
- [04-flows-and-business-rules.md](04-flows-and-business-rules.md)
- [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md)
- [08-ig-legacy-layer.md](08-ig-legacy-layer.md)
- [10-ui-security-visibility.md](10-ui-security-visibility.md)
- [12-integrations-and-misc.md](12-integrations-and-misc.md)
