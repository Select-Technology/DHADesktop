"""Reads the Valto leave app's SharePoint lists, for migration (parity plan stage 8).

The DHA Azure CLI sign-in has no SharePoint permission, so the lists are read through a flow in DEV,
"Leave - Valto export (migration)", on stsadmin's SharePoint connection there. It has an HTTP trigger and replies with
the list's columns or items. The flow is only switched on while reading and off again straight after, so its signed
URL is live for seconds. Nothing is stored in Dynamics: the data comes straight back to the caller.

    import valto_source
    rows = valto_source.items("Leave Request Data")
    cols = valto_source.fields("Leave Request Data")

    python -I valto_source.py fields       # prints every list's columns (no data)
"""
import json, os, sys, time, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

DEV = "https://dhapd-dev.crm11.dynamics.com"  # the export flow always lives here, whichever environment is migrated into

SITE = "https://dhaplanningcouk.sharepoint.com/sites/Leave-Request"
# Valto's prod environment variables (CoreLeaveRequest), read 26 Oct 2026
LISTS = {
    "Request Type Configuration": "0c7f07a7-2845-4c48-b971-900de4f7576b",
    "Employee Configuration": "213ed0b8-928d-43fb-9ffe-31c08fd0f02b",
    "Entitlement Balances": "40f27af7-7132-474f-ac4c-cf0264c9385b",
    "Leave Request Data": "718145ce-0b2d-4396-af86-ce36aabc8a29",
    "Public Holidays": "9e369f2a-8def-499e-8b1a-c20a9c76eec2",
    "Book on behalf of users": "d901bdeb-1675-4ab3-9086-49a9e86f25ed",
    "App Configuration": "d4ae4e98-d5f5-4077-ae47-543ed0050354",
}
FLOW_ENV = "964277cb-bd95-ee45-8c9b-b5dbe4520aa8"  # DHA Planning DEV
SP_CONNECTION = "shared-sharepointonl-415f9ffd-bc76-45d6-8eb5-76a7e9e42903"  # stsadmin, dev
CONN_REF = "fwp_LeaveMigrationSharePoint"
FLOW_NAME = "Leave - Valto export (migration)"
SP_API = "/providers/Microsoft.PowerApps/apis/shared_sharepointonline"


def _dev(method, path, body=None, extra=None):
    return dv.call(method, path, body, extra=extra, org=DEV)


def _q(path):
    s, d, _ = _dev("GET", path.replace(" ", "%20"))
    if s >= 300:
        raise RuntimeError(f"{path}: {d}")
    return d["value"]


def _ok(result, what):
    s, d = result[:2]
    if s >= 300:
        raise RuntimeError(f"{what}: {s} {d}")


def _definition():
    sp = lambda op, params: {"type": "OpenApiConnection",
                             "inputs": {"host": {"connectionName": "shared_sharepointonline", "operationId": op, "apiId": SP_API},
                                        "parameters": dict(dataset=SITE, **params), "authentication": "@parameters('$authentication')"}}
    items = sp("GetItems", {"table": "@triggerBody()?['list']", "$top": 5000})
    items["runtimeConfiguration"] = {"paginationPolicy": {"minimumItemCount": 100000}}
    fields = sp("HttpRequest", {"parameters/method": "GET",
                                "parameters/uri": "_api/web/lists(guid'@{triggerBody()?['list']}')/fields?$filter=Hidden eq false"
                                                  "&$select=InternalName,Title,TypeAsString,ReadOnlyField",
                                "parameters/headers": {"Accept": "application/json;odata=nometadata"}})
    reply = lambda body, after: {"type": "Response", "kind": "Http", "runAfter": {after: ["Succeeded"]},
                                 "inputs": {"statusCode": 200, "body": body}}
    # without this the caller waits for a gateway timeout when SharePoint refuses
    failed = lambda after: {"type": "Response", "kind": "Http", "runAfter": {after: ["Failed", "TimedOut"]},
                            "inputs": {"statusCode": 500, "body": f"@outputs('{after}')"}}
    return {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {"$connections": {"defaultValue": {}, "type": "Object"},
                       "$authentication": {"defaultValue": {}, "type": "SecureObject"}},
        "triggers": {"manual": {"type": "Request", "kind": "Http", "inputs": {"schema": {
            "type": "object", "properties": {"list": {"type": "string"}, "mode": {"type": "string"}}}}}},
        "actions": {"Which": {
            "type": "If", "expression": {"equals": ["@triggerBody()?['mode']", "fields"]},
            "actions": {"Get_fields": fields, "Reply_fields": reply("@body('Get_fields')?['value']", "Get_fields"),
                        "Reply_fields_failed": failed("Get_fields")},
            "else": {"actions": {"Get_items": items, "Reply_items": reply("@outputs('Get_items')?['body/value']", "Get_items"),
                                 "Reply_items_failed": failed("Get_items")}}}},
        "outputs": {}
    }


