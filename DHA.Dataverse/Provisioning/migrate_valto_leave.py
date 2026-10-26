"""Leave parity stage 8: migrate the Valto leave app (SharePoint) into Dynamics.

    python -I migrate_valto_leave.py report     [--prod]                    # no changes: what an import would do
    python -I migrate_valto_leave.py import     [--prod --yes] [--cutover YYYY-MM-DD]
    python -I migrate_valto_leave.py reconcile  [--prod]                    # Valto vs Dynamics remaining days

Targets DEV unless --prod (which also needs --yes for an import). In dev the three test users the automated tests book
leave for are left out, so the tests keep working. Valto is always read through valto_source (a flow in
dev; nothing is stored in Dynamics on the way). Reports go to migration-reports/ next to this script - they name people
and their leave, so that folder is git-ignored. Delete them when migration is done.

What is migrated (parity plan section 5, remaining-stages plan section 6):
  - Request Type Configuration -> fwp_leavetype: the types requests actually use are created if missing (absence, uses
    entitlement only if Valto's "Has Entitlement", Hours if Valto forces hours). TOIL types are not migrated (Q4).
  - Employee Configuration -> colleague configuration: approver, additional approver, base entitlement. Working days are
    NOT copied - Dynamics is the one place for them (F3) - differences are reported. Department is reported (F6).
  - Entitlement Balances -> fwp_leaveentitlement, YEARS. Valto's per-person carry-forward limit becomes the override.
  - Leave Request Data -> fwp_leaverequest, requests starting in YEARS[0] or later, keyed fwp_legacyid "LR-<ID>":
      * ending before --cutover (default today): imported with plug-ins and flows bypassed, keeping Valto's own day count -
        history, so nothing goes on timesheets and nobody is emailed;
      * from --cutover on: through the leave plug-in as if HR booked it (flows suppressed), so approved absence gets its
        time entries under the same LR-<ID> key phase A uses - existing ones are adopted, not duplicated.
    Re-running updates what changed in Valto (the nightly delta during the parallel run).
  - Book on behalf of users -> members of the "Leave - Book on behalf" team.
"""
import argparse, collections, datetime as dt, json, os, sys
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
DEV, PROD = "https://dhapd-dev.crm11.dynamics.com", "https://dhapd.crm11.dynamics.com"

YEARS = (2025, 2026, 2027)
PENDING, APPROVED, REJECTED, CANCEL_REQUESTED, CANCELLED = 800470000, 800470001, 800470002, 800470003, 800470004
DAYS_UNIT, HOURS_UNIT = 800470000, 800470001
# Valto title -> Dynamics name, where they differ
TYPE_NAMES = {"Authorised Absense": "Authorised Absence"}
SKIPPED_TYPES = {"Take TOIL", "Add TOIL"}  # TOIL not needed (Q4)
WFH = "Working From Home"
BOOK_ON_BEHALF = "Leave - Book on behalf"
REPORTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "migration-reports")
# Dev's automated tests book leave for these real people (DHA.Dataverse/Tests/dvtest.py), so a dev import leaves them out
DEV_TEST_USERS = {"e1890edf-e060-ed11-9562-002248428ff5", "e666f5d2-e060-ed11-9562-002248428ff5", "ab66fac0-75a0-f111-b8de-002248423048"}


# ---------------------------------------------------------------------------------------------------------------------
# Mapping (pure - unit tested in Tests/test_dha09_valto_migration.py)
# ---------------------------------------------------------------------------------------------------------------------

def value(x):
    """A SharePoint choice/lookup as the connector returns it ({'Value': ...}), or a plain value."""
    return x.get("Value") if isinstance(x, dict) else x


def email(person):
    return ((person or {}).get("Email") or "").strip().lower() or None


def status_of(item):
    """Valto keeps "cancellation requested" as a flag on an approved request; a cancelled one may have the flag too."""
    s = value(item.get("Status"))
    if s == "Approved":
        return CANCEL_REQUESTED if item.get("RequestCancelled_x003f_") else APPROVED
    return {"Pending": PENDING, "Rejected": REJECTED, "Cancelled": CANCELLED}.get(s)


def hhmm(hours, minutes):
    if hours is None:
        return None
    return f"{int(hours):02d}:{int(minutes or 0):02d}"


def type_name(item):
    t = value(item.get("Request_x0020_Type"))
    return TYPE_NAMES.get(t, t)


