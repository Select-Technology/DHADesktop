# Leave (replaces Valto's Core – Leave Request App)

Staff book leave, Working From Home and appointments in the desktop app's **Leave** tab. Line managers approve them
there, and HR administer entitlements there. The data is in Dynamics. This replaces Valto's canvas app, whose data
lives in SharePoint lists on `dhaplanningcouk.sharepoint.com/sites/Leave-Request`.

- Plans and decisions: `docs/leave-desktop-parity-plan.md` (feature IDs, HR decisions Q1–Q8 / F1–F6, stage notes) and
  `docs/leave-remaining-stages-plan.md`.
- UAT: `docs/leave-uat-checklist.md`.
- Basecamp: DHA-9 (approved absence becomes time entries), DHA-10 (cancelled absence deactivates them).

## The rules

| Rule | Detail |
|---|---|
| Units | **Days** for entitlement and balances (Q1). A request also stores hours, for the timesheet |
| Leave year | 1 January to 31 December for everyone. A request can't cross 31 December |
| Balance | Base + carried forward + adjustment, less approved **and pending** leave (Q6). Only types that "deduct from entitlement" count; today that's Holiday Request only |
| A day of leave | 8.5h Mon–Thu, 8h Fri (`fwp_LeaveDayHours`). *Which* days someone works comes from their colleague configuration; a day with 0 expected hours is a day off |
| Half day | Exactly half that day's leave hours. In the desktop app: Morning / Afternoon on a single day |
| Overlaps | Two absences can't overlap. Working From Home can sit alongside an appointment |
| Approvers | Approver and additional approver on colleague configuration (Q2). Either can approve. HR (Leave Administrator) can approve anyone's leave |
| Carry forward | Up to 5 days (`fwp_LeaveCarryForwardLimit`), or HR's per-person override on the year being closed. Never below zero |
| Privacy | Only the employee, their approvers and HR see the leave type. The shared calendar says "Out of Office"; timesheet entries say "Approved leave" |

## Dataverse

**Solutions:**
- `FWPLeaveTimeEntries` contains the `DHA.Leave.Plugins` assembly and phase A.
- `FWPLeaveManagement` contains the tables, steps, Custom APIs, role and flows.
- Both are deployed **managed** by `DHA.Dataverse/Provisioning/deploy_leave_management_prod.py`. Never run the dev
  provisioning scripts against prod: they would add unmanaged layers.

| Table | Notes |
|---|---|
| `fwp_leaverequest` | User-owned (owner = employee). Employee, approver, additional approver, booked by, type, dates and times, `fwp_days`, `fwp_hours`, status (Pending / Approved / Rejected / Cancellation requested / Cancelled), comments, `fwp_calendareventid`, `fwp_legacyid` (`LR-<Valto ID>` for migrated requests), `fwp_lastevent` (drives the flows) |
| `fwp_leavetype` | `fwp_isabsence` (Working From Home = No), `fwp_deductsfromentitlement`, `fwp_forcedunits` (Days / Hours), sort order. These are data, not solution components |
| `fwp_leaveentitlement` | One row per person per year, starting 1 January, owned by the employee. Entitlement, carried forward, adjustment, carry-forward override, carried to next year (all in days) |
| `fwp_publicholiday` | GOV.UK bank holidays |
| `fwp_colleagueconfiguration` | Adds approver, additional approver and base entitlement. Its existing working times are the working pattern |

**Plug-ins** (`DHA.Leave.Plugins`):
- **`LeaveRequestPlugin`:**
  - works out days and hours on the server,
  - defaults the approvers and rejects overlaps,
  - enforces the 31 December rule and who may book on someone's behalf,
  - shares the request with its approvers,
  - sets `fwp_lastevent`,
  - blocks deleting approved leave,
  - when DHA-9 is live, records or deactivates time entries.
- **`LeaveEntitlementPlugin`:** one row per person per year, named, owned by the employee.
- **`LeaveApproverChangePlugin`:** when an approver changes, shares the person's leave from 1 January last year and
  moves waiting requests to the new approver.

**Custom APIs.** Each checks the caller itself, then reads or writes as the system. That's why line managers only need
DHA-1.

| API | Who | Does |
|---|---|---|
| `fwp_GetLeaveBalance` | The employee, their approvers, HR, the book-on-behalf team | One person's balance for one year |
| `fwp_GetTeamLeave` | Approvers and HR | Who's off in a date range (type hidden as above) |
| `fwp_SetLeaveSettings` | HR | Approver, additional approver, base entitlement and working times. This is HR's **only** write to colleague configuration, which holds rates |
| `fwp_GenerateLeaveYear` | HR | A year's entitlement rows from base entitlement; skips anyone who already has one |
| `fwp_CarryForwardLeave` | HR | Carries Year − 1 into Year; **sets** the figures rather than adding, so it can be re-run |

