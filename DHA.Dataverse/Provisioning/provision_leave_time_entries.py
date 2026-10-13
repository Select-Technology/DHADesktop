"""DHA-9 phase A: provisions the FWPLeaveTimeEntries solution (column, table, env vars, plug-in, Custom APIs).

Requires `az login` as a System Administrator. Build DHA.Leave.Plugins in Release first.
Idempotent - safe to re-run; re-running uploads the latest plug-in build.
    python -I provision_leave_time_entries.py
"""
import os, sys, base64
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveTimeEntries"
PUBLISHER = "11f741bf-e290-4fc6-8729-94230f9b6c7a"  # SelectTechnology (fwp)
DLL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "DHA.Leave.Plugins", "bin", "Release", "DHA.Leave.Plugins.dll")


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


def add_component(objid, ctype):
    s, d, _ = dv.call("POST", "AddSolutionComponent", {"ComponentId": objid, "ComponentType": ctype, "SolutionUniqueName": SOL,
                                                      "AddRequiredComponents": False, "DoNotIncludeSubcomponents": False})
    ok(s, d, f"  add component {ctype}")


# 1. Solution
if not q(f"solutions?$select=solutionid&$filter=uniquename eq '{SOL}'"):
    s, d, _ = dv.call("POST", "solutions", {"uniquename": SOL, "friendlyname": "FWP Leave Time Entries", "version": "1.0.0.0",
                                            "description": "DHA-9: approved leave and bank holidays become time entries",
                                            "publisherid@odata.bind": f"/publishers({PUBLISHER})"})
    ok(s, d, "create solution")

# 2. Source key column on fwp_timeentry
s, d, _ = dv.call("GET", "EntityDefinitions(LogicalName='fwp_timeentry')/Attributes(LogicalName='fwp_leavesourcekey')?$select=MetadataId")
if s == 404:
    s, d, _ = dv.call("POST", "EntityDefinitions(LogicalName='fwp_timeentry')/Attributes", {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "fwp_LeaveSourceKey", "DisplayName": lbl("Leave Source Key"),
        "Description": lbl("Set on time entries created from approved leave or bank holidays (DHA-9). Identifies the leave request so it can be updated or removed."),
        "RequiredLevel": {"Value": "None", "CanBeChanged": True, "ManagedPropertyLogicalName": "canmodifyrequirementlevelsettings"},
        "MaxLength": 100, "FormatName": {"Value": "Text"}}, solution=SOL)
    ok(s, d, "create column fwp_leavesourcekey")

# 3. Public holiday table
s, d, _ = dv.call("GET", "EntityDefinitions(LogicalName='fwp_publicholiday')?$select=MetadataId")
if s == 404:
    s, d, _ = dv.call("POST", "EntityDefinitions", {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata",
        "SchemaName": "fwp_PublicHoliday", "DisplayName": lbl("Public Holiday"), "DisplayCollectionName": lbl("Public Holidays"),
        "Description": lbl("Bank holidays (England and Wales). Leave is never recorded on these days; everyone gets a bank holiday time entry instead (DHA-9)."),
        "OwnershipType": "OrganizationOwned", "HasActivities": False, "HasNotes": False, "IsActivity": False,
        "PrimaryNameAttribute": "fwp_name",
        "Attributes": [{
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata", "SchemaName": "fwp_Name", "IsPrimaryName": True,
            "DisplayName": lbl("Name"), "RequiredLevel": {"Value": "ApplicationRequired"}, "MaxLength": 100, "FormatName": {"Value": "Text"}}]
    }, solution=SOL)
    ok(s, d, "create table fwp_publicholiday")
    s, d, _ = dv.call("POST", "EntityDefinitions(LogicalName='fwp_publicholiday')/Attributes", {
        "@odata.type": "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata", "SchemaName": "fwp_Date", "DisplayName": lbl("Date"),
        "RequiredLevel": {"Value": "ApplicationRequired"}, "Format": "DateOnly", "DateTimeBehavior": {"Value": "TimeZoneIndependent"}}, solution=SOL)
    ok(s, d, "create column fwp_publicholiday.fwp_date")

# 4. Environment variables
def env_var(schema, name, desc, typ, default):
    rows = q(f"environmentvariabledefinitions?$select=environmentvariabledefinitionid&$filter=schemaname eq '{schema}'")
    if rows:
        return rows[0]["environmentvariabledefinitionid"]
    s, d, _ = dv.call("POST", "environmentvariabledefinitions", {"schemaname": schema, "displayname": name, "description": desc,
                                                                "type": typ, "defaultvalue": default},
                      solution=SOL, extra={"Prefer": "return=representation"})
    ok(s, d, "create env var " + schema)
    return d["environmentvariabledefinitionid"]

