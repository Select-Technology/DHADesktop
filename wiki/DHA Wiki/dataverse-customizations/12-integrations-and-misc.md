# Integrations & Misc

*Reconstructed from DHA Planning Production on 2026-08-25 via `pac env fetch`, same
method as [03-solution-landscape.md](03-solution-landscape.md#how-this-was-produced).
Fifth incremental pass — see [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md).*

This page covers the 7 solutions originally grouped as "Integrations / misc":
`FWPDocusignIntegrationPt1`, `FWPWebResources`, `FWPEnvironmentVariables`,
`FWPLandCRM`, `FWPContactCleanse`, `AzureMaps`, `BingsMapFix`. **One correction to the
original grouping**: `BingsMapFix` turns out to be published by **Infinity Software
Consulting** (the legacy "IG" partner, see [08-ig-legacy-layer.md](08-ig-legacy-layer.md)),
not the current FWP team — it belongs conceptually with that legacy layer, not here.
Left in this page since it's directly relevant to `AzureMaps` (see below), but don't
treat it as current-team work.

## The 7 solutions

| Solution | Managed | Created | Publisher | What it actually contains |
|---|---|---|---|---|
| `FWPDocusignIntegrationPt1` | Yes | 2025-10-22 | SelectTechnology | A full DocuSign e-signature data model: `fwp_docusignenvelope`, `fwp_docusigntemplate`, `fwp_docusignfieldmapping`, `fwp_docusignsendrequest` |
| `FWPWebResources` | Yes | 2025-07-30 | SelectTechnology | Two client-side scripts (see below) |
| `FWPEnvironmentVariables` | Yes | 2025-07-07 | SelectTechnology | Re-packages the same Sage/invoicing environment variables from [03-solution-landscape.md](03-solution-landscape.md#sage-integration-via-environment-variables), plus their live **Value** overrides — see below |
| `FWPLandCRM` | Yes | 2025-01-20 | Select Technology Future Workplace | A substantial sub-application: its own model-driven app (**DHA Land CRM**), a Canvas App, a dedicated security role, three new `fwp_land*` tables, and reuse of the same legacy Field Security Profiles found in the IG layer — see below |
| `FWPContactCleanse` | **No (unmanaged)** | 2025-05-08 | SelectTechnology | One unresolved component (see below) |
| `AzureMaps` | Yes | **2026-02-03** (the most recently created solution found in any pass so far) | Select Technology Future Workplace | Maps integration built directly on the legacy `isc_sitelocation`/`isc_localauthority` tables — see below |
| `BingsMapFix` | Yes | 2024-03-20 | **Infinity Software Consulting** (legacy) | One web resource, `isc_BingMapsUtility` — the map integration `AzureMaps` appears to be replacing |

## Bing Maps → Azure Maps: a migration in progress

`BingsMapFix` (IG/ISC, 2024) contains a single HTML web resource,
**`isc_BingMapsUtility`** ("Bing Maps Utility") — presumably an embedded Bing Maps
control shown somewhere (likely on Site Location records, given the naming).

`AzureMaps` (current FWP team, created **2026-02-03** — the newest solution found
across every pass of this wiki so far) replaces this with:

- Two new web resources: `fwp_isc_AzureMapsUtilityScript` (JScript) and
  `fwp_isc_AzureMapsUtility` (HTML) — note the `fwp_isc_` naming, the same
  legacy-convention-continuation pattern already seen in
  [10-ui-security-visibility.md](10-ui-security-visibility.md#the-feeamount-visibility-script--connects-to-the-george-bug).
- Ownership (`Include Subcomponents`) of **`isc_sitelocation`** — the *same legacy
  table* the IG layer built (see [08-ig-legacy-layer.md](08-ig-legacy-layer.md)).
- A reference to `isc_localauthority` (`Do not include subcomponents`).
- A Modern Flow, **"Weekly New Site Locations"** — currently **Draft (off)**.

**This is a live migration, not a replacement-in-name-only**: the current team is
building new mapping functionality (Bing → Azure) directly on top of the legacy `isc_`
site-location schema rather than migrating to a new `fwp_`-owned table. If site location
mapping looks inconsistent or partially working, check whether `BingsMapFix`'s old
control is still active alongside the new one, and whether "Weekly New Site Locations"
being Draft means the Azure Maps side isn't fully live yet.

## FWPLandCRM — a distinct sub-application

This is the largest solution in this group by far, and it's effectively its own
mini-application layered on the environment: **"DHA Land CRM"**
(`fwp_DHALandCRM`, a model-driven app — see
[03-solution-landscape.md](03-solution-landscape.md#model-driven-apps-in-this-domain))
plus:

- A dedicated Canvas App and a custom Site Map.
- A dedicated security role: **"Land CRM User"**.
- Three new tables: `fwp_landclientindustry`, `fwp_landclientlocalauthorities`,
  `fwp_landindustry` — modelling land clients' industry sector(s) and which local
  authorities they're associated with.
- References to `contact` and `account` (Include Subcomponents on `account`).

⚠️ **It also references the same two legacy Field Security Profiles found in the IG
layer** — `DHA - Client Approver` (`ba8cf995-...`) and `DHA - Non-Client Approver`
(`bffe660b-...`) — see
[08-ig-legacy-layer.md](08-ig-legacy-layer.md#field-security-profiles--likely-mechanism-behind-the-george-bug).
This is the **second** current-team solution (after the visibility script in
[10-ui-security-visibility.md](10-ui-security-visibility.md)) found actively reusing
that legacy security construct, which reinforces that these two profiles are still
load-bearing in the live security model, not dead artifacts — worth prioritising the
`fieldpermission` lookup recommended in the IG-layer page, since it's now relevant to
more than just the George bug.

## Environment variables — a correction and a reinforcement

`FWPEnvironmentVariables` re-packages the same Sage/invoicing environment variables
already documented in
[03-solution-landscape.md](03-solution-landscape.md#sage-integration-via-environment-variables)
(`fwp_SageAPIServer_Test`, `fwp_SageAPIServer_Planning`, `fwp_PlanningAuthKey`,
`fwp_TestAuthKey`, `fwp_LastInvoiceNumber`, `fwp_BillToProduction`), plus one more:
`fwp_dv_env`.

**Correction to [03-solution-landscape.md](03-solution-landscape.md)**: that page noted
`fwp_dv_env` "has no default set... appears unconfigured." That was checking the
Definition's default value only. This pass queried the separate
**`environmentvariablevalue`** records (the actual live override, which takes priority
over a Definition's default) and found **`fwp_dv_env` = `"prod"`** — it *is* configured,
just via a Value override rather than the schema default. If you're ever checking an
environment variable's live value, query `environmentvariablevalue` first — the
Definition's `defaultvalue` is only what's used when no override exists.

**Reinforcement of the plaintext-secret finding**: `fwp_TestAuthKey` and
`fwp_PlanningAuthKey` both have explicit `environmentvariablevalue` records holding the
**same plaintext key** as their Definition's default — meaning both Sage API keys are
now confirmed stored in cleartext in **two places**, not one. Doesn't change the
severity of the existing finding, but confirms it more thoroughly. See
[13-pitfalls-and-faq.md](13-pitfalls-and-faq.md#are-the-sage-api-keys-visible-to-people-who-shouldnt-see-them).

## FWPWebResources

Two scripts:

- **`fwp_updateValueSinceLastInvoice`** (JScript) — almost certainly the client-side
  counterpart to the `fwp_sched_updateValueSinceLastInvoice` flow already documented
  (Draft) in [04-flows-and-business-rules.md](04-flows-and-business-rules.md#invoicing--sage-sync-flows),
  and/or related to the `fwp_LastSyncDate` environment variable.
- **`fwp_showInvoiceAdditionalContacts`** (HTML webpage) — a display page related to
  the `fwp_additionalbillingcontacts` table documented in
  [03-solution-landscape.md](03-solution-landscape.md#custom-tables-fwp_-prefix).

## FWPDocusignIntegrationPt1

A complete e-signature data model: envelopes, templates, field mappings, and send
requests. **"Pt1" in the name strongly implies a planned "Pt2"** — worth checking with
whoever owns this integration whether that's still coming, since a "Part 1" solution
that's been live since October 2025 without a visible "Part 2" might mean the rollout is
either paused or was completed under a different solution name not yet found.

## FWPContactCleanse — unresolved

This solution has exactly one solutioncomponent row, an unmanaged component whose type
didn't resolve as a `workflow` record via this pass's queries. Given the name and that
it's the only **unmanaged** solution in this entire group (unmanaged solutions are
typically still "in progress" / not yet packaged for deployment), this is most likely a
Power Automate flow or an app that wasn't successfully resolved in this pass — flagged
as a loose end for a future pass rather than guessed at.

## See also

- [00-overview.md](00-overview.md)
- [03-solution-landscape.md](03-solution-landscape.md) — the environment-variable correction above applies there
- [04-flows-and-business-rules.md](04-flows-and-business-rules.md)
- [08-ig-legacy-layer.md](08-ig-legacy-layer.md) — `BingsMapFix` and the reused Field Security Profiles
- [10-ui-security-visibility.md](10-ui-security-visibility.md) — the same `fwp_isc_` naming pattern
- [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md)
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
