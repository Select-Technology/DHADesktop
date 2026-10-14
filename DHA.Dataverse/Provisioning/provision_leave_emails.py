"""DHA-9 phase B: leave notification emails (replaces the Valto app's approval/cancellation/reminder flows).

Creates in FWPLeaveManagement:
  - connection references fwp_LeaveDataverse / fwp_LeaveOutlook, bound to EXISTING connections (below)
  - "Leave - Notifications": fires when LeaveRequestPlugin sets fwp_lastevent and emails HR or the employee
  - "Leave - Approval reminder": weekdays 10:00 UK, emails HR the requests waiting for a decision

Recipients: HR = env var fwp_LeaveApproverEmail (default HR@dhaplanning.co.uk). If fwp_LeaveEmailOverride is
set, EVERY email goes there instead and the subject shows who it was meant for - set it in dev/test.

Connections can't be created from a script. Pass the connection ids for the target environment:
    python -I provision_leave_emails.py <dataverse-connection-id> <outlook-connection-id>
Defaults are dev's existing stsadmin connections. Check a connection is "Connected" (not "Error") in
make.powerapps.com > Connections first - an expired Outlook connection fails every send with 401.
Re-running replaces the flow definitions.
"""
import os, sys, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveManagement"
DATAVERSE_CONN = sys.argv[1] if len(sys.argv) > 1 else "ed8ce61770fd47c4a742b7c761baba32"                       # dev: fwp_AdminDataverse
OUTLOOK_CONN = sys.argv[2] if len(sys.argv) > 2 else "shared-office365-9bcdf3c9-9d19-4673-b1b6-9e0a22a4f50e"     # dev: stsadmin (signed in 2026-10-14)
DV_API = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
OL_API = "/providers/Microsoft.PowerApps/apis/shared_office365"


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(d); sys.exit(1)


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300: print(path, d); sys.exit(1)
    return d["value"]


def conn_ref(logical, name, api, connection_id):
    rows = q(f"connectionreferences?$select=connectionreferenceid,connectionid&$filter=connectionreferencelogicalname eq '{logical}'")
    if rows:
        if rows[0]["connectionid"] != connection_id:
            s, d, _ = dv.call("PATCH", f"connectionreferences({rows[0]['connectionreferenceid']})", {"connectionid": connection_id})
            ok(s, d, f"repoint {logical}")
        return
    s, d, _ = dv.call("POST", "connectionreferences", {"connectionreferencelogicalname": logical, "connectionreferencedisplayname": name,
                                                       "connectorid": api, "connectionid": connection_id}, solution=SOL)
    ok(s, d, "connection reference " + logical)


conn_ref("fwp_LeaveDataverse", "Leave - Dataverse", DV_API, DATAVERSE_CONN)
conn_ref("fwp_LeaveOutlook", "Leave - Outlook", OL_API, OUTLOOK_CONN)
LEAVE_SET = dv.call("GET", "EntityDefinitions(LogicalName='fwp_leaverequest')?$select=EntitySetName")[1]["EntitySetName"]

CONNECTIONS = {
    "shared_commondataserviceforapps": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveDataverse"},
                                        "api": {"name": "shared_commondataserviceforapps"}},
    "shared_office365": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveOutlook"},
                         "api": {"name": "shared_office365"}},
}
APPROVER_PARAM = "Leave - Approver Email (fwp_LeaveApproverEmail)"
OVERRIDE_PARAM = "Leave - Email Override (testing) (fwp_LeaveEmailOverride)"
PARAMETERS = {
    "$connections": {"defaultValue": {}, "type": "Object"},
    "$authentication": {"defaultValue": {}, "type": "SecureObject"},
    APPROVER_PARAM: {"defaultValue": "HR@dhaplanning.co.uk", "type": "String",
                     "metadata": {"schemaName": "fwp_LeaveApproverEmail"}},
    OVERRIDE_PARAM: {"defaultValue": "", "type": "String", "metadata": {"schemaName": "fwp_LeaveEmailOverride"}},
}


def dataverse(op, params):
    return {"type": "OpenApiConnection", "inputs": {"host": {"connectionName": "shared_commondataserviceforapps", "operationId": op, "apiId": DV_API},
                                                    "parameters": params, "authentication": "@parameters('$authentication')"}}


def email(to_expr, subject, body, run_after=None):
    """Send via Outlook; honours the testing override and tags the subject with the intended recipient."""
    return {"type": "OpenApiConnection", "runAfter": run_after or {},
            "inputs": {"host": {"connectionName": "shared_office365", "operationId": "SendEmailV2", "apiId": OL_API},
                       "parameters": {
                           "emailMessage/To": f"@{{if(empty(parameters('{OVERRIDE_PARAM}')), {to_expr}, parameters('{OVERRIDE_PARAM}'))}}",
                           "emailMessage/Subject": f"@{{if(empty(parameters('{OVERRIDE_PARAM}')), '', concat('[TEST - for ', {to_expr}, '] '))}}{subject}",
                           "emailMessage/Body": STYLE + body + FOOTER,
                           "emailMessage/Importance": "Normal"},
                       "authentication": "@parameters('$authentication')"}}


