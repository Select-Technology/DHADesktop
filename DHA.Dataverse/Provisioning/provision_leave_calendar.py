"""Leave parity stage 6 (N4): approved absence on the shared Outlook leave calendar, in FWPLeaveManagement.

Replaces the calendar part of Valto's "Leave Request - Send Approval Emails" flow. Valto writes to a calendar in the
Automated@dhaplanning.co.uk mailbox (its new_CalendarID), with the request type in the subject. Here every event says
"Out of Office" (F5), so the type never appears on the shared calendar, and Working From Home isn't added.

Creates:
  - env var fwp_LeaveCalendarId: the calendar's id. "default" = the connection user's own "Calendar" (dev). A flow
    won't switch on while a setting it uses has no value at all, hence a word rather than empty.
    Prod: Valto's calendar, AAMkAGZjYzk3MzFm...KAAA= (see docs/leave-desktop-parity-plan.md, stage 6 notes)
  - connection reference fwp_LeaveCalendarOutlook, separate from the email one, because the calendar belongs to the
    mailbox of whoever the connection signs in as. Prod: an Outlook connection signed in as Automated@dhaplanning.co.uk.
    Outlook connections can't be shared, so that person picks it in make.powerapps.com and turns the flow on.
  - flow "Leave - Outlook calendar": runs when LeaveRequestPlugin sets fwp_lastevent, one run at a time. Each run
    reads the request and brings the calendar into line: booked absence gets an event (attendees: employee and
    approver, as Valto); dates or type changed ("Changed") moves it; anything no longer booked loses it.
    fwp_calendareventid holds the event id. While fwp_LeaveEmailOverride is set (dev/test) no attendees are added,
    so nobody real gets an invite.

    python -I provision_leave_calendar.py [outlook-connection-id]
Default connection: dev's stsadmin Outlook connection. Re-running replaces the flow definition.
"""
import os, sys, json, base64
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveManagement"
OUTLOOK_CONN = sys.argv[1] if len(sys.argv) > 1 else "shared-office365-9bcdf3c9-9d19-4673-b1b6-9e0a22a4f50e"  # dev: stsadmin
DLL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "DHA.Leave.Plugins", "bin", "Release", "DHA.Leave.Plugins.dll")
DV_API = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
OL_API = "/providers/Microsoft.PowerApps/apis/shared_office365"
print("Target:", dv.ORG)


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(d); sys.exit(1)


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300: print(path, d); sys.exit(1)
    return d["value"]


# 1. Plug-in (adds the "Changed" event)
asm = q("pluginassemblies?$select=pluginassemblyid&$filter=name eq 'DHA.Leave.Plugins'")[0]["pluginassemblyid"]
ok(*dv.call("PATCH", f"pluginassemblies({asm})", {"content": base64.b64encode(open(DLL, "rb").read()).decode()})[:2], "update plugin assembly")

# 2. Setting and connection reference
if not q("environmentvariabledefinitions?$select=environmentvariabledefinitionid&$filter=schemaname eq 'fwp_LeaveCalendarId'"):
    ok(*dv.call("POST", "environmentvariabledefinitions", {
        "schemaname": "fwp_LeaveCalendarId", "displayname": "Leave - Calendar Id", "type": 100000000, "defaultvalue": "default",
        "description": "Outlook calendar for approved leave (in the mailbox of the fwp_LeaveCalendarOutlook connection). "
                       "'default' = that user's own Calendar. Prod: Valto's calendar in Automated@dhaplanning.co.uk."}, solution=SOL)[:2],
       "env var fwp_LeaveCalendarId")
else:
    d = q("environmentvariabledefinitions?$select=environmentvariabledefinitionid,defaultvalue&$filter=schemaname eq 'fwp_LeaveCalendarId'")[0]
    if not d["defaultvalue"]:
        ok(*dv.call("PATCH", f"environmentvariabledefinitions({d['environmentvariabledefinitionid']})", {"defaultvalue": "default"})[:2],
           "  fwp_LeaveCalendarId default -> 'default'")

rows = q("connectionreferences?$select=connectionreferenceid,connectionid&$filter=connectionreferencelogicalname eq 'fwp_LeaveCalendarOutlook'")
if rows:
    if rows[0]["connectionid"] != OUTLOOK_CONN:
        ok(*dv.call("PATCH", f"connectionreferences({rows[0]['connectionreferenceid']})", {"connectionid": OUTLOOK_CONN})[:2],
           "repoint fwp_LeaveCalendarOutlook")
else:
    ok(*dv.call("POST", "connectionreferences", {"connectionreferencelogicalname": "fwp_LeaveCalendarOutlook",
                                                 "connectionreferencedisplayname": "Leave - Calendar (Outlook)",
                                                 "connectorid": OL_API, "connectionid": OUTLOOK_CONN}, solution=SOL)[:2],
       "connection reference fwp_LeaveCalendarOutlook")
LEAVE_SET = dv.call("GET", "EntityDefinitions(LogicalName='fwp_leaverequest')?$select=EntitySetName")[1]["EntitySetName"]