def times_for(item, pattern_day=None, leave_day_hours=None):
    """(start, end) times as the desktop app stores them.
    Hours requests (Medical) keep Valto's exact times. Half days are single-day in Valto: a morning ends, and an afternoon
    starts, half a leave day from the person's own start or finish - as the desktop app books it, so the plug-in counts
    exactly half. Without a pattern (history kept as Valto counted it) Valto's own 12:30 is used."""
    if value(item.get("Units")) == "Hours":
        return (hhmm(item.get("Request_x0020_Start_x0020_Hour"), item.get("RequestStartMinutes")),
                hhmm(item.get("RequestEndHour"), item.get("RequestEndMinutes")))
    half = value(item.get("RequestDayDuration"))
    if half not in ("1/2 Day (AM)", "1/2 Day (PM)"):
        return None, None
    if pattern_day is None or not leave_day_hours:
        return (None, "12:30") if half.endswith("(AM)") else ("12:30", None)
    start, finish = pattern_day
    offset = dt.timedelta(minutes=round(float(leave_day_hours) * 60 / 2))
    t = lambda base: (dt.datetime(2000, 1, 1) + base).strftime("%H:%M")
    return (None, t(start + offset)) if half.endswith("(AM)") else (t(finish - offset), None)


def history_hours(item, working, leave_day_hours, bank_holidays):
    """Timesheet hours for a request kept as Valto counted it (display only - history never goes on timesheets).
    working: 7 booleans, Monday first; leave_day_hours: 7 decimals."""
    if value(item.get("Units")) == "Hours":
        return round(float(item.get("LeaveDuration") or 0), 2)
    start = dt.date.fromisoformat(item["RequestStartDate"][:10])
    end = dt.date.fromisoformat((item.get("RequestEndDate") or item["RequestStartDate"])[:10])
    total, d = 0.0, start
    while d <= end:
        i = d.weekday()
        if working[i] and d not in bank_holidays:
            total += float(leave_day_hours[i])
        d += dt.timedelta(days=1)
    if value(item.get("RequestDayDuration")) in ("1/2 Day (AM)", "1/2 Day (PM)"):
        total /= 2
    return round(total, 2)


def valto_remaining(entitlement, requests, deducting_types):
    """Valto's remaining days for one person-year: base + carried forward + purchased, less every request in that
    entitlement year of a type with entitlement that is approved, pending or awaiting cancellation (Q6)."""
    total = sum(float(entitlement.get(k) or 0) for k in ("BaseEntitlement", "CarryForward", "PurchasedLeave"))
    used = sum(float(r.get("LeaveDuration") or 0) for r in requests
               if value(r.get("Request_x0020_Type")) in deducting_types and status_of(r) in (APPROVED, PENDING, CANCEL_REQUESTED))
    return round(total - used, 2)


def request_year(item):
    y = value(item.get("Entitlement_x0020_Year"))
    try:
        return int(float(y))
    except (TypeError, ValueError):
        return int(item["RequestStartDate"][:4])


def request_name(full_name, start, end):
    s, e = dt.date.fromisoformat(start), dt.date.fromisoformat(end)
    return f"{full_name} - {s:%d/%m/%Y}" + (f" to {e:%d/%m/%Y}" if e != s else "")


# ---------------------------------------------------------------------------------------------------------------------
# Dynamics
# ---------------------------------------------------------------------------------------------------------------------

class Target:
    def __init__(self, org):
        self.org = org
        import dv
        self.dv = dv

    def call(self, method, path, body=None, extra=None):
        return self.dv.call(method, path.replace(" ", "%20"), body, extra=extra, org=self.org)

    def rows(self, path):
        out, url = [], path
        while url:
            s, d, _ = self.call("GET", url, extra={"Prefer": 'odata.include-annotations="*",odata.maxpagesize=5000'})
            if s >= 300:
                raise RuntimeError(f"GET {path}: {d}")
            out += d["value"]
            url = d.get("@odata.nextLink", "").split("/api/data/v9.2/")[-1] or None
        return out