**Security:**
- **DHA-1 (everyone):** their own requests and entitlements, plus read on types and holidays.
- **HR:** the "DHA - Leave Administrator" role, through the HR team linked to HR@.
- **Book on behalf:** the owner team "Leave - Book on behalf", whose members HR manage.

**Settings (environment variables):**

| Setting | What it holds |
|---|---|
| `fwp_LeaveDayHours` | Hours a day of leave records |
| `fwp_LeaveCarryForwardLimit` | Standard carry-forward limit (5) |
| `fwp_LeaveApproverEmail` | HR's address |
| `fwp_LeaveEmailOverride` | `none` in prod; an address in dev, which redirects every email and removes calendar attendees |
| `fwp_LeaveCalendarId` | The shared calendar in the Automated@ mailbox |
| `fwp_LeaveProjectId` | DHA-9's "Approved Leave" Job; **empty = no time entries** |

## Flows (FWPLeaveManagement)

| Flow | Trigger | Does |
|---|---|---|
| Leave - Notifications | `fwp_lastevent` changes | Emails the approvers (new, cancellation, withdrawn) or the employee (outcomes) |
| Leave - Approval reminder | Weekdays 10:00 | One email per approver listing what's waiting |
| Leave - Outlook calendar | `fwp_lastevent` | "Out of Office - Name" event: created on approval, moved on change, removed on cancellation. Uses the Automated@ Outlook connection, which can't be shared, so that person binds it |
| Leave - Year end | 1 January 06:00 | `fwp_GenerateLeaveYear` + `fwp_CarryForwardLeave`, then emails HR both reports |

Imports that must not email anyone send the header `MSCRM.SuppressCallbackRegistrationExpanderJob: true`.

## Timesheets (DHA-9) are separate

The Valto app never touched timesheets; staff type holiday onto the "Holiday" Job by hand. DHA-9 adds automatic
"Approved leave" time entries.

- **Until `fwp_LeaveProjectId` is set, leave is approved without time entries.** That's deliberate (26 Oct 2026), so
  replacing Valto doesn't wait on Finance creating the Job.
- **To switch DHA-9 on:** run `go_live_leave_time_entries.py <Job number>`. It also backfills approved absence already
  in Dynamics from that day on, by re-saving each request's end date.
- **Cancelled leave's entries are deactivated, never deleted (DHA-10).**
- **Leave and bank holiday entries carry £0** (decision, 26 Oct 2026). The plug-in writes `fwp_value` 0, and the hourly
  `fwp-settimeentryvalue` Azure Function, which values every other time entry, skips any entry with
  `fwp_leavesourcekey`. Anything else that values time entries must skip them too. See
  [01-value-calculation-flow-vs-plugin.md](../dataverse-customizations/01-value-calculation-flow-vs-plugin.md).

## Year-end runbook

1. The flow runs on 1 January and HR get an email with both reports.
2. **People listed with no new-year entitlement:** HR add the year on Leave → HR, then re-run **Carry forward** from
   the Year end card, for that person only.
3. **HR change someone's carry-forward limit, or last year's leave changes late:** re-run **Carry forward** for them.
   It replaces the figures.
4. **New starters are always set up by hand** (Q7).

## Migration from Valto (stage 8)

- **Reading Valto.** The DHA Azure CLI sign-in has no SharePoint permission, so `valto_source.py` reads the lists
  through "Leave - Valto export (migration)", a flow in **dev** on stsadmin's SharePoint connection. It's switched on
  only while reading.
- **`migrate_valto_leave.py`:**
  - `report` changes nothing.
  - `hr` writes HR's fix list as a spreadsheet.
  - `import` creates or updates, matching on `LR-<ID>`, so it's also the nightly delta.
  - `reconcile` compares Valto's remaining days with Dynamics, per person and year.
  - Add `--prod` for prod; an import needs `--yes` as well.
- **How requests are written:**
  - Requests ending before `--cutover` go in as history, with plug-ins and flows bypassed and Valto's day count kept.
  - Later ones go through the plug-in.
- **Reports name people** and go in the git-ignored `migration-reports/` folder. Delete them when the migration is done.
- **In dev, the three test users (Jessica, Harriet, Alice) are left out**, so the automated tests keep working.

## Testing

- `DHA.Dataverse/Tests` (dev): `python -I run_tests.py dha09`. That includes the year-end, migration-mapping and
  "approve without the Job" tests.
- `DHA.Leave.Plugins.Tests`: `dotnet test`.
