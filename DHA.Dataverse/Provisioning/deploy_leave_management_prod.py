"""Deploy the desktop leave planner to PROD, as managed solutions.

Releases: 20 Oct 2026 - stages 1-4 and 6 (FWPLeaveTimeEntries 1.0.4.0, FWPLeaveManagement 1.0.0.0, a new install).
          This one - stage 5 year-end (fwp_GenerateLeaveYear, fwp_CarryForwardLeave, "Leave - Year end" flow), the
          fwp_GetLeaveBalance fix (it counted every leave type), and fwp_LeaveEmailOverride defaulting to "none" so the
          email and calendar flows can be switched on (1.0.5.0 / 1.1.0.0, upgrades).
          26 Oct, later - leave is approved without time entries until DHA-9's Job (fwp_LeaveProjectId) is set, as in the
          Valto app; emails no longer promise timesheet entries (1.0.6.0 / 1.1.1.0).
          26 Oct, later still - leave and bank holiday time entries are written with fwp_value 0; the hourly
          fwp-settimeentryvalue Azure Function now skips them (FWPLeaveTimeEntries 1.0.7.0).

    python -I deploy_leave_management_prod.py prep      # dev: tidy, version, export both packages
    python -I deploy_leave_management_prod.py state     # prod: read-only snapshot (flows, settings, solutions)
    python -I deploy_leave_management_prod.py import    # prod: FWPLeaveTimeEntries, then FWPLeaveManagement (new or upgrade)
    python -I deploy_leave_management_prod.py post      # prod: access, book-on-behalf team, leave types, checks

Order matters: the plug-in classes the leave steps and Custom APIs run (LeaveRequestPlugin, GetLeaveBalancePlugin, ...)
live in DHA.Leave.Plugins, which ships in FWPLeaveTimeEntries. So that goes first.

What stays OFF in prod (decision 20 Oct 2026): "Leave - Notifications", "Leave - Approval reminder", "Leave - Outlook
calendar". Their Outlook connection references are left unbound. Phase A's flows keep the state they had before the
import (checked and restored). Approving leave in prod fails until phase A goes live (fwp_LeaveProjectId), by design.

Never runs the dev provisioning scripts against prod: they write components directly, which on top of a managed
solution would add unmanaged layers.
"""
import os, sys, base64, json, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

DEV, PROD = "https://dhapd-dev.crm11.dynamics.com", "https://dhapd.crm11.dynamics.com"
HERE = os.path.dirname(os.path.abspath(__file__))
SOLUTIONS = os.path.join(HERE, "..", "Solutions")
TIME_SOL, TIME_VER = "FWPLeaveTimeEntries", "1.0.7.0"
LEAVE_SOL, LEAVE_VER = "FWPLeaveManagement", "1.1.1.0"
LEAVE_FLOWS = ["Leave - Notifications", "Leave - Approval reminder", "Leave - Outlook calendar", "Leave - Year end"]
PHASE_A_FLOWS = ["Leave Request - Record Time Entries", "Leave - Record Bank Holidays", "Leave Request - Backfill Approved Leave"]
# one snapshot per release (the 20 Oct one is prod_state_before_leave_management.json)
STATE_FILE = os.path.join(HERE, "..", "Solutions", f"prod_state_before_{LEAVE_SOL}_{LEAVE_VER.replace('.', '_')}.json")

# DHA's request types, as configured in Valto (seen in prod flow history, Sep-Oct 2026). Study Leave's entitlement
# setting is to be confirmed against Valto's Request Type Configuration in migration (stage 8).
DAYS, HOURS = 800470000, 800470001
TYPES = [("Holiday Request", True, True, DAYS, 1), ("Working From Home", False, False, DAYS, 2), ("Medical", True, False, HOURS, 3),
         ("Sick Leave", True, False, DAYS, 4), ("Study Leave", True, False, DAYS, 5), ("Other", True, False, DAYS, 6)]


def env(url):
    os.environ["DATAVERSE_URL"] = url
    import importlib, dv
    importlib.reload(dv)
    assert dv.ORG == url, dv.ORG
    return dv


def ok(s, d, what):
    print(" ", what, s)
    if s >= 300:
        print(d); sys.exit(1)


