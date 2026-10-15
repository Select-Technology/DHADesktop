# DHA-9 – Approved leave becomes time entries: plan

Basecamp: DHA-9 "Approved absence creates the matching time entry automatically".

Two tracks, agreed 2026-10-13:

- **Phase A (now):** keep Valto's *Core – Leave Request App* (SharePoint lists + canvas app). Add a flow
  so approved leave and bank holidays become time entries. Branch `feature/dha-9-leave-time-entries`.
- **Phase B (alongside):** move leave booking and approval into the desktop app, with the data in
  Dataverse, then retire the Valto app. Branch `feature/leave-in-desktop-app`.

The piece both phases share, *"this leave was approved → these time entries"*, lives in Dynamics as
Custom APIs backed by a plug-in (`DHA.Leave.Plugins`). Phase A calls it from a SharePoint-triggered
flow. Phase B calls the same code from a plug-in on the new leave request table. Nothing built for A
is thrown away.

## Decisions

| Question | Decision |
|---|---|
| Full day | The colleague's expected hours for that weekday from Colleague Configuration (`fwp_<day>expectedhours`), normally 7.5. 7.5 Mon–Fri if there's no configuration |
| Part day | The overlap between the booked times and that day's start/finish: 11:00–13:00 is 2h. Covering the whole working day gives the full expected hours, so lunch isn't counted |
| Label | Generic "Approved leave", category Non-chargeable. **The absence type is never written to the timesheet** (GDPR: project managers can see timesheets) |
| Job | New **"Approved Leave"** Job, created by Finance in prod. Its id goes in env var `fwp_LeaveProjectId` |
| Bank holidays | Every active colleague (anyone with an active Colleague Configuration) gets their expected hours on each bank holiday that falls on one of their working days. Recorded against the existing **"Bank Holiday"** Job (36235), env var `fwp_BankHolidayProjectId`. Leave is never recorded on a bank holiday |
| Backfill | Already-approved leave from go-live onwards. Earlier leave was probably entered by hand |
| Cancelled / changed leave (DHA-10) | Time entries are **deactivated, never deleted**. A re-approved day reactivates its old entry; a day dropped from a request is deactivated. Every entry is traceable to its absence: `fwp_leavesourcekey` (`LR-<SharePoint ID>` / `LV-<request id>`), plus a `fwp_leaverequest` lookup for desktop requests. The desktop app only shows active entries |
| Reconciled entries | Never changed when a request is changed or cancelled |

## Phase A – what's built

**Dataverse (`FWPLeaveTimeEntries` solution, provisioned by `DHA.Dataverse/Provisioning/provision_leave_time_entries.py`):**

- `fwp_timeentry.fwp_leavesourcekey`: identifies which request created an entry (`LR-<SharePoint ID>`,
  `BH-<date>-<user>`). Re-sending a key brings its entries into line with the new dates; removing a key
  **deactivates** them (DHA-10, records are never deleted).
- `fwp_publicholiday` table (name, date), seeded from GOV.UK by `seed_public_holidays.py`.
- Environment variables: `fwp_LeaveProjectId`, `fwp_BankHolidayProjectId`, `fwp_LeaveDefaultDayHours` (7.5),
  `fwp_LeaveEntryLabel` ("Approved leave"), `fwp_BankHolidayEntryLabel` ("Bank holiday").
- Plug-in assembly `DHA.Leave.Plugins` and three Custom APIs:

| Custom API | Inputs | Does |
|---|---|---|
| `fwp_RecordLeave` | `UserEmail` or `UserId`, `StartDate`, `EndDate`, `StartTime`?, `EndTime`? (HH:mm), `LeaveKind`? (Leave/BankHoliday), `SourceKey` | One active entry per working day: updates or reactivates existing entries, deactivates days no longer covered |
| `fwp_RemoveLeave` | `SourceKey` | **Deactivates** that request's entries (DHA-10) |
| `fwp_RecordBankHolidays` | `FromDate`?, `ToDate`? | Bank holiday entries for every active colleague, idempotent |

The plug-in runs as the caller, so the flow connection must be able to create and delete any time
entry. Use the `Automated #` service account, which already owns the leave flows.

### Phase A – dev status (2026-10-13)

Provisioned in dev with test Jobs "Approved Leave" and "Bank Holiday" (env var values set in dev only)
and 2026–2028 bank holidays. Tested against the API directly:

