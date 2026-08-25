# DHA Dynamics Wiki

A living reference for **how DHA's Dynamics 365 customisations actually behave today** —
one folder per domain, written so a developer can get re-oriented in five minutes and
support can answer a "how does that work again?" question without reading code.

This repo (`DHA.DSTC.WPF`) contains three projects that together make up "DHA's Dynamics"
solution:

| Project | What it is |
|---|---|
| `DHA - Quotes into disbursements/` | The WPF desktop client ("DHA Time Management") staff use to log time entries and disbursements against Jobs/Quotes in Dataverse |
| `DHA.DSTC.Plugins/` | A Dataverse plug-in (`SetTimeEntryValuePlugin`) being introduced to replace a fragile Power Automate cloud flow |
| `DHA.TimeManagement.Installer/` | The WiX-based MSI installer for the desktop client |

The environment itself is a **Dynamics 365 Project Operations** deployment (custom table
prefix `fwp_`) — dev at `dhapd-dev.crm11.dynamics.com`, production at
`dhapd.crm11.dynamics.com` ("DHA Planning Production"). It contains **50 Dataverse
solutions**, not just the plug-in/flow pair this repo directly contains — most of the
customisation lives only in the environment itself. Where a page describes something
reconstructed from the client code or the maintainers' own notes, it says so; where it
describes something pulled directly from the live environment (via `pac env fetch`
against Dataverse), it's dated, since environments drift and a snapshot is not a
guarantee of current state.

## What belongs here vs. other docs in this repo

| | `wiki/` (here) | `docs/`, root-level `*.md` files |
|---|---|---|
| Content | Current behaviour: lifecycles, rules, gotchas | Point-in-time investigation notes (e.g. `DiagnoseForGeorge.md`, `DEBUGGING-IMPLEMENTATION-SUMMARY.md`), deployment/registration steps (`DEV-SETUP.md`), functional requirements |
| Shelf life | Kept in sync with the code, always | Frozen the day the investigation/feature shipped |
| Audience | Support doing first-line triage, developers, new hires | Whoever is debugging that specific incident or setting up that specific deployment |

If you're writing up "here's what I found debugging George's issue," it stays as its own
file. If you're describing "this is how quote visibility actually works right now" for
the next person who hits a similar symptom, that belongs here — and this wiki links out
to the investigation notes where they're the source of truth for a finding.

## Structure

One folder per domain. Inside each folder:

- **`00-overview.md`** — plain language, no class/service names. What the domain is, the
  end-to-end journey, key terms explained, and links to the deeper docs.
- **Numbered topic docs** (`01-...`, `02-...`) — the actual mechanics: lifecycles,
  validation rules, the things a plausible change would get wrong.
- **`NN-pitfalls-and-faq.md`** (last file in the folder) — symptom → cause → fix. The
  fastest lookup for support.

## Domains documented so far

| Folder | Covers |
|---|---|
| [time-management/](time-management/00-overview.md) | The WPF client's core domain: time entries and disbursements against Jobs/Quotes, the daily-hours and single-entry limits, reconciliation locking, and charge bands (Rate A/B/C) |
| [dataverse-customizations/](dataverse-customizations/00-overview.md) | What actually runs server-side: the `fwp_value` calculation (legacy cloud flow being replaced by a Dataverse plug-in — **its real logic, and a confirmed data-corruption race condition, have now been read directly from Dataverse**, not just inferred), the DHA-1→6 cumulative security role model, and the wider solution landscape: architecture, tables, 170+ flows/business rules, canvas apps, model-driven apps, a live Sage integration with a real security finding, a fully separate *previous*-implementation-partner ("IG"/Infinity Software Consulting) layer, a Bing→Azure Maps migration in progress, and — as of 2026-08-25 — verified (not just name-inferred) logic for 24 currently-active flows, surfacing a likely-live production bug and secrets hardcoded directly in flow definitions. Documented incrementally, by domain: Time/Disbursement/Billing (14 solutions), Finance/Billing/Invoicing (6), IG/ISC legacy layer (9), UI/Security/Visibility (4), Integrations/Misc (7); 40 of 50 solutions covered so far, the rest tracked as a gap list |
| [wpf-client/](wpf-client/00-overview.md) | The desktop app's own plumbing: authentication/connection layers (and which of several near-duplicate classes are actually live), single-instance/session handling, and packaging/auto-update — including a significant amount of dead/orphaned code worth knowing about before you go looking for where a behaviour lives |

## Known open items

