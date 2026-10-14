"""DHA-9 phase A: the flows that connect the Valto leave app (SharePoint) to the leave Custom APIs.

Adds to FWPLeaveTimeEntries (created OFF - they are switched on in the target environment after import):
  - StartHalfDay / EndHalfDay inputs on fwp_RecordLeave (a half day = exactly half the person's day)
  - env vars: fwp_LeaveSharePointSite, fwp_LeaveRequestListId, fwp_LeaveExcludedRequestTypes
  - connection references fwp_LeaveTimeSharePoint / fwp_LeaveTimeDataverse (bound per environment on import)
  - F1 "Leave Request - Record Time Entries"    : SharePoint item created/modified -> record or withdraw that request's entries
  - F2 "Leave - Record Bank Holidays"            : daily 06:00 -> bank holiday entries for the next 31 days
  - F3 "Leave Request - Backfill Approved Leave" : manual, run once at go-live -> approved leave from today onwards

Request types listed in fwp_LeaveExcludedRequestTypes (default "Working From Home") never become leave.
Valto "Days" requests use half-day flags (a PM start / AM finish); "Hours" requests (e.g. Medical) use their exact times.

Run against DEV (the source of the managed solution):  python -I provision_leave_flows.py
"""
import os, sys, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveTimeEntries"
SP_API = "/providers/Microsoft.PowerApps/apis/shared_sharepointonline"
DV_API = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(d); sys.exit(1)


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300: print(path, d); sys.exit(1)
    return d["value"]


# ---- half-day inputs on fwp_RecordLeave ----
api = q("customapis?$select=customapiid&$filter=uniquename eq 'fwp_RecordLeave'")[0]["customapiid"]
for name, desc in [("StartHalfDay", "The first day is a half day (exactly half that day's expected hours)."),
                   ("EndHalfDay", "The last day is a half day. On a one-day booking either flag makes it a half day.")]:
    if not q(f"customapirequestparameters?$select=customapirequestparameterid&$filter=uniquename eq '{name}' and _customapiid_value eq {api}"):
        s, d, _ = dv.call("POST", "customapirequestparameters", {
            "uniquename": name, "name": name, "displayname": name, "description": desc, "type": 0, "isoptional": True,
            "CustomAPIId@odata.bind": f"/customapis({api})"}, solution=SOL)
        ok(s, d, "fwp_RecordLeave input " + name)


# ---- settings ----
def env_var(schema, name, desc, default):
    if q(f"environmentvariabledefinitions?$select=environmentvariabledefinitionid&$filter=schemaname eq '{schema}'"):
        return
    s, d, _ = dv.call("POST", "environmentvariabledefinitions", {"schemaname": schema, "displayname": name, "description": desc,
                                                                "type": 100000000, "defaultvalue": default}, solution=SOL)
    ok(s, d, "env var " + schema)


env_var("fwp_LeaveSharePointSite", "Leave - SharePoint Site", "The Valto leave app's SharePoint site.",
        "https://dhaplanningcouk.sharepoint.com/sites/Leave-Request")
env_var("fwp_LeaveRequestListId", "Leave - Request List Id", "Id of the 'Leave Request Data' list on that site.",
        "87127c05-5e31-4e1f-9385-791de0f1a004")
env_var("fwp_LeaveExcludedRequestTypes", "Leave - Excluded Request Types",
        "Request types that are NOT absence and never become time entries, separated by ';'.", "Working From Home")

SITE = "Leave - SharePoint Site (fwp_LeaveSharePointSite)"
LIST = "Leave - Request List Id (fwp_LeaveRequestListId)"
EXCLUDED = "Leave - Excluded Request Types (fwp_LeaveExcludedRequestTypes)"
PARAMETERS = {
    "$connections": {"defaultValue": {}, "type": "Object"},
    "$authentication": {"defaultValue": {}, "type": "SecureObject"},
    SITE: {"defaultValue": "https://dhaplanningcouk.sharepoint.com/sites/Leave-Request", "type": "String",
           "metadata": {"schemaName": "fwp_LeaveSharePointSite"}},
    LIST: {"defaultValue": "87127c05-5e31-4e1f-9385-791de0f1a004", "type": "String", "metadata": {"schemaName": "fwp_LeaveRequestListId"}},
    EXCLUDED: {"defaultValue": "Working From Home", "type": "String", "metadata": {"schemaName": "fwp_LeaveExcludedRequestTypes"}},
}