- 21–31 Dec 2026 → 7 entries of 7.5h. 25 Dec and the 28 Dec substitute day are skipped and get bank holiday entries instead.
- Part day 11:00–13:00 by email → 2h. Afternoon from 13:30 → 4h.
- Re-sending a key replaces its entries. Removing deletes them, and a second remove is a no-op. An unknown email gives a clear error.
- Bank holidays over Christmas → 140 entries (70 active colleagues × 2 days) in ~17s. A repeat run creates nothing (0.2s).
- `fwp_value` stayed empty on the test entries because the value flow/plug-in isn't active in dev.

All test entries were removed afterwards.

### Phase A – production (2026-10-14)

`FWPLeaveTimeEntries` **v1.0.1.0 imported into prod as a managed solution**. It was exported from dev; the
package is `DHA.Dataverse/Solutions/FWPLeaveTimeEntries_1_0_1_0_managed.zip`. It includes the DHA-10
deactivate-not-delete behaviour. The assembly also carries `LeaveRequestPlugin`, which is inert in prod:
its steps belong to `FWPLeaveManagement`, which is not deployed.

- `fwp_BankHolidayProjectId` = prod "Bank Holiday" Job 36235 (`59e66ab8-2c48-f011-877a-6045bdcf7311`).
- Bank holidays 2026–2028 loaded into `fwp_publicholiday`.
- Smoke-tested without writing anything: bank holidays over a range with none → 0; removing an
  unknown key → 0; recording leave → "fwp_LeaveProjectId is not set".
- **Still to do before go-live:**
  1. Finance creates the "Approved Leave" Job, and `fwp_LeaveProjectId` is set to its id.
  2. Build flows F1–F3 below in prod.
  3. Release the desktop app with the DHA-10 active-only filter **before** F1 starts deactivating entries.
  4. Tell staff to stop entering leave by hand. Run F3 (backfill).

### Phase A – production update (2026-10-14, v1.0.2.0)

`FWPLeaveTimeEntries` **v1.0.2.0** imported into prod (package in `DHA.Dataverse/Solutions`). It adds:

- **Flows** (built by `provision_leave_flows.py`, the spec below is now implemented):
  - "Leave Request - Record Time Entries" (F1): **off**.
  - "Leave - Record Bank Holidays" (F2): **off**.
  - "Leave Request - Backfill Approved Leave" (F3): **on, manual only**, not run.
- **Connections:** stsadmin's SharePoint and Dataverse connections. Switching F3 on proved they can reach the list.
- **Settings:** `fwp_LeaveSharePointSite`; `fwp_LeaveRequestListId` = `718145ce-0b2d-4396-af86-ce36aabc8a29`, the
  real prod list (the id in the Valto app package is Valto's own tenant's); and `fwp_LeaveExcludedRequestTypes` =
  "Working From Home".
- **Half days:** `StartHalfDay`/`EndHalfDay` on `fwp_RecordLeave`. A Valto "Days" half day (a PM start or an AM
  finish) is exactly half the person's day, not Valto's 09:00–12:30 / 12:30–17:00. "Hours" requests (Medical)
  use their exact times.

What real Valto data showed (prod flow history, 326 requests since 11 Sep):

- Working From Home 184, Holiday Request 102, Medical 19 (Hours), Sick Leave 16, Other 3, Study Leave 2. No TOIL.
- Dates are plain dates ("2026-10-15"), so there's no BST shift. A full day is 09:00–17:00.

**Go-live (agreed 2026-10-14: as soon as the Job exists).** F2 (bank holidays) was switched **on** on 2026-10-14; the
first entries come around 24 Nov, for Christmas. When Finance has created the "Approved Leave" Job through the
normal quote-to-Job process, run:

    set DATAVERSE_URL=https://dhapd.crm11.dynamics.com
    python -I DHA.Dataverse/Provisioning/go_live_leave_time_entries.py <its Job number>

That links the Job, smoke-tests, switches F1 on, and runs the backfill once. Alongside it: tell staff to stop
entering leave by hand, and release the desktop app with the DHA-10 active-only filter.

Note: the existing "Holiday" Job (36225) carries **£2.18m** of `fwp_valueofhours` from manual holiday entries valued at
Rate A. Leave entries will add to the Approved Leave Job in the same way, so the valuation question is now pressing.

### Phase A – flows to build (maker portal, in `FWPLeaveTimeEntries`)

