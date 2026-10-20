# Leave planner: desktop app parity with the Valto "Core – Leave Request App"

**Goal:** DHA staff, managers and HR do everything they do today in Valto's *Core – Leave Request App* inside the
DHA Time Management desktop app, with the data in Dynamics. Then the Valto app is retired.

**Status (15 Oct 2026):** decisions received from HR (§0).

- **Phase A is live in prod.** Approved Valto leave becomes time entries; switch-on is waiting for the
  "Approved Leave" Job. See [DHA-9-leave-plan.md](DHA-9-leave-plan.md).
- **Phase B (this plan) is part-built in dev** on `feature/leave-in-desktop-app`.

Owner: Select Technology (Tom Halverson). Business owner: Harriet Baker (HR).

## 0. Decisions (Harriet Baker, 15 Oct 2026)

**Full parity is required.** Staff shouldn't use two systems for leave and for working from home or appointments.

| # | Decision | Effect on this plan |
|---|---|---|
| Q1 | Entitlement is counted in **days**, the same for everyone. Part-timers work **specific days**, which HR sets | D1: days only (hours stay internal, for timesheets). Working days come from the colleague's pattern |
| Q2 | **Line managers approve, with additional approvers** | M1–M6, N1, N3 in scope. HR keeps admin and oversight |
| Q3 | Leave year starts **1 January for everyone**. **Carry forward up to 5 days**, which HR can override. **No purchased leave** | D3 simplified; A3 with a per-person override; purchased leave dropped |
| Q4 | **TOIL not needed** | E8 dropped |
| Q5 | **The shared Outlook leave calendar stays** | N4 in scope |
| Q6 | **Pending counts as taken** in the balance | E9 changes to match Valto |
| Q7 | **No automatic new-starter entitlement.** HR sets each one up as people start | A5 dropped; the A2 HR screen must make set-up quick |
| Q8 | **Working From Home still needs approval and appears in team views** | E7, M3 |

### Follow-ups

| # | Question | Outcome |
|---|---|---|
| F1 | Hours a day of leave puts on the timesheet | ✅ **Decided (15 Oct): Harriet's figures, 8.5h Mon–Thu and 8h Fri.** A half day is half of that (4.25 / 4). Held in the setting `fwp_LeaveDayHours`. **Built and tested in dev, and live in phase A.** Colleague configuration still says *which* days someone works; its expected hours (8 / 7.5 in prod) are not changed, because they also drive timesheet reminders and daily progress |
| F2 | Should next year's entitlement be created automatically each January for **existing** staff, with only new starters done by hand? | **Open** |
| F3 | Where part-timers' working days are kept | ✅ The desktop app reads and writes Dynamics (colleague configuration); the Valto app is what's being replaced. Today Harriet sets working days in **Valto's Employee Configuration list**, while Dynamics keeps a separate copy for timesheets. After cutover, **Dynamics is the only place**: HR edits it in the desktop HR screen (A1). Migration compares the two and reports differences (8 people have no working days in Dynamics) |
| F4 | Phone or browser access for approvers | ✅ **No phone app.** A phone app would be a separate development project. X1 is removed; approvals happen in the desktop app |
| F5 | Wording of shared calendar events | ✅ **"Out of Office"** for every type, so the absence type never appears on the shared calendar |
| F6 | The "By Department" filter when approving: where is department held? | **Open** |