# ---- connection references (no connection in dev; bound to real connections on import) ----
def conn_ref(logical, name, api_id):
    if q(f"connectionreferences?$select=connectionreferenceid&$filter=connectionreferencelogicalname eq '{logical}'"):
        return
    s, d, _ = dv.call("POST", "connectionreferences", {"connectionreferencelogicalname": logical, "connectionreferencedisplayname": name,
                                                       "connectorid": api_id}, solution=SOL)
    ok(s, d, "connection reference " + logical)


conn_ref("fwp_LeaveTimeSharePoint", "Leave Time Entries - SharePoint", SP_API)
conn_ref("fwp_LeaveTimeDataverse", "Leave Time Entries - Dataverse", DV_API)
CONNECTIONS = {
    "shared_sharepointonline": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveTimeSharePoint"},
                                "api": {"name": "shared_sharepointonline"}},
    "shared_commondataserviceforapps": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveTimeDataverse"},
                                        "api": {"name": "shared_commondataserviceforapps"}},
}


def unbound(action_name, items, run_after=None):
    params = {"actionName": action_name}
    params.update({f"item/{k}": v for k, v in items.items()})
    return {"type": "OpenApiConnection", "runAfter": run_after or {},
            "inputs": {"host": {"connectionName": "shared_commondataserviceforapps", "operationId": "PerformUnboundAction", "apiId": DV_API},
                       "parameters": params, "authentication": "@parameters('$authentication')"}}


def request_logic(item):
    """Actions for one Valto request. `item` is an expression prefix, e.g. "triggerBody()" or "items('For_each_approved_request')"."""
    f = lambda col: f"{item}?['{col}']"
    status, rtype, units = f"{f('Status')}?['Value']", f"{f('Request_x0020_Type')}?['Value']", f"{f('Units')}?['Value']"
    is_hours = f"equals({units}, 'Hours')"
    hhmm = lambda h, m: f"concat(formatNumber(int({f(h)}), '00'), ':', formatNumber(int({f(m)}), '00'))"
    excluded = f"split(toLower(replace(parameters('{EXCLUDED}'), '; ', ';')), ';')"
    key = f"@{{concat('LR-', {f('ID')})}}"
    return {
        "Is_approved_absence": {
            "type": "If",
            "expression": {"and": [
                {"equals": [f"@{status}", "Approved"]},
                {"not": {"contains": [f"@{excluded}", f"@toLower(coalesce({rtype}, ''))"]}}]},
            "actions": {
                "Record_leave": unbound("fwp_RecordLeave", {
                    "UserEmail": f"@{f('Employee')}?['Email']",
                    "StartDate": f"@{{formatDateTime({f('RequestStartDate')}, 'yyyy-MM-dd')}}",
                    "EndDate": f"@{{formatDateTime(coalesce({f('RequestEndDate')}, {f('RequestStartDate')}), 'yyyy-MM-dd')}}",
                    # "Hours" requests (e.g. Medical) use their exact times...
                    "StartTime": f"@{{if({is_hours}, {hhmm('Request_x0020_Start_x0020_Hour', 'RequestStartMinutes')}, '')}}",
                    "EndTime": f"@{{if({is_hours}, {hhmm('RequestEndHour', 'RequestEndMinutes')}, '')}}",
                    # ..."Days" requests: a PM start or an AM finish is a half day (exactly half the person's day)
                    "StartHalfDay": f"@and(not({is_hours}), greaterOrEquals(float({f('Request_x0020_Start_x0020_Hour')}), 12))",
                    "EndHalfDay": f"@and(not({is_hours}), less(add(mul(float({f('RequestEndHour')}), 60), float({f('RequestEndMinutes')})), 780))",
                    "SourceKey": key})
            },
            "else": {"actions": {
                # Pending, rejected, cancelled, or not absence: make sure it has no active entries (deactivates; never deletes)
                "Withdraw_leave": unbound("fwp_RemoveLeave", {"SourceKey": key})
            }}
        }
    }


SCHEMA = "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"

