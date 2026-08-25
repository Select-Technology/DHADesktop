# Dataverse Customisations — Overview

## What this domain is

This covers the customisations that live **in Dynamics itself**, not in the desktop
app: the automation that calculates a time entry's £ value, and the security role model
that controls who can see and edit what.

Unlike the WPF client, there's no exported solution file for most of this in this repo —
this domain is documented from two sources: the plug-in source code
(`DHA.DSTC.Plugins`) and the maintainers' own deployment notes for the parts that live
in this repo, and **direct live queries against the DHA Planning Production Dataverse
environment** (via the Power Platform CLI, `pac env fetch`) for everything that only
exists in the environment itself — the solutions, flows, business rules, canvas apps,
and environment variables that were never exported anywhere. Where something can only be
confirmed by looking at the live environment, that's called out, along with the date it
was checked (environments drift; treat anything dated as a snapshot, not a guarantee).

## What lives here

1. **`fwp_value` calculation** — historically a Power Automate cloud flow
   (`fwp_setTimeEntryValue_2`), which Microsoft kept auto-disabling, so it's being
   replaced by a Dataverse plug-in (`SetTimeEntryValuePlugin`) that runs inside the
   transaction and can't be disabled by the platform. **This migration is mid-flight**,
   and its actual logic — plus a second, confirmed-live flow that races against it and
   corrupts Rate B/C time entries back to Rate A — has now been read directly from
   Dataverse, not just inferred. See
   [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md) and
   [14-flow-logic-verification.md](14-flow-logic-verification.md).

2. **Security roles DHA-1 through DHA-6** — a cumulative, layered role model controlling
   record-level access (Own / Office / Everyone) to time entries, disbursements, jobs,
   and reconciliation data. See [02-security-roles.md](02-security-roles.md).

3. **The wider solution landscape** — the environment turns out to contain **50
   solutions**, not just the plug-in/flow pair above. This wiki is working through them
   incrementally, by domain:
   - **Time, Disbursement & Billing** (14 solutions) — architecture, tables, flows,
     business rules, canvas apps, model-driven apps, and a live Sage integration with a
     real security finding. See [03-solution-landscape.md](03-solution-landscape.md) and
     [04-flows-and-business-rules.md](04-flows-and-business-rules.md).
   - **Finance, Billing & Invoicing** (6 solutions) — the actual Sage sync flows, more
     duplicate/superseded-flow proliferation, and a stale sync timestamp. See
     [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md).
   - **The IG/ISC legacy layer** (9 solutions) — the *previous* implementation
     partner's work (Infinity Software Consulting, predating Select Technology's "FWP"
     team): pre-sales/quoting/opportunity automation, a since-superseded bridge to an
     external "Coretime" system, a separate legacy security-role model still live
     alongside DHA-1→6, and the likely mechanism behind the George quote-visibility
     bug. See [08-ig-legacy-layer.md](08-ig-legacy-layer.md).
   - **UI, Security & Visibility** (4 solutions) — all current (2025) FWP work: a
     fee/amount visibility script that continues the legacy `isc_` visibility
     convention, and a subgrid-button-hiding customisation. See
     [10-ui-security-visibility.md](10-ui-security-visibility.md).
   - **Integrations & Misc** (7 solutions) — a DocuSign e-signature integration, a
     Bing→Azure Maps migration in progress built on the legacy `isc_sitelocation`
     table, a distinct "Land CRM" sub-application (with its own app/role/tables) that
     reuses the legacy Field Security Profiles, and a correction to an earlier
     "unconfigured" environment-variable finding. See
     [12-integrations-and-misc.md](12-integrations-and-misc.md).

   Only "Other / vendor / platform" (~11 solutions, mostly low-priority
   Microsoft/ISV/one-off tools) remains, tracked in
   [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md).

4. **Verified flow logic** — everywhere above, a flow's "purpose" was originally
   inferred from its name and state, not read. [14-flow-logic-verification.md](14-flow-logic-verification.md)
   is the exception: 24 currently-Activated flows had their actual trigger/action JSON
   pulled and read directly. It surfaced a confirmed data-corruption race condition on
   time-entry charge bands, a likely-live production bug (a hardcoded dev/test
   mismatch), secrets hardcoded directly in flow definitions (not just environment
   variables), and at least four distinct external Azure Function Apps the environment
   depends on. **Most other flows across this whole domain are still name-inferred, not
   verified** — see that page's introduction for exactly what is and isn't confirmed.

## Where to go next

- [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md)
- [02-security-roles.md](02-security-roles.md)
- [03-solution-landscape.md](03-solution-landscape.md)
- [04-flows-and-business-rules.md](04-flows-and-business-rules.md)
- [05-solution-inventory-and-gaps.md](05-solution-inventory-and-gaps.md)
- [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md)
- [08-ig-legacy-layer.md](08-ig-legacy-layer.md)
- [10-ui-security-visibility.md](10-ui-security-visibility.md)
- [12-integrations-and-misc.md](12-integrations-and-misc.md)
- [14-flow-logic-verification.md](14-flow-logic-verification.md)
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