---|---|---|
| F1 | "8.5 hours Mon–Thu, 8 hours Friday": is that the office day or the hours worked? Prod colleague configurations hold **08:30–17:30 with a 1h break = 8h worked Mon–Thu**, and **08:30–17:00 = 7.5h on Friday**. What should a day's leave put on the timesheet: 8 / 7.5, or 8.5 / 8? | A leave day records that day's expected hours from colleague configuration. That's live in phase A as soon as it's switched on |
| F2 | Should **next year's entitlement** be created automatically each January for **existing** staff (Valto's flow does this), with only new starters done by hand? | A4 |
| F3 | Part-timers' working days are set in the Valto app. Dynamics also holds a working pattern for every colleague (used for timesheet reminders). Once Valto is retired, **HR maintains the Dynamics pattern**. OK? 8 people currently have no working days at all in Dynamics | Single source of truth; migration compares the two |
| F4 | Do line managers need to approve from a **phone or browser**? The canvas app works on phones; the desktop app doesn't | X1. More relevant now that line managers approve |
| F5 | Shared calendar: whose calendar is it, and what should events say? Valto's emails use the request type; a staff-wide calendar showing "Medical" or "Sick Leave" has the same privacy issue DHA-9 avoids on timesheets | N4 |
| F6 | The "By Department" filter when approving: where is department held today? | M3 |

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
| **E1** | Choose a request type from the configured list (icons) | ✅ Type chosen first; list in `fwp_sortorder` order (no icons) | Add the type flags in **D2**; hide TOIL "add" from the normal form |
| **E2** | **Days** requests: dates + Full day / ½ AM / ½ PM. Balance and cost shown in **days** | ✅ Full day / Morning / Afternoon; cost and balance in days, hours shown for the timesheet | Show cost, balance and new balance in **days** (**D1**, Q1). Hours remain for the timesheet only |
| **E3** | **Hours** requests: number of hours (default from config) + start/end times | ✅ Hours types (Medical) switch the form to start/end times (default 09:00–10:00) | Hours-unit types (e.g. Medical) default to the times entry; default duration from settings |
| **E4** | Shows current balance, cost of this request, new balance; public holidays in the range | ✅ Days, hours, remaining now and after; company holidays in the range named | Cost and new balance on the form, in units |
| **E5** | Validation: end before start; **can't span two leave years** | ✅ End before start and the 31 Dec rule checked on the form and in the plug-in; overlaps of absences still rejected | Leave-year span rule in the plug-in and the form |
| **E6** | Mandatory "Details" field; confirmation summary before submitting | ✅ Details required; confirmation summary before sending | Required details; confirmation dialog |
| **E7** | **Working From Home** requests (approved, not absence, no entitlement, no time entry) | ✅ WFH has its own wording, no balance impact, no time entries | Non-absence types (**D2**). The plug-in must not create time entries for them; they are still approved and shown |
| ~~E8~~ | TOIL claims and balance | – | **Out of scope** (Q4: not needed) |
| **E9** | Leave balance for **previous / current / next** year: base, carried forward, total, taken (**counts pending**), remaining, carried to next year; shows your approver | ✅ Previous / current / next year; base + carried forward + adjustment, taken, booked (incl. pending), remaining, carried to next year; approvers shown | Year picker; full breakdown; approver shown; **pending counted as taken** (Q6). No purchased leave or TOIL (Q3, Q4) |
| **E10** | My requests: filter upcoming/past, status, type; detail with approver comments and when cancellation was requested | ✅ Upcoming / past / all, status and type filters; detail panel with comments and cancellation details | Filters; detail panel |
| **E11** | Cancel **with a comment**. Pending future requests cancel instantly; approved ones go to the approver | ✅ Cancel with a reason (required when it goes to the approver). **Changed from Valto:** see stage 2 notes | Cancellation comment (`fwp_cancellationcomments`); pending *past* requests go to the approver (Valto rule) |
| **E12** | "Company Holidays" list (past ones optional) | ✅ Company holidays list, past ones optional | Read-only list in the Leave tab |
| **E13** | Booking on behalf of someone (only people on the "Book on behalf" list), choosing the employee and their approver | ✅ "Booking for" on My leave for HR and the book-on-behalf team, with an optional approver; their balance and working days are used. The plug-in refuses anyone else | **H3** |

### Managers / approvers

| ID | Valto feature | Desktop today | Work |
|---|---|---|---|
| **M1** | **Line managers** approve their own people; an **Additional Approver** can approve too ("Approvals only") | ✅ Approvals and My team show for anyone named as approver or additional approver, listing only their people; HR (Leave Administrator) see everyone | **In scope (Q2).** Approver and additional approver per person (**D4**); the Approvals tab shows for anyone who approves someone and lists only their people. HR (Leave Administrator) still sees everyone |
| **M2** | Approvals list with "show approval requests" / "show cancellation requests" filters; count badge | ✅ New / cancellation filters; count on the Approvals tab | Filters; count on the Leave tab header |
| **M3** | When approving: overlapping requests, filtered **all / my employees / by department** | ✅ Who's off includes pending, Everyone / My people filter (`fwp_GetTeamLeave`). Type hidden except for your own people, WFH, or HR. Department waits for F6 | Include pending; filters; department (**D4**) |
| **M4** | "My employees": pick a person, see their balance (all years) | ✅ My team: pick a person, balance for last / this / next year (`fwp_GetLeaveBalance`) | Team view with per-person balance |
| **M5** | All employee requests: browse with filters (upcoming/past, status, type) | ✅ Their requests with when / status / type filters and detail | Team requests view |
| **M6** | **Cancel on behalf**: instant, emails the employee | ✅ Cancel on their behalf, reason required; they get the "cancelled" email naming who cancelled | Button in the team requests view; "CancelledByHR" email already exists |