def load_dynamics(t):
    users = t.rows("systemusers?$select=fullname,internalemailaddress,domainname,isdisabled")
    by_email = {}
    for u in users:
        for k in ("internalemailaddress", "domainname"):
            if u.get(k):
                by_email.setdefault(u[k].lower(), u)
    days = ("monday", "tuesday", "wednesday", "thursday", "friday")
    cfg_cols = ",".join(["_fwp_colleague_value", "_fwp_leaveapprover_value", "_fwp_additionalleaveapprover_value", "fwp_baseentitlement"]
                        + [f"fwp_{d}{k}" for d in days for k in ("start", "finish", "expectedhours")])
    configs = {c["_fwp_colleague_value"]: c for c in t.rows(f"fwp_colleagueconfigurations?$select=fwp_colleagueconfigurationid,{cfg_cols}"
                                                             f"&$filter=statecode eq 0 and _fwp_colleague_value ne null")}
    types = {r["fwp_name"]: r for r in t.rows("fwp_leavetypes?$select=fwp_name,fwp_isabsence,fwp_deductsfromentitlement,fwp_forcedunits,"
                                              "fwp_sortorder,statecode")}
    requests = {r["fwp_legacyid"]: r for r in t.rows(
        "fwp_leaverequests?$select=fwp_legacyid,fwp_status,fwp_startdate,fwp_enddate,fwp_starttime,fwp_endtime,fwp_days,"
        "fwp_requestercomments,fwp_approvercomments,fwp_cancellationcomments,fwp_calendareventid,_fwp_leavetype_value,"
        "_fwp_approver_value,_fwp_additionalapprover_value,_fwp_employee_value&$filter=startswith(fwp_legacyid,'LR-')")}
    ents = {}
    for e in t.rows("fwp_leaveentitlements?$select=_fwp_employee_value,fwp_yearstart,fwp_entitlementdays,fwp_carriedforwarddays,"
                    "fwp_carriedtonextyeardays,fwp_carryforwardoverride,fwp_adjustmentdays&$filter=statecode eq 0"):
        ents[(e["_fwp_employee_value"], int(e["fwp_yearstart"][:4]))] = e
    entry_keys = {e["fwp_leavesourcekey"] for e in t.rows("fwp_timeentries?$select=fwp_leavesourcekey&$filter=startswith(fwp_leavesourcekey,'LR-')")}
    holidays = {dt.date.fromisoformat(h["fwp_date"][:10]) for h in t.rows("fwp_publicholidaies?$select=fwp_date")}
    env = {r["schemaname"]: (r["environmentvariabledefinition_environmentvariablevalue"] or [{}])[0].get("value") or r["defaultvalue"]
           for r in t.rows("environmentvariabledefinitions?$select=schemaname,defaultvalue"
                           "&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)"
                           "&$filter=schemaname eq 'fwp_LeaveDayHours'")}
    leave_day_hours = [float(x) for x in (env.get("fwp_LeaveDayHours") or "8.5,8.5,8.5,8.5,8,0,0").split(",")]
    team = t.rows(f"teams?$select=teamid&$filter=name eq '{BOOK_ON_BEHALF}'&$expand=teammembership_association($select=systemuserid)")
    return dict(users=users, by_email=by_email, configs=configs, types=types, requests=requests, ents=ents, entry_keys=entry_keys,
                holidays=holidays, leave_day_hours=leave_day_hours, team=team[0] if team else None)


def parse_time(text, default):
    try:
        h, m = (text or "").replace(".", ":").split(":")[:2]
        return dt.timedelta(hours=int(h), minutes=int(m))
    except ValueError:
        return default


def pattern_for(cfg, leave_day_hours):
    """(working[7], (start, finish)[7]) from colleague configuration - the same rules as LeaveDayCalculator.BuildPattern."""
    working, times = [], []
    for i, d in enumerate(("monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday")):
        configured = cfg.get(f"fwp_{d}expectedhours") if cfg and i < 5 else None
        working.append(configured > 0 if configured is not None else leave_day_hours[i] > 0)
        times.append((parse_time(cfg.get(f"fwp_{d}start") if cfg else None, dt.timedelta(hours=8, minutes=30)),
                      parse_time(cfg.get(f"fwp_{d}finish") if cfg else None, dt.timedelta(hours=17, minutes=30))))
    return working, times


# ---------------------------------------------------------------------------------------------------------------------
# Plan: what an import would do (built the same way for report and import)
# ---------------------------------------------------------------------------------------------------------------------