**⚠️ Highest priority: `fwp_auto_setTimeValueWhenAddedManually` is a confirmed, live
data-corruption bug, not a suspected one.** Its actual flow logic was read directly from
Dataverse: it fires on every new time entry and unconditionally forces the charge band
to Rate A plus recalculates `fwp_value` accordingly — with no awareness of what band the
user actually selected. It races against `fwp_setTimeEntryValue_2` (which does respect
the selected band); whichever flow happens to run last wins. **This can silently
overwrite a real Rate B (Expert Witness) or Rate C entry back to Rate A today**,
independent of the plug-in migration. It must be turned off (or fixed) as part of the
plug-in cutover, not left running alongside it. See
[dataverse-customizations/01-value-calculation-flow-vs-plugin.md](dataverse-customizations/01-value-calculation-flow-vs-plugin.md#open-risk-fwp_auto_settimevaluewhenaddedmanually--confirmed-second-writer-confirmed-race-condition)
and [dataverse-customizations/14-flow-logic-verification.md](dataverse-customizations/14-flow-logic-verification.md).

**Resolved**: the `fwp_calculatedreconciledvalue` vs `fwp_reconciledvaluedirect`
question that was previously open is now partly answered — the legacy flow and the
plug-in both guard on `fwp_calculatedreconciledvalue`, so they match each other; the
plug-in migration doesn't make anything worse on this point than the flow it replaces.
See [dataverse-customizations/01-value-calculation-flow-vs-plugin.md](dataverse-customizations/01-value-calculation-flow-vs-plugin.md#resolved-the-plug-ins-reconciliation-field-matches-the-flow-it-replaces).

**A likely live production bug**: `fwp_auto_reconcileInvoiceOnRequestForApproval`
hardcodes `"environment": "dev"` in its call to the reconciliation Azure Function
despite running in Production. See
[dataverse-customizations/14-flow-logic-verification.md](dataverse-customizations/14-flow-logic-verification.md#headline-findings).

**Two Sage API keys are stored in plaintext, now confirmed in at least three places**:
the environment variable's default, its value override, **and hardcoded directly inside
two flow definitions** (`fwp_auto_copyClientToSage`, `fwp_auto_publishInvoice_v2`) —
neither flow actually uses the documented environment variables at all. See
[dataverse-customizations/03-solution-landscape.md](dataverse-customizations/03-solution-landscape.md#sage-integration-via-environment-variables).

**There are now at least four inactive, superseded copies of invoice-publishing flows**
(`fwp_auto_publishInvoice` × 3, `fwp_auto_setDisbursementMarginFromCategory` × 2, plus a
couple of business-rule near-duplicates) spread across solutions, none deleted after
their replacements shipped. Not urgent, but worth a cleanup pass. See
[dataverse-customizations/06-finance-billing-invoicing.md](dataverse-customizations/06-finance-billing-invoicing.md#duplicate-proliferation-continued).

**A separate, older security-role model from the previous implementation partner ("IG")
is still live alongside DHA-1→6** — 10 flat, per-persona roles (`DHA - Managing
Director`, `DHA - Finance`, etc.), each tied to a legacy dashboard. If DHA-1→6 doesn't
explain a user's access, check whether they also hold one of these. See
[dataverse-customizations/08-ig-legacy-layer.md](dataverse-customizations/08-ig-legacy-layer.md#the-legacy-security-model-dha---role-not-dha-1→6).

**The George quote-visibility bug (in [time-management/04-pitfalls-and-faq.md](time-management/04-pitfalls-and-faq.md))
likely traces to one of three legacy Field Security Profiles** from that same IG layer
(`DHA - Client Approver`, `DHA - Non-Client Approver`, `Project Admin Team`) gating
`isc_projectnumbervisible` — not yet confirmed definitively; the follow-up query needed
to close it out is in
[dataverse-customizations/08-ig-legacy-layer.md](dataverse-customizations/08-ig-legacy-layer.md#field-security-profiles--likely-mechanism-behind-the-george-bug).
A current-team JS web resource, `fwp_isc_projectamount_visibility`, deliberately
continues the same `isc_` visibility-field naming convention for fee/amount visibility
on dashboards — its actual source wasn't pulled in this pass, see
[dataverse-customizations/10-ui-security-visibility.md](dataverse-customizations/10-ui-security-visibility.md#the-feeamount-visibility-script--connects-to-the-george-bug).
**A second current-team solution (`FWPLandCRM`) was also found reusing the same two
legacy Field Security Profiles**, making the `fieldpermission` follow-up query more
broadly relevant than just the George bug. See
[dataverse-customizations/12-integrations-and-misc.md](dataverse-customizations/12-integrations-and-misc.md#fwplandcrm--a-distinct-sub-application).

**A Bing Maps → Azure Maps migration is in progress**, built directly on the legacy
`isc_sitelocation` table rather than a new FWP-owned one, with a related sync flow
currently Draft/off. See
[dataverse-customizations/12-integrations-and-misc.md](dataverse-customizations/12-integrations-and-misc.md#bing-maps--azure-maps-a-migration-in-progress).

**DHA's integrations span at least four separate external Azure Function Apps**
(`dha-reconciliation-balancer`, `dhainvoicecreator`, `dhadataverseeditor...`,
`dhasageinterface`), none source-controlled anywhere visible from Dataverse — each is a
real gap in what this wiki can verify without direct Azure access. See
[dataverse-customizations/14-flow-logic-verification.md](dataverse-customizations/14-flow-logic-verification.md#headline-findings).

**Reminder**: outside of the 24 flows verified in
[dataverse-customizations/14-flow-logic-verification.md](dataverse-customizations/14-flow-logic-verification.md),
every other flow across this wiki is still documented by name/state inference only, not
by reading its actual logic — see that page's introduction for exactly what's confirmed
vs. inferred.

## Maintenance rule

**If a change alters a lifecycle, a validation rule, or which class/field is actually
live, update the relevant wiki page in the same change.** A stale wiki page that still
sounds authoritative is worse than no wiki page — it sends the next person down the
wrong path with confidence. This is a discipline, not a tool — there's no fence checking
it, it only stays true if it's part of the habit.