### HR / administration

| ID | Valto feature (list or flow) | Desktop today | Work |
|---|---|---|---|
| **A1** | *Employee Configuration*: approver, additional approver, leave-year start, base entitlement, units, working days, (department) | ✅ HR tab: approver, additional approver, base entitlement, working week (start / finish / break), through `fwp_SetLeaveSettings`. Department waits for F6 | Fields on colleague configuration (**D4**); HR screen |
| **A2** | *Entitlement Balances* per person per year | ✅ HR tab: entitlement per year incl. new starters and the carry-forward limit override; one row per person per year, owned by the employee | HR entitlement screen; units |
| **A3** | **Year-end carry forward**, capped at the limit | ❌ | Runs once each January: unused days carried forward, **capped at 5** (Q3). **HR can override per person** (`fwp_carryforwardoverride` on the entitlement row) before or after the run |
| **A4** | **Generate next year's entitlement** | ❌ | Runs each January for existing staff, from their base entitlement. **Pending F2** |
| ~~A5~~ | New-starter entitlement created automatically | – | **Out of scope** (Q7). HR creates new starters' entitlement on the A2 screen |
| **A6** | *Request Type Configuration*: has entitlement, forced units, icon | 🟡 Deducts flag only | **D2** |
| **A7** | *App Configuration*: carry-forward limit, generate-on date, default hours, TOIL toggle, show purchased leave, admin account | ✅ Environment variables: carry-forward limit (5), leave-day hours. No TOIL or purchased settings | Environment variables (**D5**): carry-forward limit (5) and default hours only. No TOIL or purchased settings |
| **A8** | *Public Holidays* list | ✅ `fwp_publicholiday` from GOV.UK | Add an HR screen or a Dynamics view to add DHA-only closure days, if needed |
| **A9** | *Book on behalf of users* list | ✅ Owner team "Leave - Book on behalf"; HR add members in Dynamics | A Dynamics team, "Leave - Book on behalf" (**H3**) |

### Notifications and integrations

| ID | Valto feature | Desktop today | Work |
|---|---|---|---|
| **N1** | New request → email the **approver** (notes when booked on behalf) | ✅ To the request's approver, additional approver copied, HR if none; says when booked on someone's behalf | Send to the request's approver and additional approver (Q2); mention booked-by |
| **N2** | Outcome emails (approved, rejected, cancellation approved or declined) | ✅ | Wording review with Harriet |
| **N3** | Weekday 10:00 reminder **per approver** of their outstanding requests | ✅ One reminder per approver (and additional approver) listing their waiting requests | One email per approver listing their outstanding requests (Q2) |
| **N4** | **Shared Outlook calendar**: event on approval, deleted on cancellation (`CalendarID`) | ✅ "Leave - Outlook calendar" flow (dev). "Out of Office - Name", shown as Out of Office, employee and approver invited; moves when dates change, removed on cancellation. Working From Home not added | **In scope (Q5).** Flow on Approved / Cancelled / date change using `fwp_calendareventid`. Same calendar as Valto (its `new_CalendarID`). Events say **"Out of Office"** (F5) |
| **N5** | Deep links from emails to the app | ❌ | Link to the Dynamics record (works on phone); optional `dha-time://` desktop link later |
| **N6** | Item-level SharePoint permissions (employee, approver, owners) | ✅ Shared with approver and additional approver; changing someone's approver shares their leave from last year onwards and moves waiting requests | Extend sharing to additional approvers (**M1**) |
| ~~N7~~ | Mobile/browser access (the canvas app works anywhere) | – | **Out of scope (F4).** No phone app; a phone app would be its own project |

### Not carried over (by design)

- **Valto branding settings** (colours, logo, US date format, debug mode): not needed.
- **Valto's "user override":** the desktop app already has admin impersonation.

---

## 3. Design changes

**D1 – Units: days (Q1).**
- Entitlement, balances and request cost are in **days**.
- Each request stores `fwp_days`, calculated by the plug-in: 1 per full working day, 0.5 per half day, hours ÷ that
  day's expected hours for part days such as a Medical appointment.
- It keeps `fwp_hours` for the timesheet entries.
- `fwp_leaveentitlement` moves from hours to days: rename the columns and convert, since it's dev-only so far.
- Types that are booked in hours (Medical) still take times, but count in days against any entitlement.

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