STRING, NUMBER = 100000000, 100000001
env_var("fwp_LeaveProjectId", "Leave - Approved Leave Job Id", "msdyn_projectid of the Job that approved leave is recorded against.", STRING, None)
env_var("fwp_BankHolidayProjectId", "Leave - Bank Holiday Job Id", "msdyn_projectid of the Job that bank holidays are recorded against (prod: Bank Holiday 36235).", STRING, None)
env_var("fwp_LeaveDefaultDayHours", "Leave - Default Day Hours", "Hours in a full day of leave when a colleague has no working pattern.", NUMBER, "7.5")
env_var("fwp_LeaveEntryLabel", "Leave - Time Entry Label", "Comment written on leave time entries. Deliberately generic - never the absence type.", STRING, "Approved leave")
env_var("fwp_BankHolidayEntryLabel", "Leave - Bank Holiday Label", "Comment written on bank holiday time entries.", STRING, "Bank holiday")

# 5. Plugin assembly + types
content = base64.b64encode(open(DLL, "rb").read()).decode()
rows = q("pluginassemblies?$select=pluginassemblyid&$filter=name eq 'DHA.Leave.Plugins'")
if rows:
    asm = rows[0]["pluginassemblyid"]
    s, d, _ = dv.call("PATCH", f"pluginassemblies({asm})", {"content": content})
    ok(s, d, "update plugin assembly")
else:
    s, d, _ = dv.call("POST", "pluginassemblies", {"name": "DHA.Leave.Plugins", "content": content, "isolationmode": 2, "sourcetype": 0,
                                                   "culture": "neutral", "version": "1.0.0.0", "publickeytoken": None},
                      extra={"Prefer": "return=representation"})
    ok(s, d, "register plugin assembly")
    asm = d["pluginassemblyid"]
add_component(asm, 91)

types = {}
for cls in ["RecordLeavePlugin", "RemoveLeavePlugin", "RecordBankHolidaysPlugin"]:
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

# 6. Custom APIs
STR, DT, INT, DEC, GUID = 10, 1, 7, 2, 12  # customapifieldtype
def api(name, desc, cls, params, outputs):
    rows = q(f"customapis?$select=customapiid&$filter=uniquename eq '{name}'")
    if rows:
        return
    body = {"uniquename": name, "name": name, "displayname": name.replace("fwp_", ""), "description": desc,
            "bindingtype": 0, "isfunction": False, "isprivate": False, "allowedcustomprocessingsteptype": 0,
            "PluginTypeId@odata.bind": f"/plugintypes({types[cls]})",
            "CustomAPIRequestParameters": [{"uniquename": p, "name": p, "displayname": p, "description": dsc, "type": t,
                                            "isoptional": opt} for p, t, opt, dsc in params],
            "CustomAPIResponseProperties": [{"uniquename": p, "name": p, "displayname": p, "description": p, "type": t} for p, t in outputs]}
    s, d, _ = dv.call("POST", "customapis", body, solution=SOL)
    ok(s, d, "custom api " + name)

counts = [("EntriesCreated", INT), ("EntriesRemoved", INT), ("TotalHours", DEC)]
api("fwp_RecordLeave", "Creates or replaces the time entries for one approved leave request (DHA-9).", "RecordLeavePlugin", [
    ("UserId", GUID, True, "Dynamics user (systemuserid). Give this or UserEmail."),
    ("UserEmail", STR, True, "Email of the person on leave. Give this or UserId."),
    ("StartDate", DT, False, "First day of leave."),
    ("EndDate", DT, True, "Last day of leave (defaults to StartDate)."),
    ("StartTime", STR, True, "HH:mm leave starts on the first day; omit for the start of the working day."),
    ("EndTime", STR, True, "HH:mm leave ends on the last day; omit for the end of the working day."),
    ("LeaveKind", STR, True, "Leave (default) or BankHoliday."),
    ("SourceKey", STR, False, "Identifies the request, e.g. LR-123. Re-sending the same key replaces its entries.")], counts)
api("fwp_RemoveLeave", "Removes the time entries for a cancelled or rejected leave request (DHA-9).", "RemoveLeavePlugin", [
    ("SourceKey", STR, False, "The key the entries were recorded with.")], [("EntriesRemoved", INT)])
api("fwp_RecordBankHolidays", "Creates bank holiday time entries for every active colleague for public holidays in a date range (DHA-9).",
    "RecordBankHolidaysPlugin", [
    ("FromDate", DT, True, "Start of range (defaults to today)."),
    ("ToDate", DT, True, "End of range (defaults to FromDate + 31 days).")], counts)

s, d, _ = dv.call("POST", "PublishXml", {"ParameterXml": "<importexportxml><entities><entity>fwp_timeentry</entity><entity>fwp_publicholiday</entity></entities></importexportxml>"})
ok(s, d, "publish")
