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
| Reconciled entries | Never deleted or duplicated when a request is changed or cancelled |

## Phase A – what's built

**Dataverse (`FWPLeaveTimeEntries` solution, provisioned by `DHA.Dataverse/Provisioning/provision_leave_time_entries.py`):**

- `fwp_timeentry.fwp_leavesourcekey`: identifies which request created an entry (`LR-<SharePoint ID>`,
  `BH-<date>-<user>`). Re-sending a key replaces its entries; removing a key deletes them.
- `fwp_publicholiday` table (name, date), seeded from GOV.UK by `seed_public_holidays.py`.
- Environment variables: `fwp_LeaveProjectId`, `fwp_BankHolidayProjectId`, `fwp_LeaveDefaultDayHours` (7.5),
  `fwp_LeaveEntryLabel` ("Approved leave"), `fwp_BankHolidayEntryLabel` ("Bank holiday").
- Plug-in assembly `DHA.Leave.Plugins` and three Custom APIs:

| Custom API | Inputs | Does |
|---|---|---|
| `fwp_RecordLeave` | `UserEmail` or `UserId`, `StartDate`, `EndDate`, `StartTime`?, `EndTime`? (HH:mm), `LeaveKind`? (Leave/BankHoliday), `SourceKey` | Replaces the entries for `SourceKey` with one entry per working day |
| `fwp_RemoveLeave` | `SourceKey` | Deletes that request's entries |
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
    Removing a key with no entries does nothing.
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

### Phase A – open points

- `fwp_value` is calculated as hours × Rate A for every time entry, including today's manual leave
  entries. Leave entries will carry a value too. Confirm WIP/billing reports ignore Non-chargeable,
  or decide leave should be valued at 0.
- Should leave entries be read-only in the desktop app? Today the app would let someone edit or
  delete one, and the next change to the request would recreate it.

## Phase B – leave in the desktop app

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
