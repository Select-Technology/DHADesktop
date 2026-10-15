# Leave planner: desktop app parity with the Valto "Core – Leave Request App"

**Goal:** DHA staff, managers and HR do everything they do today in Valto's *Core – Leave Request App* inside the
DHA Time Management desktop app, with the data in Dynamics. Then the Valto app is retired.

**Status (15 Oct 2026):**

- **Phase A is live in prod.** Approved Valto leave becomes time entries; switch-on is waiting for the
  "Approved Leave" Job. See [DHA-9-leave-plan.md](DHA-9-leave-plan.md).
- **Phase B (this plan) is part-built in dev** on `feature/leave-in-desktop-app`.

Owner: Select Technology (Tom Halverson). Business owner: Harriet Baker (HR).

---

## 1. Where we're starting from

### 1.1 What the Valto app does

Taken from the prod solution `CoreLeaveRequest` (Valto LTD, managed, installed Jul 2022): its 11 flows, the 11 screens
in its canvas app package (last saved 21 Mar 2023), and 326 real requests from prod flow history (11 Sep – 14 Oct 2026).
Data lives in seven SharePoint lists on `dhaplanningcouk.sharepoint.com/sites/Leave-Request`.

**Request types in use** (326 requests, last month):

| Type | Count | Units | Note |
|---|---|---|---|
| Working From Home | 184 | Days | **Not absence**, but goes through the same request/approval flow |
| Holiday Request | 102 | Days | Uses entitlement |
| Medical | 19 | Hours | Part-day appointments, exact times |
| Sick Leave | 16 | Days | |
| Other | 3 | Days | |
| Study Leave | 2 | Days | |
| TOIL (add / take) | 0 | – | Supported by the app behind a "TOIL Toggle"; not used in the last month |

Valto times:
- A full day is 09:00–17:00.
- A morning half day is 09:00–12:30; an afternoon half day is 12:30–17:00.
- "Hours" requests store exact start and end times.

### 1.2 Current state of the desktop version (dev)

| Area | Built |
|---|---|
| **Data** | `fwp_leaverequest` (user-owned)<br>`fwp_leavetype` (name, deducts from entitlement)<br>`fwp_leaveentitlement` (user-owned, **hours**: entitlement, carried forward, purchased, adjustment)<br>`fwp_publicholiday`<br>`fwp_colleagueconfiguration.fwp_leaveapprover`, plus the existing working pattern |
| **Server rules** (`LeaveRequestPlugin`) | Hours worked out server-side from the working pattern and bank holidays<br>Approver defaulted<br>Overlaps rejected<br>Request shared with its approver<br>Approve → time entries; cancel → entries deactivated (DHA-10)<br>Delete of booked leave blocked<br>`fwp_lastevent` set for emails |
| **My leave tab** | Request form (full day / morning / afternoon / set times, type, comment, live hours preview)<br>Current-year balance<br>My requests<br>Cancel (pending → withdrawn; approved → cancellation request) |
| **Approvals tab** | **HR only** (HR@ group team + stsadmin)<br>Waiting for decision (new + cancellation)<br>Detail with the person's balance before and after, and who else is off<br>Approve / reject with a comment<br>Who's off in the next 30 days |
| **Emails** | "Leave - Notifications": new / cancellation / withdrawn → HR; outcomes → employee<br>"Leave - Approval reminder": weekdays 10:00 |
| **Security** | **Staff:** own requests and entitlements via DHA-1<br>**HR:** "DHA - Leave Administrator" role through the HR@ group team |
| **Tests** | `DHA.Dataverse/Tests`: 56 automated DHA-9 checks against dev, plus unit tests |

---

## 2. Feature parity matrix

Status: ✅ done · 🟡 partly · ❌ not started. IDs are used throughout this plan.

### Employees