def build_plan(valto, dyn, cutover):
    notes = collections.defaultdict(list)
    user = lambda person: dyn["by_email"].get(email(person)) if email(person) else None
    ref = lambda u: u["systemuserid"] if u else None

    # Types the migrated requests use
    rt = {r["Title"]: r for r in valto["Request Type Configuration"]}
    deducting = {title for title, r in rt.items() if r.get("HasEntitlement_x003f_") and title not in SKIPPED_TYPES}
    requests = [r for r in valto["Leave Request Data"] if (r.get("RequestStartDate") or "")[:4] >= str(YEARS[0])]
    used = collections.Counter(value(r.get("Request_x0020_Type")) for r in requests)
    new_types, sort = [], max([t.get("fwp_sortorder") or 0 for t in dyn["types"].values()] + [0])
    for title, n in used.items():
        name = TYPE_NAMES.get(title, title)
        if title in SKIPPED_TYPES or title not in rt:
            notes["types"].append(f"{n} requests of type '{title}' - not migrated")
            continue
        existing = dyn["types"].get(name)
        if existing is None or existing.get("statecode") == 1:
            sort += 1
            new_types.append((name, existing, {"fwp_name": name, "fwp_isabsence": name != WFH,
                                               "fwp_deductsfromentitlement": title in deducting,
                                               "fwp_forcedunits": HOURS_UNIT if value(rt[title].get("ForcedUnits")) == "Hours" else DAYS_UNIT,
                                               "fwp_requiresapproval": True, "fwp_sortorder": sort, "statecode": 0, "statuscode": 1}))
        elif bool(existing.get("fwp_deductsfromentitlement")) != (title in deducting):
            notes["types"].append(f"'{name}': uses entitlement is {existing.get('fwp_deductsfromentitlement')} in Dynamics, "
                                  f"{title in deducting} in Valto - Dynamics left as it is, check")

    # Employee configuration -> colleague configuration
    config_changes = []
    for ec in valto["Employee Configuration"]:
        u = user(ec.get("Employee"))
        if not u:
            notes["people"].append(f"Employee Configuration: {email(ec.get('Employee'))} has no Dynamics user")
            continue
        cfg = dyn["configs"].get(u["systemuserid"])
        if not cfg:
            notes["people"].append(f"{u['fullname']}: no colleague configuration in Dynamics - approver and entitlement not set")
            continue
        want = {"_fwp_leaveapprover_value": ref(user(ec.get("Approver"))),
                "_fwp_additionalleaveapprover_value": ref(user(ec.get("AdditionalApprover"))),
                "fwp_baseentitlement": ec.get("BaseEntitlement")}
        if ec.get("Approver") and not want["_fwp_leaveapprover_value"]:
            notes["people"].append(f"{u['fullname']}: approver {email(ec.get('Approver'))} has no Dynamics user")
        if want["_fwp_leaveapprover_value"] == u["systemuserid"]:
            want["_fwp_leaveapprover_value"] = None
            notes["people"].append(f"{u['fullname']}: Valto has them as their own approver - left empty")
        if want["_fwp_additionalleaveapprover_value"] == u["systemuserid"]:
            want["_fwp_additionalleaveapprover_value"] = None
        diff = {k: v for k, v in want.items() if (cfg.get(k) or None) != (v or None) and not (k == "fwp_baseentitlement" and v is None)}
        if diff:
            config_changes.append((u, cfg, diff))
        # working days: reported, never copied (F3)
        for i, d in enumerate(("Monday", "Tuesday", "Wednesday", "Thursday", "Friday")):
            v_works = value(ec.get(d)) not in (None, "Off")
            hours = cfg.get(f"fwp_{d.lower()}expectedhours")
            if hours is None:
                notes["working days"].append(f"{u['fullname']}: {d} has no hours in Dynamics (Valto: {value(ec.get(d))})")
            elif v_works != (hours > 0):
                notes["working days"].append(f"{u['fullname']}: {d} is '{value(ec.get(d))}' in Valto, {hours}h in Dynamics")
            elif value(ec.get(d)) not in ("Full Day", "Off"):
                notes["working days"].append(f"{u['fullname']}: {d} is '{value(ec.get(d))}' in Valto ({hours}h in Dynamics)")
        if value(ec.get("Department")):
            notes["departments"].append(f"{u['fullname']}: {value(ec.get('Department'))}")

    # Entitlements
    limits = {email(ec.get("Employee")): ec.get("CarryForwardLimit") for ec in valto["Employee Configuration"]}
    seen = {}
    for eb in sorted(valto["Entitlement Balances"], key=lambda r: r.get("Modified") or ""):
        year = int(eb.get("Year") or 0)
        if year not in YEARS:
            continue
        u = user(eb.get("Employee"))
        if not u:
            notes["people"].append(f"Entitlement {year}: {email(eb.get('Employee'))} has no Dynamics user")
            continue
        if (u["systemuserid"], year) in seen:
            notes["entitlements"].append(f"{u['fullname']} has two {year} rows in Valto - the later one is used")
        seen[(u["systemuserid"], year)] = (u, eb)
    ent_changes = []
    for (_, year), (u, eb) in seen.items():
        limit = limits.get(email(eb.get("Employee")))
        want = {"fwp_entitlementdays": eb.get("BaseEntitlement") or 0, "fwp_carriedforwarddays": eb.get("CarryForward") or 0,
                "fwp_carriedtonextyeardays": eb.get("CarriedForwardtonextholidayyear"),
                "fwp_carryforwardoverride": limit if limit else None}
        if eb.get("PurchasedLeave"):
            want["fwp_adjustmentdays"] = eb["PurchasedLeave"]
            notes["entitlements"].append(f"{u['fullname']} {year}: {eb['PurchasedLeave']} purchased days put in the adjustment")
        if eb.get("Comments"):
            notes["entitlements"].append(f"{u['fullname']} {year}: Valto comment not migrated: {' '.join(eb['Comments'].split())[:160]}")
        existing = dyn["ents"].get((u["systemuserid"], year))
        diff = {k: v for k, v in want.items() if existing is None or (existing.get(k) or 0) != (v or 0)}
        if diff:
            ent_changes.append((u, year, existing, diff))

    # Requests
    request_changes = []
    for r in requests:
        key = f"LR-{r['ID']}"
        u = user(r.get("Employee"))
        if not u:
            notes["requests"].append(f"{key}: {email(r.get('Employee'))} has no Dynamics user - not migrated")
            continue
        title = value(r.get("Request_x0020_Type"))
        tname = TYPE_NAMES.get(title, title)
        if title in SKIPPED_TYPES or not title:
            continue
        status = status_of(r)
        start, end = r["RequestStartDate"][:10], (r.get("RequestEndDate") or r["RequestStartDate"])[:10]
        existing = dyn["requests"].get(key)
        # through the plug-in from the cutover on, or if it already has time entries; otherwise kept as history
        live = end >= cutover or key in dyn["entry_keys"]
        cfg = dyn["configs"].get(u["systemuserid"])
        working, times = pattern_for(cfg, dyn["leave_day_hours"])
        weekday = dt.date.fromisoformat(start).weekday()
        st, et = times_for(r, times[weekday] if live else None, dyn["leave_day_hours"][weekday])
        body = {"fwp_legacyid": key, "fwp_status": status, "fwp_startdate": start, "fwp_enddate": end,
                "fwp_starttime": st, "fwp_endtime": et,
                "fwp_requestercomments": r.get("RequestorComments"), "fwp_approvercomments": r.get("ApproverComments"),
                "fwp_cancellationcomments": r.get("CancellationComments"),
                "fwp_calendareventid": r.get("CalendarID") if status in (APPROVED, CANCEL_REQUESTED) else None,
                "_type": tname, "_employee": u["systemuserid"], "_approver": ref(user(r.get("Approver"))),
                "_additional": ref(user(r.get("AdditionalApprover"))), "_bookedby": ref(user(r.get("Author"))) or u["systemuserid"]}
        if r.get("RequestCancelledon"):
            body["fwp_cancellationrequestedon"] = r["RequestCancelledon"]
        if not live:
            body["fwp_days"] = round(float(r.get("LeaveDuration") or 0), 2) if value(r.get("Units")) != "Hours" else None
            body["fwp_hours"] = history_hours(r, working, dyn["leave_day_hours"], dyn["holidays"])
            if body["fwp_days"] is None:
                body["fwp_days"] = round(body["fwp_hours"] / float(dyn["leave_day_hours"][weekday] or 8.5), 2)
            body["fwp_name"] = request_name(u["fullname"], start, end)
        if existing is None:
            request_changes.append(("create", key, live, u, body, None))
        else:
            compare = {"fwp_status": existing.get("fwp_status"), "fwp_startdate": (existing.get("fwp_startdate") or "")[:10],
                       "fwp_enddate": (existing.get("fwp_enddate") or "")[:10], "fwp_starttime": existing.get("fwp_starttime"),
                       "fwp_endtime": existing.get("fwp_endtime"), "fwp_requestercomments": existing.get("fwp_requestercomments"),
                       "fwp_approvercomments": existing.get("fwp_approvercomments"),
                       "fwp_cancellationcomments": existing.get("fwp_cancellationcomments")}
            diff = {k: body[k] for k, v in compare.items() if (v or None) != (body.get(k) or None)}
            if diff:
                request_changes.append(("update", key, live, u, diff, existing))

    # Book on behalf
    members = {m["systemuserid"] for m in (dyn["team"] or {}).get("teammembership_association", [])}
    book = []
    for b in valto["Book on behalf of users"]:
        u = user(b.get("User"))
        if not u:
            notes["people"].append(f"Book on behalf: {email(b.get('User'))} has no Dynamics user")
        elif u.get("isdisabled"):
            notes["people"].append(f"Book on behalf: {u['fullname']} is disabled in Dynamics - not added to the team")
        elif u["systemuserid"] not in members:
            book.append(u)
    return dict(new_types=new_types, config_changes=config_changes, ent_changes=ent_changes, request_changes=request_changes,
                book=book, notes=notes, deducting=deducting, requests=requests)


