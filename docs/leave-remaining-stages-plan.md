# Leave planner: remaining stages to retire the Valto app

**Goal:** finish moving everything Valto's *Core – Leave Request App* does into the DHA Time Management desktop app,
then switch Valto off.

**Status (26 Oct 2026):**
- **Done and in prod since 20 Oct** (stages 1–4 and 6): booking, balances, approvals, team views, the HR tab and the
  Outlook calendar flow. Emails, the calendar and approving are not switched on yet.
- **Stage 5 (year-end) built and tested in dev (26 Oct).** Not yet in prod. See the parity plan's stage 5 notes.
- **Left:** switching on what's deployed, loose ends, migration, cutover, and putting year-end into prod.

Harriet's update to Select on 21 Oct described it as "80% of the way there".

This plan follows on from [leave-desktop-parity-plan.md](leave-desktop-parity-plan.md), which has the feature IDs
(E1…, M1…, A1…, N1…), the design (D1–D7) and the notes for each finished stage. Phase A (approved leave becomes time
entries) is in [DHA-9-leave-plan.md](DHA-9-leave-plan.md).

Owner: Select Technology (Tom Halverson). Business owner: Harriet Baker (HR).

---

## 1. What's left at a glance

| Stage | Contents | Blocked by | Estimate (days) |
|---|---|---|---|
| **R1. Switch on what's in prod** | Approved Leave Job, emails, calendar, HR data fixes | Finance (Job), Automated@ sign-in | 1 |
| **R2. Parity loose ends** | Department filter, default hours, decisions to confirm, email wording | F6, Harriet's answers | 1.5 |
| **5. Year-end** ✅ *(dev, 26 Oct)* | Carry forward and next year's entitlement each January (A3, A4) | F2 (for the generate step only) | ~~2~~ done |
| **7. Email links** *(optional)* | N5 links from emails into Dynamics | – | 0.5 |
| **8. Migration** | Valto SharePoint lists into Dynamics, reconciliation report | R1 | 4 |
| **9. Release and cutover** | Merge, UAT, parallel run, go-live, retire Valto | all | 4 |
| | **Total** | | **~12.5 days** (~10.5 left), plus optional 0.5 |

**Order:** R1 → R2 and stage 5 side by side → 8 → 9. Stage 7 can go in anywhere before 9.

### The January deadline

The leave year starts on **1 January** for everyone (Q3), and whichever system is live then has to do the year-end.
There are two ways to handle it:

| Option | What happens | Needs |
|---|---|---|
| **A. Cut over before Christmas** *(recommended)* | Dynamics does the first year-end | Stages R1–9 finished by **mid-December**; stage 5 live and tested before 31 Dec |
| **B. Cut over in January** | Valto does one last year-end; migration brings across its 2027 rows | Stage 5 can wait, but migration must run after Valto's January flow, and staff use Valto through the busy Christmas booking period |

Option A means Christmas leave and the year-end both happen in one system. **Decide with Harriet by 6 Nov**, as it
sets the dates for everything below.

---

## 2. Stage R1: switch on what's already in prod

Nothing here needs new development. These are the "what doesn't yet" items from the 20 Oct deployment.

| # | Step | Who | Notes |
|---|---|---|---|
| R1.1 | Create the **"Approved Leave" Job** through the normal quote-to-Job process | Finance (DHA) | Until this exists, **approving leave fails** in the desktop app |
| R1.2 | Run `go_live_leave_time_entries.py <Job number>` | Select | Sets `fwp_LeaveProjectId`, smoke-tests, switches on "Record Time Entries" and runs the backfill once (DHA-9 plan) |
| R1.3 | 🟡 Give `fwp_LeaveEmailOverride` a "none"-style value in prod, or change the flows to treat empty as "no override" | Select | **Built 26 Oct, not yet in prod:** the setting now defaults to "none", and every flow treats empty or "none" as no override. Ships in FWPLeaveManagement 1.1.0.0 |
| R1.4 | Bind the Outlook connection references; switch on **"Leave - Notifications"** and **"Leave - Approval reminder"** | Select | Test with one HR request and one approver first |
| R1.5 | Set `fwp_LeaveCalendarId` to Valto's calendar id (stage 6 notes) | Select | |
| R1.6 | Sign in as **Automated@dhaplanning.co.uk**, bind "Leave - Calendar (Outlook)" and switch on **"Leave - Outlook calendar"** | DHA IT + Select | Outlook connections can't be shared, so this can't be scripted. Book 15 minutes with whoever holds that mailbox |
| R1.7 | Fix colleague configuration data | HR | **Ellie Parton's Friday break is "1:00"** (must be "01:00"). **8 people have no working times**, including Harriet. The HR tab flags both |
| R1.8 | ✅ Check **Study Leave**'s "uses entitlement" setting against Valto's Request Type Configuration | Select | Checked 26 Oct: it doesn't use entitlement in Valto or in Dynamics |
| R1.9 | Confirm installer **1.4.1** is on everyone's PC | DHA IT | It includes the Leave tab and the DHA-7/8 on-stop changes |