**D3 – Leave years: calendar years (Q3).**
- 1 January to 31 December for everyone. One `fwp_leaveentitlement` row per person per year.
- The plug-in rejects requests that span 31 Dec / 1 Jan (**E5**): book them as two requests, as Valto requires.

**D4 – Colleague configuration additions** (on the existing `fwp_colleagueconfiguration`):
- **Leave fields:** `fwp_leaveapprover` (done), `fwp_additionalleaveapprover`, `fwp_baseentitlement` (days),
  `fwp_department` (or reuse an existing field; **F6**).
- **Working days:** the existing per-day expected hours are the working pattern (0 = not a working day; Q1, **F3**).
- **Rates:** this table also holds charge rates, which every member of staff can already read (see wiki
  *02-security-roles*). The new leave fields add nothing sensitive; rates are out of scope here.

**D5 – Settings** (environment variables in `FWPLeaveManagement`):
- `fwp_LeaveCarryForwardLimit` (days, default **5**)
- `fwp_LeaveDefaultHours` (for Hours-type requests)
- `fwp_LeaveCalendarId` (the shared Outlook calendar)
- `fwp_LeaveApproverEmail` (exists; becomes the HR copy address)
- `fwp_LeaveEmailOverride` (exists, dev only)

**D6 – Year-end automation (January).** Custom APIs in `DHA.Leave.Plugins`, called by one scheduled flow on 1 Jan:
- `fwp_GenerateLeaveYear`: next year's rows from base entitlement, for existing staff (**F2**).
- `fwp_CarryForwardLeave`: unused days, capped at 5 unless the person's `fwp_carryforwardoverride` is set, go into
  the new year's carried forward. Records "carried to next year" on the old row.

Both are idempotent, safe to re-run if HR changes an override, and unit tested.

**D7 – Ownership.**
- **Requests:** a request is assigned to the **employee** even when HR books it (fixes the known gap in
  `test_request_booked_on_behalf_is_owned_by_the_employee`).
- **Entitlement rows:** owned by the employee.

---

## 4. Delivery plan

Each stage ends with its automated tests passing in dev and a demo to Harriet.

| Stage | Contents | Depends on | Estimate (days) |
|---|---|---|---|
| **0. Decisions** | ✅ Q1–Q8 and F1, F3–F5 answered 15 Oct. **F2, F6 open** | – | – |
| **1. Model** ✅ *(dev, 16 Oct)* | D1–D5, D7: schema, type flags (incl. WFH), days, calendar years, approver fields, ownership fix; seed DHA's real leave types. Department (F6) still to add | F6 | 3 |
| **2. Employee parity** ✅ *(dev, 16 Oct)* | E1–E7, E9–E12: days-based form and balance (pending counted), year-span rule, mandatory details + confirmation, WFH requests, multi-year balance, request filters, cancellation comment, company holidays | 1 | 5 |
| **3. Approver parity** ✅ *(dev, 16 Oct)* | M1–M6, N1, N3, N6: line managers + additional approvers, filters/badge, team views incl. WFH, per-person balance, cancel on behalf, per-approver emails and reminder | 1 | 6 |
| **4. HR administration** ✅ *(dev, 16 Oct)* | A1, A2, A7, A9, E13: colleague leave settings, entitlement editor (incl. new starters, carry-forward override), book on behalf | 1 | 4 |
| **5. Year-end** | A3, A4 (D6): January generate + carry forward with override | 1, F2 | 2 |
| **6. Outlook calendar** ✅ *(dev, 16 Oct)* | N4 ("Out of Office" events) | – | 1.5 |
| **7. Optional** | N5 email links into Dynamics records | – | 0.5 |
| **8. Migration** | §5 scripts, dry runs against a copy of the lists, reconciliation report | 1 | 4 |
| **9. Release and cutover** | §6: merge desktop branches, UAT, parallel run, go-live, Valto retirement | all | 4 |
| | **Total** | | **~30 days**, plus optional 0.5 |

Stages 2–5 can overlap once stage 1 is done.

---

### Stage 1 notes (16 Oct)