def q(dv, path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300:
        sys.exit(f"{path}: {d}")
    return d["value"]


def solution(dv, name):
    rows = q(dv, f"solutions?$select=solutionid,version,ismanaged&$filter=uniquename eq '{name}'")
    return rows[0] if rows else None


def flows(dv, names):
    flt = " or ".join(f"name eq '{n}'" for n in names)
    return {w["name"]: w for w in q(dv, f"workflows?$select=workflowid,name,statecode&$filter=category eq 5 and ({flt})")}


def package(name, version):
    return os.path.join(SOLUTIONS, f"{name}_{version.replace('.', '_')}_managed.zip")


def prep():
    dv = env(DEV)
    print("DEV:", dv.ORG)
    # 1. systemuser came in with the lookups; ship no layer on Dynamics' own user table
    sid = solution(dv, LEAVE_SOL)["solutionid"]
    user_meta = dv.call("GET", "EntityDefinitions(LogicalName='systemuser')?$select=MetadataId")[1]["MetadataId"]
    if q(dv, f"solutioncomponents?$select=solutioncomponentid&$filter=_solutionid_value eq {sid} and objectid eq {user_meta}"):
        ok(*dv.call("POST", "RemoveSolutionComponent", {
            "SolutionComponent": {"@odata.type": "Microsoft.Dynamics.CRM.solutioncomponent", "solutioncomponentid": user_meta},
            "ComponentType": 1, "SolutionUniqueName": LEAVE_SOL})[:2], "removed systemuser from " + LEAVE_SOL)
    # 2. versions
    for name, ver in ((TIME_SOL, TIME_VER), (LEAVE_SOL, LEAVE_VER)):
        sol = solution(dv, name)
        if sol["version"] != ver:
            ok(*dv.call("PATCH", f"solutions({sol['solutionid']})", {"version": ver})[:2], f"{name} version -> {ver}")
    # 3. leave flows off for the export (a managed import tries to switch on flows that were on in the source,
    #    and these have unbound Outlook connections in prod), then back on in dev
    on = [w for w in flows(dv, LEAVE_FLOWS).values() if w["statecode"] == 1]
    for w in on:
        ok(*dv.call("PATCH", f"workflows({w['workflowid']})", {"statecode": 0, "statuscode": 1})[:2], "dev off for export: " + w["name"])
    try:
        for name, ver in ((TIME_SOL, TIME_VER), (LEAVE_SOL, LEAVE_VER)):
            s, d, _ = dv.call("POST", "ExportSolution", {"SolutionName": name, "Managed": True})
            ok(s, d if s >= 300 else "", f"export {name} {ver}")
            open(package(name, ver), "wb").write(base64.b64decode(d["ExportSolutionFile"]))
            print("   ->", os.path.relpath(package(name, ver), HERE))
    finally:
        for w in on:
            ok(*dv.call("PATCH", f"workflows({w['workflowid']})", {"statecode": 1, "statuscode": 2})[:2], "dev back on: " + w["name"])


def state():
    dv = env(PROD)
    print("PROD (read-only):", dv.ORG)
    snap = {"solutions": {n: solution(dv, n) for n in (TIME_SOL, LEAVE_SOL)},
            "phase_a_flows": {n: w["statecode"] for n, w in flows(dv, PHASE_A_FLOWS).items()},
            "leave_flows": {n: w["statecode"] for n, w in flows(dv, LEAVE_FLOWS).items()}}
    vals = {}
    for d in q(dv, "environmentvariabledefinitions?$select=schemaname,defaultvalue&$filter=startswith(schemaname,'fwp_Leave') or "
                   "startswith(schemaname,'fwp_BankHoliday')&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)"):
        v = d["environmentvariabledefinition_environmentvariablevalue"]
        vals[d["schemaname"]] = {"default": d["defaultvalue"], "value": v[0]["value"] if v else None}
    snap["settings"] = vals
    snap["connections"] = {c["connectionreferencelogicalname"]: c["connectionid"] for c in
                           q(dv, "connectionreferences?$select=connectionreferencelogicalname,connectionid&$filter=startswith(connectionreferencelogicalname,'fwp_Leave')")}
    snap["tables"] = {t: bool(q(dv, f"EntityDefinitions?$select=LogicalName&$filter=LogicalName eq '{t}'"))
                      for t in ("fwp_leaverequest", "fwp_leavetype", "fwp_leaveentitlement")}
    print(json.dumps(snap, indent=1))
    return snap


def import_one(dv, name, ver, params=None):
    data = base64.b64encode(open(package(name, ver), "rb").read()).decode()
    body = {"CustomizationFile": data, "OverwriteUnmanagedCustomizations": False, "PublishWorkflows": False,
            "ImportJobId": None}
    body.pop("ImportJobId")
    if params:
        body["ComponentParameters"] = params
    s, d, _ = dv.call("POST", "ImportSolutionAsync", body)
    ok(s, d if s >= 300 else "", f"import {name} {ver} started")
    op = d["AsyncOperationId"]
    while True:
        time.sleep(15)
        a = dv.call("GET", f"asyncoperations({op})?$select=statecode,statuscode,message")[1]
        if a["statecode"] == 3:
            if a["statuscode"] != 30:
                sys.exit(f"  import {name} FAILED: {a['statuscode']} {a.get('message')}")
            print(f"  import {name} {ver} succeeded")
            return
        print("   ...", a["statuscode"])


def do_import():
    before = json.load(open(STATE_FILE)) if os.path.exists(STATE_FILE) else None
    if before is None:
        before = state()
        json.dump(before, open(STATE_FILE, "w"), indent=1)
    dv = env(PROD)
    print("PROD:", dv.ORG)

    sol = solution(dv, TIME_SOL)
    if sol["version"] != TIME_VER:
        import_one(dv, TIME_SOL, TIME_VER)
    # put phase A's flows back as they were (an upgrade can change a flow's state)
    for n, w in flows(dv, PHASE_A_FLOWS).items():
        was = before["phase_a_flows"].get(n)
        if was is not None and w["statecode"] != was:
            ok(*dv.call("PATCH", f"workflows({w['workflowid']})", {"statecode": was, "statuscode": 2 if was == 1 else 1})[:2],
               f"restored {n} to {'on' if was == 1 else 'off'}")

    current = solution(dv, LEAVE_SOL)
    if current and current["version"] != LEAVE_VER:
        import_one(dv, LEAVE_SOL, LEAVE_VER)  # upgrade: connection references are already bound
    elif not current:
        # Dataverse: stsadmin's prod connection, as phase A uses. Outlook references stay unbound (flows off)
        dataverse = before["connections"].get("fwp_LeaveTimeDataverse")
        params = [{"@odata.type": "Microsoft.Dynamics.CRM.connectionreference", "connectionreferencelogicalname": "fwp_LeaveDataverse",
                   "connectionreferencedisplayname": "Leave - Dataverse", "connectorid": "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps",
                   "connectionid": dataverse}] if dataverse else None
        import_one(dv, LEAVE_SOL, LEAVE_VER, params)
    for n, w in flows(dv, LEAVE_FLOWS).items():
        if w["statecode"] == 1:
            ok(*dv.call("PATCH", f"workflows({w['workflowid']})", {"statecode": 0, "statuscode": 1})[:2], "kept off: " + n)
        else:
            print("  off as intended:", n)


def post():
    dv = env(PROD)
    print("PROD:", dv.ORG)
    import subprocess
    for script in ("provision_leave_staff_access.py", "provision_leave_approvers.py"):
        print(f"--- {script}")
        r = subprocess.run([sys.executable, "-I", os.path.join(HERE, script)], env=dict(os.environ, DATAVERSE_URL=PROD), text=True,
                           capture_output=True)
        print(r.stdout[-2500:], r.stderr[-1500:])
        if r.returncode:
            sys.exit(f"{script} failed")

    # The managed import brings the plug-in steps in DISABLED (they're enabled in dev). Without them prod leave would skip
    # every server rule (overlaps, booking on behalf, days, sharing) - switch them on
    for step in q(dv, "sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid,name,statecode&$filter=startswith(name,'Leave')"):
        if step["statecode"] != 0:
            ok(*dv.call("PATCH", f"sdkmessageprocessingsteps({step['sdkmessageprocessingstepid']})", {"statecode": 0, "statuscode": 1})[:2],
               "enabled step " + step["name"])

    # Book-on-behalf team (A9): an owner team with no roles; HR add members in Dynamics
    if not q(dv, "teams?$select=teamid&$filter=name eq 'Leave - Book on behalf'"):
        root_bu = q(dv, "businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null")[0]["businessunitid"]
        me = dv.call("GET", "WhoAmI")[1]["UserId"]
        ok(*dv.call("POST", "teams", {"name": "Leave - Book on behalf", "teamtype": 0,
                                      "description": "Leave parity A9: members may book leave for other people. HR (Leave Administrator) can already.",
                                      "businessunitid@odata.bind": f"/businessunits({root_bu})",
                                      "administratorid@odata.bind": f"/systemusers({me})"})[:2], "team Leave - Book on behalf")

    # The email override needs a default or a value, or no leave flow can be switched on. A managed upgrade doesn't change an
    # existing definition's default (seen 26 Oct), so give prod the value "none" (= no override)
    ov = q(dv, "environmentvariabledefinitions?$select=environmentvariabledefinitionid,defaultvalue&$filter=schemaname eq 'fwp_LeaveEmailOverride'"
               "&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)")
    if ov and not ov[0]["defaultvalue"] and not ov[0]["environmentvariabledefinition_environmentvariablevalue"]:
        ok(*dv.call("POST", "environmentvariablevalues", {
            "value": "none", "schemaname": "fwp_LeaveEmailOverride_value",
            "EnvironmentVariableDefinitionId@odata.bind": f"/environmentvariabledefinitions({ov[0]['environmentvariabledefinitionid']})"})[:2],
           "fwp_LeaveEmailOverride = none")

    # Leave types: data, not solution components
    existing = {t["fwp_name"] for t in q(dv, "fwp_leavetypes?$select=fwp_name")}
    for name, absence, deducts, units, order in TYPES:
        if name not in existing:
            ok(*dv.call("POST", "fwp_leavetypes", {"fwp_name": name, "fwp_isabsence": absence, "fwp_deductsfromentitlement": deducts,
                                                   "fwp_forcedunits": units, "fwp_requiresapproval": True, "fwp_sortorder": order})[:2],
               "leave type " + name)

    # Checks that write nothing
    print("--- checks")
    s, d, _ = dv.call("POST", "fwp_GetLeaveBalance", {"Year": 2026})
    print("  fwp_GetLeaveBalance (own, 2026):", s, d if s >= 300 else {k: v for k, v in d.items() if not k.startswith("@")})
    s, d, _ = dv.call("POST", "fwp_GetTeamLeave", {"FromDate": "2026-10-01T00:00:00Z", "ToDate": "2026-12-31T00:00:00Z"})
    print("  fwp_GetTeamLeave:", s, (len(d["value"]), "rows") if s < 300 else d)
    steps = q(dv, "sdkmessageprocessingsteps?$select=name,statecode&$filter=startswith(name,'Leave')")
    print("  plug-in steps:", len(steps), "all enabled" if all(x["statecode"] == 0 for x in steps) else steps)
    print("  leave types:", sorted(t["fwp_name"] for t in q(dv, "fwp_leavetypes?$select=fwp_name&$filter=statecode eq 0")))
    print("  year-end APIs:", sorted(a["uniquename"] for a in q(dv, "customapis?$select=uniquename&$filter=uniquename eq 'fwp_GenerateLeaveYear' "
                                                                   "or uniquename eq 'fwp_CarryForwardLeave'")))
    print("  email override:", [(d["defaultvalue"], [v["value"] for v in d["environmentvariabledefinition_environmentvariablevalue"]]) for d in
                                q(dv, "environmentvariabledefinitions?$select=defaultvalue&$filter=schemaname eq 'fwp_LeaveEmailOverride'"
                                      "&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)")])
    print("  flows:", {n: ("on" if w["statecode"] == 1 else "off") for n, w in flows(dv, LEAVE_FLOWS + PHASE_A_FLOWS).items()})


if __name__ == "__main__":
    {"prep": prep, "state": state, "import": do_import, "post": post}[sys.argv[1]]()