| ID | Valto feature | Desktop today | Work |
|---|---|---|---|
| **E1** | Choose a request type from the configured list (icons) | 🟡 Dropdown of `fwp_leavetype` | Add the type flags in **D2**; hide TOIL "add" from the normal form |
| **E2** | **Days** requests: dates + Full day / ½ AM / ½ PM. Balance and cost shown in **days** | 🟡 Morning/afternoon split the working pattern; everything is in **hours** | Days/hours units (**D1**); show cost and new balance in the person's units |
| **E3** | **Hours** requests: number of hours (default from config) + start/end times | 🟡 "Set times" gives the hours | Hours-unit types (e.g. Medical) default to the times entry; default duration from settings |
| **E4** | Shows current balance, cost of this request, new balance; public holidays in the range | 🟡 Preview hours + bank holiday note; balance on the side | Cost and new balance on the form, in units |
| **E5** | Validation: end before start; **can't span two leave years** | 🟡 End before start; overlaps (Valto doesn't check overlaps) | Leave-year span rule in the plug-in and the form |
| **E6** | Mandatory "Details" field; confirmation summary before submitting | ❌ Comment optional, no confirmation | Required details; confirmation dialog |
| **E7** | **Working From Home** requests (approved, not absence, no entitlement, no time entry) | ❌ | Non-absence types (**D2**). The plug-in must not create time entries for them; they are still approved and shown |
| **E8** | **TOIL**: claim (date, from/to, breaks, what was achieved) → approved claims build a TOIL balance; "Take TOIL" spends it | ❌ | **Only if Harriet confirms TOIL is used** (**Q4**). TOIL claim form, TOIL balance, take-TOIL type |
| **E9** | Leave balance for **previous / current / next** year: base, purchased (toggle), carried forward, total, taken (**counts pending**), remaining, carried to next year, TOIL; shows your approver | 🟡 Current year only; pending separate; no approver | Year picker; full breakdown; approver shown; agree how pending counts (**Q6**) |
| **E10** | My requests: filter upcoming/past, status, type; detail with approver comments and when cancellation was requested | 🟡 List only | Filters; detail panel |
| **E11** | Cancel **with a comment**. Pending future requests cancel instantly; approved ones go to the approver | 🟡 Instant/approval logic done; no comment | Cancellation comment (`fwp_cancellationcomments`); pending *past* requests go to the approver (Valto rule) |
| **E12** | "Company Holidays" list (past ones optional) | ❌ | Read-only list in the Leave tab |
| **E13** | Booking on behalf of someone (only people on the "Book on behalf" list), choosing the employee and their approver | ❌ | **H3** |

### Managers / approvers

| ID | Valto feature | Desktop today | Work |
|---|---|---|---|
| **M1** | **Line managers** approve their own people; an **Additional Approver** can approve too ("Approvals only") | ❌ HR only (by decision, 14 Oct) | Approver and additional approver per person (**D4**); Approvals tab for anyone who approves someone. **Q2** decides whether this happens or HR stays the only approver |
| **M2** | Approvals list with "show approval requests" / "show cancellation requests" filters; count badge | 🟡 Combined list; no filters or badge | Filters; count on the Leave tab header |
| **M3** | When approving: overlapping requests, filtered **all / my employees / by department** | 🟡 "Also off" for approved leave across everyone | Include pending; filters; department (**D4**) |
| **M4** | "My employees": pick a person, see their balance (all years) | ❌ | Team view with per-person balance |
| **M5** | All employee requests: browse with filters (upcoming/past, status, type) | ❌ | Team requests view |
| **M6** | **Cancel on behalf**: instant, emails the employee | ❌ (HR can set Cancelled in Dynamics) | Button in the team requests view; "CancelledByHR" email already exists |

### HR / administration