# ---------------------------------------------------------------------------------------------------------------------
# Writing
# ---------------------------------------------------------------------------------------------------------------------

BYPASS = {"MSCRM.BypassCustomPluginExecution": "true", "MSCRM.SuppressCallbackRegistrationExpanderJob": "true"}
NO_FLOWS = {"MSCRM.SuppressCallbackRegistrationExpanderJob": "true"}


def apply(t, plan, dyn, log):
    for name, existing, body in plan["new_types"]:
        s, d, _ = (t.call("PATCH", f"fwp_leavetypes({existing['fwp_leavetypeid']})", body) if existing
                   else t.call("POST", "fwp_leavetypes", body, extra={"Prefer": "return=representation"}))
        log(f"leave type {name}: {s}")
        if s < 300 and not existing:
            dyn["types"][name] = d
    type_ids = {n: tp["fwp_leavetypeid"] for n, tp in dyn["types"].items()}

    for u, cfg, diff in plan["config_changes"]:
        body = {}
        for k, field in (("_fwp_leaveapprover_value", "fwp_LeaveApprover"), ("_fwp_additionalleaveapprover_value", "fwp_AdditionalLeaveApprover")):
            if k in diff and diff[k]:
                body[f"{field}@odata.bind"] = f"/systemusers({diff[k]})"
        if "fwp_baseentitlement" in diff:
            body["fwp_baseentitlement"] = diff["fwp_baseentitlement"]
        for k, field in (("_fwp_leaveapprover_value", "fwp_LeaveApprover"), ("_fwp_additionalleaveapprover_value", "fwp_AdditionalLeaveApprover")):
            if k in diff and not diff[k]:
                t.call("DELETE", f"fwp_colleagueconfigurations({cfg['fwp_colleagueconfigurationid']})/{field}/$ref")
        if body:
            s, d, _ = t.call("PATCH", f"fwp_colleagueconfigurations({cfg['fwp_colleagueconfigurationid']})", body)
            log(f"leave settings {u['fullname']}: {s}" + ("" if s < 300 else f" {d}"))

    for u, year, existing, diff in plan["ent_changes"]:
        if existing:
            s, d, _ = t.call("PATCH", f"fwp_leaveentitlements({existing['fwp_leaveentitlementid']})", diff)
        else:
            s, d, _ = t.call("POST", "fwp_leaveentitlements", dict(diff, **{"fwp_Employee@odata.bind": f"/systemusers({u['systemuserid']})",
                                                                          "fwp_yearstart": f"{year}-01-01"}))
        log(f"entitlement {u['fullname']} {year}: {s}" + ("" if s < 300 else f" {d}"))

    # Requests: one person at a time (the plug-in's overlap check), several people at once
    by_person = collections.defaultdict(list)
    for change in plan["request_changes"]:
        by_person[change[3]["systemuserid"]].append(change)
    failures = []

    def person(changes):
        # creates before updates, oldest first
        for action, key, live, u, body, existing in sorted(changes, key=lambda c: (c[0] != "create", c[4].get("fwp_startdate") or "")):
            result = write_request(t, action, key, live, u, body, existing, type_ids)
            if result:
                failures.append(result)
    with ThreadPoolExecutor(6) as pool:
        list(pool.map(person, by_person.values()))
    for f in failures:
        log(f)

    for u in plan["book"] if dyn["team"] else []:
        # one at a time: a single user Dynamics won't accept (e.g. disabled) fails the whole call
        s, d, _ = t.call("POST", f"teams({dyn['team']['teamid']})/Microsoft.Dynamics.CRM.AddMembersTeam",
                         {"Members": [{"@odata.type": "Microsoft.Dynamics.CRM.systemuser", "systemuserid": u["systemuserid"]}]})
        log(f"book-on-behalf team + {u['fullname']}: {s}" + ("" if s < 300 else f" {error_text(d)}"))
    return failures


