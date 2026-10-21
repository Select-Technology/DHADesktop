# Project On Stop (DHA-7 / DHA-8)

Finance can put a Job on stop when the client isn't paying. Anyone who records time against
that Job then gets a red **"Warning: Project on stop"** message. The time entry is **still
saved**: the work has been done and has to be recorded. This is a warning only. There is no
sound and no notification to anyone else.

Basecamp: DHA-7 (Finance can place a project on stop), DHA-8 (warning when recording time).

## Dataverse

| Item | Detail |
|---|---|
| Column | `msdyn_project.fwp_onstop` ("On Stop"): Yes/No, default No, **column-secured**, **audited** |
| Existing projects | Read as `null`, which is treated as *not* on stop everywhere |
| Profile `DHA - Project On Stop: Finance` | Read + Create + Update. Members: users holding **DHA-4: Finance User** (added individually) |
| Profile `DHA - Project On Stop: Read` | Read only. Member: the **root business unit default team** (prod `dhapd` holds all ~94 users) |
| Project forms | On Stop sits in the header next to Status Reason on **Information** and **Project - DHA1** |
| Time entry form | `fwp_projectOnStopWarning` web resource on the `fwp_timeentry` **Information** form: OnLoad + `fwp_project` OnChange |
| Solution | `FWPProjectOnStop` (publisher `SelectTechnology`) |

The decision to stop a project is manual. Deriving it from debt or invoice ageing was rejected
(Liz). No reason is recorded with the stop.

**Why two profiles.** A secured column is invisible to anyone who isn't in a profile granting
read. Without the Read profile, the planners (the people who need the warning) would see
`null` and never be warned. If users are ever moved out of the root business unit, add the
default team of their new BU to the Read profile.

**Keeping the Finance profile in step with the role.** Column security profiles are assigned
per user or team, not per security role. When someone gains or loses DHA-4: Finance User, add
or remove them from `DHA - Project On Stop: Finance` too. System Administrators can always
edit the field.

## Where the warning appears

| Surface | When selected | When saved |
|---|---|---|
| WPF client, main time entry panel | Red banner under the client box (`ProjectOnStopBanner`) | Red modal (`ProjectOnStopWarningDialog`) after a single or block entry is saved |
| WPF client, Edit Time Entry dialog | Red banner under the project picker | Red modal after the update is saved |
| Dynamics, time entry form | Red form notification (ERROR level) | Alert dialog after save |

The WPF list data can be stale: Finance may set the flag after the list loaded, and pinned
projects come from local storage. So the client re-reads `fwp_onstop` directly
(`ProjectService.IsProjectOnStop`) on selection and again after each save. If that check
fails, the cached value is used and the save is never blocked.

The wording is a single constant, `Project.OnStopWarningMessage` in the WPF app, plus `MESSAGE`
in the web resource. Change both together.

Not covered: the `TimeAndDisbursementPage` canvas app, and the Disbursements tab (DHA-8 is
about time only).

## Deploying to production

**Order matters.** The WPF client requests `fwp_onstop` in every project query. Release the
client **only after** the column exists in prod, or every user's project list will fail to load.

1. Export `FWPProjectOnStop` from dev and import it into prod. This brings the column, both
   profiles (with their permissions), the web resource and the three form changes.
2. In prod, add the members, because profile membership does not travel in a solution:
   - **Read:** the `dhapd` root BU default team.
   - **Finance:** current DHA-4 holders: Julie Thorne, Grace Paternoster, Alex Hicken,
     David Hicken, Harriet Baker, Elizabeth Homewood, Theo Homewood, Louise Overton.
     Add Select Support or Dynamics Automation only if they genuinely need to set it.
3. Confirm auditing is on for Project in prod.
4. Release the WPF client.

## Production (deployed 2026-10-14)

`FWPProjectOnStop` **v1.0.1.0, managed**. Deployed by `DHA.Dataverse/Provisioning/deploy_project_on_stop_prod.py`
(on the leave branches). The package is in `DHA.Dataverse/Solutions`.

- **Forms.** Dev and prod forms had drifted. "Project - DHA1" and Time Entry "Information" were rebuilt from
  **prod's** current definitions plus only our change, so prod kept "Unreconciled Value" and DHA1's roles. Prod's
  Project **Information** form, the one Finance uses (DHA1 is admin-only in prod), has unmanaged edits on top,
  so On Stop was added to its header **directly in prod**. A future managed form change to that form won't show
  until its unmanaged layer is dealt with.
- **Profile members (prod):** Read ← `dhapd` root team (everyone). Finance ← Harriet Baker, Grace Paternoster,
  Alex Hicken, Elizabeth Homewood, David Hicken, Julie Thorne, Theo Homewood, Louise Overton. The service
  accounts (Select Support, Dynamics Automation) were left out.
- **Verified:** column secured and audited, both forms carry On Stop, the time entry form carries the warning
  script, and a planner and a finance user can both read the flag.
- **Not yet live:** the **desktop** warning (banner and dialog). It ships with the next desktop release, which must
  come after this, since the app queries `fwp_onstop`.

## Dev test status (2026-10-13)

- A Project Manager (Jessica Bache, impersonated) can **read** `fwp_onstop` but is refused
  when updating it ("does not have update permissions to a On Stop secured field").
- The change shows up in the Project's audit history.
- `Test Project for Time & Billing` (`12a3e411-5414-f011-9989-000d3ad5d75f`) is left
  **on stop** in dev for manual testing.
- Not yet proven: the Finance positive path as a real non-admin user. Dev's role assignments
  don't match prod: no non-admin user there holds DHA-4 together with the base roles needed
  to update a project. Julie Thorne has been added to the Finance profile in dev, but she
  lacks DHA roles there.