Flows need a connection, which can't be created from a script, so these are built by hand.

**F1 – Leave Request – Record Time Entries**

- Trigger: SharePoint *When an item is created or modified*. Site =
  `https://dhaplanningcouk.sharepoint.com/sites/Leave-Request`, list = *Leave Request Data*.
- Settings: concurrency control **on, degree 1**, so two edits to the same request can't race.
- Condition: `Status/Value` equals `Approved`.
  - **Yes:** Dataverse *Perform an unbound action* `fwp_RecordLeave`:
    - `UserEmail` = `Employee/Email`
    - `StartDate` = `convertTimeZone(triggerBody()?['RequestStartDate'],'UTC','GMT Standard Time','yyyy-MM-dd')`
    - `EndDate` = `convertTimeZone(triggerBody()?['RequestEndDate'],'UTC','GMT Standard Time','yyyy-MM-dd')`
    - `StartTime` = `concat(padLeft(string(Request Start Hour),2,'0'),':',padLeft(string(RequestStartMinutes),2,'0'))`
    - `EndTime` = same with `RequestEndHour` / `RequestEndMinutes`
    - `SourceKey` = `concat('LR-', triggerBody()?['ID'])`
  - **No** (Pending, Rejected, Cancelled, anything else): `fwp_RemoveLeave` with the same `SourceKey`.
    Removing a key with no active entries does nothing.
- On failure (*Configure run after → has failed*): email the support mailbox the request ID and the
  error. The usual cause is an employee email that doesn't match a Dynamics user.

> ⚠️ **Verify the dates on real items first.** The Valto flows add a `Timezone Offset` field to the
> dates, so the stored values may be local midnight in UTC. Look at a summer (BST) request and confirm
> `convertTimeZone` gives the booked date, not the day before. Check the hour/minute fields on a
> full-day request too: midnight-to-midnight and 09:00–17:30 both give full days.

**F2 – Leave – Record Bank Holidays**

- Trigger: Recurrence, daily 06:00 UK.
- Dataverse *Perform an unbound action* `fwp_RecordBankHolidays`, `FromDate` = today, `ToDate` = today + 31 days.

**F3 – Leave Request – Backfill** (instant, run once at go-live)

- SharePoint *Get items*, filter `Status eq 'Approved' and RequestEndDate ge '<today>'`, top 5000.
- Apply to each (concurrency 1) → `fwp_RecordLeave` with the same mapping as F1.

### Phase A – go-live checklist

1. Finance creates the "Approved Leave" Job in prod.
2. Build `DHA.Leave.Plugins` (Release), run `provision_leave_time_entries.py` against prod
   (`DATAVERSE_URL=https://dhapd.crm11.dynamics.com`). Or export the solution from dev and import it.
3. Set `fwp_LeaveProjectId` and `fwp_BankHolidayProjectId` in prod.
4. Run `seed_public_holidays.py` against prod.
5. Build F1–F3. Turn on F1 and F2, run F3 once.
6. Tell staff: stop entering leave by hand. Leave already entered by hand **and** approved after
   go-live would be doubled, which is why the backfill starts at go-live, not earlier.

### DHA-10 – check before go-live

Deactivated time entries must not count anywhere. The desktop app and the default Dynamics views
("Active Time Entries", "My Time Entries") already filter to active. Not yet checked:

- The **"Time Records for Reconciliation"** view has no status filter, so it will list deactivated leave entries.
- The reconciliation process (`DHA.ReconciliationPlugin`, the reconciliation Azure Function) and the WIP and
  invoice flows: do they filter on `statecode`? Leave entries are Non-chargeable, so the risk is mainly to
  hours totals, but it needs confirming.

### Phase A – open points

- `fwp_value` is calculated as hours × Rate A for every time entry, including today's manual leave
  entries. Leave entries will carry a value too. Confirm WIP/billing reports ignore Non-chargeable,
  or decide leave should be valued at 0.
- Should leave entries be read-only in the desktop app? Today the app would let someone edit or
  delete one, and the next change to the request would recreate it.

## Phase B – leave in the desktop app

> **Full parity plan:** [leave-desktop-parity-plan.md](leave-desktop-parity-plan.md). It has a feature-by-feature
> matrix against the Valto app, design changes, stages, migration, cutover and the decisions needed from HR.
> The sections below are the original outline.