def write_request(t, action, key, live, u, body, existing, type_ids):
    body = dict(body)
    data = {k: v for k, v in body.items() if not k.startswith("_") and v is not None}
    if action == "create":
        if body["_type"] not in type_ids:
            return f"{key}: type '{body['_type']}' isn't in Dynamics - not migrated"
        data["fwp_LeaveType@odata.bind"] = f"/fwp_leavetypes({type_ids[body['_type']]})"
        data["fwp_Employee@odata.bind"] = f"/systemusers({body['_employee']})"
        data["fwp_BookedBy@odata.bind"] = f"/systemusers({body['_bookedby']})"
        for k, field in (("_approver", "fwp_Approver"), ("_additional", "fwp_AdditionalApprover")):
            if body[k] and body[k] != body["_employee"]:
                data[f"{field}@odata.bind"] = f"/systemusers({body[k]})"
        if not live:
            data["ownerid@odata.bind"] = f"/systemusers({body['_employee']})"
    headers = dict(BYPASS if not live else NO_FLOWS, Prefer="return=representation")
    path = "fwp_leaverequests" if action == "create" else f"fwp_leaverequests({existing['fwp_leaverequestid']})"
    s, d, _ = t.call("POST" if action == "create" else "PATCH", path, data, extra=headers)
    if s >= 300 and action == "create" and not live and "ownerid@odata.bind" in data:
        # the employee can't own leave requests (no DHA-1 - dev test users); keep it with the person migrating
        data.pop("ownerid@odata.bind")
        s, d, _ = t.call("POST", path, data, extra=headers)
    if s >= 300:
        return f"{key} ({u['fullname']}, {body.get('fwp_startdate', '')}) {action} failed: {error_text(d)}"
    if action == "create" and not live:
        # what the plug-in does on create: the approvers can see and decide it
        rid = d["fwp_leaverequestid"]
        for k in ("_approver", "_additional"):
            if body[k] and body[k] != body["_employee"]:
                t.call("POST", "GrantAccess", {
                    "Target": {"@odata.type": "Microsoft.Dynamics.CRM.fwp_leaverequest", "fwp_leaverequestid": rid},
                    "PrincipalAccess": {"Principal": {"@odata.type": "Microsoft.Dynamics.CRM.systemuser", "systemuserid": body[k]},
                                        "AccessMask": "ReadAccess, WriteAccess"}})
    return None