# 3. Flow
CAL = "Leave - Calendar Id (fwp_LeaveCalendarId)"
OVERRIDE = "Leave - Email Override (testing) (fwp_LeaveEmailOverride)"
# No override when the setting is empty or "none" (prod: an environment variable with no value stops a flow switching on)
NO_OVERRIDE = f"or(empty(parameters('{OVERRIDE}')), equals(toLower(parameters('{OVERRIDE}')), 'none'))"
AUTH = "@parameters('$authentication')"
R = "outputs('Get_the_request')?['body"
SD = f"substring(string({R}/fwp_startdate']), 0, 10)"
ED = f"substring(string(coalesce({R}/fwp_enddate'], {R}/fwp_startdate'])), 0, 10)"
LAST = "triggerOutputs()?['body/fwp_lastevent']"


def dataverse(op, params, run_after):
    return {"type": "OpenApiConnection", "runAfter": run_after,
            "inputs": {"host": {"connectionName": "shared_commondataserviceforapps", "operationId": op, "apiId": DV_API},
                       "parameters": params, "authentication": AUTH}}


def outlook(op, params, run_after):
    return {"type": "OpenApiConnection", "runAfter": run_after,
            "inputs": {"host": {"connectionName": "shared_office365", "operationId": op, "apiId": OL_API},
                       "parameters": params, "authentication": AUTH}}


def save_event_id(value, run_after):
    return dataverse("UpdateRecord", {"entityName": LEAVE_SET, "recordId": "@triggerOutputs()?['body/fwp_leaverequestid']",
                                      "item/fwp_calendareventid": value}, run_after)


BODY = (f"<p>@{{{R}/fwp_employee/fullname']}} is out of office.</p>"
        f"<p>From @{{formatDateTime(outputs('Start'), 'ddd dd MMM yyyy HH:mm')}} to "
        f"@{{if(outputs('All_day'), formatDateTime(addDays(outputs('End'), -1), 'ddd dd MMM yyyy'), formatDateTime(outputs('End'), 'ddd dd MMM yyyy HH:mm'))}}</p>"
        f"<p>Approved by @{{coalesce({R}/fwp_approver/fullname'], 'HR')}}.</p>"
        "<p style=\"color:#64748b;font-size:12px\">Added by DHA Time Management.</p>")

