"""DHA-7: sets On Stop = No on OPEN projects that have never had it set, so the header shows "No" instead of "---".

Projects created since the column was added already default to No. Completed and Closed projects are left
empty on purpose (agreed 2026-10-15). Custom plug-ins and Power Automate flows are bypassed, so nothing else
fires; each project's Modified On still changes and the audit history gets a "--- -> No" entry.

    set DATAVERSE_URL=https://dhapd.crm11.dynamics.com
    python -I backfill_project_on_stop.py            # dry run: counts only
    python -I backfill_project_on_stop.py --apply    # does it
"""
import json, os, sys, uuid, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

APPLY = "--apply" in sys.argv
LEAVE_ALONE = ("Completed", "Closed")
BATCH = 200


def get(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300:
        sys.exit(f"{path}: {d}")
    return d


# statuscode values for Completed / Closed, by label (so nothing is hard-coded per environment)
opts = get("EntityDefinitions(LogicalName='msdyn_project')/Attributes(LogicalName='statuscode')"
           "/Microsoft.Dynamics.CRM.StatusAttributeMetadata?$select=LogicalName&$expand=OptionSet($select=Options)")["OptionSet"]["Options"]
skip = {o["Value"]: o["Label"]["UserLocalizedLabel"]["Label"] for o in opts if o["Label"]["UserLocalizedLabel"]["Label"] in LEAVE_ALONE}
assert len(skip) == len(LEAVE_ALONE), f"couldn't find all of {LEAVE_ALONE}: {skip}"

flt = "fwp_onstop eq null" + "".join(f" and statuscode ne {v}" for v in skip)
ids, url = [], f"msdyn_projects?$select=msdyn_projectid&$filter={flt}"
while url:
    s, d, _ = dv.call("GET", url.replace(" ", "%20"), extra={"Prefer": "odata.maxpagesize=5000"})
    if s >= 300:
        sys.exit(str(d))
    ids += [r["msdyn_projectid"] for r in d["value"]]
    nxt = d.get("@odata.nextLink")
    url = nxt.split("/api/data/v9.2/", 1)[1] if nxt else None
print(f"{dv.ORG}: {len(ids)} open projects with On Stop never set (leaving {', '.join(skip.values())} alone)")
if not APPLY:
    sys.exit("dry run - add --apply to update them")

done = failed = 0
for i in range(0, len(ids), BATCH):
    chunk, boundary = ids[i:i + BATCH], f"batch_{uuid.uuid4()}"
    parts = []
    for pid in chunk:
        parts.append(f"--{boundary}\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\n"
                     f"PATCH {dv.ORG}/api/data/v9.2/msdyn_projects({pid}) HTTP/1.1\r\n"
                     "Content-Type: application/json\r\nIf-Match: *\r\n"
                     "MSCRM.BypassBusinessLogicExecution: CustomSync,CustomAsync\r\n"
                     "MSCRM.SuppressCallbackRegistrationExpanderJob: true\r\n\r\n"
                     '{"fwp_onstop": false}\r\n')
    body = ("".join(parts) + f"--{boundary}--\r\n").encode()
    req = urllib.request.Request(f"{dv.ORG}/api/data/v9.2/$batch", data=body, method="POST", headers={
        "Authorization": "Bearer " + dv.tok(), "Content-Type": f"multipart/mixed; boundary={boundary}",
        "OData-Version": "4.0", "OData-MaxVersion": "4.0", "Prefer": "odata.continue-on-error"})
    with urllib.request.urlopen(req, timeout=600) as r:
        text = r.read().decode()
    ok_n = text.count("HTTP/1.1 204")
    done += ok_n
    failed += len(chunk) - ok_n
    if ok_n < len(chunk):
        print("  errors in batch:", [l for l in text.splitlines() if '"message"' in l][:3])
    print(f"  {min(i + BATCH, len(ids))}/{len(ids)} - updated {done}, failed {failed}")
print(f"done: {done} updated, {failed} failed")
