"""Leave parity stage 5 (year-end), in FWPLeaveManagement.

  - uploads DHA.Leave.Plugins (build it in Release first)
  - Custom API fwp_GenerateLeaveYear (GenerateLeaveYearPlugin): a year's entitlement rows from each person's base
    entitlement; people who already have one are left alone (A4)
  - Custom API fwp_CarryForwardLeave (CarryForwardLeavePlugin): unused days from last year into this one, capped at
    fwp_LeaveCarryForwardLimit (5) unless HR set an override on last year's row. Sets, never adds, so re-runs are safe (A3)
  - flow "Leave - Year end": 1 January 06:00 UK, runs both and emails HR (fwp_LeaveApproverEmail) the reports.
    Uses the connection references made by provision_leave_emails.py. Created OFF unless --on is given.
    --no-generate leaves out fwp_GenerateLeaveYear from the flow (if HR decide against automatic entitlements, F2).

Both APIs are HR only and can also be run by hand - e.g. again after HR change an override.
Idempotent; re-running replaces the flow definition.
    python -I provision_leave_year_end.py [--on] [--no-generate]
"""
import os, sys, base64, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveManagement"
DLL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "DHA.Leave.Plugins", "bin", "Release", "DHA.Leave.Plugins.dll")
INT, STR, GUID = 7, 10, 12  # customapifieldtype
EMPLOYEE = ("EmployeeId", GUID, True, "systemuserid of one person. Default: everyone.")
DV_API = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
OL_API = "/providers/Microsoft.PowerApps/apis/shared_office365"
TURN_ON = "--on" in sys.argv
GENERATE = "--no-generate" not in sys.argv
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
for cls in ["GenerateLeaveYearPlugin", "CarryForwardLeavePlugin"]:
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
    existing = q(f"customapis?$select=customapiid&$filter=uniquename eq '{name}'")
    if existing:
        cid = existing[0]["customapiid"]
        have = {p["uniquename"] for p in q(f"customapirequestparameters?$select=uniquename&$filter=_customapiid_value eq {cid}")}
        for p, t, opt, dsc in params:
            if p not in have:
                ok(*dv.call("POST", "customapirequestparameters", {
                    "uniquename": p, "name": p, "displayname": p, "description": dsc, "type": t, "isoptional": opt,
                    "CustomAPIId@odata.bind": f"/customapis({cid})"}, solution=SOL)[:2], f"  {name} parameter {p}")
        print("custom api", name, "exists")
        return
    body = {"uniquename": name, "name": name, "displayname": name.replace("fwp_", ""), "description": desc,
            "bindingtype": 0, "isfunction": False, "isprivate": False, "allowedcustomprocessingsteptype": 0,
            "PluginTypeId@odata.bind": f"/plugintypes({types[cls]})",
            "CustomAPIRequestParameters": [{"uniquename": p, "name": p, "displayname": p, "description": dsc, "type": t,
                                            "isoptional": opt} for p, t, opt, dsc in params],
            "CustomAPIResponseProperties": [{"uniquename": p, "name": p, "displayname": p, "description": p, "type": t} for p, t in outputs]}
    ok(*dv.call("POST", "customapis", body, solution=SOL)[:2], "custom api " + name)


api("fwp_GenerateLeaveYear", "HR only: create a leave year's entitlement rows from each person's base entitlement; anyone who already "
                             "has one is left alone (leave parity A4).", "GenerateLeaveYearPlugin",
    [("Year", INT, True, "Leave year to create, e.g. 2027. Default: this year."), EMPLOYEE], [("Created", INT), ("Report", STR)])
api("fwp_CarryForwardLeave", "HR only: carry unused days from Year - 1 into Year, capped at the limit or HR's override. Sets the "
                             "figures rather than adding, so it can be re-run (leave parity A3).", "CarryForwardLeavePlugin",
    [("Year", INT, True, "The new leave year, e.g. 2027 carries 2026's unused days. Default: this year."), EMPLOYEE],
    [("Carried", INT), ("Report", STR)])

# 3. Flow
CONNECTIONS = {
    "shared_commondataserviceforapps": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveDataverse"},
                                        "api": {"name": "shared_commondataserviceforapps"}},
    "shared_office365": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveOutlook"},
                         "api": {"name": "shared_office365"}},
}
APPROVER_PARAM = "Leave - Approver Email (fwp_LeaveApproverEmail)"
OVERRIDE_PARAM = "Leave - Email Override (testing) (fwp_LeaveEmailOverride)"
# No override when the setting is empty or "none" (prod: an environment variable with no value stops a flow switching on)
NO_OVERRIDE = f"or(empty(parameters('{OVERRIDE_PARAM}')), equals(toLower(parameters('{OVERRIDE_PARAM}')), 'none'))"
PARAMETERS = {
    "$connections": {"defaultValue": {}, "type": "Object"},
    "$authentication": {"defaultValue": {}, "type": "SecureObject"},
    APPROVER_PARAM: {"defaultValue": "HR@dhaplanning.co.uk", "type": "String", "metadata": {"schemaName": "fwp_LeaveApproverEmail"}},
    OVERRIDE_PARAM: {"defaultValue": "none", "type": "String", "metadata": {"schemaName": "fwp_LeaveEmailOverride"}},
}
YEAR = "@int(formatDateTime(convertFromUtc(utcNow(), 'GMT Standard Time'), 'yyyy'))"
HR_EMAIL = f"parameters('{APPROVER_PARAM}')"
STYLE = "<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#1e293b;max-width:600px\">"
FOOTER = "<p style=\"color:#64748b;font-size:12px;margin-top:24px\">This is an automated message from DHA Time Management.</p></div>"


