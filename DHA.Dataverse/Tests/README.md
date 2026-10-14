# DHA tests

## Automated (dev only)

```
cd DHA.Dataverse/Tests
python -I run_tests.py          # everything, ~6 minutes; writes last-run.md
python -I run_tests.py dha09    # one card
```

Needs `az login` as stsadmin, Node (for the DHA-8 form script) and the .NET SDK. `dvtest.py` refuses to
run against anything but dev. Tests create data prefixed `ZZTEST` (dates in 2027) and remove it afterwards.
Each request/approval sends real emails to the dev redirect address (`fwp_LeaveEmailOverride`, stsadmin).

| File | Card | What it covers |
|---|---|---|
| `test_dha07_project_on_stop.py` | DHA-7 | Column secured and audited; on both project forms; Finance/Read profiles; a PM can see the stop but can't set or clear it; set and clear land in the audit history |
| `test_dha08_on_stop_warning.py` | DHA-8 | The **deployed** form script run in Node (`js/onstop_warning.harness.js`) against a fake form: on load, on change, after save, null flag, no project, lookup failure. Registered on the time entry form; planners can read the flag; saving time on a stopped project isn't blocked |
| `test_dha09_leave_time_entries.py` | DHA-9 (A) | Custom APIs: full, part and multi-day hours, bank holidays and weekends skipped, part-time and missing patterns, entry contents, validation, idempotence, reconciled entries, bank holidays for everyone, PM can't record leave for others |
| `test_dha09_leave_requests.py` | DHA-9 (B) | Desktop requests: server-side hours, overlaps, every status change, approver default and sharing, delete rules, DHA-1 and HR security |
| `test_dha09_leave_emails.py` | DHA-9 (B) | Flows on; redirect set; a request approved one second after creation still sends "Requested" then "Approved" |
| `test_dha10_deactivate_cancelled_leave.py` | DHA-10 | Deactivate, never delete; reactivate rather than duplicate; only the right absence's entries; the 9.5h-vs-7.5h case; traceability; views |
| `test_dha16_internal_source.py` | DHA-16 | Required level; field on every active quote form; enforcement outside the form; existing quotes with no source |

**Known gaps** are marked `expectedFailure` (reported as ⚠️) so they stay visible without failing the run. Fixing
one turns it into ❓ "gap fixed?", and the marker should then be removed.

## Manual: desktop app (UI can't be automated)

Use the build from the branch under test. Sign in as stsadmin and click **🧪 DEV**.
"Test Project for Time & Billing" is left **on stop** in dev for this.

**DHA-8 (branch `feature/dha-7-8-project-on-stop`)**
1. Select the test project → a red "⛔ Warning: Project on stop" banner appears under the client box.
2. Select any other project → the banner disappears.
3. Add 1h against the test project → the entry is saved **and** a red "I understand" dialog appears. No sound.
4. Block-add 3 days against it → one dialog after all three are saved.
5. Edit an existing entry and switch it to the test project → red banner in the dialog; dialog again after saving.
6. In Dynamics, take the project off stop → reselect it in the app → no banner (the app re-checks on selection).

**DHA-9 / DHA-10 (branch `feature/leave-in-desktop-app`)**
1. Leave → My leave: the balance shows entitlement / taken / booked / remaining in days.
2. Pick Mon–Fri → preview says 37.5 hours; pick dates over a bank holiday → it isn't counted; Morning → 3.75h.
3. Request it → it appears as Pending; booking overlapping dates gives "You already have leave booked…".
4. Leave → Approvals (stsadmin / HR only): the request is listed with balance before and after; approve it.
5. Time Entries tab → "Approved leave" entries on those days.
6. My leave → Cancel request → Approvals shows "Wants to cancel approved leave" → Approve cancellation.
7. Time Entries tab → the leave entries are gone from the list and the day's total (they're deactivated in Dynamics, not deleted).
