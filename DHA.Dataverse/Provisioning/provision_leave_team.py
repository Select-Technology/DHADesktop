"""Leave parity stage 3 (approvers): read-only Custom APIs and approver-change sharing, in FWPLeaveManagement.

  - uploads DHA.Leave.Plugins (build it in Release first)
  - Custom API fwp_GetLeaveBalance  (GetLeaveBalancePlugin)  - one person's balance for a year; employee, their approvers, HR
  - Custom API fwp_GetTeamLeave     (GetTeamLeavePlugin)     - who's off in a range; approvers and HR; type withheld
                                                               except for the caller's own people and non-absence types
  - step LeaveApproverChangePlugin on fwp_colleagueconfiguration Create/Update (post-op, sync, filtered on the two
    approver fields): new approvers get access to the person's leave; waiting requests move to them

Both APIs check the caller inside the plug-in (no execute privilege), then read as SYSTEM. Idempotent.
    python -I provision_leave_team.py
"""
import os, sys, base64
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveManagement"
DLL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "DHA.Leave.Plugins", "bin", "Release", "DHA.Leave.Plugins.dll")
BOOL, DT, DEC, ECOLL, INT, GUID = 0, 1, 2, 4, 7, 12  # customapifieldtype
print("Target:", dv.ORG)


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(d); sys.exit(1)


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300: print(path, d); sys.exit(1)
    return d["value"]


# 1. Assembly
asm = q("pluginassemblies?$select=pluginassemblyid&$filter=name eq 'DHA.Leave.Plugins'")[0]["pluginassemblyid"]
ok(*dv.call("PATCH", f"pluginassemblies({asm})", {"content": base64.b64encode(open(DLL, "rb").read()).decode()})[:2], "update plugin assembly")

types = {}
for cls in ["GetLeaveBalancePlugin", "GetTeamLeavePlugin", "LeaveApproverChangePlugin"]:
    tn = "DHA.Leave.Plugins." + cls
    rows = q(f"plugintypes?$select=plugintypeid&$filter=typename eq '{tn}'")
    if rows:
        types[cls] = rows[0]["plugintypeid"]
        continue
    s, d, _ = dv.call("POST", "plugintypes", {"typename": tn, "name": tn, "friendlyname": cls,
                                              "pluginassemblyid@odata.bind": f"/pluginassemblies({asm})"},
                      extra={"Prefer": "return=representation"})
    ok(s, d, "plugin type " + cls)
    types[cls] = d["plugintypeid"]


# 2. Custom APIs
def api(name, desc, cls, params, outputs):
    if q(f"customapis?$select=customapiid&$filter=uniquename eq '{name}'"):
        print("custom api", name, "exists")
        return
    body = {"uniquename": name, "name": name, "displayname": name.replace("fwp_", ""), "description": desc,
            "bindingtype": 0, "isfunction": False, "isprivate": False, "allowedcustomprocessingsteptype": 0,
            "PluginTypeId@odata.bind": f"/plugintypes({types[cls]})",
            "CustomAPIRequestParameters": [{"uniquename": p, "name": p, "displayname": p, "description": dsc, "type": t,
                                            "isoptional": opt} for p, t, opt, dsc in params],
            "CustomAPIResponseProperties": [{"uniquename": p, "name": p, "displayname": p, "description": p, "type": t} for p, t in outputs]}
    ok(*dv.call("POST", "customapis", body, solution=SOL)[:2], "custom api " + name)


api("fwp_GetLeaveBalance", "One person's leave balance for a leave year, in days. For the employee, their approvers and HR (leave parity M4).",
    "GetLeaveBalancePlugin", [
        ("EmployeeId", GUID, True, "systemuserid of the person (defaults to the caller)."),
        ("Year", INT, True, "Leave (calendar) year, e.g. 2027 (defaults to this year).")],
    [("HasEntitlement", BOOL), ("BaseDays", DEC), ("CarriedForwardDays", DEC), ("AdjustmentDays", DEC),
     ("CarriedToNextYearDays", DEC), ("TakenDays", DEC), ("BookedDays", DEC), ("PendingDays", DEC)])
api("fwp_GetTeamLeave", "Pending and booked leave overlapping a date range, for approvers and HR. The type is withheld except for the "
    "caller's own people and non-absence types (leave parity M3).", "GetTeamLeavePlugin", [
        ("FromDate", DT, False, "First day of the range."),
        ("ToDate", DT, False, "Last day of the range (at most a year after FromDate).")],
    [("Leave", ECOLL)])

# 3. Step on colleague configuration
FILTER = "fwp_leaveapprover,fwp_additionalleaveapprover"
for message in ("Create", "Update"):
    name = f"LeaveApproverChangePlugin: {message} of fwp_colleagueconfiguration (Post)"
    if q(f"sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid&$filter=name eq '{name}'"):
        print("step", name, "exists")
        continue
    msg = q(f"sdkmessages?$select=sdkmessageid&$filter=name eq '{message}'")[0]["sdkmessageid"]
    flt = q(f"sdkmessagefilters?$select=sdkmessagefilterid&$filter=primaryobjecttypecode eq 'fwp_colleagueconfiguration' "
            f"and _sdkmessageid_value eq {msg}")[0]["sdkmessagefilterid"]
    body = {"name": name, "stage": 40, "mode": 0, "rank": 1, "supporteddeployment": 0,
            "eventhandler_plugintype@odata.bind": f"/plugintypes({types['LeaveApproverChangePlugin']})",
            "sdkmessageid@odata.bind": f"/sdkmessages({msg})", "sdkmessagefilterid@odata.bind": f"/sdkmessagefilters({flt})"}
    if message == "Update":
        body["filteringattributes"] = FILTER
    ok(*dv.call("POST", "sdkmessageprocessingsteps", body, solution=SOL)[:2], "step " + name)
print("done")