f1 = {"$schema": SCHEMA, "contentVersion": "1.0.0.0", "parameters": PARAMETERS, "outputs": {},
      "triggers": {"When_a_leave_request_is_created_or_modified": {
          "type": "OpenApiConnection", "recurrence": {"interval": 5, "frequency": "Minute"},
          "splitOn": "@triggerOutputs()?['body/value']",
          # one request at a time, so two quick edits to the same request can't race
          "runtimeConfiguration": {"concurrency": {"runs": 1}},
          "inputs": {"host": {"connectionName": "shared_sharepointonline", "operationId": "GetOnUpdatedItems", "apiId": SP_API},
                     "parameters": {"dataset": f"@parameters('{SITE}')", "table": f"@parameters('{LIST}')"},
                     "authentication": "@parameters('$authentication')"}}},
      "actions": request_logic("triggerBody()")}

f2 = {"$schema": SCHEMA, "contentVersion": "1.0.0.0", "parameters": PARAMETERS, "outputs": {},
      "triggers": {"Daily_at_6": {"type": "Recurrence", "recurrence": {"frequency": "Day", "interval": 1, "timeZone": "GMT Standard Time",
                                                                          "schedule": {"hours": ["6"], "minutes": [0]}}}},
      "actions": {"Record_bank_holidays": unbound("fwp_RecordBankHolidays", {
          "FromDate": "@{formatDateTime(utcNow(), 'yyyy-MM-dd')}",
          "ToDate": "@{formatDateTime(addDays(utcNow(), 31), 'yyyy-MM-dd')}"})}}

backfill_item = "items('For_each_approved_request')"
backfill_logic = request_logic(backfill_item)
backfill_logic["Is_approved_absence"]["else"] = {"actions": {}}   # backfill only records; nothing to withdraw
f3 = {"$schema": SCHEMA, "contentVersion": "1.0.0.0", "parameters": PARAMETERS, "outputs": {},
      "triggers": {"manual": {"type": "Request", "kind": "Button", "inputs": {"schema": {"type": "object", "properties": {}, "required": []}}}},
      "actions": {
          "Approved_requests_from_today": {
              "type": "OpenApiConnection",
              "inputs": {"host": {"connectionName": "shared_sharepointonline", "operationId": "GetItems", "apiId": SP_API},
                         "parameters": {"dataset": f"@parameters('{SITE}')", "table": f"@parameters('{LIST}')",
                                        "$filter": "Status eq 'Approved' and RequestEndDate ge '@{formatDateTime(utcNow(), 'yyyy-MM-dd')}'",
                                        "$top": 5000},
                         "authentication": "@parameters('$authentication')"}},
          "For_each_approved_request": {
              "type": "Foreach", "runAfter": {"Approved_requests_from_today": ["Succeeded"]},
              "foreach": "@outputs('Approved_requests_from_today')?['body/value']",
              "runtimeConfiguration": {"concurrency": {"repetitions": 1}},
              "actions": backfill_logic}}}


def flow(name, desc, definition):
    clientdata = json.dumps({"properties": {"connectionReferences": CONNECTIONS, "definition": definition, "templateName": ""},
                             "schemaVersion": "1.0.0.0"})
    rows = q(f"workflows?$select=workflowid,statecode&$filter=name eq '{name}' and category eq 5")
    if rows:
        if rows[0]["statecode"] == 1:
            ok(*dv.call("PATCH", f"workflows({rows[0]['workflowid']})", {"statecode": 0, "statuscode": 1})[:2], f"  turn off {name}")
        ok(*dv.call("PATCH", f"workflows({rows[0]['workflowid']})", {"clientdata": clientdata, "description": desc})[:2], "update flow " + name)
    else:
        s, d, _ = dv.call("POST", "workflows", {"name": name, "description": desc, "category": 5, "type": 1, "primaryentity": "none",
                                                "statecode": 0, "statuscode": 1, "clientdata": clientdata}, solution=SOL)
        ok(s, d, "create flow (off) " + name)


flow("Leave Request - Record Time Entries",
     "DHA-9/10: when a leave request in the Valto app is approved, its time entries are recorded; if it is cancelled, "
     "rejected or changed to a non-absence type they are deactivated. Excludes fwp_LeaveExcludedRequestTypes.", f1)
flow("Leave - Record Bank Holidays", "DHA-9: daily 06:00, bank holiday time entries for every active colleague for the next 31 days.", f2)
flow("Leave Request - Backfill Approved Leave",
     "DHA-9: run ONCE at go-live. Records time entries for leave already approved in the Valto app from today onwards.", f3)