| ID | Valto feature (list or flow) | Desktop today | Work |
|---|---|---|---|
| **A1** | *Employee Configuration*: approver, additional approver, leave-year start, base entitlement, units, working days, (department) | 🟡 Approver lookup; working pattern exists | Fields on colleague configuration (**D4**); HR screen |
| **A2** | *Entitlement Balances* per person per year | 🟡 Table exists, no screen | HR entitlement screen; units |
| **A3** | **Year-end carry forward** (monthly flow, applied in the configured month, capped at the limit) | ❌ | Scheduled flow + Custom API (**D6**) |
| **A4** | **Generate next year's entitlement** (monthly flow) | ❌ | Scheduled flow + Custom API (**D6**) |
| **A5** | New-starter entitlement on creating an employee configuration (**switched off in Valto since Sep 2025**) | ❌ | Create the current and next year when a colleague configuration is created, if HR wants it (**Q7**) |
| **A6** | *Request Type Configuration*: has entitlement, forced units, icon | 🟡 Deducts flag only | **D2** |
| **A7** | *App Configuration*: carry-forward limit, generate-on date, default hours, TOIL toggle, show purchased leave, admin account | ❌ | Environment variables (**D5**) |
| **A8** | *Public Holidays* list | ✅ `fwp_publicholiday` from GOV.UK | Add an HR screen or a Dynamics view to add DHA-only closure days, if needed |
| **A9** | *Book on behalf of users* list | ❌ | A Dynamics team, "Leave - Book on behalf" (**H3**) |

### Notifications and integrations

| ID | Valto feature | Desktop today | Work |
|---|---|---|---|
| **N1** | New request → email the **approver** (notes when booked on behalf) | 🟡 Goes to HR@ | Send to the request's approver(s) once **M1** is decided; mention booked-by |
| **N2** | Outcome emails (approved, rejected, cancellation approved or declined) | ✅ | Wording review with Harriet |
| **N3** | Weekday 10:00 reminder **per approver** of their outstanding requests | 🟡 One email to HR | Per approver once **M1** is decided |
| **N4** | **Shared Outlook calendar**: event on approval, deleted on cancellation (`CalendarID`) | ❌ | **Only if still used** (**Q5**). Flow on Approved / Cancelled using the request's `fwp_calendareventid` |
| **N5** | Deep links from emails to the app | ❌ | Link to the Dynamics record (works on phone); optional `dha-time://` desktop link later |
| **N6** | Item-level SharePoint permissions (employee, approver, owners) | ✅ Different mechanism: user-owned rows + sharing + roles | Extend sharing to additional approvers (**M1**) |
| **N7** | Mobile/browser access (the canvas app works anywhere) | ❌ Desktop only | Model-driven app "DHA Leave" for approvers and HR (**X1**) |

### Not carried over (by design)

- **Valto branding settings** (colours, logo, US date format, debug mode): not needed.
- **Valto's "user override":** the desktop app already has admin impersonation.

---

## 3. Design changes

**D1 – Units (days and hours).**
- Keep **hours** as the stored truth on each request (`fwp_hours`, already calculated server-side).
- Add `fwp_days` (calculated by the plug-in: 1 per full working day, 0.5 per half day; hours ÷ that day's expected
  hours for part days).
- Each entitlement row gets `fwp_units` (Days/Hours). Balances are computed in that unit.
- Leave types get "forced units" (Medical = Hours).
- Pending **Q1**.

**D2 – Leave type behaviour flags** on `fwp_leavetype`:

| Flag | Meaning |
|---|---|
| `fwp_isabsence` | Creates "Approved leave" time entries. Off for Working From Home |
| `fwp_deductsfromentitlement` | Exists today |
| `fwp_forcedunits` | Days or Hours |
| `fwp_requiresapproval` | Yes for all types today |
| `fwp_istoil` | Add / Take, if **E8** goes ahead |
| `fwp_sortorder` | Order in the list |
| `fwp_icon` | Optional |

The plug-in's time-entry step checks `fwp_isabsence`. The phase A setting `fwp_LeaveExcludedRequestTypes`
then becomes redundant after cutover.

**D3 – Leave years.**
- A leave year runs from the person's `fwp_leaveyearstart` (day/month) for 12 months.
- Each `fwp_leaveentitlement` row covers one leave year.
- The plug-in rejects requests that span two leave years (**E5**).
- Balance views select by year (**E9**).

**D4 – Colleague configuration additions** (on the existing `fwp_colleagueconfiguration`):
- **Leave fields:** `fwp_leaveapprover` (done), `fwp_additionalleaveapprover`, `fwp_leaveyearstart`, `fwp_baseentitlement`, `fwp_entitlementunits`, `fwp_department` (or reuse an existing department field; confirm).
- **Rates:** this table also holds charge rates, which every member of staff can already read (see wiki
  *02-security-roles*). The new leave fields add nothing sensitive; rates are out of scope here.