STYLE = "<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#1e293b;max-width:600px\">"
FOOTER = "<p style=\"color:#64748b;font-size:12px;margin-top:24px\">This is an automated message from DHA Time Management.</p></div>"

# Values from "Get the request" (one call, expanding employee and leave type)
R = "outputs('Get_the_request')?['body"
EMPLOYEE = f"{R}/fwp_employee/fullname']"
EMPLOYEE_EMAIL = f"{R}/fwp_employee/internalemailaddress']"
APPROVER_EMAIL = f"parameters('{APPROVER_PARAM}')"
DATES = (f"@{{formatDateTime({R}/fwp_startdate'], 'ddd dd MMM yyyy')}}@{{if(empty({R}/fwp_starttime']), '', concat(' ', {R}/fwp_starttime']))}}"
         f" to @{{formatDateTime(coalesce({R}/fwp_enddate'], {R}/fwp_startdate']), 'ddd dd MMM yyyy')}}"
         f"@{{if(empty({R}/fwp_endtime']), '', concat(' ', {R}/fwp_endtime']))}}")
DETAILS = ("<table style=\"border-collapse:collapse;margin:12px 0\">"
           f"<tr><td style=\"padding:4px 12px 4px 0;color:#64748b\">Employee</td><td>@{{{EMPLOYEE}}}</td></tr>"
           f"<tr><td style=\"padding:4px 12px 4px 0;color:#64748b\">Dates</td><td>{DATES}</td></tr>"
           f"<tr><td style=\"padding:4px 12px 4px 0;color:#64748b\">Type</td><td>@{{coalesce({R}/fwp_leavetype/fwp_name'], '-')}}</td></tr>"
           f"<tr><td style=\"padding:4px 12px 4px 0;color:#64748b\">Hours</td><td>@{{{R}/fwp_hours']}}</td></tr>"
           "</table>")
REQUESTER_COMMENT = f"<p><b>Their comments:</b> @{{coalesce({R}/fwp_requestercomments'], '(none)')}}</p>"
APPROVER_COMMENT = f"@{{if(empty({R}/fwp_approvercomments']), '', concat('<p><b>Comment from HR:</b> ', {R}/fwp_approvercomments'], '</p>'))}}"
TO_APPROVALS = "<p>Review it in the DHA Time Management app: <b>Leave &rarr; Approvals</b>.</p>"
# Short dates for subjects
SUBJECT_DATES = f"@{{formatDateTime({R}/fwp_startdate'], 'dd/MM/yyyy')}}"

CASES = {
    "Requested": email(APPROVER_EMAIL, f"Leave request: @{{{EMPLOYEE}}} from {SUBJECT_DATES}",
                       "<p>A new leave request is waiting for a decision.</p>" + DETAILS + REQUESTER_COMMENT + TO_APPROVALS),
    "CancellationRequested": email(APPROVER_EMAIL, f"Leave cancellation request: @{{{EMPLOYEE}}} from {SUBJECT_DATES}",
                                   "<p>This person wants to cancel leave that has already been approved.</p>" + DETAILS + TO_APPROVALS),
    "Withdrawn": email(APPROVER_EMAIL, f"Leave request withdrawn: @{{{EMPLOYEE}}} from {SUBJECT_DATES}",
                       "<p>This pending leave request has been withdrawn by the employee. No action is needed.</p>" + DETAILS),
    "Approved": email(EMPLOYEE_EMAIL, f"Your leave from {SUBJECT_DATES} has been approved",
                      "<p>Your leave has been approved and added to your timesheet as &quot;Approved leave&quot;.</p>" + DETAILS + APPROVER_COMMENT),
    "Rejected": email(EMPLOYEE_EMAIL, f"Your leave request from {SUBJECT_DATES} was not approved",
                      "<p>Your leave request has not been approved.</p>" + DETAILS + APPROVER_COMMENT),
    "CancellationApproved": email(EMPLOYEE_EMAIL, f"Your leave from {SUBJECT_DATES} has been cancelled",
                                  "<p>Your cancellation has been approved. The leave and its timesheet entries have been removed.</p>" + DETAILS + APPROVER_COMMENT),
    "CancellationDeclined": email(EMPLOYEE_EMAIL, f"Your leave from {SUBJECT_DATES} is still booked",
                                  "<p>Your request to cancel this leave was not approved, so it remains booked.</p>" + DETAILS + APPROVER_COMMENT),
    "CancelledByHR": email(EMPLOYEE_EMAIL, f"Your leave from {SUBJECT_DATES} has been cancelled by HR",
                           "<p>HR has cancelled this approved leave. Its timesheet entries have been removed.</p>" + DETAILS + APPROVER_COMMENT),
}

