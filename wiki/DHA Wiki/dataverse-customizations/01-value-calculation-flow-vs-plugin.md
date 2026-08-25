# fwp_value Calculation: Cloud Flow → Plug-in Migration

## What it computes

For a chargeable time entry, `fwp_value` (Money) = `fwp_decimalhours × the acting
colleague's rate for the selected charge band`. The rate comes from that colleague's
`fwp_colleagueconfiguration` record (`fwp_ratea`/`fwp_rateb`/`fwp_ratec`, selected by
`fwp_chargerate` on the time entry — Rate A/B/C).

**Neither the desktop app nor a human ever sets this field directly.** The WPF client
writes the hours and the chosen band; something server-side turns that into a value.
See [time-management/01-time-entries.md](../time-management/01-time-entries.md) and
[03-reconciliation-and-charge-bands.md](../time-management/03-reconciliation-and-charge-bands.md).

## The old mechanism: `fwp_setTimeEntryValue_2` (Power Automate cloud flow)

**Verified directly from the flow's `clientdata` on 2026-08-25** (see
[14-flow-logic-verification.md](14-flow-logic-verification.md#time-entry-value-calculation) —
this was previously described from the plug-in's own code comments only; it's now
confirmed against the actual flow):

- Triggers on **Create or Update** of `fwp_timeentry`, with **no filtering attributes**
  — it fires on literally any field change to any time entry, not just hours/rate.
- Guard: skip if `fwp_calculatedreconciledvalue` is null or zero — i.e. **confirmed as
  the same field the plug-in checks** (see below, this resolves part of the previously
  open question).
- Reads `fwp_colleagueconfiguration` for the acting colleague's Rate A/B/C, selects by
  `fwp_chargerate`, computes `fwp_value = rate × fwp_decimalhours`.
- "Skip if already correct" guard, **but it only ever compares against the Rate A
  calculation**, never B or C.

**Problem, now understood precisely, not just "Microsoft auto-disables it"**: because
this flow re-triggers on its own Update, has no filtering attributes, and its "already
correct" check is hardcoded to Rate A — **every Band B or C time entry causes the flow
to retrigger and rewrite itself repeatedly**: it writes the correct B/C value, that
Update refires the flow, the guard (which only knows about Rate A) sees a mismatch
again, and it recalculates and writes again. This is a plausible, concrete, self-inflicted
cause for the flow's reliability problems — not only the platform auto-disabling flows,
but a genuine retrigger loop on a meaningful fraction of live records. A "Watchdog"
process is referenced in the plug-in's deployment notes as an existing detection
mechanism for value-less entries (Detection B) — the Watchdog itself isn't part of this
repo; it's mentioned only as context for what stays in place during the cutover.

## The new mechanism: `SetTimeEntryValuePlugin`

`DHA.DSTC.Plugins\SetTimeEntryValuePlugin.cs` — a synchronous, pre-operation plug-in
registered on `Create` and `Update` of `fwp_timeentry`, designed to be a durable,
in-transaction replacement:

- Reads the merged current state (Target for changed fields, PreImage for unchanged
  ones on Update).
- **Guard 1**: skip if `fwp_calculatedreconciledvalue` is set (see the open risk below).
- **Guard 2**: only write if the computed value differs from the current `fwp_value`.
- Resolves the rate via a `fwp_colleagueconfiguration` lookup on the team member; no
  configuration row → rate `0` (same effective behaviour as the flow).
- A `context.Depth > 1` guard against re-entrancy, though writing on the Target in
  pre-operation means there's no second `Update` to recurse into anyway.
- Assembly is strong-named (required for Dataverse plug-ins), no external dependencies.

## Status as of the last deployment notes (`DHA.DSTC.Plugins\DEV-SETUP.md`)

The pre-deploy steps (build, sign, formula cross-check against real prod data) were
done; registering in **dev** in isolation (cloud flow turned off in dev only), verifying
against the flow's output, and eventually cutting prod over (register the plug-in, turn
the prod flow off, keep it dormant as a fallback) were the remaining steps — **check the
live dev/prod environments for current status rather than assuming this document is
up to date**, since this is exactly the kind of state that moves outside this repo.