definition = {
    "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
    "contentVersion": "1.0.0.0",
    "parameters": {
        "$connections": {"defaultValue": {}, "type": "Object"},
        "$authentication": {"defaultValue": {}, "type": "SecureObject"},
        CAL: {"defaultValue": "default", "type": "String", "metadata": {"schemaName": "fwp_LeaveCalendarId"}},
        OVERRIDE: {"defaultValue": "none", "type": "String", "metadata": {"schemaName": "fwp_LeaveEmailOverride"}},
    },
    "triggers": {
        "When_a_leave_request_event_happens": {
            "type": "OpenApiConnectionWebhook",
            # one run at a time: each run syncs the calendar to the request as it is now, so runs mustn't overlap
            "runtimeConfiguration": {"concurrency": {"runs": 1}},
            "inputs": {"host": {"connectionName": "shared_commondataserviceforapps", "operationId": "SubscribeWebhookTrigger", "apiId": DV_API},
                       "parameters": {"subscriptionRequest/message": 3, "subscriptionRequest/entityname": "fwp_leaverequest",
                                      "subscriptionRequest/scope": 4, "subscriptionRequest/filteringattributes": "fwp_lastevent"},
                       "authentication": AUTH}}
    },
    "actions": {
        "Get_the_request": dataverse("GetItem", {
            "entityName": LEAVE_SET, "recordId": "@triggerOutputs()?['body/fwp_leaverequestid']",
            "$select": "fwp_startdate,fwp_enddate,fwp_starttime,fwp_endtime,fwp_status,fwp_calendareventid",
            "$expand": "fwp_Employee($select=fullname,internalemailaddress),fwp_Approver($select=fullname,internalemailaddress),"
                       "fwp_LeaveType($select=fwp_isabsence)"}, {}),
        "Get_calendars": outlook("CalendarGetTables_V2", {}, {"Get_the_request": ["Succeeded"]}),
        "Default_calendar": {"type": "Query", "runAfter": {"Get_calendars": ["Succeeded"]},
                             "inputs": {"from": "@body('Get_calendars')?['value']", "where": "@equals(item()?['name'], 'Calendar')"}},
        "Calendar": {"type": "Compose", "runAfter": {"Default_calendar": ["Succeeded"]},
                     "inputs": f"@if(or(empty(parameters('{CAL}')), equals(parameters('{CAL}'), 'default')), "
                               f"first(body('Default_calendar'))?['id'], parameters('{CAL}'))"},
        # Approved (or awaiting cancellation) absence. No type = absence; Working From Home has fwp_isabsence = No
        "On_the_calendar": {"type": "Compose", "runAfter": {"Calendar": ["Succeeded"]},
                            "inputs": f"@and(or(equals({R}/fwp_status'], 800470001), equals({R}/fwp_status'], 800470003)), "
                                      f"not(equals({R}/fwp_leavetype/fwp_isabsence'], false)))"},
        "Event_id": {"type": "Compose", "runAfter": {"On_the_calendar": ["Succeeded"]},
                     "inputs": f"@coalesce({R}/fwp_calendareventid'], '')"},
        "All_day": {"type": "Compose", "runAfter": {"Event_id": ["Succeeded"]},
                    "inputs": f"@and(empty({R}/fwp_starttime']), empty({R}/fwp_endtime']))"},
        # Part days use the request's times; a missing one is the usual start (08:30) or finish (17:30)
        "Start": {"type": "Compose", "runAfter": {"All_day": ["Succeeded"]},
                  "inputs": f"@if(outputs('All_day'), concat({SD}, 'T00:00:00'), concat({SD}, 'T', coalesce({R}/fwp_starttime'], '08:30'), ':00'))"},
        "End": {"type": "Compose", "runAfter": {"Start": ["Succeeded"]},
                "inputs": f"@if(outputs('All_day'), concat(formatDateTime(addDays({ED}, 1), 'yyyy-MM-dd'), 'T00:00:00'), "
                          f"concat({ED}, 'T', coalesce({R}/fwp_endtime'], '17:30'), ':00'))"},
        "Remove_old_event": {
            "type": "If", "runAfter": {"End": ["Succeeded"]},
            "expression": f"@and(not(empty(outputs('Event_id'))), or(not(outputs('On_the_calendar')), equals({LAST}, 'Changed')))",
            "actions": {
                "Delete_event": outlook("CalendarDeleteItem_V2", {"calendar": "@outputs('Calendar')", "event": "@outputs('Event_id')"}, {}),
                # already gone (deleted by hand) is fine
                "Clear_event_id": save_event_id("", {"Delete_event": ["Succeeded", "Failed"]}),
            },
            "else": {"actions": {}}
        },
        "Add_event": {
            "type": "If", "runAfter": {"Remove_old_event": ["Succeeded"]},
            "expression": f"@and(outputs('On_the_calendar'), or(empty(outputs('Event_id')), equals({LAST}, 'Changed')))",
            "actions": {
                "Create_event": outlook("V4CalendarPostItem", {
                    "table": "@outputs('Calendar')",
                    "item/subject": f"Out of Office - @{{{R}/fwp_employee/fullname']}}",
                    "item/start": "@outputs('Start')",
                    "item/end": "@outputs('End')",
                    "item/timeZone": "(UTC+00:00) Dublin, Edinburgh, Lisbon, London",  # UK local time, incl. BST
                    "item/isAllDay": "@outputs('All_day')",
                    # Valto invited the employee and approver; not while testing, so nobody real is invited from dev
                    "item/requiredAttendees": f"@{{if({NO_OVERRIDE}, concat({R}/fwp_employee/internalemailaddress'], ';', "
                                              f"coalesce({R}/fwp_approver/internalemailaddress'], '')), '')}}",
                    "item/body": BODY,
                    "item/showAs": "oof",
                    "item/isReminderOn": False,
                    "item/responseRequested": False,
                }, {}),
                "Save_event_id": save_event_id("@body('Create_event')?['id']", {"Create_event": ["Succeeded"]}),
            },
            "else": {"actions": {}}
        }
    },
    "outputs": {}
}

CONNECTIONS = {
    "shared_commondataserviceforapps": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveDataverse"},
                                        "api": {"name": "shared_commondataserviceforapps"}},
    "shared_office365": {"runtimeSource": "embedded", "connection": {"connectionReferenceLogicalName": "fwp_LeaveCalendarOutlook"},
                         "api": {"name": "shared_office365"}},
}
NAME = "Leave - Outlook calendar"
DESC = "Leave parity N4: approved absence as 'Out of Office' on the shared Outlook leave calendar; moved or removed when it changes."
clientdata = json.dumps({"properties": {"connectionReferences": CONNECTIONS, "definition": definition, "templateName": ""}, "schemaVersion": "1.0.0.0"})
rows = q(f"workflows?$select=workflowid,statecode&$filter=name eq '{NAME}' and category eq 5")
if rows:
    wid = rows[0]["workflowid"]
    if rows[0]["statecode"] == 1:
        ok(*dv.call("PATCH", f"workflows({wid})", {"statecode": 0, "statuscode": 1})[:2], "  turn off to update")
    ok(*dv.call("PATCH", f"workflows({wid})", {"clientdata": clientdata, "description": DESC})[:2], "update flow " + NAME)
else:
    s, d, _ = dv.call("POST", "workflows", {"name": NAME, "description": DESC, "category": 5, "type": 1, "primaryentity": "none",
                                            "clientdata": clientdata}, solution=SOL, extra={"Prefer": "return=representation"})
    ok(s, d, "create flow " + NAME)
    wid = d["workflowid"]
ok(*dv.call("PATCH", f"workflows({wid})", {"statecode": 1, "statuscode": 2})[:2], "  turn on " + NAME)