**Until migration and cutover (stage 9),** Valto stays the system of record. Leave booked in the desktop app now is
real, so tell HR not to let staff book in both. Keep the desktop app's Leave tab to HR and a pilot group if needed.

**Done when:** a request approved in the desktop app in prod makes timesheet entries, sends emails and appears on the
shared calendar.

---

## 3. Stage R2: parity loose ends

| # | Item | Feature | Work |
|---|---|---|---|
| R2.1 | **Department** filter when approving, and department on the HR tab | M3, A1 (F6) | **Open: where is department held?** Then add it to colleague configuration (or reuse a field), `fwp_SetLeaveSettings` and `fwp_GetTeamLeave`. If DHA don't use it, drop it and record that |
| R2.2 | Default duration for Hours types (Medical) | E3, D5 | The form defaults to 09:00–10:00 and `fwp_LeaveDefaultHours` isn't read anywhere. Read the setting, or remove it from D5 |
| R2.3 | **Cancelling a pending request that has already started** | E11 | Built differently from Valto: it's withdrawn straight away, not sent to the approver (stage 2 notes). **Confirm with Harriet** |
| R2.4 | Email wording | N2 | Harriet reviews the notification, outcome and reminder emails; update the templates |
| R2.5 | DHA-only closure days (e.g. a Christmas shutdown day) | A8 | Ask whether DHA has any. If yes, add them as `fwp_publicholiday` rows through a Dynamics view; no new screen |
| R2.6 | ✅ Tidy the parity plan (26 Oct) | – | Removed the leftover TOIL unit test from §7 and the duplicated follow-ups table in §0 |

**Done when:** Harriet has answered R2.1, R2.3, R2.4 and R2.5, and the changes pass the DHA-9 tests in dev.

---

## 4. Stage 5: year-end (A3, A4)

**✅ Built and tested in dev, 26 Oct.** What was built and how it behaves is in the parity plan's *Stage 5 notes*.
Still to do:
- Harriet sees a dev dry run.
- Answer F2; if it's "no", re-run the script with `--no-generate`.
- Deploy to prod with stage 9.
- Turn the flow on in prod before 31 December.

The design below is what was built, except that the "last run" is shown in HR's email and the HR tab's report, not
stored.

As designed in D6 of the parity plan.

**Build** (in `DHA.Leave.Plugins`, with unit tests):
- `fwp_GenerateLeaveYear`: creates next year's `fwp_leaveentitlement` row for every active colleague from their base
  entitlement, owned by the employee and named "<person> <year>". Skips anyone who already has one.
- `fwp_CarryForwardLeave`: works out each person's unused days (pending counted as taken, Q6). Carries forward up to
  `fwp_LeaveCarryForwardLimit` (5), or the person's `fwp_carryforwardoverride` if HR set one. Writes "carried to next
  year" on the old row and "carried forward" on the new one.
- One scheduled flow, **"Leave - Year end"**, that runs both on 1 January. It can also be run by hand.

**Rules:**
- **Safe to re-run.** Running it again after HR changes an override, or after late changes to last year's leave,
  recalculates rather than adding twice.
- **Leave booked after the run** for last year (late sick leave, say) needs the carry-forward run again. HR
  re-runs it from the flow; the HR tab shows when it last ran.
- **New starters** are still set up by hand (Q7).

**Open: F2.** Should next year's entitlement be generated automatically for existing staff? If Harriet says no,
build only `fwp_CarryForwardLeave`, and HR create every row on the HR tab.

**Tests:** unit tests for the carry-forward maths (cap, override, negative balance, part-timers, half days);
DHA-9 dev tests that run both APIs twice for 2026→2027 and check nothing doubles.

**Done when:** a dry run in dev for 2026→2027 matches a hand calculation for five test people, and Harriet has
seen the result.

---

## 5. Stage 7 (optional): links from emails (N5)

Add a link to the request's Dynamics record in each email. It opens in a browser or on a phone, which partly makes
up for having no phone app (F4). A `dha-time://` link into the desktop app can come later.

---

## 6. Stage 8: migration from SharePoint

### Progress (26 Oct)

**Built:**
- **`valto_source.py`**
  - Reads any Valto list through a flow in dev, "Leave - Valto export (migration)", on stsadmin's SharePoint connection.
    The DHA Azure CLI sign-in has no SharePoint permission.
  - The flow is switched on only while reading, so its signed URL is live for seconds.
  - The data comes straight back to the script and is never stored in Dynamics.
