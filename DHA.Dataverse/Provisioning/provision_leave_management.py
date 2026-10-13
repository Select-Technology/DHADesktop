"""DHA-9 phase B: provisions the FWPLeaveManagement solution - leave booked in the desktop app.

Run provision_leave_time_entries.py (phase A) first: this reuses its plug-in assembly, public holiday
table and settings. Requires `az login` as a System Administrator. Idempotent - safe to re-run.
    python -I provision_leave_management.py

Creates: fwp_leavetype, fwp_leaverequest, fwp_leaveentitlement, fwp_colleagueconfiguration.fwp_leaveapprover,
and the LeaveRequestPlugin steps. Security roles (who can read leave types, approve, administer) are a
manual step - see docs/DHA-9-leave-plan.md.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveManagement"
PUBLISHER = "11f741bf-e290-4fc6-8729-94230f9b6c7a"  # SelectTechnology (fwp)
REQ = {"Value": "None", "CanBeChanged": True, "ManagedPropertyLogicalName": "canmodifyrequirementlevelsettings"}


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(d); sys.exit(1)


def lbl(t):
    return {"@odata.type": "Microsoft.Dynamics.CRM.Label",
            "LocalizedLabels": [{"@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel", "Label": t, "LanguageCode": 1033}]}


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300: print(path, d); sys.exit(1)
    return d["value"]


def exists(entity, attr=None):
    path = f"EntityDefinitions(LogicalName='{entity}')" + (f"/Attributes(LogicalName='{attr}')" if attr else "") + "?$select=MetadataId"
    return dv.call("GET", path)[0] == 200


def table(schema, name, plural, desc, ownership):
    logical = schema.lower()
    if exists(logical):
        return
    s, d, _ = dv.call("POST", "EntityDefinitions", {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata", "SchemaName": schema,
        "DisplayName": lbl(name), "DisplayCollectionName": lbl(plural), "Description": lbl(desc),
        "OwnershipType": ownership, "HasActivities": False, "HasNotes": True, "IsActivity": False, "IsAuditEnabled": {"Value": True},
        "PrimaryNameAttribute": "fwp_name",
        "Attributes": [{"@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata", "SchemaName": "fwp_Name", "IsPrimaryName": True,
                        "DisplayName": lbl("Name"), "RequiredLevel": REQ, "MaxLength": 200, "FormatName": {"Value": "Text"}}]
    }, solution=SOL)
    ok(s, d, "table " + logical)


def column(entity, body):
    if exists(entity, body["SchemaName"].lower()):
        return
    body.setdefault("RequiredLevel", REQ)
    s, d, _ = dv.call("POST", f"EntityDefinitions(LogicalName='{entity}')/Attributes", body, solution=SOL)
    ok(s, d, f"  {entity}.{body['SchemaName'].lower()}")


def text(schema, name, length=100):
    return {"@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata", "SchemaName": schema, "DisplayName": lbl(name),
            "MaxLength": length, "FormatName": {"Value": "Text"}}


def memo(schema, name):
    return {"@odata.type": "Microsoft.Dynamics.CRM.MemoAttributeMetadata", "SchemaName": schema, "DisplayName": lbl(name),
            "MaxLength": 2000, "Format": "TextArea"}


def date(schema, name):
    return {"@odata.type": "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata", "SchemaName": schema, "DisplayName": lbl(name),
            "Format": "DateOnly", "DateTimeBehavior": {"Value": "TimeZoneIndependent"}}


def number(schema, name):
    return {"@odata.type": "Microsoft.Dynamics.CRM.DecimalAttributeMetadata", "SchemaName": schema, "DisplayName": lbl(name),
            "Precision": 2, "MinValue": -1000, "MaxValue": 10000}


def boolean(schema, name, default):
    return {"@odata.type": "Microsoft.Dynamics.CRM.BooleanAttributeMetadata", "SchemaName": schema, "DisplayName": lbl(name),
            "DefaultValue": default,
            "OptionSet": {"@odata.type": "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata",
                          "TrueOption": {"Value": 1, "Label": lbl("Yes")}, "FalseOption": {"Value": 0, "Label": lbl("No")}}}


def lookup(entity, schema, name, target):
    if exists(entity, schema.lower()):
        return
    s, d, _ = dv.call("POST", "RelationshipDefinitions", {
        "@odata.type": "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata",
        "SchemaName": f"fwp_{target}_{entity}_{schema.lower()}",
        "ReferencedEntity": target, "ReferencingEntity": entity,
        "CascadeConfiguration": {"Assign": "NoCascade", "Delete": "RemoveLink", "Merge": "NoCascade", "Reparent": "NoCascade",
                                 "Share": "NoCascade", "Unshare": "NoCascade"},
        "Lookup": {"@odata.type": "Microsoft.Dynamics.CRM.LookupAttributeMetadata", "SchemaName": schema, "DisplayName": lbl(name),
                   "RequiredLevel": REQ}}, solution=SOL)
    ok(s, d, f"  {entity}.{schema.lower()} -> {target}")


# Solution
if not q(f"solutions?$select=solutionid&$filter=uniquename eq '{SOL}'"):
    s, d, _ = dv.call("POST", "solutions", {"uniquename": SOL, "friendlyname": "FWP Leave Management", "version": "1.0.0.0",
                                            "description": "DHA-9 phase B: leave requests, entitlements and approvals in Dynamics",
                                            "publisherid@odata.bind": f"/publishers({PUBLISHER})"})
    ok(s, d, "create solution")

# Leave types (replaces the SharePoint Request Type Configuration list)
table("fwp_LeaveType", "Leave Type", "Leave Types", "Annual leave, medical, compassionate... Only visible to the employee, their approver and HR.", "OrganizationOwned")
column("fwp_leavetype", boolean("fwp_DeductsFromEntitlement", "Deducts From Entitlement", True))

# Leave requests - user-owned (owner = employee) so the platform keeps them private
table("fwp_LeaveRequest", "Leave Request", "Leave Requests", "A request for leave. The type is private; time entries created from it are generic.", "UserOwned")
lookup("fwp_leaverequest", "fwp_Employee", "Employee", "systemuser")
lookup("fwp_leaverequest", "fwp_Approver", "Approver", "systemuser")
lookup("fwp_leaverequest", "fwp_BookedBy", "Booked By", "systemuser")
lookup("fwp_leaverequest", "fwp_LeaveType", "Leave Type", "fwp_leavetype")
column("fwp_leaverequest", date("fwp_StartDate", "Start Date"))
column("fwp_leaverequest", date("fwp_EndDate", "End Date"))
column("fwp_leaverequest", text("fwp_StartTime", "Start Time", 5))
column("fwp_leaverequest", text("fwp_EndTime", "End Time", 5))
column("fwp_leaverequest", number("fwp_Hours", "Hours"))
column("fwp_leaverequest", memo("fwp_RequesterComments", "Requester Comments"))
column("fwp_leaverequest", memo("fwp_ApproverComments", "Approver Comments"))
column("fwp_leaverequest", text("fwp_CalendarEventId", "Calendar Event Id", 400))
column("fwp_leaverequest", text("fwp_LegacyId", "Legacy Id", 100))
column("fwp_leaverequest", {
    "@odata.type": "Microsoft.Dynamics.CRM.PicklistAttributeMetadata", "SchemaName": "fwp_Status", "DisplayName": lbl("Status"),
    "OptionSet": {"@odata.type": "Microsoft.Dynamics.CRM.OptionSetMetadata", "IsGlobal": False, "OptionSetType": "Picklist", "Options": [
        {"Value": 800470000, "Label": lbl("Pending")}, {"Value": 800470001, "Label": lbl("Approved")},
        {"Value": 800470002, "Label": lbl("Rejected")}, {"Value": 800470003, "Label": lbl("Cancellation Requested")},
        {"Value": 800470004, "Label": lbl("Cancelled")}]},
    "DefaultFormValue": 800470000})

# Entitlements - held in hours; the desktop app shows days using the colleague's usual day
table("fwp_LeaveEntitlement", "Leave Entitlement", "Leave Entitlements", "One row per person per leave year.", "OrganizationOwned")
lookup("fwp_leaveentitlement", "fwp_Employee", "Employee", "systemuser")
column("fwp_leaveentitlement", date("fwp_YearStart", "Leave Year Start"))
column("fwp_leaveentitlement", number("fwp_EntitlementHours", "Entitlement (hours)"))
column("fwp_leaveentitlement", number("fwp_CarriedForwardHours", "Carried Forward (hours)"))
column("fwp_leaveentitlement", number("fwp_PurchasedHours", "Purchased (hours)"))
column("fwp_leaveentitlement", number("fwp_AdjustmentHours", "Adjustment (hours)"))

# Approver on the existing colleague configuration (falls back to the user's manager)
lookup("fwp_colleagueconfiguration", "fwp_LeaveApprover", "Leave Approver", "systemuser")

s, d, _ = dv.call("POST", "PublishXml", {"ParameterXml": "<importexportxml><entities><entity>fwp_leavetype</entity><entity>fwp_leaverequest</entity>"
                                         "<entity>fwp_leaveentitlement</entity><entity>fwp_colleagueconfiguration</entity></entities></importexportxml>"})
ok(s, d, "publish")

# Plug-in steps for LeaveRequestPlugin (assembly registered by phase A script - re-run it after building)
rows = q("plugintypes?$select=plugintypeid&$filter=typename eq 'DHA.Leave.Plugins.LeaveRequestPlugin'")
if rows:
    ptype = rows[0]["plugintypeid"]
else:
    asm = q("pluginassemblies?$select=pluginassemblyid&$filter=name eq 'DHA.Leave.Plugins'")
    if not asm:
        sys.exit("Register DHA.Leave.Plugins first (provision_leave_time_entries.py).")
    s, d, _ = dv.call("POST", "plugintypes", {"typename": "DHA.Leave.Plugins.LeaveRequestPlugin", "name": "DHA.Leave.Plugins.LeaveRequestPlugin",
                                              "friendlyname": "LeaveRequestPlugin",
                                              "pluginassemblyid@odata.bind": f"/pluginassemblies({asm[0]['pluginassemblyid']})"},
                      extra={"Prefer": "return=representation"})
    ok(s, d, "plugin type LeaveRequestPlugin")
    ptype = d["plugintypeid"]

FILTER = "fwp_startdate,fwp_enddate,fwp_starttime,fwp_endtime,fwp_status,fwp_approver"
IMAGE = "fwp_employee,fwp_approver,fwp_startdate,fwp_enddate,fwp_starttime,fwp_endtime,fwp_status,fwp_legacyid"
for message, stage in [("Create", 20), ("Update", 20), ("Create", 40), ("Update", 40)]:
    name = f"LeaveRequestPlugin: {message} of fwp_leaverequest ({'Pre' if stage == 20 else 'Post'})"
    if q(f"sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid&$filter=name eq '{name}'"):
        continue
    msg = q(f"sdkmessages?$select=sdkmessageid&$filter=name eq '{message}'")[0]["sdkmessageid"]
    flt = q(f"sdkmessagefilters?$select=sdkmessagefilterid&$filter=primaryobjecttypecode eq 'fwp_leaverequest' and _sdkmessageid_value eq {msg}")[0]["sdkmessagefilterid"]
    body = {"name": name, "stage": stage, "mode": 0, "rank": 1, "supporteddeployment": 0,
            "eventhandler_plugintype@odata.bind": f"/plugintypes({ptype})",
            "sdkmessageid@odata.bind": f"/sdkmessages({msg})", "sdkmessagefilterid@odata.bind": f"/sdkmessagefilters({flt})"}
    if message == "Update":
        body["filteringattributes"] = FILTER
    s, d, _ = dv.call("POST", "sdkmessageprocessingsteps", body, solution=SOL, extra={"Prefer": "return=representation"})
    ok(s, d, "step " + name)
    if message == "Update":
        s, d2, _ = dv.call("POST", "sdkmessageprocessingstepimages", {
            "name": "PreImage", "entityalias": "PreImage", "imagetype": 0, "messagepropertyname": "Target", "attributes": IMAGE,
            "sdkmessageprocessingstepid@odata.bind": f"/sdkmessageprocessingsteps({d['sdkmessageprocessingstepid']})"})
        ok(s, d2, "  pre image")