## Resolved: the plug-in's reconciliation field matches the flow it replaces

**Update 2026-08-25, verified against the flow's actual logic**: this was flagged as an
open risk — the plug-in's Guard 1 checks `fwp_calculatedreconciledvalue`, while the WPF
client's own reconciliation lock (`ReconciliationValidationHelper`) checks a different
field, `fwp_reconciledvaluedirect` (see
[time-management/03-reconciliation-and-charge-bands.md](../time-management/03-reconciliation-and-charge-bands.md)).
Reading `fwp_setTimeEntryValue_2`'s actual `clientdata` confirms **the legacy flow
guards on `fwp_calculatedreconciledvalue` too** — the exact same field the plug-in uses.
**The plug-in is behaviourally consistent with the flow it's replacing on this point; the
migration does not introduce a new mismatch.**

The gap that remains is a **pre-existing** one, not a plugin-migration risk: the flow
(and now the plug-in) have only ever guarded on `fwp_calculatedreconciledvalue`, while
the WPF client's edit/delete lock checks `fwp_reconciledvaluedirect` — a different field.
If those two fields aren't always in sync, a row the client considers "reconciled and
locked" could still get its `fwp_value` recalculated by the flow/plug-in, because neither
has ever checked the field the client checks. This has been true since before the
plug-in existed — worth raising with whoever maintains the reconciliation
Azure Function (see
[04-flows-and-business-rules.md](04-flows-and-business-rules.md#reconciliation-the-real-mechanism))
about whether the two fields are meant to be kept in sync, but it is **not** a reason to
delay the plug-in cutover specifically — the plug-in doesn't make this any worse than the
flow it replaces.

## Open risk: `fwp_auto_setTimeValueWhenAddedManually` — confirmed second writer, confirmed race condition

This risk is now **confirmed**, not just suspected. `fwp_auto_setTimeValueWhenAddedManually`
(Activated) triggers on Create of `fwp_timeentry` wherever `fwp_value IS NULL` — i.e.
every new time entry — reads only `fwp_ratea` (never B or C), and **unconditionally
writes both `fwp_chargerate = 800470000` (Rate A) and `fwp_value`, with no reconciliation
guard at all.**

Because `fwp_setTimeEntryValue_2` *also* fires on Create, both flows can run against the
same new record with no guaranteed ordering between two independent Power Automate
flows. Depending on which runs last:

- If `fwp_setTimeEntryValue_2` runs last: the correct band-based value survives.
- If `fwp_auto_setTimeValueWhenAddedManually` runs last: **the user's actual Rate B/C
  selection is silently overwritten back to Rate A**, and the value is recalculated at
  the Rate A rate.

**This is a live data-quality risk today, independent of the plug-in migration** — it
would affect any Band B ("Expert Witness") or Band C time entry created right now,
depending on flow execution timing. See
[14-flow-logic-verification.md](14-flow-logic-verification.md#time-entry-value-calculation)
for the full verified detail on both flows.

**For the plug-in cutover specifically**: turning off only `fwp_setTimeEntryValue_2` (as
the plug-in's own documentation implies) would leave `fwp_auto_setTimeValueWhenAddedManually`
active — meaning the race condition above would continue, now between this flow and the
plug-in instead of between the two flows. **This flow needs to be turned off (or fixed)
as part of the cutover, not left running.**

**Before the plug-in is registered in production**, this is the single most load-bearing
thing to check.

## See also

- [00-overview.md](00-overview.md)
- [03-solution-landscape.md](03-solution-landscape.md)
- [04-flows-and-business-rules.md](04-flows-and-business-rules.md)
- [14-flow-logic-verification.md](14-flow-logic-verification.md) — the verified flow logic behind both findings above
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
- `DHA.DSTC.Plugins\DEV-SETUP.md`, `DHA.DSTC.Plugins\README-registration.md` — the
  build/registration steps themselves