def _ensure_flow():
    """Creates (or refreshes) the export flow, OFF. Returns its workflow id."""
    if not _q(f"connectionreferences?$select=connectionreferenceid&$filter=connectionreferencelogicalname eq '{CONN_REF}'"):
        _ok(_dev("POST", "connectionreferences", {"connectionreferencelogicalname": CONN_REF,
                                                     "connectionreferencedisplayname": "Leave - Valto export (migration)",
                                                     "connectorid": SP_API, "connectionid": SP_CONNECTION}), "connection reference")
    clientdata = json.dumps({"properties": {
        "connectionReferences": {"shared_sharepointonline": {"runtimeSource": "embedded",
                                                             "connection": {"connectionReferenceLogicalName": CONN_REF},
                                                             "api": {"name": "shared_sharepointonline"}}},
        "definition": _definition(), "templateName": ""}, "schemaVersion": "1.0.0.0"})
    rows = _q(f"workflows?$select=workflowid,statecode,clientdata&$filter=name eq '{FLOW_NAME}' and category eq 5")
    if rows:
        wid = rows[0]["workflowid"]
        if rows[0]["statecode"] == 1:
            _set_on(wid, False)
        if json.loads(rows[0]["clientdata"])["properties"]["definition"] != _definition():
            _ok(_dev("PATCH", f"workflows({wid})", {"clientdata": clientdata}), "update export flow")
        return wid
    s, d, _ = _dev("POST", "workflows", {"name": FLOW_NAME, "category": 5, "type": 1, "primaryentity": "none", "statecode": 0,
                                            "statuscode": 1, "clientdata": clientdata,
                                            "description": "Leave migration: reads a Valto SharePoint list for migrate_valto_leave.py. "
                                                           "Switched on only while reading."},
                      extra={"Prefer": "return=representation"})
    _ok((s, d), "create export flow")
    return d["workflowid"]


def _set_on(wid, on):
    _ok(_dev("PATCH", f"workflows({wid})", {"statecode": 1 if on else 0, "statuscode": 2 if on else 1}), "switch export flow")


def _flow_api(method, path, body=None):
    req = urllib.request.Request(f"https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/{FLOW_ENV}/{path}"
                                 f"?api-version=2016-11-01", method=method, data=json.dumps(body or {}).encode(),
                                 headers={"Authorization": "Bearer " + dv.az_token("https://service.flow.microsoft.com/"),
                                          "Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read() or b"{}")


def _call(list_name, mode):
    wid = _ensure_flow()
    resource = _dev("GET", f"workflows({wid})?$select=resourceid")[1]["resourceid"]  # the flow's name in the Flow API
    _set_on(wid, True)
    try:
        for attempt in range(6):  # a flow switched on seconds ago can 404 here at first
            try:
                url = _flow_api("POST", f"flows/{resource}/triggers/manual/listCallbackUrl")["response"]["value"]
                break
            except urllib.error.HTTPError as e:
                if e.code != 404 or attempt == 5:
                    raise
                time.sleep(5)
        req = urllib.request.Request(url, method="POST", data=json.dumps({"list": LISTS[list_name], "mode": mode}).encode(),
                                     headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            raise RuntimeError(f"{list_name} ({mode}): {e.code} {e.read()[:500]}")
    finally:
        _set_on(wid, False)


def fields(list_name):
    """The list's visible columns: InternalName, Title, TypeAsString, ReadOnlyField."""
    return _call(list_name, "fields")


def items(list_name):
    """Every item, as the SharePoint connector returns them (choices as {'Value'}, people as {'Email', ...})."""
    return _call(list_name, "items")


if __name__ == "__main__" and sys.argv[1:2] == ["fields"]:
    for name in LISTS:
        print(f"\n== {name}")
        for f in fields(name):
            if not f["ReadOnlyField"]:
                print(f"  {f['InternalName']:40} {f['TypeAsString']:15} {f['Title']}")