- **`migrate_valto_leave.py`**
  - Modes: `report` (changes nothing), `import` (re-runnable, so it's also the nightly delta) and `reconcile`.
  - Reports go to `migration-reports/`, which is git-ignored because the reports name people.
  - Mapping is unit tested offline in `Tests/test_dha09_valto_migration.py` (14 tests).
- **How requests are written:**
  - Requests ending before the cutover date go in with plug-ins and flows bypassed. They keep Valto's day count, go
    on no timesheet and email nobody.
  - Later ones go through the leave plug-in with flows suppressed. Approved absence gets time entries under `LR-<ID>`,
    adopting any phase A made.
  - Half days are booked the way the desktop app books them.
  - Approvers are given access, as the plug-in does.

**What Valto holds** (read 26 Oct; counts only):
- 12,628 requests since 2022. 5,974 start in 2025 or later, and only those are migrated (2025–2027 balances).
  - Of those 5,974: 3,349 Working From Home, 1,973 Holiday, 355 Medical (hours), 186 Sick.
  - The rest are Study, Compassionate, Other, Authorised Absence, and Maternity / Paternity.
- **Three types Dynamics doesn't have yet:** Compassionate Leave, Authorised Absence (spelt "Absense" in Valto), and
  Maternity / Paternity Leave. All are absence and don't use entitlement. The import adds them.
- **TOIL types are configured but have never been used** since 2025.
- **Study Leave doesn't use entitlement in Valto either.** Dynamics matches, so R1.8 is answered.
- **Department:** Valto holds it as a lookup on Employee Configuration, filled in for 57 of 95 people (values such as
  Gatwick, Transport, Design). This answers where it is held (F6). Whether to bring it across is still Harriet's call.
- **Carry-forward limit:** one person has their own limit (4). It becomes their override.
- **Entitlement Balances:** 2022–2027, all in days, with no purchased leave.
  - Two people have duplicate year rows; the newer one is used.
  - 123 rows have HR comments (e.g. why days were added). Dynamics has no column for them, so they're listed in the
    report for HR.
- **Requests never span a year end, and half days are always one-day bookings.** Both match the desktop rules.

**Prod dry run (`report --prod`, 26 Oct, read only):**

| | |
|---|---|
| Leave types to add | 3 |
| People whose leave settings would change | 83 |
| Entitlement rows to create | 255 |
| Requests to create | 5,693: 5,498 history, 195 from today on through the plug-in |
| Book-on-behalf team members to add | 6 |
| Requests not migrated | 281, all belonging to leavers (and one SharePoint test account) with no Dynamics user |

**Blocks a clean reconciliation: HR must fix working weeks first (R1.7).**
- About a dozen current staff have no working hours on any day in Dynamics, so their leave would count as 0 days.
- Several part-timers are 'Off' on a day in Valto but have 8h that day in Dynamics, so they'd be charged for days
  they don't work.
- About 7 current staff have no colleague configuration in prod at all.
- The report lists each one. HR fix them on the HR tab (or whoever sets up rates adds the missing configurations).

**Dev import and reconciliation (26 Oct):**
- **Imported:**
  - 5,666 requests: about 5,475 as history, the rest through the plug-in.
  - The 3 new leave types, the entitlements, leave settings, and the book-on-behalf team.
  - Dev leaves out the three test users. Ellie Fannon is disabled in dev, so wasn't added to the team.
- **A second run is a clean delta.** It recreated nothing and updated one request that Valto had approved in the
  meantime, through the plug-in.
- **Reconciliation: 239 of 242 person-years match Valto exactly.** The other 3 are requests the plug-in refused because
  they overlap another absence in Valto: genuine double-bookings, for HR to resolve.
- **Bug found and fixed (dev only so far):** `fwp_GetLeaveBalance` didn't read the leave type, so Working From Home,
  Sick and every other type counted against the balance.
  - It's live in prod, so the desktop balances there are wrong for anyone with typed requests. (There are none yet,
    because prod has no leave data.)
  - A new test covers it.
  - **The fix needs deploying to prod**, in the next `FWPLeaveTimeEntries` version.
- **Tests:** DHA-9 111 pass, 1 skipped.

**Next:**
1. Deploy the balance fix (and stage 5) to prod.
2. Give Harriet the prod report's lists: working days, people with no configuration, the 3 double-bookings, and the
   123 entitlement comments.
3. Run prod `report` again after HR's fixes, then `import` at the start of the parallel run, then `reconcile`.

### Original design

As in §5 of the parity plan.

