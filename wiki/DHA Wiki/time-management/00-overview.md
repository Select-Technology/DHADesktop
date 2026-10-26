# Time Management — Overview

## What this domain is

DHA staff log two kinds of things against the work they do: **Time Entries** (hours
worked) and **Disbursements** (out-of-pocket costs — mileage, printing, etc.). Both are
logged through the "DHA Time Management" desktop app and stored in Dynamics.

Everything a person logs is attached to one of two kinds of "thing you did the work
for":

- A **Job** (called a Project in Dynamics, `msdyn_project`)
- A **Quote** (`quote`) — work logged before a job has a project number

A time entry or disbursement is always attached to exactly one of these — never both,
never neither. The app enforces this itself; it isn't a Dynamics-level rule.

## The end-to-end journey

1. A colleague opens the desktop app and signs in with their Microsoft account (cached
   after the first login, so this is usually silent).
2. They pick a Job or a Quote, and log either:
   - **Time**: a date, hours + minutes, a category (Chargeable / Non-Chargeable /
     Speculative / Hourly Rate), and — for chargeable work — a **charge band** (Rate A,
     B, or C).
   - **Disbursements**: a date, a type (mileage, printing, etc.), and an amount — either
     typed directly or calculated from units × a per-unit charge.
3. For chargeable time entries, Dynamics itself calculates a **£ value**
   (`fwp_value` = hours × the logged-in person's rate for the chosen band). The app never
   writes this value — see
   [dataverse-customizations/01-value-calculation-flow-vs-plugin.md](../dataverse-customizations/01-value-calculation-flow-vs-plugin.md).
4. Time entries lock automatically about a week after the work date, regardless of
   anything else, so people can't indefinitely edit old timesheets.
5. When Finance **reconciles** a time entry or disbursement (assigns it a reconciled
   value), it becomes permanently locked in the app — editing or deleting it is blocked
   with an explanatory message.

## Key terms

| Term | Meaning |
|---|---|
| **Job / Project** | `msdyn_project` — the thing time/disbursements are billed against once work has a project number |
| **Quote** | `quote` — the thing time/disbursements are billed against *before* a project number exists |
| **Classification** | Which of Job/Quote a given time entry or disbursement belongs to |
| **Category** | Chargeable / Non-Chargeable / Speculative / Hourly Rate — how time is labelled for billing |
| **Charge band (Rate A/B/C)** | Chosen per time entry; determines which of the logged-in colleague's three configured rates applies. Always defaults to Rate A on a new entry — it does not remember your last choice |
| **Colleague Configuration** | A per-person Dynamics record (`fwp_colleagueconfiguration`) holding that person's £ value for Rate A/B/C, plus their expected weekday hours (used for holiday calculations, not for validating logged time) |
| **`fwp_value`** | The calculated £ value of a chargeable time entry. Computed server-side, not by the app |
| **Reconciled** | Finance has assigned a reconciled value to the record. Once reconciled, the app blocks further edits/deletes |
| **Locked** | A separate, purely date-driven rule for time entries: entries become uneditable roughly a week after the work date, independent of reconciliation |

## Where to go next

- [01-time-entries.md](01-time-entries.md) — fields, validation, and the locking rules in detail
- [02-disbursements.md](02-disbursements.md) — fields, types, and the (thin) validation that exists
- [03-reconciliation-and-charge-bands.md](03-reconciliation-and-charge-bands.md) — how reconciliation blocks edits, and how charge bands/rates resolve
- [04-pitfalls-and-faq.md](04-pitfalls-and-faq.md) — symptom → cause → fix
- [05-project-on-stop.md](05-project-on-stop.md) — Finance's On Stop flag on Jobs and the red warning when time is recorded against one (DHA-7/DHA-8)
- [06-leave.md](06-leave.md) — the Leave tab that replaces Valto's leave app: rules, tables, plug-ins, flows, year-end and migration runbooks