- **Booked on behalf.** A request is assigned to the employee only if they can read leave requests (DHA-1). Dynamics
  refuses the assignment otherwise, and a failed call can't be caught inside the save, so the plug-in checks first.
  Without DHA-1 the booking stays with whoever made it. Everyone holds DHA-1 in prod, so it applies everywhere there.
  In dev no test user can hold DHA-1 (it isn't available in their business unit), so that one check is skipped in dev.
- **Updating a plug-in step's pre-image fails** with a generic error. The script deletes and recreates it instead.
- **Tests (dev):** DHA-9 66 pass, 1 skipped (above); DHA-10 10 pass; 49 unit tests pass.

### Stage 2 notes (16 Oct)

- **Cancelling a pending request that has already started.** Valto sends it to the approver. Here it's withdrawn
  straight away, like any pending request, and the approver gets the "withdrawn" email. Sending it to the approver
  would have put unapproved leave on the timesheet, and declining the cancellation would have approved it. The plug-in
  now refuses Pending → Cancellation requested from any client. **Check with Harriet.**
- **Hours types** default to 09:00–10:00 on the form. `fwp_LeaveDefaultHours` isn't used for this yet.
- **Tests (dev):** DHA-9 70 pass, 1 skipped (3 new for cancelling); DHA-10 10 pass; 54 unit tests pass.

### Stage 3 notes (16 Oct)

- **What line managers can see.** They only hold DHA-1. Their people's requests are shared with them. Balances and
  "who's off" come from two read-only Custom APIs (`fwp_GetLeaveBalance`, `fwp_GetTeamLeave`) that check the caller
  and then read as the system. No new role, and no access to colleague configuration or entitlement tables.
- **Leave type privacy.** In "who's off", only HR, the person's own approvers, and non-absence types (Working From
  Home) show the type. Everyone else sees name, dates and status, like the "Out of Office" calendar.
- **Changing an approver** in colleague configuration (`LeaveApproverChangePlugin`) shares that person's leave from
  1 January last year with the new approvers, and moves requests still waiting for a decision to them. The new
  approver isn't emailed about requests that move. They appear in the next morning's reminder.
- **Cancelled on someone's behalf** (by an approver or HR, pending or approved) now sends the employee the
  "cancelled" email naming who did it, not the approver a "withdrawn" email.
- **Fixed:** a one-day request saved without an end date wasn't caught by the overlap check. The plug-in now stores
  the end date.
- **Tests (dev):** DHA-9 78 pass, 1 skipped; DHA-10 10 pass; 60 unit tests pass.

### Stage 4 notes (16 Oct)

- **HR still can't write colleague configuration** (it holds rates). The HR tab saves through `fwp_SetLeaveSettings`,
  which checks the caller is HR and writes only approver, additional approver, base entitlement and working times.
- **Working days are times, not hours.** Expected hours per day are a formula column (finish − start − break) that reads
  the times by character position, so a time without a leading zero gives no hours at all. The API stores times as
  HH:mm, and the HR tab flags times Dynamics can't read.
- **Prod data (read-only check, 16 Oct):** 86 active colleague configurations. 8 have no times at all, including Harriet.
  **Ellie Parton's Friday break is "1:00"**, so her Friday counts as no hours. Leave booked on a Friday would put
  nothing on her timesheet, and that day wouldn't count against her balance. Four people mark days off with "0",
  which works. **HR to fix Ellie's Friday and fill in the 8** (the migration report lists them too).
- **Booking for someone else** is now enforced by the plug-in: HR or the "Leave - Book on behalf" team only. Before
  this, anyone with DHA-1 could do it through the API.
- **Entitlements:** one per person per year, starting 1 January, named "<person> <year>", and given to the employee
  (if they hold DHA-1). "Carried to next year" is left to the year-end process (stage 5).
- **Testing incident (dev only):** an early version of the settings API treated parameters that weren't sent as
  "clear", and wiped a test user's (Alice's) Tuesday–Thursday times. Restored from an earlier session's query (the
  breaks inferred as "1:00", matching her other days). Fixed, with a test that unsent settings are left alone.
- **Tests (dev):** DHA-9 86 pass, 1 skipped; DHA-10 10 pass; 60 unit tests pass.

### Stage 6 notes (16 Oct)

- **Whose calendar.** Valto writes to a calendar in the **Automated@dhaplanning.co.uk** mailbox, through that
  account's Outlook connection (prod connection `18aa8aaf…`, "Connected"). Its id, Valto's `new_CalendarID`:
  `AAMkAGZjYzk3MzFmLTQ3MzAtNGUxOS04Yzc4LWNkMTQ3MTk4MDg2ZQBGAAAAAACmQX1Bp6iMQ53111j57DPKBwBXWe41EVWRRbiFD94BYUQzAAAAAAEGAABXWe41EVWRRbiFD94BYUQzAAApYA8oAAA=`