def error_text(d):
    try:
        return json.loads(d)["error"]["message"][:300]
    except Exception:
        return str(d)[:300]


# ---------------------------------------------------------------------------------------------------------------------
# Reports
# ---------------------------------------------------------------------------------------------------------------------

def write_report(name, lines):
    os.makedirs(REPORTS, exist_ok=True)
    path = os.path.join(REPORTS, f"{dt.datetime.now():%Y%m%d-%H%M} {name}.md")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("Report:", path)


def plan_report(plan, target, cutover):
    rc = plan["request_changes"]
    count = collections.Counter((a, "live" if live else "history") for a, _, live, *_ in rc)
    lines = [f"# Valto migration plan - {target} - {dt.datetime.now():%d %b %Y %H:%M}", "",
             f"Cutover date: {cutover} (requests ending before it are kept as history, off timesheets).", "",
             "| What | Count |", "|---|---|",
             f"| Leave types to add | {len(plan['new_types'])}: {', '.join(n for n, *_ in plan['new_types']) or '-'} |",
             f"| Leave settings to change | {len(plan['config_changes'])} |",
             f"| Entitlements to create or change | {len(plan['ent_changes'])} |",
             f"| Requests to create (history / through plug-in) | {count[('create', 'history')]} / {count[('create', 'live')]} |",
             f"| Requests to update (history / through plug-in) | {count[('update', 'history')]} / {count[('update', 'live')]} |",
             f"| Book-on-behalf members to add | {len(plan['book'])} |", ""]
    if plan["config_changes"]:
        lines += ["## Leave settings changes", ""]
        lines += [f"- {u['fullname']}: {', '.join(k.replace('_value', '').strip('_') for k in diff)}" for u, _, diff in plan["config_changes"]]
        lines.append("")
    updates = [c for c in rc if c[0] == "update"]
    if updates:
        lines += ["## Requests changed in Valto since the last run", ""]
        lines += [f"- {key} {u['fullname']}: {', '.join(sorted(diff))}" for _, key, _, u, diff, _ in updates[:300]]
        lines.append("")
    for section, items in plan["notes"].items():
        lines += [f"## {section.capitalize()} ({len(items)})", ""] + [f"- {i}" for i in sorted(set(items))] + [""]
    return lines


