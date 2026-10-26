# The IG/ISC Legacy Layer

*Reconstructed from DHA Planning Production on 2026-08-24 via `pac env fetch`, same
method as [03-solution-landscape.md](03-solution-landscape.md#how-this-was-produced).
Third incremental pass — see [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md).*

This page covers the 9 `IG`-prefixed solutions: `IGDHACustomisations`,
`IGDHAEnvVarsConRefs`, `IGDHAFlows`, `IGDHAWorkflows`, `IGQuoteUpgrade`, `IG_2024`,
`IG_870880`, `IG_AUG_Fix`, `IG_Opportunity_Enhancements`. **These predate Select
Technology's ("FWP") team taking over the environment** — they were built by an earlier
implementation partner, confirmed below.

## Who built this, and when

All 9 solutions are published by **Infinity Software Consulting** (publisher unique
name `infinitysoftwareconsulting`) — confirmed via the `solution.publisherid` lookup.
"ISC" and "IG" are the same partner: the oldest artifact found in this whole layer, a
classic workflow named `ISC [PROJECT] - 01 - Set Job Name` (created 2020-11-30), uses
the "ISC" initialism directly, while everything from slightly later onward uses the "IG"
prefix instead.

Three solutions (`IGDHAFlows`, `IGDHACustomisations`, `IGDHAWorkflows`) share a build
version stamp of `20230509.1702` (9 May 2023) with incrementing patch numbers —
long-lived "platform" solutions patched repeatedly (`IGDHACustomisations` up to `.13`).
The other five (`IGQuoteUpgrade`, `IG_Opportunity_Enhancements`, `IG_2024`,
`IG_AUG_Fix`, `IG_870880`) use plain `1.0.0.x` versioning and read as one-off
ticket/hotfix solutions — `IG_870880` in particular looks named after a support ticket
number.

**Nothing in this layer has been newly authored since August 2024.** Every workflow's
`createdon` falls between November 2020 and August 2024. That doesn't mean it's inert,
though — see below.

## What IG actually built

Unlike the FWP layer (which owns time entries, disbursements, and invoicing), **IG's
automation is concentrated on the pre-sales pipeline**: `Quote`, `Opportunity`, `Project`
(the classic Project Service Automation entity), plus DHA-specific reference tables
`isc_localauthority`, `isc_sitelocation`, `isc_sector`, `isc_subsector`, `isc_tablea`,
and `Contact`/`Account` (client). Quote and Opportunity are the two most heavily and
repeatedly touched tables, customised across five of the nine solutions.

Concretely, the automation covers things like: setting/locking quote numbers, alerting
finance when a quote needs checks, carrying site locations and local authorities over
from Opportunity → Quote → Project, flagging potential district-wide conflicts, quote
approval alerts, and a set of dashboards (see below).

### The Coretime bridge — superseded by the FWP layer

A distinct cluster of automation — `IGDHAEnvVarsConRefs`'s environment variables and
several flows in `IGDHAFlows` — integrates Dynamics with an **external legacy
time-tracking/billing system called "Coretime"**:

| Environment variable | Default value |
|---|---|
| `isc_CoretimeTimeItemstable` | `coretimedev.timeitems` |
| `isc_CoretimeBudItemstable` | `coretimedev.buditems` |
| `isc_CoretimeProjectstable` | `coretimedev.projects` |
| `isc_CoretimeContactstable` | `coretimedev.contacts` |
| `isc_CoretimeClientstable` | `coretimedev.clients` |

Flows syncing to it: `IG-CHILD-Get next database Code (Coretime)`,
`IG-CHILD-GetTimeItemRows+Update (Coretime)`, and four
`Insert/UpdateCoretimeRow` flows firing on Quote/Contact/Client/Project events, plus a
classic workflow `IG-Project-OnCreateCheckQuoteInCoretime`.

**This is almost certainly what the FWP time/disbursement/billing layer replaced.**
Before FWP, time and billing apparently lived in a separate product ("Coretime") that
Dynamics synced to; FWP brought that natively into Dataverse
(`fwp_timeentry`/`fwp_disbursement`/`fwp_dhainvoice`, see
[03-solution-landscape.md](03-solution-landscape.md)). Consistent with this:
`IG-Project-OnCreateCheckQuoteInCoretime` — the newest, most specific Coretime workflow
— is now **Draft**, and several of the Coretime sync flows share an identical
modification timestamp (2024-10-04), consistent with a bulk deactivation event rather
than organic edits.

### Evidence the current team is actively winding this layer down

The most recently *modified* records in this entire layer are disproportionately the
ones now sitting in **Draft**:

| Flow/rule | State | Last modified |
|---|---|---|
| `IG-Quotes-Alert Users of Quote Approval` | Draft | 2026-01-24 |
| `IG-Quotes-Send Quote Author Quote Number` | Draft | 2025-12-17 |
| `IG-Clients-Notify Finance of New Client Needing Approval` | Draft | 2025-10-28 |
| `IG-Project-OnCreateCheckQuoteInCoretime` | Draft | 2025-10-28 |
| `IG-Emails-Set Has Attachments` | Draft | 2025-08-23 |

One business rule is explicitly self-labelled by whoever last touched it:
**`(Deprecated) - Set Quote Number when Quote is Approved`** (Draft). Read together,
this looks like the current (FWP) team **incrementally deactivating individual pieces
of the IG layer as equivalent functionality is rebuilt**, rather than IG's own work
still being actively extended. If you're the one doing that deactivation work, this
page is meant to be the running record of what's already been turned off and why —
update it as you go.

## The legacy security model (DHA - <role>, not DHA-1→6)

`IGDHACustomisations` defines **10 security roles**, all still present in the
environment: `DHA - Project Manager`, `DHA - Team Project Manager`, `DHA - Admin`,
`DHA - Management KPIs`, `View Append Projects`, `DHA - Copy of Account Manager`,
`DHA - Land Dashboard`, `DHA - Managing Director`, `DHA - Copy of Project Manager`,
`DHA - Finance`.

⚠️ **These are a completely separate, older role model from the DHA-1→6 cumulative
model documented in [02-security-roles.md](02-security-roles.md).** None of them share
an ID with a DHA-1→6 role, and the pattern is structurally different: one flat role per
persona (with a matching dashboard — see below), rather than a cumulative ladder. **If
you're troubleshooting a user's access and DHA-1→6 alone doesn't explain what they can
see, check whether they're also holding one of these legacy roles** — a user's actual
effective permissions are the union of every role they hold, legacy or current.

The 10 roles line up directly with a set of legacy dashboard forms found in the same
solution: `Managing Director Dashboard`, `Finance Dashboard`, `Management KPIs`,
`Quote Land Dashboard`, `My Dashboard` — e.g. "DHA - Managing Director" role ↔
"Managing Director Dashboard" form. This was IG's access model: a role exists mainly to
grant a person their one relevant dashboard.

### Field Security Profiles — likely mechanism behind the George bug

Three Field Security Profiles, all `IGDHACustomisations`: **`DHA - Client Approver`**,
**`DHA - Non-Client Approver`**, and **`Project Admin Team`**. None has a description
populated, so this can't be confirmed from metadata alone, but the first two are exactly
the shape of construct that would gate visibility of a field like
`isc_projectnumbervisible` (presumably: project number visible only once a quote is
past the "client approver" stage, or visible only to non-client-facing roles) — the
field at the centre of the
[George quote-visibility bug](../time-management/04-pitfalls-and-faq.md). **To close
this out definitively**, query `fieldpermission` filtered to these three profile IDs and
check which one actually grants/denies read on `isc_projectnumbervisible`:

- `DHA - Client Approver` — `ba8cf995-d593-ec11-b400-6045bd0ee125`
- `DHA - Non-Client Approver` — `bffe660b-d693-ec11-b400-6045bd0ee125`
- `Project Admin Team` — `07078af8-e833-eb11-a813-000d3a86d545`

## Minor findings

- **A real person's name is hardcoded into a production environment variable's
  description**: `isc_EmailAddressfornewClientNotification`'s description reads "The
  email address for Grace who needs to be notified of creation of Account." Not a
  security issue, just poor practice worth avoiding in anything new.
- **Two separate customised Quote "Main" forms exist** ("Quote" and a "Project
  Information" variant) — a possible source of "which form am I even looking at"
  confusion; not confirmed as an active problem, just noted.
- The one Canvas App in this layer (`ProjectService_DefaultCommandLibrary`) is the same
  kind of Power Apps auto-generated command-bar app already seen in earlier passes, not
  bespoke UI.

## See also

- [00-overview.md](00-overview.md)
- [02-security-roles.md](02-security-roles.md) — for the current DHA-1→6 model this legacy layer is separate from
- [03-solution-landscape.md](03-solution-landscape.md) — the FWP layer that appears to supersede the Coretime bridge
- [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md)
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
- [time-management/04-pitfalls-and-faq.md](../time-management/04-pitfalls-and-faq.md) — the George/`isc_projectnumbervisible` bug