### Data model (Dataverse, new solution `FWPLeaveManagement`)

| Table | Key columns |
|---|---|
| `fwp_leaverequest` (user-owned; owner = employee) | employee, approver, booked by, leave type, start date + time, end date + time, hours (calculated with `LeaveDayCalculator`), status (Pending / Approved / Rejected / Cancellation requested / Cancelled), requester and approver comments, Outlook event id, legacy SharePoint ID |
| `fwp_leaveentitlement` | employee, leave year start, base entitlement, carried forward, purchased, adjustment, taken (calculated), remaining |
| `fwp_leavetype` | name, deducts from entitlement?, needs approval? (replaces *Request Type Configuration*) |
| `fwp_colleagueconfiguration` (existing, extend) | leave approver, leave year start, base entitlement, carry-forward limit. The working pattern is already here |
| `fwp_publicholiday` | from phase A |

**Privacy:** the leave type (including medical) lives only on `fwp_leaverequest`. Only the employee
(owner), their approver (shared by plug-in) and a *DHA – Leave Administrator* role (Harriet) can read
it. Project managers have no access. Time entries stay generic.

### Server logic (reuses `DHA.Leave.Plugins`)

- `fwp_leaverequest` create/update plug-in: work out hours with `LeaveDayCalculator`, default the
  approver from colleague configuration, reject overlapping requests, share with the approver, and
  warn when it would exceed the balance.
- On status → Approved: `LeaveTimeEntryService.RecordLeave(key "LV-<id>")`. On Rejected/Cancelled: `RemoveLeave`.
- Flows (Dataverse triggers, no SharePoint): approval request and outcome emails, the shared Outlook
  calendar event, the daily approver reminder, yearly entitlement generation and carry-forward.

### Desktop app – new "Leave" tab

1. **My leave:** balance (entitlement / taken / booked / remaining), request form (dates; full,
   morning, afternoon or custom times; type; comment) with a live hours preview using the same
   calculator, my requests with cancel.
2. **Approvals** (shown to approvers): pending list, approve/reject with a comment, who's-off view
   for the team.
3. **Admin** (Leave Administrator): book on behalf, entitlements and carry-forward, public holidays.
4. Leave shown on the existing calendar view.

A small model-driven app ("DHA Leave") over the same tables lets managers approve from a phone or
browser. It's nearly free once the data is in Dataverse, and covers the desktop-only gap.

### Phase B – dev status (2026-10-13)

`provision_leave_management.py` has been run in dev: tables, approver column and the four
LeaveRequestPlugin steps. Tested end to end against the API as a System Administrator:

- Request 9–13 Nov → 37.5h, Pending, named "Jessica Bache - 09/11/2026 to 13/11/2026".
- An overlapping request is rejected with a readable message.
- Approve → 5 time entries. Shortening to 9 Nov plus the morning of 10 Nov → entries replaced (7.5h + 3.75h).
- Cancellation requested → entries kept. Cancelled → entries removed.

Not yet done:
- **Staff access (done in dev 2026-10-14).** No new role. `provision_leave_staff_access.py` adds the
  leave privileges to **DHA-1: Individual**, which everyone holds. Leave requests: create/read/write/append
  **own only**. Entitlements: read **own only**. Leave types and public holidays: read all. Also read
  own colleague configuration and append-to user. `fwp_leaveentitlement` was recreated as
  **user-owned** (owner = employee) so "own" works; organisation-owned would have meant all-or-nothing.
- **HR access** (`provision_leave_approvers.py`: HR group team + "DHA - Leave Administrator" role) is
  written but **not yet run**. It needs explicit sign-off because it grants read/write on everyone's leave.
- **Ownership when booking on behalf.** A request is owned by whoever creates it. When HR books for
  someone, the plug-in will need to assign the request (and entitlements created by HR) to the employee,
  or they won't see it under "own".
- **Approvers.** No approver is set in dev: Jessica has no manager and no `fwp_leaveapprover`.
  Approvers come from the SharePoint *Employee Configuration* list at migration.
- **Plug-in assembly.** Dev now runs the phase B build (it includes LeaveRequestPlugin). Deploy
  from this branch from now on; uploading the phase A build would fail because it lacks that plug-in type.

### Phase B – notification emails (done in dev 2026-10-14)

`provision_leave_emails.py` creates two flows in `FWPLeaveManagement`:

- **Leave - Notifications.** Triggered when `LeaveRequestPlugin` sets `fwp_lastevent`. Emails HR on
  Requested / CancellationRequested / Withdrawn, and the employee on Approved / Rejected /
  CancellationApproved / CancellationDeclined / CancelledByHR. The event column exists because Dataverse
  flow triggers can't see the previous status, so "approved" and "cancellation declined" would look the same.
- **Leave - Approval reminder.** Weekdays 10:00 UK. Emails HR the requests waiting for a decision, if there are any.

Settings: `fwp_LeaveApproverEmail` (default HR@dhaplanning.co.uk). `fwp_LeaveEmailOverride` sends
**every** leave email to one address, with the intended recipient in the subject. **It is set to
stsadmin in dev and must be blank in prod.** In dev the emails send from stsadmin's Outlook
connection. Several older dev Outlook connections are expired ("Error" in make.powerapps.com), and
they fail every send with 401.

Tested in dev: all 8 event emails and the reminder sent successfully.

Deleting an approved (or cancellation-requested) request is blocked by the plug-in (DHA-10): it has to be cancelled, which
deactivates its time entries and keeps the request.

### Phase B – what the Valto canvas app does that the flows didn't show (2026-10-14)

Read from the app package in prod (*Core Leave Request App*, last saved 21 Mar 2023):

| Valto screen / feature | Desktop app |
|---|---|
| **TOIL.** Claim time off in lieu (date, from/to, breaks, a description of what was done). Approved TOIL builds a TOIL balance that a "Take TOIL" leave type draws from. Behind an App Configuration "TOIL Toggle" | ❌ **Ask Harriet if TOIL is switched on and used** |
| Leave types flagged "Has Entitlement?" and "Forced Units" (days/hours); entitlement units per person per year; half day AM (09:00–12:30) / PM (12:30–17:00) | Partly: deducts-from-entitlement flag, morning/afternoon split on the working pattern; hours only |
| Validation: a request can't span two holiday years; the end date can't be before the start | Partly: end-before-start only |
| Leave balance for the previous, current and next year: base, purchased, carry forward, total, taken (**counts pending**), remaining, carried to next year, TOIL; shows your approver | Current year only; pending shown separately |
| My requests: filter upcoming/past, status, type; details include approver comments; cancel **with a comment**; pending future requests cancel instantly | No filters; no cancellation comment |
| Manager area: "my employees" (approver, or an **Additional Approver** who can only approve), each one's balance, all their requests with filters, **cancel on behalf** (instant, emails the employee), overlap view by all / my employees / **department** | HR-only approvals; no per-employee balance or request browser; no cancel on behalf |
| Book on behalf, restricted to people on a "Book on behalf of users" list, choosing the employee and their approver | ❌ |
| "Company Holidays" list screen | ❌ (bank holidays are only skipped in the hours) |
| Admin settings: branding, US date format, debug, show purchased leave, impersonate a user | Not needed (the desktop app has its own impersonation) |

### Migration and cutover

1. Read the SharePoint lists (needs Sites.Read access or an Excel export from Harriet): employee
   configuration → colleague configuration fields; entitlement balances (current + next year) →
   `fwp_leaveentitlement`; requests (last year onwards) → `fwp_leaverequest`, keeping the legacy ID.
2. Migrated approved requests keep source key `LR-<id>` so the time entries phase A created are
   adopted, not duplicated.
3. Cutover day: Valto app read-only → final delta migration → turn off F1 and the Valto flows →
   release the desktop app. Decommission the Valto solution after a quiet month.

### Phase B – rough effort

| Slice | Days |
|---|---|
| Dataverse schema, security, plug-in | 4–5 |
| Desktop: My leave | 5–7 |
| Desktop: Approvals | 3–4 |
| Desktop: Admin + entitlements, carry-forward | 4–5 |
| Flows: emails, calendar, reminders, year-end | 3–4 |
| Migration scripts + rehearsal | 3 |
| Testing, UAT with Harriet | 4–5 |
| **Total** | **26–33** |

### Phase B – questions for Harriet

- The full list of leave types, and which ones deduct from entitlement. Is entitlement in days or hours?
- Leave year start, carry-forward limit, and how purchased leave works.
- Who approves: always the line manager? Any fallback approver?
- Is the shared Outlook leave calendar still used?
- Does anyone book leave who doesn't record time (so wouldn't have the desktop app)?