**D5 – Settings** (environment variables in `FWPLeaveManagement`):
- `fwp_LeaveCarryForwardLimit`
- `fwp_LeaveCarryForwardMonth`
- `fwp_LeaveGenerateNextYearMonth`
- `fwp_LeaveDefaultHours`
- `fwp_LeaveTOILEnabled`
- `fwp_LeaveShowPurchased`
- `fwp_LeaveApproverEmail` (exists)
- `fwp_LeaveEmailOverride` (exists, dev only)

**D6 – Year-end automation.** Custom APIs in `DHA.Leave.Plugins`, called by scheduled flows:
- `fwp_GenerateLeaveYear`: creates next year's entitlement rows (base + purchased 0).
- `fwp_CarryForwardLeave`: unused entitlement, capped at the limit → next year's carried forward, and records
  "carried to next year" on the old row.

Both are idempotent and unit tested, like `LeaveDayCalculator`.

**D7 – Ownership.**
- **Requests:** a request is assigned to the **employee** even when HR books it (fixes the known gap in
  `test_request_booked_on_behalf_is_owned_by_the_employee`).
- **Entitlement rows:** owned by the employee.

---

## 4. Delivery plan

Each stage ends with its automated tests passing in dev and a demo to Harriet.

| Stage | Contents | Depends on | Estimate (days) |
|---|---|---|---|
| **0. Decisions** | Harriet answers **Q1–Q9**; agree the wording of emails and screens | – | 0.5 meeting |
| **1. Model** | D1–D5, D7: schema, type flags, units, leave years, ownership fix; seed DHA's real leave types | 0 | 3 |
| **2. Employee parity** | E1–E7, E9–E12: units-aware form, cost/new balance, leave-year rule, mandatory details + confirmation, WFH requests, multi-year balance, request filters, cancellation comment, company holidays | 1 | 6 |
| **3. Approver parity** | M1–M6, N1, N3, N6: approvers and additional approvers, filters/badge, team views, per-person balance, cancel on behalf, per-approver emails and reminder | 1, Q2 | 6 |
| **4. HR administration** | A1, A2, A7, A9, E13: colleague leave settings screen, entitlement editor, settings, book on behalf | 1 | 4 |
| **5. Year-end** | A3–A5 (D6): generate year, carry forward, new starters | 1, Q3/Q7 | 3 |
| **6. Optional** | E8 TOIL, N4 Outlook calendar, N5 links, X1 model-driven app | Q4, Q5, Q8 | 2–6 |
| **7. Migration** | §5 scripts, dry runs against a copy of the lists, reconciliation report | 1 | 4 |
| **8. Release and cutover** | §6: merge desktop branches, UAT, parallel run, go-live, Valto retirement | all | 4 |
| | **Total** | | **~32–36 days**, plus optional 2–6 |

Stages 2–5 can overlap once stage 1 is done.

---

## 5. Migration from SharePoint

Read through a Power Automate flow using the existing stsadmin SharePoint connection, which was proved to reach the
lists on 14 Oct. The list ids are in the Valto app's prod environment variables. Leave Request Data is
`718145ce-0b2d-4396-af86-ce36aabc8a29`.

| Valto list | Destination | Notes |
|---|---|---|
| Request Type Configuration | `fwp_leavetype` | Map Has Entitlement, Forced Units; set `fwp_isabsence` (WFH = No) |
| Employee Configuration | `fwp_colleagueconfiguration` leave fields | Match on employee email (`internalemailaddress` or UPN). Report anyone with no colleague configuration |
| Entitlement Balances | `fwp_leaveentitlement` | Previous, current and next leave year. Convert days↔hours only for display; keep the unit |
| Leave Request Data | `fwp_leaverequest` | **Current and next leave year, plus the previous year** (balances need it). `fwp_legacyid = "LR-<ID>"`, so the time entries phase A created are **adopted, not duplicated**. Imported with the plug-in's time-entry step **bypassed**, then reconciled |
| Public Holidays | compare with `fwp_publicholiday` | Add any DHA-only closure days |
| Book on behalf of users | team "Leave - Book on behalf" | |

