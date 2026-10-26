# Flow Logic Verification

*Produced 2026-08-25. Everywhere else in this wiki, a flow's "purpose" is inferred from
its name, category, and state — not verified. This page is the exception: for 24
currently-**Activated** Modern Flows in the FWP layer, the actual `clientdata` (the
flow's real trigger + action definition) was pulled directly from Dataverse and read.
One flow (`FWP: Set Unreconciled Value On Demand`, in
[04-flows-and-business-rules.md](04-flows-and-business-rules.md#reconciliation-the-real-mechanism))
was already verified in an earlier pass and isn't repeated here.*

**Why this matters**: several of the corrections and new findings below are more
significant than routine documentation gaps — they change what's true elsewhere in this
wiki. Each is cross-linked to the page it corrects.

## Headline findings

1. **The `fwp_calculatedreconciledvalue` vs `fwp_reconciledvaluedirect` question is
   now partially resolved.** `fwp_setTimeEntryValue_2` — the legacy flow, i.e. the
   *original* source of truth the plugin is meant to replace — guards on
   `fwp_calculatedreconciledvalue`, the same field the plugin uses. **The plugin is
   consistent with the flow it replaces.** The mismatch is specifically between
   (flow/plugin, using `fwp_calculatedreconciledvalue`) and (the WPF client's own lock,
   using `fwp_reconciledvaluedirect`) — a pre-existing gap, not something the plugin
   migration introduces. See
   [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md).

2. **A likely root cause for the legacy flow's instability, found by reading its own
   logic.** `fwp_setTimeEntryValue_2` triggers on both Create *and* Update of
   `fwp_timeentry` with no filtering attributes, and its own "skip if already correct"
   guard only ever compares against a **Rate A** calculation — never Rate B or C. For
   any Band B/C time entry, after the flow writes the correct value, its own write
   re-triggers itself, the guard condition is still true (value ≠ Rate A×hours, since
   the row is genuinely on B/C), so it recalculates and writes again. This is a
   plausible, concrete explanation for exactly the reliability problems already cited
   as the reason for retiring this flow — not just "Microsoft auto-disables it," but a
   self-inflicted retrigger loop on a meaningful fraction of records.

3. **`fwp_auto_setTimeValueWhenAddedManually` is confirmed as a second, independent
   writer of `fwp_value` — and it silently forces every new time entry to Rate A,
   overwriting whatever band the user actually selected.** It triggers on Create where
   `fwp_value IS NULL` (i.e. every new entry), reads only `fwp_ratea` (not B/C), and
   writes both `fwp_chargerate = 800470000` (Rate A) and `fwp_value` — unconditionally,
   with no reconciliation guard at all. Since `fwp_setTimeEntryValue_2` **also** fires
   on Create, both flows can run on the same new record with no guaranteed order — a
   genuine race condition that can silently overwrite a user's Rate B/C selection back
   to Rate A depending on which flow happens to run last. **This is a live data-quality
   risk today, independent of the plugin migration**, and needs its own investigation.
   See [time-management/03-reconciliation-and-charge-bands.md](../time-management/03-reconciliation-and-charge-bands.md).

4. **The Sage integration hardcodes its production URL and auth key directly in flow
   JSON — it does not use the documented environment variables at all.**
   `fwp_auto_copyClientToSage` and `fwp_auto_publishInvoice_v2` both call
   `https://dha.hypersage.co.uk:50027/...` with a literal `AuthToken` header value
   baked into the flow definition — the same plaintext key already flagged as an
   environment-variable security issue in
   [03-solution-landscape.md](03-solution-landscape.md#sage-integration-via-environment-variables).
   **The secret is now confirmed to exist in at least three places**: the environment
   variable's default, its value override, and directly inside two flow definitions.
   `fwp_SageAPIServer_Planning`/`fwp_SageAPIServer_Test`/`fwp_PlanningAuthKey`/
   `fwp_TestAuthKey`/`fwp_BillToProduction` appear to be **unused by the actual
   Sage-calling flows found in this pass** — always-prod, no environment-variable-driven
   switching exists in what was read. Whether they're used by some other, unaudited
   flow is unknown.

5. **A likely live production bug**: `fwp_auto_reconcileInvoiceOnRequestForApproval`
   hardcodes `"environment": "dev"` in its call to the reconciliation Azure Function,
   despite being an Activated flow in the **Production** environment. If the Azure
   Function actually branches on that value, invoice-approval reconciliation may be
   silently running against dev data/config in production.

6. **The DHA integration landscape includes at least four separate external Azure
   Function Apps**, not one: `dha-reconciliation-balancer.azurewebsites.net` (time
   reconciliation, already known), `dhainvoicecreator.azurewebsites.net` (PDF invoice
   generation, Sage PO sync trigger), `dhadataverseeditor20250307131216.azurewebsites.net`
   (a general-purpose "Dataverse Editor" utility — quote customer updates, quote
   closure, invoice-approval reconciliation), and `dhasageinterface.azurewebsites.net`
   (client find-or-create against Sage). None of their source code is visible from
   Dataverse — each represents a real gap in what this wiki can verify without direct
   access to Azure.

7. **The "Send Time Reminder Email" flow appears to be dead prototype code, not the
   live reminder mechanism.** The actual daily reminder system
   (`fwp_sched_timesheetReminder`) is fully self-contained — it does not call
   `FWP: Send Time Reminder Email` at all, despite the latter being Activated. That flow
   contains unwired placeholder content (a hardcoded fake period label, a hardcoded
   fake `sarah.johnson@company.com` test address in a response payload that's never
   actually returned, since the flow has no Response action).

## Verified flows

### Time entry value calculation

| Flow | Verified behaviour |
|---|---|
| `fwp_setTimeEntryValue_2` | Trigger: Create/Update of `fwp_timeentry`, no filter. Guards on `fwp_calculatedreconciledvalue` (null/zero = not reconciled). Reads `fwp_colleagueconfiguration` for the band-selected rate (A/B/C via `fwp_chargerate`). Writes `fwp_value = rate × fwp_decimalhours`. **Bug**: its own "already correct" check only compares against Rate A, causing a self-retrigger loop on Band B/C rows (see headline finding #2) |
| `fwp_auto_setTimeValueWhenAddedManually` | Trigger: Create of `fwp_timeentry` where `fwp_value IS NULL` (i.e. every new row). Reads only `fwp_ratea`. **Forces `fwp_chargerate` to Rate A and writes `fwp_value` accordingly — unconditionally, no reconciliation guard.** Confirmed second writer, confirmed race condition with the flow above (see headline finding #3) |
| `fwp_instant_getLastWeeksHours` | Manual, called from a Power App via `Flow()`, passing the current user's GUID. Computes last week's total hours (FetchXml aggregate on `fwp_decimalhours`), compares against `fwp_colleagueconfiguration.fwp_contractedhours` for a completeness flag, and separately computes variance against `fwp_totalweeklyhours` (a different config field). Returns a small JSON response for the calling app. **This is also the child flow `fwp_sched_timesheetReminder` calls** — see below |
| `fwp_setTimeClientOnCreate` | Trigger: Create of `fwp_timeentry`. Resolves Project → Project's Customer account, stamps that onto the time entry's `fwp_Client` lookup |
| `fwp_inst_setTimeClients` | Manual, no inputs. Bulk version of the flow above — re-runs the same Project→Customer→Client resolution across up to 5,000 existing time entries (no pagination beyond that cap). A backfill/repair tool, not an automatic process |

### Invoicing

| Flow | Verified behaviour |
|---|---|
| `fwp_auto_generatepdfinvoice` | Trigger: the invoice's Business Process Flow (`fwp_dhainvoiceflow`) reaching a specific stage. Skips dummy invoices (`fwp_isdummy`). Dispatches to `dhainvoicecreator.azurewebsites.net/api/InvoiceGenerator`. **Does not reference `fwp_creatingPDF`** anywhere visible — if that mutex flag is real, it's set inside the Azure Function, not this flow |
| `fwp_auto_setInvoiceDueDate` | Trigger: Create of `fwp_dhainvoice`. Single action: `fwp_duedate = fwp_invoicedate + 28 days`, flat offset, no conditions. This is the exact calculation to compare against the "Set Invoice Due Date" Business Rule already flagged as a possible duplicate mechanism in [04-flows-and-business-rules.md](04-flows-and-business-rules.md#invoicing--sage-sync-flows) |
| `fwp_auto_setBillingContactDetailOnInvoice` | Trigger: change to `fwp_dhainvoice.fwp_billingcontact`. **Reads from the standard `contacts` table**, not `fwp_additionalbillingcontacts` as previously assumed — copies name/address fields onto the invoice, or clears them if the contact is removed |
| `fwp_auto_reconcileInvoiceOnRequestForApproval` | Trigger: invoice BPF reaching a specific "approval" stage. Dispatches to `dhadataverseeditor.../api/ReconcileInvoiceAll` with **a hardcoded `"environment":"dev"`** despite running in production (headline finding #5). Directly (not via the Function) resets every linked disbursement's `fwp_invoiced` flag to `false` — a side effect not implied by the name |
| `fwp_auto_copyInvoicePoToProject` | Trigger: change to `fwp_dhainvoice.fwp_purchaseorder`, **gated on `fwp_usethispoforfutureinvoices = true`** (not unconditional, as the name alone would suggest). Copies the PO number onto the invoice's parent `msdyn_project.fwp_ponumber` |

### Sage sync

| Flow | Verified behaviour |
|---|---|
| `fwp_auto_copyClientToSage` | Trigger: `account` create/update, no filter. POSTs to `dha.hypersage.co.uk:50027/api/customer/` with a **hardcoded** production URL and auth token. Writes the returned ledger code back to `account.isc_ledgercode`. On failure, notifies the record creator and emails `support@select-technology.co.uk` |
| `fwp_auto_publishInvoice_v2` | Trigger: invoice BPF reaching a "ready to publish" stage. Builds a full Sage sales-invoice payload (professional services line + one line per disbursement nominal/tax group), POSTs to the same hardcoded Sage endpoint, emails the customer a branded PDF invoice on success, logs the raw Sage response onto the invoice, and alerts a named individual + `support@select-technology.co.uk`/`invoices@`/`accounts@dhaplanning.co.uk` on failure. This is the confirmed live `_v2` |
| `fwp_sched_callSagePOSync` | **Hourly** recurrence (not daily). Pure dispatcher — single HTTP POST to `dhainvoicecreator.azurewebsites.net/api/SyncSagePurchaseOrders` with `{"environment":"prod"}` hardcoded. No Dataverse actions of its own; all actual sync logic is inside the Function, invisible from here |
| `fwp_instant_AlignClientWithSage` | Manual (Power Apps button). A genuinely different tool from `fwp_auto_copyClientToSage` above — calls a **third** Function App (`dhasageinterface.azurewebsites.net/api/CreateOrFindClient`), which can return `preExisting` to avoid duplicate Sage clients, then stamps `fwp_sageid`/`fwp_sagealigned`/`fwp_sagepreexisting` plus an audit note |
| `fwp_defaultDisbursementsToStandardTax` | Trigger: Create of `fwp_disbursement`. Looks up the `msdyn_taxcode` named "Standard VAT" and defaults the new disbursement to it. Purely internal Dataverse-to-Dataverse, no external calls |

### Disbursements & quotes

| Flow | Verified behaviour |
|---|---|
| `fwp_auto_setDisbursementMarginFromCategory` (the Activated copy) | Trigger: `fwp_disbursement` event. Looks up the matching `fwp_disbursementcategory` by `fwp_type` and copies its `fwp_defaultmargin` onto the disbursement's `fwp_margin`. Matches its name exactly |
| `fwp_attachQuoteCostsToProject` | Trigger: `msdyn_project` create. Polls (up to 1 hour) until the new project's linked quote is populated, then **re-parents** every active `fwp_timeentry` and `fwp_disbursement` on that quote onto the new project, and notifies the project owner of the counts moved. A genuine ongoing process, more specific than its name suggests (it moves actual records, not abstract "costs") |
| `FWP: Set Quote Customer` | Manual (Power App). Thin wrapper — has no Dataverse connector actions of its own; hands `quoteId`/`newClientId` to `dhadataverseeditor.../api/ProcessAction` (`UpdateQuoteClient`), which does the actual write inside the Function |
| `Process Quote Closure with Azure Function` | Manual (Power App, takes a list of quote IDs). Dispatches to `dhadataverseeditor.../api/CloseQuotes` with a hardcoded reason `"Project completed"`. Confirmed as the **same** Dataverse-editor Function App as "Set Quote Customer" above, a distinct utility service, **not** `dha-reconciliation-balancer` |
| `TimeOpeningBalance` | **Confirmed one-off migration tool**, not ongoing automation: reads a hardcoded Excel file (`TimeImport.xlsx` on a specific OneDrive drive), matches rows to a manually-entered project number, and creates dummy time entries, a dummy invoice, and a reconciliation record per match — explicitly to migrate historical balances from "CoreTime" (see [08-ig-legacy-layer.md](08-ig-legacy-layer.md#the-coretime-bridge--superseded-by-the-fwp-layer)). Contains hardcoded fallback GUIDs — evidence this was run as a manual, supervised cutover step, not something to re-run casually |

### Reminders & configuration

| Flow | Verified behaviour |
|---|---|
| `FWP: Send Time Reminder Email` | **Likely dead/prototype.** A manual HTTP-callable webhook expecting a large pre-computed payload; renders and sends one email. Contains unwired placeholder content and a fake test email address in a response that's never returned (no Response action exists). Nothing found in this pass calls it |
| `fwp_sched_timesheetReminder` | **The actual live reminder system.** Daily recurrence (~11:00 UK). Queries eligible colleagues (excludes service/admin accounts and anyone with `fwp_excludefromreminders`), calls `fwp_instant_getLastWeeksHours` as a child flow per colleague, and — if incomplete — sends an **escalating** reminder by day of week: Monday (employee only) → Tuesday (CC manager, High importance) → Wednesday (CC manager + a named individual, "FINAL REMINDER"). No reminder Thu/Fri. Logs every send to `fwp_timesheetreminder` |
| `fwp_inst_pushAllUserConfigurations` | Manual. Seeds a **new** `fwp_colleagueconfiguration` record (hardcoded 09:00–17:30 pattern, £200 Rate A, one named individual excluded) for every eligible colleague. **No existing-record check — re-running it duplicates configuration records for everyone.** Reads as a one-time initial-setup tool, not something safe to run again |
| `IG-Projects-Alert Users of Project Live` | Confirmed genuine unmigrated IG-era logic (operates entirely on legacy `isc_` fields: `isc_projectlive`, `isc_projectlivedate`, `isc_sitelocations`) living inside an FWP-named solution. Alerts only the Project Manager (not "Users" plural, despite the name) — and does so **twice** (one direct email, one Dataverse email-activity send) with a bug where the email body shows a raw GUID instead of the resolved client name |

## See also

- [00-overview.md](00-overview.md)
- [01-value-calculation-flow-vs-plugin.md](01-value-calculation-flow-vs-plugin.md)
- [03-solution-landscape.md](03-solution-landscape.md)
- [04-flows-and-business-rules.md](04-flows-and-business-rules.md)
- [06-finance-billing-invoicing.md](06-finance-billing-invoicing.md)
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
