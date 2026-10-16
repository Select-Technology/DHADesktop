"""Leave parity stage 4 (HR administration), in FWPLeaveManagement.

  - uploads DHA.Leave.Plugins (build it in Release first)
  - Custom API fwp_SetLeaveSettings (SetLeaveSettingsPlugin): HR set approver, additional approver, base entitlement and
    working days (start / finish / break per day) on colleague configuration - without HR's role being able to write that
    table (it holds rates). Expected hours are a formula column worked out from those times.
  - LeaveEntitlementPlugin steps on fwp_leaveentitlement: one row per person per year from 1 January, named, owned by
    the employee
  - owner team "Leave - Book on behalf" (Valto's "Book on behalf of users" list). HR add members in Dynamics
    (Settings > Security > Teams). Members need no extra role: DHA-1 lets them create requests, and the plug-in
    checks the team before accepting a booking for someone else.

Idempotent.
    python -I provision_leave_hr.py
"""
import os, sys, base64
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveManagement"
DLL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "DHA.Leave.Plugins", "bin", "Release", "DHA.Leave.Plugins.dll")
DEC, STR, GUID = 2, 10, 12  # customapifieldtype
BOOK_ON_BEHALF = "Leave - Book on behalf"
print("Target:", dv.ORG)


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(d); sys.exit(1)


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300: print(path, d); sys.exit(1)
    return d["value"]


# 1. Assembly and types
asm = q("pluginassemblies?$select=pluginassemblyid&$filter=name eq 'DHA.Leave.Plugins'")[0]["pluginassemblyid"]
ok(*dv.call("PATCH", f"pluginassemblies({asm})", {"content": base64.b64encode(open(DLL, "rb").read()).decode()})[:2], "update plugin assembly")
types = {}
for cls in ["SetLeaveSettingsPlugin", "LeaveEntitlementPlugin"]:
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

# 2. Custom API (an early dev version took hours per day - replaced, as parameter types can't be changed in place)
existing = q("customapis?$select=customapiid&$filter=uniquename eq 'fwp_SetLeaveSettings'")
if existing and not q(f"customapirequestparameters?$select=uniquename&$filter=_customapiid_value eq {existing[0]['customapiid']} "
                      f"and uniquename eq 'MondayStart'"):
    ok(*dv.call("DELETE", f"customapis({existing[0]['customapiid']})")[:2], "remove old fwp_SetLeaveSettings")
    existing = []
if existing:
    print("custom api fwp_SetLeaveSettings exists")
else:
    params = [("EmployeeId", GUID, False, "systemuserid of the person."),
              ("ApproverId", GUID, True, "Leave approver (empty GUID clears). Omit to leave unchanged."),
              ("AdditionalApproverId", GUID, True, "Additional approver (empty GUID clears). Omit to leave unchanged."),
              ("BaseEntitlement", DEC, True, "Days per leave year. Omit to leave unchanged.")] + \
             [(f"{d}{k}", STR, True, f"{d} {k.lower()} time, HH:mm. Start and finish both empty = not a working day. "
                                     "Omit all three to leave the day unchanged.")
              for d in ("Monday", "Tuesday", "Wednesday", "Thursday", "Friday") for k in ("Start", "Finish", "Break")]
    body = {"uniquename": "fwp_SetLeaveSettings", "name": "fwp_SetLeaveSettings", "displayname": "SetLeaveSettings",
            "description": "HR only: set a person's leave approver, additional approver, base entitlement and working days on "
                           "colleague configuration (leave parity A1). Writes nothing else on that table.",
            "bindingtype": 0, "isfunction": False, "isprivate": False, "allowedcustomprocessingsteptype": 0,
            "PluginTypeId@odata.bind": f"/plugintypes({types['SetLeaveSettingsPlugin']})",
            "CustomAPIRequestParameters": [{"uniquename": p, "name": p, "displayname": p, "description": dsc, "type": t, "isoptional": opt}
                                           for p, t, opt, dsc in params]}
    ok(*dv.call("POST", "customapis", body, solution=SOL)[:2], "custom api fwp_SetLeaveSettings")

# 3. Entitlement steps
FILTER, IMAGE = "fwp_employee,fwp_yearstart", "fwp_employee,fwp_yearstart"
for message, stage in [("Create", 20), ("Update", 20), ("Create", 40)]:
    name = f"LeaveEntitlementPlugin: {message} of fwp_leaveentitlement ({'Pre' if stage == 20 else 'Post'})"
    if q(f"sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid&$filter=name eq '{name}'"):
        print("step", name, "exists")
        continue
    msg = q(f"sdkmessages?$select=sdkmessageid&$filter=name eq '{message}'")[0]["sdkmessageid"]
    flt = q(f"sdkmessagefilters?$select=sdkmessagefilterid&$filter=primaryobjecttypecode eq 'fwp_leaveentitlement' "
            f"and _sdkmessageid_value eq {msg}")[0]["sdkmessagefilterid"]
    body = {"name": name, "stage": stage, "mode": 0, "rank": 1, "supporteddeployment": 0,
            "eventhandler_plugintype@odata.bind": f"/plugintypes({types['LeaveEntitlementPlugin']})",
            "sdkmessageid@odata.bind": f"/sdkmessages({msg})", "sdkmessagefilterid@odata.bind": f"/sdkmessagefilters({flt})"}
    if message == "Update":
        body["filteringattributes"] = FILTER
    s, d, _ = dv.call("POST", "sdkmessageprocessingsteps", body, solution=SOL, extra={"Prefer": "return=representation"})
    ok(s, d, "step " + name)
    if message == "Update":
        ok(*dv.call("POST", "sdkmessageprocessingstepimages", {
            "name": "PreImage", "entityalias": "PreImage", "imagetype": 0, "messagepropertyname": "Target", "attributes": IMAGE,
            "sdkmessageprocessingstepid@odata.bind": f"/sdkmessageprocessingsteps({d['sdkmessageprocessingstepid']})"}, solution=SOL)[:2],
           "  pre image")

# 4. Book-on-behalf team (owner team in the root business unit, no roles)
if q(f"teams?$select=teamid&$filter=name eq '{BOOK_ON_BEHALF}'"):
    print("team", BOOK_ON_BEHALF, "exists")
else:
    root_bu = q("businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null")[0]["businessunitid"]
    me = dv.call("GET", "WhoAmI")[1]["UserId"]
    ok(*dv.call("POST", "teams", {"name": BOOK_ON_BEHALF, "teamtype": 0,
                                  "description": "Leave parity A9: members may book leave for other people. HR (Leave Administrator) can already.",
                                  "businessunitid@odata.bind": f"/businessunits({root_bu})",
                                  "administratorid@odata.bind": f"/systemusers({me})"})[:2], "team " + BOOK_ON_BEHALF)
print("done")