notifications = {
    "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
    "contentVersion": "1.0.0.0",
    "parameters": PARAMETERS,
    "triggers": {
        "When_a_leave_request_event_happens": {
            "type": "OpenApiConnectionWebhook",
            "inputs": {"host": {"connectionName": "shared_commondataserviceforapps", "operationId": "SubscribeWebhookTrigger", "apiId": DV_API},
                       "parameters": {"subscriptionRequest/message": 4, "subscriptionRequest/entityname": "fwp_leaverequest",
                                      "subscriptionRequest/scope": 4, "subscriptionRequest/filteringattributes": "fwp_lastevent"},
                       "authentication": "@parameters('$authentication')"}}
    },
    "actions": {
        "Get_the_request": dataverse("GetItem", {
            "entityName": LEAVE_SET, "recordId": "@triggerOutputs()?['body/fwp_leaverequestid']",
            "$select": "fwp_startdate,fwp_enddate,fwp_starttime,fwp_endtime,fwp_hours,fwp_requestercomments,fwp_approvercomments,fwp_lastevent",
            "$expand": "fwp_Employee($select=fullname,internalemailaddress),fwp_LeaveType($select=fwp_name)"}),
        "Which_event": {
            "type": "Switch", "runAfter": {"Get_the_request": ["Succeeded"]},
            "expression": f"@{R}/fwp_lastevent']",
            "cases": {name: {"case": name, "actions": {f"Email_{name}": action}} for name, action in CASES.items()},
            "default": {"actions": {}}
        }
    },
    "outputs": {}
}

ROW = ("@{concat('<tr><td style=\"padding:4px 12px 4px 0\">', item()?['fwp_employee/fullname'], '</td><td style=\"padding:4px 12px 4px 0\">', "
       "formatDateTime(item()?['fwp_startdate'], 'ddd dd MMM'), ' to ', formatDateTime(coalesce(item()?['fwp_enddate'], item()?['fwp_startdate']), 'ddd dd MMM'), "
       "'</td><td style=\"padding:4px 12px 4px 0\">', if(equals(item()?['fwp_status'], 800470003), 'Cancellation', 'New request'), '</td></tr>')}")
reminder = {
    "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
    "contentVersion": "1.0.0.0",
    "parameters": PARAMETERS,
    "triggers": {
        "Weekdays_at_10": {"type": "Recurrence",
                           "recurrence": {"frequency": "Week", "interval": 1, "timeZone": "GMT Standard Time",
                                          "schedule": {"weekDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
                                                       "hours": ["10"], "minutes": [0]}}}
    },
    "actions": {
        "Waiting_requests": dataverse("ListRecords", {
            "entityName": LEAVE_SET, "$select": "fwp_startdate,fwp_enddate,fwp_status",
            "$filter": "fwp_status eq 800470000 or fwp_status eq 800470003",
            "$expand": "fwp_Employee($select=fullname)", "$orderby": "fwp_startdate asc"}),
        "Anything_waiting": {
            "type": "If", "runAfter": {"Waiting_requests": ["Succeeded"]},
            "expression": {"greater": ["@length(outputs('Waiting_requests')?['body/value'])", 0]},
            "actions": {
                "Rows": {"type": "Select", "inputs": {"from": "@outputs('Waiting_requests')?['body/value']", "select": ROW}},
                "Email_HR": email(APPROVER_EMAIL,
                                  "Leave waiting for a decision: @{length(outputs('Waiting_requests')?['body/value'])}",
                                  "<p>These leave requests are waiting for a decision:</p><table style=\"border-collapse:collapse\">"
                                  "<tr><th align=\"left\">Employee</th><th align=\"left\">Dates</th><th align=\"left\">Waiting for</th></tr>"
                                  "@{join(body('Rows'), '')}</table>" + TO_APPROVALS,
                                  run_after={"Rows": ["Succeeded"]}),
            },
            "else": {"actions": {}}
        }
    },
    "outputs": {}
}


def flow(name, desc, definition):
    clientdata = json.dumps({"properties": {"connectionReferences": CONNECTIONS, "definition": definition, "templateName": ""},
                             "schemaVersion": "1.0.0.0"})
    rows = q(f"workflows?$select=workflowid,statecode&$filter=name eq '{name}' and category eq 5")
    if rows:
        wid = rows[0]["workflowid"]
        if rows[0]["statecode"] == 1:
            ok(*dv.call("PATCH", f"workflows({wid})", {"statecode": 0, "statuscode": 1})[:2], f"  turn off {name} to update")
        s, d, _ = dv.call("PATCH", f"workflows({wid})", {"clientdata": clientdata, "description": desc})
        ok(s, d, "update flow " + name)
    else:
        s, d, _ = dv.call("POST", "workflows", {"name": name, "description": desc, "category": 5, "type": 1, "primaryentity": "none",
                                                "clientdata": clientdata}, solution=SOL, extra={"Prefer": "return=representation"})
        ok(s, d, "create flow " + name)
        wid = d["workflowid"]
    s, d, _ = dv.call("PATCH", f"workflows({wid})", {"statecode": 1, "statuscode": 2})
    ok(s, d, "  turn on " + name)


flow("Leave - Notifications", "DHA-9: emails HR or the employee when a leave request is made, decided or cancelled.", notifications)
flow("Leave - Approval reminder", "DHA-9: weekdays 10:00, emails HR the leave requests waiting for a decision.", reminder)