def reconcile(valto, dyn, t, plan):
    """Per person and year: Valto's remaining days against Dynamics' (fwp_GetLeaveBalance)."""
    by_person_year = collections.defaultdict(list)
    for r in plan["requests"]:
        by_person_year[(email(r.get("Employee")), request_year(r))].append(r)
    rows, diffs = [], 0
    latest = {}  # the same row the import used where Valto has two for a year
    for eb in sorted(valto["Entitlement Balances"], key=lambda r: r.get("Modified") or ""):
        latest[(email(eb.get("Employee")), int(eb.get("Year") or 0))] = eb
    for eb in latest.values():
        year = int(eb.get("Year") or 0)
        e = email(eb.get("Employee"))
        u = dyn["by_email"].get(e) if e else None
        if year not in YEARS or not u:
            continue
        v = valto_remaining(eb, by_person_year[(e, year)], plan["deducting"])
        s, d, _ = t.call("POST", "fwp_GetLeaveBalance", {"EmployeeId": u["systemuserid"], "Year": year})
        if s >= 300:
            rows.append((u["fullname"], year, v, None, "error: " + error_text(d)))
            continue
        dyn_remaining = round(d["BaseDays"] + d["CarriedForwardDays"] + d["AdjustmentDays"] - d["TakenDays"] - d["BookedDays"], 2)
        diff = round(dyn_remaining - v, 2)
        if abs(diff) >= 0.01:
            diffs += 1
        rows.append((u["fullname"], year, v, dyn_remaining, "" if abs(diff) < 0.01 else f"{diff:+}"))
    lines = [f"# Valto reconciliation - {t.org} - {dt.datetime.now():%d %b %Y %H:%M}", "",
             f"{len(rows)} person-years compared; **{diffs} differ**. Remaining days = entitlement + carried forward + "
             "adjustment, less approved and pending leave of types that use entitlement.", "",
             "| Person | Year | Valto | Dynamics | Difference |", "|---|---|---|---|---|"]
    lines += [f"| {n} | {y} | {v} | {dr} | {df} |" for n, y, v, dr, df in sorted(rows, key=lambda r: (r[4] == "", r[0], r[1]))]
    # Where the difference comes from: requests whose day count differs
    lines += ["", "## Requests whose days differ (Valto vs Dynamics)", ""]
    ids = {r["fwp_legacyid"]: r for r in t.rows("fwp_leaverequests?$select=fwp_legacyid,fwp_days,_fwp_employee_value"
                                                 "&$filter=startswith(fwp_legacyid,'LR-')")}
    for r in plan["requests"]:
        if value(r.get("Request_x0020_Type")) not in plan["deducting"] or status_of(r) not in (APPROVED, PENDING, CANCEL_REQUESTED):
            continue
        mine = ids.get(f"LR-{r['ID']}")
        if mine and abs((mine.get("fwp_days") or 0) - float(r.get("LeaveDuration") or 0)) >= 0.01:
            u = dyn["by_email"].get(email(r.get("Employee")))
            lines.append(f"- LR-{r['ID']} {u['fullname'] if u else '?'} {r['RequestStartDate'][:10]}: Valto {r.get('LeaveDuration')}, "
                         f"Dynamics {mine.get('fwp_days')}")
    write_report("reconciliation", lines)
    print(f"{len(rows)} person-years, {diffs} differ")


# ---------------------------------------------------------------------------------------------------------------------

def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("mode", choices=["report", "import", "reconcile"])
    p.add_argument("--prod", action="store_true")
    p.add_argument("--yes", action="store_true", help="needed with --prod for an import")
    p.add_argument("--cutover", default=dt.date.today().isoformat())
    a = p.parse_args()
    org = PROD if a.prod else DEV
    if a.mode == "import" and a.prod and not a.yes:
        sys.exit("Importing into PROD needs --yes as well.")

    import valto_source
    print("Reading Valto...")
    valto = {name: valto_source.items(name) for name in valto_source.LISTS}
    t = Target(org)
    print("Reading", org)
    dyn = load_dynamics(t)
    if not a.prod:
        dyn["by_email"] = {k: u for k, u in dyn["by_email"].items() if u["systemuserid"] not in DEV_TEST_USERS}
    plan = build_plan(valto, dyn, a.cutover)
    target = "PROD" if a.prod else "dev"

    if a.mode == "report":
        write_report(f"plan {target}", plan_report(plan, target, a.cutover))
    elif a.mode == "import":
        if a.prod:
            for flow in t.rows("workflows?$select=name,statecode&$filter=category eq 5 and statecode eq 1 and "
                               "(name eq 'Leave - Notifications' or name eq 'Leave - Outlook calendar')"):
                print("Note:", flow["name"], "is on - imports suppress its trigger, but check after.")
        lines = plan_report(plan, target, a.cutover)
        log_lines = []
        failures = apply(t, plan, dyn, lambda m: (print(m), log_lines.append(f"- {m}")))
        write_report(f"import {target}", lines + ["## Import log", ""] + log_lines)
        print(f"Done. {len(failures)} requests failed - see the report.")
    else:
        reconcile(valto, dyn, t, plan)


if __name__ == "__main__":
    main()