def unbound(action_name, run_after=None):
    return {"type": "OpenApiConnection", "runAfter": run_after or {},
            "inputs": {"host": {"connectionName": "shared_commondataserviceforapps", "operationId": "PerformUnboundAction", "apiId": DV_API},
                       "parameters": {"actionName": action_name, "item/Year": YEAR}, "authentication": "@parameters('$authentication')"}}


def report(action):
    """The API's text report as HTML lines; a step that didn't run or failed says so."""
    return (f"@{{if(equals(actions('{action}')?['status'], 'Succeeded'), "
            f"replace(outputs('{action}')?['body/Report'], decodeUriComponent('%0A'), '<br>'), "
            f"concat('<b>This step did not complete (', actions('{action}')?['status'], ').</b> Ask Select Technology to check the flow run.'))}}")


actions = {}
if GENERATE:
    actions["Generate_this_year"] = unbound("fwp_GenerateLeaveYear")
# Carry forward runs even if generating failed for someone - it lists anyone without a row for the new year
actions["Carry_forward"] = unbound("fwp_CarryForwardLeave", {"Generate_this_year": ["Succeeded", "Failed"]} if GENERATE else None)
body = (STYLE + "<p>The leave year-end has run. Please check the figures below; anything that needs changing can be fixed on the "
        "<b>Leave &rarr; HR</b> tab, and carry forward can be run again from there.</p>"
        + (f"<h3>New entitlements</h3><p>{report('Generate_this_year')}</p>" if GENERATE else "")
        + f"<h3>Carry forward</h3><p>{report('Carry_forward')}</p>" + FOOTER)
actions["Email_HR"] = {
    "type": "OpenApiConnection", "runAfter": {"Carry_forward": ["Succeeded", "Failed"]},
    "inputs": {"host": {"connectionName": "shared_office365", "operationId": "SendEmailV2", "apiId": OL_API},
               "parameters": {
                   "emailMessage/To": f"@{{if({NO_OVERRIDE}, {HR_EMAIL}, parameters('{OVERRIDE_PARAM}'))}}",
                   "emailMessage/Subject": f"@{{if({NO_OVERRIDE}, '', concat('[TEST - for ', {HR_EMAIL}, '] '))}}"
                                           f"Leave year-end @{{{YEAR[1:]}}}",
                   "emailMessage/Body": body,
                   "emailMessage/Importance": "Normal"},
               "authentication": "@parameters('$authentication')"}}

definition = {
    "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
    "contentVersion": "1.0.0.0",
    "parameters": PARAMETERS,
    "triggers": {
        # Every 12 months from 1 January 2027 (there's no yearly recurrence)
        "Every_1_January": {"type": "Recurrence",
                            "recurrence": {"frequency": "Month", "interval": 12, "startTime": "2027-01-01T06:00:00",
                                           "timeZone": "GMT Standard Time"}}
    },
    "actions": actions,
    "outputs": {}
}

NAME = "Leave - Year end"
DESC = "Leave parity A3/A4: on 1 January creates the new leave year's entitlements, carries forward unused days and emails HR."
clientdata = json.dumps({"properties": {"connectionReferences": CONNECTIONS, "definition": definition, "templateName": ""},
                         "schemaVersion": "1.0.0.0"})
rows = q(f"workflows?$select=workflowid,statecode&$filter=name eq '{NAME}' and category eq 5")
if rows:
    wid = rows[0]["workflowid"]
    if rows[0]["statecode"] == 1:
        ok(*dv.call("PATCH", f"workflows({wid})", {"statecode": 0, "statuscode": 1})[:2], f"  turn off {NAME} to update")
    ok(*dv.call("PATCH", f"workflows({wid})", {"clientdata": clientdata, "description": DESC})[:2], "update flow " + NAME)
else:
    s, d, _ = dv.call("POST", "workflows", {"name": NAME, "description": DESC, "category": 5, "type": 1, "primaryentity": "none",
                                            "clientdata": clientdata}, solution=SOL, extra={"Prefer": "return=representation"})
    ok(s, d, "create flow " + NAME)
    wid = d["workflowid"]
if TURN_ON:
    ok(*dv.call("PATCH", f"workflows({wid})", {"statecode": 1, "statuscode": 2})[:2], "  turn on " + NAME)
else:
    print(f"  {NAME} left off (--on to switch it on)")
print("done")