- **What changes from Valto.**
  - The subject is "Out of Office - Name", not "[Request Type] - Name", and the body has no type (F5).
  - Events show as Out of Office rather than Busy.
  - Working From Home isn't added.
  - Same as Valto: the employee and approver are invited.
- **Changed dates or type.** When approved leave has its dates or type changed, the plug-in records a "Changed"
  event and the flow moves the calendar entry. Each run reads the request and brings the calendar into line, one
  run at a time.
- **Prod go-live (manual, once):**
  1. Set `fwp_LeaveCalendarId` to the id above.
  2. Someone signed in as Automated@ opens the solution in make.powerapps.com. They set the "Leave - Calendar
     (Outlook)" connection reference to their Outlook connection and turn on "Leave - Outlook calendar". Outlook
     connections can't be shared, so this can't be scripted as stsadmin.
  3. Leave `fwp_LeaveEmailOverride` empty, or no one is invited.
- **Migration (§5):** copy Valto's `CalendarID` into `fwp_calendareventid`, so cancelling migrated leave removes
  the event Valto made.
- **Dev:** events go to stsadmin's own Calendar (`fwp_LeaveCalendarId` = "default"), with no attendees while the
  email redirect is on.
- **Tests (dev):** DHA-9 90 pass, 1 skipped (4 new calendar tests); 64 unit tests pass.

### Prod deployment (20 Oct 2026)

Stages 1–4 and 6 are in prod, so the desktop Leave tab works there. Deployed by
`DHA.Dataverse/Provisioning/deploy_leave_management_prod.py` (`prep`, `state`, `import`, `post`). Packages are in
`DHA.Dataverse/Solutions`. The pre-import snapshot is `prod_state_before_leave_management.json`.

- **Solutions (managed):**
  - `FWPLeaveTimeEntries` 1.0.4.0: the assembly with all the leave plug-ins.
  - `FWPLeaveManagement` 1.0.0.0: the tables, plug-in steps, Custom APIs, Leave Administrator role and flows.
  - Shared tables get only the four leave columns (three on colleague configuration, one on time entry), with no
    forms or views. `systemuser` was taken out of the solution before export.
- **Access:**
  - DHA-1: own leave requests and entitlements, plus read on leave types and public holidays.
  - The HR team is linked to HR@ and holds Leave Administrator.
  - The "Leave - Book on behalf" team exists with no members.
- **Leave types:** the six Valto types, the same as dev. Study Leave's "uses entitlement" setting still needs
  checking against Valto.
- **Off:** "Leave - Notifications", "Leave - Approval reminder" and "Leave - Outlook calendar". Their Outlook
  connection references are unbound. Phase A flows are unchanged: Bank Holidays and Backfill on, Record Time
  Entries off.
- **Gotcha:** the managed import brought all 10 plug-in steps in **disabled**. The script now switches them on.
- **Smoke test:** one test request (2 days, 17 hours) was created and deleted. The overlap and 31 December rules
  were refused as expected. No leave data in prod.
- **What works now:**
  - booking, viewing and cancelling leave
  - balances, approvals lists, team views and the HR tab
- **What doesn't yet:**
  - **Approving fails** until phase A's `fwp_LeaveProjectId` is set (the Finance Job).
  - No emails and no calendar until those flows are switched on. The email override setting has no value in prod,
    which stops a flow switching on, so give it a "none"-style value or change the flows first.
  - No entitlements yet. HR add them in the HR tab, or they come in with migration (§5).

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

## 8. Decisions

Q1–Q8 are answered: see **§0** (Harriet, 15 Oct 2026). Open follow-ups **F1–F6** are listed there too.

## 9. Risks

| Risk | Mitigation |
|---|---|
| Desktop-only access: nothing on phones (accepted, F4). Approvers away from a PC can't approve | Daily reminder emails; HR can approve on anyone's behalf; additional approvers cover absences |
| Balance mismatches after migration | Reconciliation report gate; parallel run |
| Two systems live at once (double booking) | A short parallel run limited to pilot users; Valto made read-only at cutover |
| Form drift between dev and prod | Rebuild forms from prod; compare before every import (see the DHA-7/8 notes) |
| Leave entries carry £ value (hours × Rate A) | Finance decision; independent of this work (see the DHA-9 plan) |
| The desktop app release is shared with other changes | One combined, tested release (§6.1) |