**Build:**
1. A **flow** (stsadmin's SharePoint connection, already proved to reach the lists) that reads each Valto list into
   a staging table or JSON file. Reading in Python via Graph is the alternative if the flow is slow.
2. A **script** in `DHA.Dataverse/Provisioning/migrate_valto_leave.py` with `dry-run`, `import` and `delta` modes,
   that maps:

| Valto list | Into | Key points |
|---|---|---|
| Request Type Configuration | check against `fwp_leavetype` | Types are already seeded; report differences only |
| Employee Configuration | colleague configuration leave fields | Match on email. Report people with no colleague configuration, and where Valto's working days differ from Dynamics (F3) |
| Entitlement Balances | `fwp_leaveentitlement` | 2025, 2026 and 2027 (if Valto has made it). Days |
| Leave Request Data | `fwp_leaverequest` | 2025 onwards. `fwp_legacyid = "LR-<ID>"`. Phase A's time entries are **adopted, not duplicated**. Import with the time-entry step bypassed. Copy `CalendarID` into `fwp_calendareventid`. Don't send emails |
| Public Holidays | compare with `fwp_publicholiday` | Report only |
| Book on behalf of users | "Leave - Book on behalf" team | |

3. A **reconciliation report:** per person and year, Valto's remaining days against the desktop's. **It must match to
   the half day**, or every difference must be explained and signed off by Harriet.

**Steps:**
1. Dry run in dev against a copy of the lists. Fix mappings until the report is clean.
2. Dry run against prod lists, reading only. Share the report with Harriet.
3. Use `delta` mode nightly during the parallel run (stage 9).

**Risks:**
- Valto requests made by people who have left: import them for history, owned by stsadmin if the user is disabled.
- Emails or calendar events firing on import: bypass the flows by importing with the email override set and the
  calendar flow off, then switch back.

---

## 7. Stage 9: release and cutover

As in §6 of the parity plan, updated for what's happened since.

1. **Merge to master.** `feature/leave-in-desktop-app` is 40 commits ahead of `master` and already includes the
   DHA-7/8 merge (installer 1.4.1). It also now carries the fee extension and close-quote commits. Open a PR, review
   and merge. Bump the installer if anything changed since 1.4.1.
2. **Deploy any changes** from R2 and stage 5 to prod as managed, with `deploy_leave_management_prod.py`. Check the
   plug-in steps come in enabled (they didn't on 20 Oct).
3. **UAT** with Harriet and two line managers. Use the parity matrix as the checklist; every ID ticked or signed off
   as a known difference.
4. **Parallel run (1 week).** HR and the pilot users book in the desktop app; nightly `delta` migration. Valto stays
   the system of record.
5. **Cutover day:**
   - Make Valto read-only (remove *Contribute* on the SharePoint lists).
   - Final `delta` migration and reconciliation report.
   - Switch off the Valto flows and phase A's "Leave Request - Record Time Entries" (the plug-in takes over).
   - Retire `fwp_LeaveExcludedRequestTypes` (D2).
   - HR emails all staff: where to book, approve and see the calendar.
6. **Retire Valto after one quiet month:** remove the `CoreLeaveRequest` solution and keep the SharePoint site
   read-only as an archive.
7. **Update the wiki** (`wiki/DHA Wiki/`): a leave page for staff, approvers and HR, plus the year-end runbook.

**Rollback:** until step 6, turn the Valto flows and SharePoint permissions back on, and export desktop requests made
since cutover back to the list by script.

---

## 8. Decisions needed

| # | Question | For | Needed by |
|---|---|---|---|
| **T1** | Cut over before Christmas (option A) or in January (option B)? | Harriet | 6 Nov |
| **F2** | Generate next year's entitlement automatically for existing staff? | Harriet | Start of stage 5 |
| **F6** | Valto holds department on Employee Configuration (57 of 95 filled). Is the filter still wanted, and should it come across? | Harriet | R2 |
| R2.3 | OK that a started pending request is withdrawn straight away when cancelled? | Harriet | R2 |
| R2.5 | Any DHA-only closure days? | Harriet | R2 |
| – | When will Finance create the "Approved Leave" Job? | DHA Finance | Now (blocks R1) |
| – | Leave entries carry £ value (hours × Rate A), like the £2.18m on the Holiday Job | DHA Finance | Before R1.2 ideally; not a blocker |
| – | Who can sign in as Automated@ to bind the calendar connection? | DHA IT | R1 |

## 9. Suggested timetable (option A)

| Week of | Work |
|---|---|
| 26 Oct | Decisions T1, F2, F6 requested; R1 as far as Finance allows |
| 2 Nov | R1 finished; R2; stage 5 build |
| 9 Nov | Stage 5 tested in dev; stage 8 build and dev dry run |
| 16 Nov | Prod dry run of migration; reconciliation with Harriet; merge and deploy |
| 23 Nov | UAT. Bank holiday entries for Christmas start around 24 Nov (phase A) |
| 30 Nov | Parallel run |
| 7 Dec | **Cutover** |
| 1 Jan | First year-end in Dynamics; check the results with HR on 2 Jan |
| Early Feb | Retire Valto |
