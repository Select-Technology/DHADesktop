# Security Roles: DHA-1 → DHA-6

*Derived from the live role configuration in the DHA Planning Production environment
(`dhapd`) as documented in `DHA - Quotes into disbursements/DHA-Security-Roles-Guide.md`
(2026-07-28). Roles are edited directly in Dynamics — if they change, this page (and that
source guide) need refreshing from the live definitions.*

## The one thing to understand first: the roles are cumulative

DHA-1 → DHA-6 are **layers that stack**, not alternatives. To give someone "Level 4,"
assign **DHA-1, DHA-2, DHA-3, and DHA-4** — not DHA-4 alone. Each higher level adds
capability on top of the levels below it; assigning a higher role without the ones below
it produces broken/unexpected behaviour.

## Access scopes

| Scope | Meaning |
|---|---|
| **Own** | Only records the user created / is assigned to |
| **Office** | Records owned by anyone in the user's business unit |
| **Everyone** | All records company-wide |
| **—** | No access |

This is why a user might say "I can only see my own time entries on a job" — the other
entries genuinely exist, they just aren't permitted to see records they don't own.

## What each level adds

| Level | Role name | In plain English |
|---|---|---|
| **DHA-1** | Individual | Base level. Log/manage **own** time. See/edit Jobs and Disbursements **within your office**. Full access to Quotes |
| **DHA-2** | Project Manager | Adds **everyone's** time entries and disbursements on a job, plus reconciliation-data visibility |
| **DHA-3** | Planning Director | Adds the ability to **assign (reassign)** Jobs, Opportunities, and Quotes to other people. No additional create/edit access beyond Level 2 |
| **DHA-4** | Finance User | Full create/edit/delete on Jobs, Disbursements, Disbursement Categories, Disbursement & Time Reconciliation; can edit all time entries. Runs the financial side end-to-end |
| **DHA-5** | Business Admin | **Currently adds nothing** beyond Levels 1–4 — flagged for review, looks like a placeholder |
| **DHA-6** | Finance Data Viewer | Adds view/edit of **Tax Codes** (finance reference data). Nothing else distinctive |

## Cumulative access by level

"Level N" = holds every role from DHA-1 up to and including DHA-N.

| Record type | Lvl 1 | Lvl 2 | Lvl 3 | Lvl 4 | Lvl 5 | Lvl 6 |
|---|---|---|---|---|---|---|
| Time Entries | Own | Everyone | Everyone | Everyone | Everyone | Everyone |
| Disbursements | Office | Everyone | Everyone | Everyone | Everyone | Everyone |
| Jobs / Projects | Office (view/edit) | Office | Office | Everyone (full) | Everyone (full) | Everyone (full) |
| Disbursement Reconciliation | — | View | View | Full | Full | Full |
| Time Reconciliation | — | — | — | Full* | Full* | Full* |
| Disbursement Categories | View | View | View | Full | Full | Full |
| Quotes | Full | Full | Full | Full | Full | Full |

\* Time Reconciliation at Level 4+ is create/view/edit but **not** delete.

Notes:

- **Time Entries at Level 1**: full create/edit/delete, but only on the user's **own**
  entries.
- **Jobs/Projects at Levels 1–3**: view/edit only — **cannot create or delete**. Full
  control (including create/delete) arrives at Level 4.
- **Invoices** (`fwp_dhainvoice`) and **Billing Rates** are **not** part of the DHA-1→6
  ladder at all — separate finance/administration roles govern these. Don't expect any
  DHA level to grant invoice or billing-rate access.
- Two capabilities sit outside the table: Level 3 adds **reassign** rights on
  Jobs/Opportunities/Quotes; Level 6 adds **edit of Tax Codes**.

## Using this for a support ticket

**Symptom**: "On a job's Billings tab I can see my own time entries, but not other
people's."

- **Cause**: the user holds Level 1 (Individual) only — Own-level access to time
  entries.
- **Fix**: add DHA-2 (Project Manager) for team-wide visibility, **keeping DHA-1
  assigned** (roles are cumulative, never replace a lower level).
- **Do not** widen the DHA-1 role definition itself to fix one person — that changes
  visibility for every Individual company-wide. Assign the extra role to the individual
  instead.

**Rules of thumb:**

- To raise someone to Level N, assign DHA-1 through DHA-N — never a level on its own.
- "I can only see my own…" almost always means they're missing the level above their
  current one.
- Anyone doing finance/reconciliation work needs Level 4 (i.e. 1–4 assigned).
- Editing an individual's role assignments affects only them. Editing a **role
  definition** affects everyone who holds it — escalate definition changes rather than
  doing them ad hoc.

## A separate, older role model also exists — check both

The DHA-1→6 ladder above is the **current** model. The environment also still contains
**10 legacy roles from the previous implementation partner** ("IG"/Infinity Software
Consulting) that predate DHA-1→6 entirely: `DHA - Project Manager`,
`DHA - Team Project Manager`, `DHA - Admin`, `DHA - Management KPIs`,
`View Append Projects`, `DHA - Copy of Account Manager`, `DHA - Land Dashboard`,
`DHA - Managing Director`, `DHA - Copy of Project Manager`, `DHA - Finance`. These are
**not** DHA-1→6 under an old name — they're structurally different (one flat role per
persona, each tied to a specific legacy dashboard, rather than a cumulative ladder) and
still exist as live role definitions in the environment. See
[08-ig-legacy-layer.md](08-ig-legacy-layer.md#the-legacy-security-model-dha---role-not-dha-1→6)
for the full list and the dashboards they map to.

**If DHA-1→6 alone doesn't explain a user's access (they can see/do more, or less, than
their DHA-1→6 assignment implies), check whether they also hold one of these legacy
roles** — a user's effective permissions are the union of everything they're assigned,
current or legacy.

## See also

- [00-overview.md](00-overview.md)
- [08-ig-legacy-layer.md](08-ig-legacy-layer.md)
- [13-pitfalls-and-faq.md](13-pitfalls-and-faq.md)
- `DHA - Quotes into disbursements/DHA-Security-Roles-Guide.md` — the original guide this page is adapted from