**Reconciliation report:** per person, Valto remaining vs desktop remaining per year. It must match to the half day
before cutover.

---

## 6. Release and cutover

1. **Desktop build.** Merge `feature/dha-7-8-project-on-stop` and `feature/leave-in-desktop-app` into one release.
   That release carries the DHA-8 banner, the DHA-10 active-only filter and the Leave tab. Phase A goes live first
   on its own (see the DHA-9 plan).
2. **Deploy `FWPLeaveManagement` to prod as managed.**
   - Don't overwrite prod forms: rebuild any changed form from prod's definition (the lesson from the DHA-7/8
     deployment).
   - Bind connections on import.
   - Set `fwp_LeaveEmailOverride` blank.
3. **UAT** with Harriet and two line managers, against the stage checklists and the `Tests/README` manual steps.
4. **Parallel run (1 week).** HR and the pilot users book in the desktop app; migration deltas run nightly.
   Valto stays the system of record.
5. **Cutover day:**
   - Valto read-only: remove *Contribute* from the SharePoint lists.
   - Final delta migration + reconciliation.
   - Switch off the Valto flows and phase A's "Leave Request - Record Time Entries".
   - Release the desktop app to everyone.
   - Staff email from HR.
6. **Retire Valto after one quiet month:** remove the `CoreLeaveRequest` solution, keep the SharePoint site
   read-only as an archive.

**Rollback:** until step 6, re-enable the Valto flows and SharePoint permissions. Desktop requests made after cutover
are exported back by script. Phase A time entries are unaffected.

---

## 7. Testing

- **Automated (dev):** extend `DHA.Dataverse/Tests` per feature ID. The test names reference the IDs,
  e.g. `test_e5_request_cannot_span_leave_years`.
- **New unit tests:** units conversion, leave-year boundaries, carry-forward maths, TOIL balance.
- **Migration:** dry run against a copy of the lists, plus the reconciliation report above. Zero unexplained differences.
- **Manual desktop checklist** per stage (as in `Tests/README.md`).
- **UAT sign-off by Harriet** before cutover.

---

## 8. Decisions needed (Harriet / DHA)

| # | Question | Affects |
|---|---|---|
| **Q1** | Is entitlement counted in **days** or **hours**, per person or for everyone? How do part-timers' days work? | D1, E2–E4, E9 |
| **Q2** | Who approves: **line managers** (as Valto allows, with additional approvers) or **HR only** (as the desktop does today)? | M1–M6, N1, N3 |
| **Q3** | Leave year start (one date for everyone, or per person); **carry-forward limit**; how purchased leave works | D3, A3, A4 |
| **Q4** | Is **TOIL** used, and should it come across? | E8 |
| **Q5** | Is the **shared Outlook leave calendar** still used? | N4 |
| **Q6** | Should pending requests count as "taken" in the balance (Valto does) or be shown separately (desktop does)? | E9 |
| **Q7** | Should new starters get an entitlement automatically (Valto's flow has been off since Sep 2025)? | A5 |
| **Q8** | Do approvers need **phone/browser** access? | X1 |
| **Q9** | Should **Working From Home** requests still need approval and appear in team views? | E7, M3 |

## 9. Risks

| Risk | Mitigation |
|---|---|
| Desktop-only access: nothing on phones | X1 model-driven app for approvers and HR, or keep Valto for WFH only (not recommended) |
| Balance mismatches after migration | Reconciliation report gate; parallel run |
| Two systems live at once (double booking) | A short parallel run limited to pilot users; Valto made read-only at cutover |
| Form drift between dev and prod | Rebuild forms from prod; compare before every import (see the DHA-7/8 notes) |
| Leave entries carry £ value (hours × Rate A) | Finance decision; independent of this work (see the DHA-9 plan) |
| The desktop app release is shared with other changes | One combined, tested release (§6.1) |
